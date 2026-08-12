namespace Diffusion.Video;

/// <summary>
/// Technical details about a video file, read on demand for the info pane.
///
/// Every field is nullable: not every container exposes every property through the libraries
/// available here, and a field that could not be determined is left null so the UI can omit it
/// rather than show a guess.
/// </summary>
public class VideoInfo
{
    public int? Width { get; set; }

    public int? Height { get; set; }

    public TimeSpan? Duration { get; set; }

    public double? FrameRate { get; set; }

    /// <summary>
    /// Container format, derived from the file extension (MP4, WEBM, MKV, AVI...).
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// Video codec, usually the FourCC reported by the decoder (avc1, hev1, vp09...).
    /// </summary>
    public string? VideoCodec { get; set; }

    /// <summary>
    /// Video bitrate in bits per second.
    /// </summary>
    public long? VideoBitrate { get; set; }

    /// <summary>
    /// True when <see cref="VideoBitrate"/> was derived from the file size and duration rather
    /// than reported by the decoder, so the UI can label it as approximate.
    /// </summary>
    public bool VideoBitrateIsApproximate { get; set; }

    public string? AudioFormat { get; set; }

    /// <summary>
    /// Audio bitrate in bits per second.
    /// </summary>
    public long? AudioBitrate { get; set; }

    public string? Resolution => Width.HasValue && Height.HasValue ? $"{Width} x {Height}" : null;
}
