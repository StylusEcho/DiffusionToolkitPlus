using Diffusion.Database;
using Diffusion.Toolkit.Classes;
using Diffusion.Toolkit.Controls;
using Diffusion.Toolkit.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Diffusion.Common;
using Diffusion.Toolkit.Services;
using Screen = System.Windows.Forms.Screen;

namespace Diffusion.Toolkit
{
    /// <summary>
    /// Interaction logic for Preview.xaml
    /// </summary>
    public partial class PreviewWindow : BorderlessWindow
    {
        private DataStore _dataStore => ServiceLocator.DataStore;
        private PreviewModel _model;
        private Action _onNext;

        public Action<string> OnDrop { get; set; }

        //public Action<int> Changed { get; set; }
        public Action AdvanceSlideShow { get; set; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            //PreviewPane.SetFocus();
            base.OnSourceInitialized(e);

            // The window handle only exists from here on, and we need it to find the monitor
            UpdateControlBarMaxWidth();
        }

        public PreviewWindow()
        {
            _model = new PreviewModel();
            InitializeComponent();
            DataContext = _model;

            _model.Close = new RelayCommand<object>(o =>
            {
                Close();
            });

            PreviewPane.IsPopout = true;

            ServiceLocator.TaggingService.TagUpdated += (sender, arguments) =>
            {
                //Changed?.Invoke(arguments.Id);
            };

            var mainModel = ServiceLocator.MainModel;

            _model.ToggleFitToPreview = mainModel.ToggleFitToPreview;
            _model.ToggleActualSize = mainModel.ToggleActualSize;
            _model.ToggleAutoAdvance = mainModel.ToggleAutoAdvance;
            _model.ToggleInfo = mainModel.ToggleInfoCommand;
            _model.ToggleTagsCommand = mainModel.ToggleTagsCommand;

            //_slideShowDelay = mainModel.Settings.SlideShowDelay;
            _model.ToggleFullScreen = new RelayCommand<object>((o) => ToggleFullScreen());
            _model.StartStopSlideShow = new RelayCommand<object>((o) => SpaceBarAction());
            _model.OpenWithCommand = new AsyncCommand<string>((o) => OpenWith(this, o));

            _model.TogglePlayPauseCommand = new RelayCommand<object>((o) => TogglePlayPause());
            _model.ToggleLoopCommand = new RelayCommand<object>((o) => ToggleLoop());
            _model.ToggleMuteCommand = new RelayCommand<object>((o) => ToggleMute());

            _model.IsTopHover = true;

            Task.Delay(3000).ContinueWith(t =>
            {
                if (_mouseTriggered) return;
                Dispatcher.Invoke(() =>
                {
                    _model.IsTopHover = false;
                });
            });

            _debounceCloseTopBar = Utility.Debounce(() =>
            {
                _model.IsTopHover = false;
            }, 2000);

            _debounceCloseBottomBar = Utility.Debounce(() =>
            {
                _model.IsBottomHover = false;
            }, 2000);

            _debounceCloseInfoOverlay = Utility.Debounce(() =>
            {
                if (_model.CurrentImage != null)
                {
                    _model.CurrentImage.IsParametersVisible = false;
                }
            }, 2000);

            _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _positionTimer.Tick += PositionTimerOnTick;

            PreviewPane.MediaOpened += PreviewPaneOnMediaOpened;

            SizeChanged += (sender, args) => UpdateControlBarMaxWidth();
            LocationChanged += (sender, args) => UpdateControlBarMaxWidth();

            Closing += OnClosing;
        }

        private Action _debounceCloseTopBar;
        private Action _debounceCloseBottomBar;
        private Action _debounceCloseInfoOverlay;
        private readonly DispatcherTimer _positionTimer;
        private bool _isScrubbing;

        /// <summary>
        /// Limits the video control bar to 75% of the width of the monitor the window is on.
        /// </summary>
        private void UpdateControlBarMaxWidth()
        {
            var handle = new WindowInteropHelper(this).Handle;

            if (handle == IntPtr.Zero) return;

            var screen = Screen.FromHandle(handle);

            _model.ControlBarMaxWidth = screen.Bounds.Width * 0.75;
        }

