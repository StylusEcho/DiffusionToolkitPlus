using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Diffusion.Common;
using Diffusion.Video;

namespace Diffusion.Toolkit
{
    /// <summary>
    /// Decodes one side of a video comparison into frames that can be asked for by index, on a
    /// thread of its own. The compare view shows a frame only once both sides have the frame
    /// for the same moment, which is what keeps the two in step frame for frame.
    /// </summary>
    /// <remarks>
    /// A clip small enough is decoded from start to finish and kept, so looping, stepping and
    /// scrubbing back cost nothing - generated clips are short, and this is the usual case.
    /// Anything larger keeps a window of frames just behind and ahead of the playhead, seeking
    /// when the playhead jumps.
    /// </remarks>
    internal sealed class CompareVideoSource : IDisposable
    {
        /// <summary>
        /// The most decoded frames one side may hold to keep a whole clip. 720p is about 2.7 MB a
        /// frame, so this is around six seconds of 720p at 24 fps, and far more at the sizes most
        /// generators produce.
        /// </summary>
        private const long KeepAllBudgetBytes = 384L * 1024 * 1024;

        /// <summary>Frames decoded ahead of the playhead when the clip is too big to keep.</summary>
        private const int Lookahead = 12;

        /// <summary>Frames kept behind the playhead, for a step or two back.</summary>
        private const int Behind = 4;

        private readonly VideoFrameReader _reader;
        private readonly Dictionary<int, BitmapSource> _frames = new();
        private readonly object _lock = new();
        private readonly AutoResetEvent _signal = new(false);
        private readonly Thread _thread;

        private volatile bool _disposed;
        private volatile int _wanted;
        private volatile int _frameCount;
        private volatile bool _failed;

        private bool _keepAll;
        private bool _endFound;

        public string Path { get; }

        public double FrameRate { get; }

        /// <summary>
        /// The best known number of frames. Starts as what the container reports, and is put
        /// right if decoding finds the end somewhere else.
        /// </summary>
        public int FrameCount => _frameCount;

        public double DurationSeconds => _frameCount / FrameRate;

        /// <summary>Nothing could be decoded. A failed side is left out of the sync.</summary>
        public bool Failed => _failed;

        private CompareVideoSource(string path, VideoFrameReader reader)
        {
            Path = path;
            _reader = reader;

            FrameRate = reader.FrameRate;
            _frameCount = reader.ReportedFrameCount;

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = $"Compare decoder {System.IO.Path.GetFileName(path)}"
            };

            // The frames are WPF bitmaps, built here and frozen before they leave this thread
            _thread.SetApartmentState(ApartmentState.STA);
        }

        /// <summary>Opens a file and starts decoding it, or returns null if it cannot be read.</summary>
        public static CompareVideoSource? Open(string path)
        {
            var reader = VideoFrameReader.Open(path);

            if (reader == null)
            {
                Logger.Log($"Compare could not open {path} for decoding");
                return null;
            }

            var source = new CompareVideoSource(path, reader);

            source._thread.Start();

            return source;
        }

        /// <summary>
        /// The frame at an index if it has been decoded, and a hint to the decoder to work from
        /// there next.
        /// </summary>
        public BitmapSource? Get(int index)
        {
            Want(index);

            lock (_lock)
            {
                return _frames.GetValueOrDefault(index);
            }
        }

        public void Want(int index)
        {
            if (_wanted == index) return;

            _wanted = index;
            _signal.Set();
        }

