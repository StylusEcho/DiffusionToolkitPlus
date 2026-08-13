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

        /// <summary>
        /// Space belongs to playback, always. Without this the key would be swallowed by whichever
        /// button in the control bar happens to have focus and "press" it instead.
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                SpaceBarAction();
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
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
            // Asking for the overlay pins it open. Only the pane revealed by brushing the right
            // edge is transient, and that path clears the flag itself.
            _model.ToggleInfo = new RelayCommand<object>(o =>
            {
                _infoOverlayPinned = true;
                mainModel.ToggleInfoCommand.Execute(o);
            });

            _model.ToggleTagsCommand = mainModel.ToggleTagsCommand;

            //_slideShowDelay = mainModel.Settings.SlideShowDelay;
            _model.ToggleFullScreen = new RelayCommand<object>((o) => ToggleFullScreen());
            _model.StartStopSlideShow = new RelayCommand<object>((o) => StartStopSlideShow());
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

            // Utility.Debounce continues on TaskScheduler.Default, so this has to marshal back -
            // it reads the overlay's hover state and the pointer position, which are UI-thread only
            _debounceCloseInfoOverlay = Utility.Debounce(() => Dispatcher.Invoke(CloseInfoOverlayIfPointerAway), 2000);

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

        /// <summary>
        /// True when the overlay was asked for rather than revealed by hovering the right edge.
        /// A pinned overlay stays put when the pointer leaves it; only a hover-revealed one is
        /// tidied away again.
        /// </summary>
        private bool _infoOverlayPinned;
        private readonly DispatcherTimer _positionTimer;
        private bool _isScrubbing;
        private bool _isTimerUpdate;

        /// <summary>
        /// Limits the video control bar to 75% of the width of the monitor the window is on.
        /// </summary>
        private void UpdateControlBarMaxWidth()
        {
            var handle = new WindowInteropHelper(this).Handle;

            if (handle == IntPtr.Zero) return;

            var screen = Screen.FromHandle(handle);

            _model.ControlBarMaxWidth = screen.Bounds.Width * 0.60;
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

            // Flagged so the slider's ValueChanged can tell our own update from a user seek
            _isTimerUpdate = true;
            _model.PositionSeconds = PreviewPane.Position.TotalSeconds;
            _isTimerUpdate = false;

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
            // Nothing to mute on a still image, and flipping the flag there would silently change
            // what the next video does
            if (!PreviewPane.HasPlayer) return;

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

        /// <summary>
        /// Seeks on any change the timer didn't make. Clicking the track moves the thumb without
        /// necessarily raising the drag events, so relying on those alone loses track clicks.
        /// </summary>
        private void Seek_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isTimerUpdate) return;
            if (!PreviewPane.HasPlayer) return;

            // Belt and braces: ignore anything that just echoes where the player already is
            if (Math.Abs(e.NewValue - PreviewPane.Position.TotalSeconds) < 0.75) return;

            PreviewPane.Position = TimeSpan.FromSeconds(e.NewValue);

            _model.NotifyTimeChanged();
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
        /// <remarks>
        /// This has to tunnel from the window. The preview pane's scroll dragger listens on
        /// PreviewMouseMove over the image, so a bubbling handler further up never sees the move.
        /// </remarks>
        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            var position = e.GetPosition(this);

            if (PreviewPane.HasPlayer && position.Y >= ActualHeight - BottomHoverZone)
            {
                _model.IsBottomHover = true;
            }

            if (_model.CurrentImage == null) return;

            if (ServiceLocator.ExtendedSettings.InfoOverlayOnRightEdge && IsPointerInInfoOverlayEdge(position))
            {
                // Brushing the edge reveals it, but does not pin it
                if (!_model.CurrentImage.IsParametersVisible)
                {
                    _infoOverlayPinned = false;
                }

                _model.CurrentImage.IsParametersVisible = true;
            }
            else if (!InfoOverlayPanel.IsMouseOver)
            {
                // Leaving the overlay itself already schedules the close, but the overlay does not
                // fill its column - it is top aligned and inset - so the pointer can move off it
                // without ever raising MouseLeave. Moving away anywhere has to close it too.
                _debounceCloseInfoOverlay();
            }
        }

        /// <summary>
        /// True when the pointer is in the strip at the right edge that reveals the info overlay.
        /// A very narrow strip is near impossible to land on, so a usable minimum is enforced.
        /// </summary>
        private bool IsPointerInInfoOverlayEdge(Point position)
        {
            var edgeWidth = Math.Max(16, ServiceLocator.ExtendedSettings.InfoOverlayEdgeWidth);

            return position.X >= ActualWidth - edgeWidth;
        }

        /// <summary>
        /// Hides the info overlay once the pointer has left it, unless it was pinned open, or the
        /// pointer has come back to it or to the strip that reveals it while the close was pending.
        /// </summary>
        private void CloseInfoOverlayIfPointerAway()
        {
            if (_infoOverlayPinned) return;
            if (_model.CurrentImage == null) return;
            if (InfoOverlayPanel.IsMouseOver) return;
            if (IsPointerInInfoOverlayEdge(Mouse.GetPosition(this))) return;

            _model.CurrentImage.IsParametersVisible = false;
        }

        private const int BottomHoverZone = 80;

        private void InfoOverlay_OnMouseLeave(object sender, MouseEventArgs e)
        {
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