        private void PreviewPaneOnMediaOpened(object? sender, EventArgs e)
        {
            _model.IsVideo = PreviewPane.HasPlayer;
            _model.DurationSeconds = PreviewPane.Duration.TotalSeconds;
            _model.IsPlaying = PreviewPane.IsPlaying;
            _model.IsMuted = PreviewPane.IsMuted;
            _model.NotifyTimeChanged();

            if (_model.IsVideo)
            {
                _positionTimer.Start();
            }
        }

        private void PositionTimerOnTick(object? sender, EventArgs e)
        {
            if (!PreviewPane.HasPlayer)
            {
                _model.IsVideo = false;
                _positionTimer.Stop();
                return;
            }

            _model.IsPlaying = PreviewPane.IsPlaying;

            // While the user drags the thumb, the slider is the source of truth, not the player
            if (_isScrubbing) return;

            var duration = PreviewPane.Duration.TotalSeconds;

            if (Math.Abs(duration - _model.DurationSeconds) > 0.01)
            {
                _model.DurationSeconds = duration;
            }

            _model.PositionSeconds = PreviewPane.Position.TotalSeconds;
            _model.NotifyTimeChanged();
        }

        /// <summary>
        /// Space plays and pauses a video, and falls back to the slideshow for still images.
        /// </summary>
        private void SpaceBarAction()
        {
            if (PreviewPane.HasPlayer)
            {
                TogglePlayPause();
                return;
            }

            StartStopSlideShow();
        }

        private void TogglePlayPause()
        {
            PreviewPane.TogglePlayPause();
            _model.IsPlaying = PreviewPane.IsPlaying;
        }

        private void ToggleLoop()
        {
            ServiceLocator.Settings.LoopVideo = !ServiceLocator.Settings.LoopVideo;
        }

        private void ToggleMute()
        {
            PreviewPane.ToggleMute();
            _model.IsMuted = PreviewPane.IsMuted;
        }

        private void Seek_OnDragStarted(object sender, DragStartedEventArgs e)
        {
            _isScrubbing = true;
        }

        private void Seek_OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isScrubbing = false;

            PreviewPane.Position = TimeSpan.FromSeconds(_model.PositionSeconds);
        }

        private void BottomBar_OnMouseEnter(object sender, MouseEventArgs e)
        {
            _model.IsBottomHover = true;
        }

        private void BottomBar_OnMouseLeave(object sender, MouseEventArgs e)
        {
            _debounceCloseBottomBar();
        }

        /// <summary>
        /// Reveals the bottom control bar when the pointer nears the bottom of the window, and
        /// optionally the info overlay when it reaches the right edge.
        /// </summary>
        private void Window_OnMouseMove(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(this);

            if (PreviewPane.HasPlayer && position.Y >= ActualHeight - BottomHoverZone)
            {
                _model.IsBottomHover = true;
            }

            var settings = ServiceLocator.ExtendedSettings;

            if (settings.InfoOverlayOnRightEdge
                && _model.CurrentImage != null
                && position.X >= ActualWidth - Math.Max(1, settings.InfoOverlayEdgeWidth))
            {
                _model.CurrentImage.IsParametersVisible = true;
            }
        }

        private const int BottomHoverZone = 80;

        private void InfoOverlay_OnMouseLeave(object sender, MouseEventArgs e)
        {
            // Auto-hide only pairs with the edge gesture. Without it the overlay stays put until
            // "I" is pressed again, which is the behaviour people expect from a toggle.
            if (!ServiceLocator.ExtendedSettings.InfoOverlayOnRightEdge) return;

            _debounceCloseInfoOverlay();
        }