        private void Run()
        {
            try
            {
                if (_frameCount <= 0)
                {
                    CountFrames();
                }

                if (_frameCount <= 0)
                {
                    _failed = true;
                    return;
                }

                // Stride is rounded up to four bytes a row, as the decoder lays it out
                var frameBytes = (long)((_reader.Width * 3 + 3) & ~3) * Math.Max(_reader.Height, 1);

                _keepAll = frameBytes * _frameCount <= KeepAllBudgetBytes;

                while (!_disposed)
                {
                    if (!DecodeNext())
                    {
                        _signal.WaitOne();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Compare decoding {Path} failed{Environment.NewLine}{e}");

                lock (_lock)
                {
                    if (_frames.Count == 0) _failed = true;
                }
            }
            finally
            {
                _reader.Dispose();
            }
        }

        /// <summary>
        /// For containers that do not say how many frames they hold: read to the end and count.
        /// Generated clips are short enough for this to take a moment at most.
        /// </summary>
        private void CountFrames()
        {
            var count = 0;

            while (!_disposed && _reader.TryRead(out _))
            {
                count++;
            }

            _frameCount = count;

            _reader.Seek(0);
        }

        /// <summary>
        /// Decodes one frame if there is one worth decoding. False when there is nothing to do
        /// until the playhead moves.
        /// </summary>
        private bool DecodeNext()
        {
            if (_failed) return false;

            if (_keepAll)
            {
                // Straight through from the start, never seeking: every frame lands on the index
                // it really has, and once the end is reached the whole clip is in hand
                return !_endFound && ReadOne();
            }

            var count = _frameCount;

            if (count <= 0) return false;

            var wanted = Math.Clamp(_wanted, 0, count - 1);

            Evict(wanted - Behind, wanted + Lookahead);

            var next = -1;

            lock (_lock)
            {
                for (var i = wanted; i <= Math.Min(wanted + Lookahead, count - 1); i++)
                {
                    if (!_frames.ContainsKey(i))
                    {
                        next = i;
                        break;
                    }
                }
            }

            if (next < 0) return false;

            // Reading on is cheaper than seeking when the frame is only a little ahead
            if (_reader.Position > next || next - _reader.Position > Lookahead)
            {
                SeekAtOrBefore(next);
            }

            return ReadOne();
        }

        /// <summary>
        /// Puts the reader at or before a frame. A seek can land past where it was asked, and
        /// the frames it jumped over would then never be decoded, so it backs off until it lands
        /// short and reads forward from there.
        /// </summary>
        private void SeekAtOrBefore(int index)
        {
            var target = index;
            var back = Math.Max(1, (int)Math.Ceiling(FrameRate));

            _reader.Seek(target);

            while (_reader.Position > index && target > 0)
            {
                target = Math.Max(0, target - back);
                back *= 2;

                _reader.Seek(target);
            }
        }

        private bool ReadOne()
        {
            var index = _reader.Position;

            if (!_reader.TryRead(out var raw) || raw == null)
            {
                OnReadFailed(index);
                return true;
            }

            if (index + 1 > _frameCount)
            {
                // Reported short; the frames past it are real
                _frameCount = index + 1;
            }

            // Only what is near the playhead is worth building, unless the whole clip is kept
            if (!_keepAll)
            {
                var wanted = _wanted;

                if (index < wanted - Behind || index > wanted + Lookahead) return true;
            }

            var bitmap = BitmapSource.Create(raw.Width, raw.Height, 96, 96, PixelFormats.Bgr24, null, raw.Pixels, raw.Stride);

            bitmap.Freeze();

            lock (_lock)
            {
                _frames[index] = bitmap;
            }

            return true;
        }

        /// <summary>
        /// Reading stopped short. Straight through from the start, that is simply the end. After
        /// a seek, it only says the end is somewhere before here, so go back and find it.
        /// </summary>
        private void OnReadFailed(int index)
        {
            if (index <= 0)
            {
                // Not even the first frame - there is nothing here to show
                _endFound = true;
                _failed = true;
                return;
            }

            if (_keepAll)
            {
                _endFound = true;
                _frameCount = index;
                return;
            }

            // Back a second at a time until a frame reads, then forward to where reading stops
            var back = Math.Max(1, (int)Math.Ceiling(FrameRate));
            var from = index;

            while (from > 0 && !_disposed)
            {
                from = Math.Max(0, from - back);

                _reader.Seek(from);

                if (_reader.TryRead(out _)) break;
            }

            var last = _reader.Position;

            while (!_disposed && _reader.TryRead(out _))
            {
                last = _reader.Position;
            }

            _frameCount = Math.Max(1, last);
        }

        private void Evict(int from, int to)
        {
            lock (_lock)
            {
                if (_frames.Count == 0) return;

                foreach (var key in _frames.Keys.Where(k => k < from || k > to).ToList())
                {
                    _frames.Remove(key);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            _signal.Set();

            // The reader belongs to the decoder thread and is closed there, after the frame in
            // hand, so the file is let go of without pulling it out from under a decode
            _thread.Join(TimeSpan.FromSeconds(2));

            lock (_lock)
            {
                _frames.Clear();
            }
        }
    }
}
