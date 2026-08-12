using System;
using System.Windows.Input;
using Diffusion.Toolkit.Models;
using Diffusion.Toolkit.Services;

namespace Diffusion.Toolkit;

public class PreviewModel : BaseNotify
{

    private ImageViewModel? _currentImage;
    private bool _fitToPreview;

    public PreviewModel()
    {
        _currentImage = new ImageViewModel();

        // Uncapped until the window knows which monitor it is on
        ControlBarMaxWidth = double.PositiveInfinity;
    }
        
    public ImageViewModel? CurrentImage
    {
        get => _currentImage;
        set => SetField(ref _currentImage, value);
    }

    public bool NSFWBlur
    {
        get;
        set => SetField(ref field, value);
    }

    public bool SlideShowActive
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand Close { get; set;  }
    public ICommand ToggleFitToPreview { get; set; }
    public ICommand ToggleActualSize { get; set; }
    public ICommand ToggleTagsCommand { get; set; }
    public ICommand ToggleAutoAdvance { get; set; }
    public ICommand StartStopSlideShow { get; set; }
    public ICommand ToggleInfo { get; set; }
    public ICommand ToggleFullScreen { get; set; }

    public ICommand OpenWithCommand { get; set; }

    public bool IsTopHover
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Drives the auto-hiding video control bar at the bottom of the window, mirroring
    /// <see cref="IsTopHover"/>.
    /// </summary>
    public bool IsBottomHover
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// True when the current item is a video, so the control bar has something to control.
    /// </summary>
    public bool IsVideo
    {
        get;
        set => SetField(ref field, value);
    }

    public bool IsPlaying
    {
        get;
        set => SetField(ref field, value);
    }

    public bool IsMuted
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Playback position in seconds. The slider binds to this; the window pushes the value into
    /// the player when the user drags, and pulls it from the player on a timer otherwise.
    /// </summary>
    public double PositionSeconds
    {
        get;
        set => SetField(ref field, value);
    }

    public double DurationSeconds
    {
        get;
        set => SetField(ref field, value);
    }

    public string PositionText => FormatTime(PositionSeconds);

    public string DurationText => FormatTime(DurationSeconds);

    /// <summary>
    /// Caps the control bar at 75% of the monitor width so it doesn't span an ultrawide display.
    /// </summary>
    public double ControlBarMaxWidth
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand TogglePlayPauseCommand { get; set; }
    public ICommand ToggleLoopCommand { get; set; }
    public ICommand ToggleMuteCommand { get; set; }

    /// <summary>
    /// Raises change notifications for the formatted time labels, which are derived properties.
    /// </summary>
    public void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;

        var time = TimeSpan.FromSeconds(seconds);

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    public MainModel MainModel => ServiceLocator.MainModel;
}