        private void RestartSlideShowTimer()
        {
            if (_slideShowTimer != null && _model.SlideShowActive)
            {
                _slideShowTimer.Change(TimeSpan.FromSeconds(_slideShowDelay), TimeSpan.FromSeconds(_slideShowDelay));
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            _slideShowTimer?.Dispose();
            _positionTimer.Stop();
        }

        private Timer? _slideShowTimer = null;
        private int _slideShowDelay => ServiceLocator.Settings.SlideShowDelay;

        private void SlideShowAdvance(object? state)
        {
            Dispatcher.Invoke(() =>
            {
                AdvanceSlideShow?.Invoke();
            });
        }

        private void StartStopSlideShow()
        {
            if (_slideShowTimer == null)
            {
                _slideShowTimer = new Timer(SlideShowAdvance, null, TimeSpan.FromSeconds(_slideShowDelay), TimeSpan.FromSeconds(_slideShowDelay));
                _model.SlideShowActive = true;

            }
            else
            {
                if (_model.SlideShowActive)
                {
                    _slideShowTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _model.SlideShowActive = false;
                }
                else
                {
                    _slideShowTimer.Change(TimeSpan.FromSeconds(_slideShowDelay), TimeSpan.FromSeconds(_slideShowDelay));
                    _model.SlideShowActive = true;
                }
            }
        }

        private bool _isFullScreen = false;
        //private WindowState _lastWindowState;
        //private WindowStyle _lastWindowStyle;

        private Brush _background;
        private bool _mouseTriggered;

        private void ToggleFullScreen()
        {
            _isFullScreen = !_isFullScreen;
            if (_isFullScreen)
            {
                _background = this.BackgroundGrid.Background;
                BackgroundGrid.Background = new SolidColorBrush(Colors.Black);
            }
            else
            {
                BackgroundGrid.Background = _background;
            }
            SetFullScreen(_isFullScreen);
        }

        public void ShowFullScreen()
        {
            Show();
            _isFullScreen = false;
            ToggleFullScreen();
        }

        public void SetNSFWBlur(bool value)
        {
            _model.NSFWBlur = value;
        }

        public void SetCurrentImage(ImageViewModel? value)
        {
            _model.CurrentImage = value;

            // A still image tears down the player, so drop the control bar until a video loads
            if (value is not { Type: ImageType.Video })
            {
                _positionTimer.Stop();
                _model.IsVideo = false;
                _model.IsPlaying = false;
                _model.PositionSeconds = 0;
                _model.DurationSeconds = 0;
                _model.NotifyTimeChanged();
            }
        }


        private void PreviewPane_OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent("DTCustomDragSource"))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                OnDrop?.Invoke(files[0]);
            }
        }

        private void PreviewPane_OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            OnPreviewKeyUp(e);

            SetFocus();
        }

        public void SetFocus()
        {
            PreviewPane.SetFocus();
        }

        private void PreviewPane_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            RestartSlideShowTimer();

            OnPreviewKeyDown(e);


            SetFocus();
        }

        public void LoadImage(ThumbnailViewModel thumbnail)
        {
            _model.CurrentImage = thumbnail.CurrentImage;
        }

        private void PreviewPane_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        private void Play_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            StartStopSlideShow();
        }

        private void Star_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            ServiceLocator.MainModel.ShowTags = !ServiceLocator.MainModel.ShowTags;
        }

        private void AutoAdvance_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            ServiceLocator.MainModel.AutoAdvance = !ServiceLocator.MainModel.AutoAdvance;
        }

        private async Task OpenWith(object sender, string? arg)
        {
            await ServiceLocator.ExternalApplicationsService.OpenWith(sender, int.Parse(arg));
        }

        private void UIElement_OnMouseEnter(object sender, MouseEventArgs e)
        {
            _mouseTriggered = true;
            _model.IsTopHover = true;
        }

        private void UIElement_OnMouseLeave(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("Left Top bar");
            _debounceCloseTopBar();
            //_model.IsTopHover = false;
        }

        private void FullScreen_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            ToggleFullScreen();
        }

        private void PreviewPane_OnKeyDown(object sender, KeyEventArgs e)
        {
            OnKeyDown(e);
        }

        private void PreviewPane_OnLoaded(object sender, RoutedEventArgs e)
        {
           PreviewPane.SetFocus();
        }
    }


}