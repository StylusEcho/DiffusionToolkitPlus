using System;
using Diffusion.Toolkit.Services;
using Diffusion.Video;

namespace Diffusion.Toolkit.Models;

/// <summary>
/// Formats <see cref="VideoInfo"/> for the metadata pane.
///
/// Each row exposes both its text and a Show flag. A row is shown only when the user has enabled
/// that field in settings <i>and</i> the value could actually be read from the file - fields that
/// the available libraries can't determine for a given container are omitted rather than guessed.
/// </summary>
public class VideoInfoViewModel : BaseNotify
{
    private readonly VideoInfo _info;

    public VideoInfoViewModel(VideoInfo info)
    {
        _info = info;
    }

    private static Configuration.VideoMetadataSettings Options => ServiceLocator.ExtendedSettings.VideoMetadata;

    public string? Resolution => _info.Resolution;

    public bool ShowResolution => Options.ShowResolution && !string.IsNullOrEmpty(Resolution);

    public string? Length => _info.Duration.HasValue ? FormatDuration(_info.Duration.Value) : null;

    public bool ShowLength => Options.ShowLength && !string.IsNullOrEmpty(Length);

    public string? FrameRate => _info.FrameRate.HasValue ? $"{_info.FrameRate.Value:0.###} fps" : null;

    public bool ShowFrameRate => Options.ShowFrameRate && !string.IsNullOrEmpty(FrameRate);

    public string? Container => _info.Container;

    public bool ShowContainer => Options.ShowContainer && !string.IsNullOrEmpty(Container);

    public string? VideoCodec => _info.VideoCodec;

    public bool ShowVideoCodec => Options.ShowVideoCodec && !string.IsNullOrEmpty(VideoCodec);

    public string? VideoBitrate => _info.VideoBitrate.HasValue
        ? FormatBitrate(_info.VideoBitrate.Value) + (_info.VideoBitrateIsApproximate ? " (approx.)" : string.Empty)
        : null;

    public bool ShowVideoBitrate => Options.ShowVideoBitrate && !string.IsNullOrEmpty(VideoBitrate);

    public string? AudioFormat => _info.AudioFormat;

    public bool ShowAudioFormat => Options.ShowAudioFormat && !string.IsNullOrEmpty(AudioFormat);

    public string? AudioBitrate => _info.AudioBitrate.HasValue ? FormatBitrate(_info.AudioBitrate.Value) : null;

    public bool ShowAudioBitrate => Options.ShowAudioBitrate && !string.IsNullOrEmpty(AudioBitrate);

    /// <summary>
    /// False when nothing at all could be read, so the whole section can be hidden.
    /// </summary>
    public bool HasAny => ShowResolution || ShowLength || ShowFrameRate || ShowContainer
                          || ShowVideoCodec || ShowVideoBitrate || ShowAudioFormat || ShowAudioBitrate;

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private static string FormatBitrate(long bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000)
        {
            return $"{bitsPerSecond / 1_000_000d:0.##} Mbps";
        }

        return $"{bitsPerSecond / 1000d:0.#} kbps";
    }
}
