using System;
using System.Diagnostics;
using System.Windows.Media;
using Diffusion.Toolkit.Services;

namespace Diffusion.Toolkit
{
    /// <summary>
    /// Plays a video Windows cannot decode itself, by decoding it here and showing the frames in
    /// an image - HEVC without the HEVC extension installed, AV1, 10-bit or 4:4:4 H.264 and the
    /// like. The thumbnails for those come from the same decoder, which is why they show when
    /// the video does not.
    /// </summary>
    /// <remarks>
    /// Picture only: the sound is in the same file Windows could not open. Paced by the screen
    /// refresh; if decoding falls behind, the playhead waits for it rather than skipping on.
    /// </remarks>
    internal sealed class DecodedVideoPlayer : IDisposable
    {
        private readonly VideoFrameSource _source;
        private readonly System.Windows.Controls.Image _target;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private TimeSpan _lastTick;
        private double _position;
        private int _shown = -1;
        private bool _attached;

        public DecodedVideoPlayer(VideoFrameSource source, System.Windows.Controls.Image target)
        {
            _source = source;
            _target = target;

            _lastTick = _stopwatch.Elapsed;

            CompositionTarget.Rendering += OnRendering;
            _attached = true;
        }

        public string Path => _source.Path;

        public bool IsPlaying { get; private set; }

        /// <summary>Played to the end with looping off, and holding the last frame.</summary>
        public bool IsEnded { get; private set; }

        /// <summary>Raised on reaching the end with looping off.</summary>
        public event EventHandler? Ended;

        public TimeSpan Duration => TimeSpan.FromSeconds(_source.DurationSeconds);

        public TimeSpan Position
        {
            get => TimeSpan.FromSeconds(_position);
            set => Seek(value.TotalSeconds);
        }

        public void Play()
        {
            if (IsEnded)
            {
                _position = 0;
                IsEnded = false;
            }

            IsPlaying = true;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        private void Seek(double seconds)
        {
            var last = Math.Max(0, _source.DurationSeconds - 1.0 / _source.FrameRate);

            _position = Math.Clamp(seconds, 0, last);
            IsEnded = false;

            // Shown on the next refresh; asking now starts the decoder on it straight away
            _source.Want(FrameAt(_position));
        }

        private int FrameAt(double seconds)
        {
            var count = _source.FrameCount;

            if (count <= 0) return 0;

            var index = (int)Math.Floor(seconds * _source.FrameRate + 1e-6);

            return Math.Clamp(index, 0, count - 1);
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = _stopwatch.Elapsed;
            var elapsed = (now - _lastTick).TotalSeconds;

            _lastTick = now;

            if (_source.Failed) return;

            var duration = _source.DurationSeconds;

            var target = _position;
            var reachedEnd = false;

            if (IsPlaying)
            {
                target += elapsed;

                if (duration > 0 && target >= duration)
                {
                    if (ServiceLocator.Settings?.LoopVideo != false)
                    {
                        target %= duration;
                    }
                    else
                    {
                        target = duration;
                        reachedEnd = true;
                    }
                }
            }

            var index = FrameAt(target);

            if (index != _shown)
            {
                var frame = _source.Get(index);

                // Not decoded yet: hold here and let the time go, rather than run on ahead of
                // the picture
                if (frame == null) return;

                _target.Source = frame;
                _shown = index;
            }
            else
            {
                _source.Want(index);
            }

            _position = target;

            if (reachedEnd)
            {
                IsPlaying = false;
                IsEnded = true;
                Ended?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_attached)
            {
                CompositionTarget.Rendering -= OnRendering;
                _attached = false;
            }

            _target.Source = null;
            _source.Dispose();
        }
    }
}
