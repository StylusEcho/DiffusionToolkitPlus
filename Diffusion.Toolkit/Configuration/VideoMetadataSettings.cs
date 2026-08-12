namespace Diffusion.Toolkit.Configuration;

/// <summary>
/// Controls which video properties are shown in the metadata pane.
/// </summary>
public class VideoMetadataSettings : SettingsContainer
{
    public VideoMetadataSettings()
    {
        ShowResolution = true;
        ShowLength = true;
        ShowFrameRate = true;
        ShowContainer = true;
        ShowVideoCodec = true;
        ShowVideoBitrate = true;
        ShowAudioFormat = true;
        ShowAudioBitrate = true;
    }

    public bool ShowResolution
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public bool ShowLength
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public bool ShowFrameRate
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public bool ShowContainer
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public bool ShowVideoCodec
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public bool ShowVideoBitrate
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public bool ShowAudioFormat
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public bool ShowAudioBitrate
    {
        get;
        set => UpdateValue(ref field, value);
    }
}
