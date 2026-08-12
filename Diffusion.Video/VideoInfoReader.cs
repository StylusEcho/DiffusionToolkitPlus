using Emgu.CV;
using Emgu.CV.CvEnum;
using MetadataExtractor;
using MetadataExtractor.Formats.Avi;

namespace Diffusion.Video;

/// <summary>
/// Reads technical details out of a video file using the libraries the project already carries -
/// OpenCV via Emgu for the video stream, and MetadataExtractor for AVI audio details, which
/// OpenCV does not expose.
/// </summary>
/// <remarks>
/// Nothing here is persisted. Video details are read when a video is previewed and cached in
/// memory, so the database stays byte for byte what the original Diffusion Toolkit expects.
/// </remarks>
public static class VideoInfoReader
{
    public static VideoInfo Read(string path)
    {
        var info = new VideoInfo
        {
            Container = GetContainer(path)
        };

        ReadVideoStream(path, info);
        ReadAudioStream(path, info);
        FillBitrate(path, info);

        return info;
    }

    private static string? GetContainer(string path)
    {
        var extension = Path.GetExtension(path);

        return string.IsNullOrEmpty(extension) ? null : extension.TrimStart('.').ToUpperInvariant();
    }

    private static void ReadVideoStream(string path, VideoInfo info)
    {
        try
        {
            using var capture = new VideoCapture(path);

            if (!capture.IsOpened) return;

            var width = capture.Get(CapProp.FrameWidth);
            var height = capture.Get(CapProp.FrameHeight);
            var fps = capture.Get(CapProp.Fps);
            var frameCount = capture.Get(CapProp.FrameCount);

            if (width > 0) info.Width = (int)width;
            if (height > 0) info.Height = (int)height;
            if (fps is > 0 and < 1000) info.FrameRate = Math.Round(fps, 3);

            if (fps > 0 && frameCount > 0)
            {
                info.Duration = TimeSpan.FromSeconds(frameCount / fps);
            }

            info.VideoCodec = DecodeFourCC(capture.Get(CapProp.FourCC));

            // CAP_PROP_BITRATE. Referenced by value because it isn't present on every build of
            // the OpenCV bindings; an unknown property simply returns 0.
            var bitrate = capture.Get((CapProp)47);

            // OpenCV reports this in kbit/s, and returns 0 when the backend doesn't know
            if (bitrate > 0)
            {
                info.VideoBitrate = (long)(bitrate * 1000);
            }
        }
        catch (Exception)
        {
            // A file we can't decode simply has no details to show
        }
    }

    /// <summary>
    /// Audio details are only reachable for AVI with what we have. MP4, MKV and WebM audio is
    /// left unset, and the UI omits those rows rather than showing a placeholder.
    /// </summary>
    private static void ReadAudioStream(string path, VideoInfo info)
    {
        if (!string.Equals(Path.GetExtension(path), ".avi", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);

            var avi = directories.OfType<AviDirectory>().FirstOrDefault();

            if (avi == null) return;

            var audioCodec = avi.GetDescription(AviDirectory.TagAudioCodec);

            if (!string.IsNullOrWhiteSpace(audioCodec))
            {
                info.AudioFormat = audioCodec;
            }

            if (info.VideoCodec == null)
            {
                var videoCodec = avi.GetDescription(AviDirectory.TagVideoCodec);

                if (!string.IsNullOrWhiteSpace(videoCodec))
                {
                    info.VideoCodec = videoCodec;
                }
            }
        }
        catch (Exception)
        {
            // Unreadable metadata is not worth surfacing here
        }
    }

    /// <summary>
    /// Falls back to file size over duration when the decoder didn't report a bitrate. That is the
    /// combined stream rate rather than the video stream alone, so it is flagged as approximate.
    /// </summary>
    private static void FillBitrate(string path, VideoInfo info)
    {
        if (info.VideoBitrate.HasValue) return;
        if (info.Duration is not { TotalSeconds: > 0 }) return;

        try
        {
            var length = new FileInfo(path).Length;

            if (length <= 0) return;

            info.VideoBitrate = (long)(length * 8 / info.Duration.Value.TotalSeconds);
            info.VideoBitrateIsApproximate = true;
        }
        catch (Exception)
        {
            // No size, no estimate
        }
    }

    /// <summary>
    /// OpenCV packs the codec FourCC into a double. Zero means the backend didn't report one.
    /// </summary>
    private static string? DecodeFourCC(double value)
    {
        var code = (int)value;

        if (code == 0) return null;

        var chars = new[]
        {
            (char)(code & 0xFF),
            (char)((code >> 8) & 0xFF),
            (char)((code >> 16) & 0xFF),
            (char)((code >> 24) & 0xFF)
        };

        var text = new string(chars).Trim();

        return text.Any(c => char.IsControl(c) || c == '\0') || text.Length == 0 ? null : text;
    }
}
