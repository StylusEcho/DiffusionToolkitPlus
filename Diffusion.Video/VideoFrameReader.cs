using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace Diffusion.Video;

/// <summary>
/// One decoded frame as packed 24-bit BGR rows, ready to hand to an imaging library.
/// </summary>
public sealed record RawFrame(byte[] Pixels, int Width, int Height, int Stride);

/// <summary>
/// Reads a video one frame at a time, by index. Used where frames have to be lined up exactly -
/// a media player decodes on its own clock and gives no way to say which frame is on screen.
/// </summary>
/// <remarks>
/// Not thread safe. Each instance belongs to the one thread that reads from it.
/// </remarks>
public sealed class VideoFrameReader : IDisposable
{
    private readonly VideoCapture _capture;
    private readonly Mat _frame = new();
    private readonly Mat _converted = new();

    public int Width { get; }

    public int Height { get; }

    public double FrameRate { get; }

    /// <summary>
    /// The frame count the container reports. Some containers - WebM especially - report
    /// nothing, or a figure that is a little out; reading past the real end is how to be sure.
    /// </summary>
    public int ReportedFrameCount { get; }

    /// <summary>The index of the frame the next <see cref="TryRead"/> returns.</summary>
    public int Position { get; private set; }

    private VideoFrameReader(VideoCapture capture)
    {
        _capture = capture;

        Width = (int)capture.Get(CapProp.FrameWidth);
        Height = (int)capture.Get(CapProp.FrameHeight);

        var fps = capture.Get(CapProp.Fps);

        // A missing or nonsense rate would make every frame index meaningless; 30 is the
        // commonest rate, and wrong only in pacing rather than in which frames line up
        FrameRate = fps is > 0 and < 1000 ? fps : 30;

        var count = capture.Get(CapProp.FrameCount);

        ReportedFrameCount = count is > 0 and < int.MaxValue ? (int)count : 0;
    }

    /// <summary>Opens a file for reading, or returns null if it cannot be decoded.</summary>
    public static VideoFrameReader? Open(string path)
    {
        VideoCapture? capture = null;

        try
        {
            capture = new VideoCapture(path);

            if (!capture.IsOpened)
            {
                capture.Dispose();
                return null;
            }

            return new VideoFrameReader(capture);
        }
        catch (Exception)
        {
            capture?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Moves to a frame. The decoder starts from the keyframe before it and decodes forward, so
    /// this is exact but not free - reading on from where it already is costs less.
    /// </summary>
    public void Seek(int index)
    {
        if (index < 0) index = 0;

        _capture.Set(CapProp.PosFrames, index);

        // Where it actually landed, which is not always where it was asked to go
        var landed = _capture.Get(CapProp.PosFrames);

        Position = landed >= 0 ? (int)landed : index;
    }

    /// <summary>
    /// Reads the frame at <see cref="Position"/> and moves on by one. False at the end of the
    /// file, or if the frame cannot be decoded.
    /// </summary>
    public bool TryRead(out RawFrame? frame)
    {
        frame = null;

        if (!_capture.Read(_frame) || _frame.IsEmpty) return false;

        Position++;

        var source = _frame;

        // Decoded frames are BGR almost always; anything else is brought into line so the
        // caller only ever deals with the one layout
        if (_frame.NumberOfChannels == 4)
        {
            CvInvoke.CvtColor(_frame, _converted, ColorConversion.Bgra2Bgr);
            source = _converted;
        }
        else if (_frame.NumberOfChannels == 1)
        {
            CvInvoke.CvtColor(_frame, _converted, ColorConversion.Gray2Bgr);
            source = _converted;
        }

        if (source.Depth != DepthType.Cv8U || source.NumberOfChannels != 3) return false;

        var stride = source.Step;
        var pixels = new byte[stride * source.Rows];

        Marshal.Copy(source.DataPointer, pixels, 0, pixels.Length);

        frame = new RawFrame(pixels, source.Cols, source.Rows, stride);

        return true;
    }

    public void Dispose()
    {
        _frame.Dispose();
        _converted.Dispose();
        _capture.Dispose();
    }
}
