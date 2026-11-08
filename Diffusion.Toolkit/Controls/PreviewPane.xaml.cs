using Diffusion.Common;
using Diffusion.Database;
using Diffusion.Toolkit.Behaviors;
using Diffusion.Toolkit.Classes;
using Diffusion.Toolkit.Models;
using Diffusion.Toolkit.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using Image = System.Drawing.Image;
using Point = System.Windows.Point;

namespace Diffusion.Toolkit.Controls
{
    /// <summary>
    /// Interaction logic for PreviewPane.xaml
    /// </summary>
    public partial class PreviewPane : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(
                name: nameof(IsLoading),
                propertyType: typeof(bool),
                ownerType: typeof(PreviewPane),
                typeMetadata: new FrameworkPropertyMetadata(
                    defaultValue: false,
                    propertyChangedCallback: PropertyChangedCallback)
            );

        public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register(
                name: nameof(Image),
                propertyType: typeof(ImageViewModel),
                ownerType: typeof(PreviewPane),
                typeMetadata: new FrameworkPropertyMetadata(
                    defaultValue: default(ImageViewModel),
                    propertyChangedCallback: PropertyChangedCallback)
            );


        public static readonly DependencyProperty NSFWBlurProperty =
            DependencyProperty.Register(
                name: nameof(NSFWBlur),
                propertyType: typeof(bool),
                ownerType: typeof(PreviewPane),
                typeMetadata: new FrameworkPropertyMetadata(
                    defaultValue: false,
                    propertyChangedCallback: PropertyChangedCallback)
            );

        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register(
                name: nameof(Volume),
                propertyType: typeof(double),
                ownerType: typeof(PreviewPane),
                typeMetadata: new FrameworkPropertyMetadata(
                    defaultValue: 1.0d,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    propertyChangedCallback: PropertyChangedCallback)
            );

        private int? originalRating;

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);

                    if (child != null && child is T)
                        yield return (T)child;

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                        yield return childOfChild;
                }
            }
        }

        private static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.Property.Name == nameof(Image))
            {
                var preview = ((PreviewPane)d);
                var model = (ImageViewModel)e.NewValue;
                preview.SetHandler(model);
                preview.originalRating = model.Rating;

                // The same file again - a reload that rebuilt the model - leaves the media element
                // with nothing to open, so whatever is playing it now carries on
                var samePath = e.OldValue is ImageViewModel { Path: { } oldPath }
                               && string.Equals(oldPath, model?.Path, StringComparison.OrdinalIgnoreCase);

                if (!samePath)
                {
                    // The media element is reused from one video to the next, so whatever took
                    // over for the last one has to step aside for this one to get its own chance
                    preview.StopDecodedPlayback(forget: true);
                    preview.WatchForOpen(model);
                }

                //if (model.Type == ImageType.Video)
                //{
                //    if (preview is not { IsPopout: false, MainModel.IsPreviewOpen: true })
                //    {
                //        preview.isPlaying = true;
                //        preview.Player?.Play();
                //    }
                //}
            }
        }

        private void ImageViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImageViewModel.Image))
            {
                ResetView();
            }
        }

        private void SetHandler(ImageViewModel model)
        {
            model.PropertyChanged += ImageViewModelOnPropertyChanged;
        }

        private void ResetView()
        {
            ApplyViewScale();
        }

        /// <summary>
        /// Scales the preview to the current view mode. Fit to Preview and Actual Size only apply to
        /// the popped out viewer - the docked pane is too narrow to browse at actual size, so it
        /// always fits, and the two toggles leave it alone.
        /// </summary>
        private void ApplyViewScale()
        {
            if (!IsPopout)
            {
                FitToPreview();
                return;
            }

            if (ServiceLocator.MainModel is { FitToPreview: true })
            {
                FitToPreview();
            }

            if (ServiceLocator.MainModel is { ActualSize: true })
            {
                ActualSize();
            }
        }

        /// <summary>
        /// True when the preview scales to fit, which the docked pane always does.
        /// </summary>
        public bool IsFitToPreview => !IsPopout || ServiceLocator.MainModel is { FitToPreview: true };

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public bool NSFWBlur
        {
            get => (bool)GetValue(NSFWBlurProperty);
            set => SetValue(NSFWBlurProperty, value);
        }

        public ImageViewModel? Image
        {
            get => (ImageViewModel)GetValue(ImageProperty);
            set => SetValue(ImageProperty, value);
        }

        public double Volume
        {
            get => (double)GetValue(VolumeProperty);
            set => SetValue(VolumeProperty, value);
        }

        public ICommand CopyPathCommand { get; set; }


        private ScrollDragger? _scrollDragger = null;

        private System.Windows.Controls.Image? Preview = null;
        private System.Windows.Controls.ScrollViewer? ScrollViewer = null;
        private System.Windows.Controls.MediaElement? Player = null;

        /// <summary>
        /// Plays the current video instead of the media element when Windows cannot decode it.
        /// </summary>
        private DecodedVideoPlayer? _decoded;

        /// <summary>The image in the video template that the decoded frames are shown in.</summary>
        private System.Windows.Controls.Image? _decodedFrame;

        /// <summary>The video decoding has been started for, so it is started only once.</summary>
        private string? _decodingPath;

        /// <summary>
        /// The video Windows could not play. Remembered across the pane being unloaded and loaded
        /// again - leaving the page and coming back - so it goes straight back to decoding.
        /// </summary>
        private string? _unplayablePath;

        /// <summary>
        /// Catches a video that neither opens nor fails. Windows usually says when it cannot play
        /// something, but not always - it can sit on a black frame for ever.
        /// </summary>
        private DispatcherTimer? _openWatchdog;

        private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(5);

        public PreviewPane()
        {
            InitializeComponent();
            InitIcons();
            //_scrollDragger = new ScrollDragger(Preview, ScrollViewer, handCursor, grabCursor);
            SizeChanged += OnSizeChanged;


            CopyPathCommand = new RelayCommand<object>(ServiceLocator.ContextMenuService.CopyPath);

            if (ServiceLocator.MainModel != null)
            {
                ServiceLocator.MainModel.PropertyChanged += MainModelOnPropertyChanged;
            }

            // A decoder left running would keep its thread, its frames and a refresh handler
            // after the viewer has closed
            Unloaded += (_, _) =>
            {
                StopOpenWatchdog();
                StopDecodedPlayback(forget: false);
            };
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyViewScale();
        }

        private void FitToPreview()
        {
            if (Image is { Image: { }, Type: ImageType.Image })
            {

                var ratio = ActualWidth / ActualHeight;

                double factor;

                var hfactor = ActualHeight / Image.Image.Height;
                var vfactor = ActualWidth / Image.Image.Width;

                factor = Math.Min(hfactor, vfactor);

                Preview?.LayoutTransform = new ScaleTransform(factor, factor);

                UpdateLayout();
            }
        }

        private void ActualSize()
        {
            if (Image is { Image: { }, Type: ImageType.Image })
            {
                Preview?.LayoutTransform = new ScaleTransform(1, 1);
                UpdateLayout();
            }
        }

        private void ResetScrollbars()
        {
            ScrollViewer.ScrollToHorizontalOffset(0);
            ScrollViewer.ScrollToVerticalOffset(0);
        }

        private Cursor handCursor;
        private Cursor grabCursor;
        private Action<int, int?> _rate;


        public MainModel MainModel => ServiceLocator.MainModel;

        private void InitIcons()
        {
            try
            {
                Uri handIconUri = new Uri("pack://application:,,,/Icons/hand.cur", UriKind.RelativeOrAbsolute);
                handCursor = new Cursor(Application.GetResourceStream(handIconUri).Stream);
                Uri grabIconUri = new Uri("pack://application:,,,/Icons/grab.cur", UriKind.RelativeOrAbsolute);
                grabCursor = new Cursor(Application.GetResourceStream(grabIconUri).Stream);
                //Unloaded += OnUnloaded;
            }
            catch (Exception e)
            {
            }
        }


        //private void OnUnloaded(object sender, RoutedEventArgs e)
        //{
        //    handCursor.Dispose();
        //    grabCursor.Dispose();
        //}


        public void ResetZoom()
        {
            // Reset means back to actual size in the viewer, but back to the fit in the docked pane,
            // which has no other resting state
            if (!IsPopout)
            {
                ApplyViewScale();
                return;
            }

            if (Image is { Image: { }, Type: ImageType.Image })
            {
                Preview?.LayoutTransform = new ScaleTransform(1, 1);

                ScrollViewer?.ScrollToHorizontalOffset(0);
                ScrollViewer?.ScrollToVerticalOffset(0);
                this.UpdateLayout();
            }

        }

        private void Zoom(MouseWheelEventArgs e)
        {
            if (Image.Type == ImageType.Video) return;

            // The docked pane always fits, so there is no zoom level to move away from
            if (!IsPopout) return;

            Point mouseAtImage = e.GetPosition(Preview); // ScrollViewer_CanvasMain.TranslatePoint(middleOfScrollViewer, Canvas_Main);
            Point mouseAtScrollViewer = e.GetPosition(ScrollViewer);

            // Calculate the new zoom level based on the mouse wheel delta
            double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;

            zoomDelta = Preview.LayoutTransform.Value.M11 * zoomDelta;

            double newZoom = Math.Min(Math.Max(Preview.LayoutTransform.Value.M11 + zoomDelta, 0.1), 10);


            Preview.LayoutTransform = new ScaleTransform(newZoom, newZoom);

            ScrollViewer?.ScrollToHorizontalOffset(0);
            ScrollViewer?.ScrollToVerticalOffset(0);
            this.UpdateLayout();

            Vector offset = Preview.TranslatePoint(mouseAtImage, ScrollViewer) - mouseAtScrollViewer; // (Vector)middleOfScrollViewer;
            ScrollViewer?.ScrollToHorizontalOffset(offset.X);
            ScrollViewer?.ScrollToVerticalOffset(offset.Y);
            this.UpdateLayout();
        }

        private void UIElement_OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            var active = true;

            var scrollNavigation = ServiceLocator.Settings.ScrollNavigation;



            if (ctrlPressed)
            {
                if (scrollNavigation)
                {
                    // Zooming is a viewer-only gesture. In the docked pane it would both break the
                    // fit and, because these two flags now govern the viewer alone, silently switch
                    // the viewer out of Fit to Preview from the other side of the app.
                    if (IsPopout)
                    {
                        ServiceLocator.MainModel.FitToPreview = false;
                        ServiceLocator.MainModel.ActualSize = false;

                        Zoom(e);
                    }

                    e.Handled = true;
                }
            }
            else
            {
                if (scrollNavigation)
                {
                    Key vkey = e.Delta > 0 ? Key.Left : e.Delta < 0 ? Key.Right : Key.None;

                    Debug.WriteLine(e.Delta);

                    //var ps = PresentationSource.FromVisual((ScrollViewer)sender);

                    switch (vkey)
                    {
                        case Key.Left:
                            ServiceLocator.ThumbnailNavigationService.MovePrevious();
                            break;
                        case Key.Right:
                            ServiceLocator.ThumbnailNavigationService.MoveNext();
                            break;
                    }

                    e.Handled = true;
                }
                else
                {
                    Zoom(e);
                    e.Handled = true;
                }
            }
        }

        public void ZoomPreview(double zoomDelta)
        {
            if (Image.Type == ImageType.Video) return;

            // The docked pane always fits, so +/- do nothing there
            if (!IsPopout) return;

            var mouseAtScrollViewer = new Point(ScrollViewer.ViewportWidth / 2, ScrollViewer.ViewportHeight / 2);
            Point mouseAtImage = ScrollViewer.TranslatePoint(mouseAtScrollViewer, Preview);

            zoomDelta = Preview.LayoutTransform.Value.M11 * zoomDelta;

            double newZoom = Math.Min(Math.Max(Preview.LayoutTransform.Value.M11 + zoomDelta, 0.1), 10);

            Preview.LayoutTransform = new ScaleTransform(newZoom, newZoom);

            ScrollViewer.ScrollToHorizontalOffset(0);
            ScrollViewer.ScrollToVerticalOffset(0);
            this.UpdateLayout();

            Vector offset = Preview.TranslatePoint(mouseAtImage, ScrollViewer) - mouseAtScrollViewer;
            ScrollViewer.ScrollToHorizontalOffset(offset.X);
            ScrollViewer.ScrollToVerticalOffset(offset.Y);
            this.UpdateLayout();
        }

        private void Grid_OnKeyDown(object sender, KeyEventArgs e)
        {
            //if (e.Key == Key.I && e.KeyboardDevice.Modifiers == ModifierKeys.None)
            //{
            //    Image.IsParametersVisible = !Image.IsParametersVisible;
            //}
            //if (e.Key == Key.F && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control) && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
            //{
            //    MainModel.FitToPreview = !MainModel.FitToPreview;
            //}

            switch (e.Key)
            {
                case Key.X or Key.Delete when e.KeyboardDevice.Modifiers == ModifierKeys.None:
                    Image.ForDeletion = !Image.ForDeletion;
                    ServiceLocator.TaggingService.ForDeletion(this, Image.Id, Image.ForDeletion);
                    break;
                case Key.N when e.KeyboardDevice.Modifiers == ModifierKeys.None:
                    Image.NSFW = !Image.NSFW;
                    ServiceLocator.TaggingService.NSFW(this, Image.Id, Image.NSFW);
                    break;
                case Key.F when e.KeyboardDevice.Modifiers == ModifierKeys.None:
                    Image.Favorite = !Image.Favorite;
                    ServiceLocator.TaggingService.Favorite(this, Image.Id, Image.Favorite);
                    break;
                case Key.OemTilde:
                    Image.Rating = null;
                    ServiceLocator.TaggingService.Rate(this, Image.Id, null);
                    break;
                case >= Key.D0 and <= Key.D9 when e.KeyboardDevice.Modifiers == ModifierKeys.None:
                    {
                        int? rating = e.Key switch
                        {
                            Key.D1 => 1,
                            Key.D2 => 2,
                            Key.D3 => 3,
                            Key.D4 => 4,
                            Key.D5 => 5,
                            Key.D6 => 6,
                            Key.D7 => 7,
                            Key.D8 => 8,
                            Key.D9 => 9,
                            Key.D0 => 10,
                        };

                        if (Image.Rating == rating)
                        {
                            rating = null;
                        }

                        Image.Rating = rating;

                        ServiceLocator.TaggingService.Rate(this, Image.Id, rating);
                        break;
                    }
                case Key.D0 when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    ResetZoom();
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                    ZoomPreview(0.1);
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                    ZoomPreview(-0.1);
                    e.Handled = true;
                    break;
            }
        }

        public void ToggleInfo()
        {
            // The two viewers keep separate overlay state, so toggle whichever one we are
            if (IsPopout)
            {
                Image.IsParametersVisible = !Image.IsParametersVisible;
            }
            else
            {
                Image.IsSideParametersVisible = !Image.IsSideParametersVisible;
            }
        }

        public bool IsPopout { get; set; }

        public Action OnPopout { get; set; }

        private void MainModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Both toggles are popped-out-viewer only, so the docked pane ignores the change
            if (e.PropertyName is nameof(MainModel.FitToPreview) or nameof(MainModel.ActualSize))
            {
                if (IsPopout)
                {
                    ApplyViewScale();
                }

                OnPropertyChanged(nameof(IsFitToPreview));
            }
            if (e.PropertyName == nameof(MainModel.ShowTags))
            {
                OnPropertyChanged(nameof(IsRatingsBarVisible));
            }
        }

        private void Grid_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            OnPreviewKeyDown(e);
        }

        //private void PreviewPane_OnGotFocus(object sender, RoutedEventArgs e)
        //{
        //    ScrollViewer.Focus();
        //}

        public void SetFocus()
        {
            Grid.Focus();
        }

        private void Grid_OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            OnPreviewKeyUp(e);
        }

        private void Grid_OnMouseMove(object sender, MouseEventArgs e)
        {
            Window window = Window.GetWindow(this);

            var ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            if (e.LeftButton == MouseButtonState.Pressed && !ctrlPressed)
            {
                var canDrag = Image.Type == ImageType.Image
                    ? ScrollViewer is { ScrollableHeight: 0, ScrollableWidth: 0 }
                    : true;

                if (canDrag)
                {
                    DataObject dataObject = new DataObject();
                    dataObject.SetData(DataFormats.FileDrop, new[] { Image.Path });
                    dataObject.SetData("DTCustomDragSource", true);

                    DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Move | DragDropEffects.Copy);

                    window.Cursor = null;

                    e.Handled = true;
                }
            }
        }


        //private void ScrollViewer_OnMouseLeave(object sender, MouseEventArgs e)
        //{
        //    ScrollViewer.Cursor = Cursors.Arrow;
        //}

        //private void ScrollViewer_OnMouseDown(object sender, MouseButtonEventArgs e)
        //{
        //    ScrollViewer.Cursor = grabCursor;
        //}

        //private void ScrollViewer_OnMouseUp(object sender, MouseButtonEventArgs e)
        //{
        //    ScrollViewer.Cursor = handCursor;
        //}

        private void NavigatePrevious_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ServiceLocator.ThumbnailNavigationService.MovePrevious();
            e.Handled = true;
        }

        private void NavigateNext_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ServiceLocator.ThumbnailNavigationService.MoveNext();
            e.Handled = true;
        }

        private void Player_OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollDragger?.Close();

            ScrollViewer = null;
            Preview = null;
            _scrollDragger = null;

            Player = (MediaElement)sender;

            ApplyMuteState();

            isPlaying = true;
            Player?.Play();

            NotifyPlaybackStateChanged();
        }

        private void Player_OnMediaEnded(object sender, RoutedEventArgs e)
        {
            isEnded = true;
            isPlaying = false;
            if (ServiceLocator.Settings.LoopVideo)
            {
                Player?.Position = TimeSpan.FromMilliseconds(1);
                isEnded = false;
                isPlaying = true;
            }

            NotifyPlaybackStateChanged();
        }

        private void ScrollViewer_OnLoaded(object sender, RoutedEventArgs e)
        {
            StopOpenWatchdog();
            StopDecodedPlayback(forget: true);

            Player = null;

            // A still image replaced the video, so anything driving playback controls should stop
            NotifyPlaybackStateChanged();

            ScrollViewer = (ScrollViewer)sender;
            Preview = FindVisualChildren<System.Windows.Controls.Image>(ScrollViewer).First();

            _scrollDragger = new ScrollDragger(Preview, ScrollViewer, handCursor, grabCursor);

            // The image the scale applies to has only just been found. ResetView may have run
            // before this against the previous one - or against none at all, coming from a video -
            // so the scale has to be applied again now there is something to apply it to.
            ApplyViewScale();

            SetFocus();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private bool isPlaying = true;
        private bool isEnded = false;

        /// <summary>
        /// Static so muting survives closing and reopening the viewer, which builds a fresh window
        /// each time. There is only ever one popped out pane, and this is deliberately not
        /// persisted - it lasts the session and no longer.
        /// </summary>
        private static bool _userMuted;

        private void Player_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            // In the popped out viewer a click has to stay free for the double click that leaves
            // full screen, and playback is driven from the control bar and the space bar instead.
            if (IsPopout) return;

            TogglePlayPause();
            e.Handled = true;
        }

        public void Pause()
        {
            isPlaying = false;

            if (_decoded != null)
            {
                _decoded.Pause();
            }
            else
            {
                Player?.Pause();
            }

            NotifyPlaybackStateChanged();
        }

        /// <summary>
        /// Raised once the media element has a duration, so a host can size its seek bar.
        /// </summary>
        public event EventHandler? MediaOpened;

        /// <summary>
        /// True while a video is loaded. Still images don't create a player.
        /// </summary>
        public bool HasPlayer => Player != null || _decoded != null;

        public bool IsPlaying => isPlaying;

        /// <summary>
        /// Pushes the ratings bar to the top of the pane. The popped out viewer sets this so the
        /// bar doesn't sit on top of the video control bar.
        /// </summary>
        public static readonly DependencyProperty RatingsBarAlignmentProperty = DependencyProperty.Register(
            nameof(RatingsBarAlignment),
            typeof(VerticalAlignment),
            typeof(PreviewPane),
            new PropertyMetadata(VerticalAlignment.Bottom)
        );

        public VerticalAlignment RatingsBarAlignment
        {
            get => (VerticalAlignment)GetValue(RatingsBarAlignmentProperty);
            set => SetValue(RatingsBarAlignmentProperty, value);
        }

        /// <summary>
        /// Keeps the ratings bar clear of anything the host draws over the pane. The popped out
        /// viewer has a hit testable strip along the top for its own controls, which would
        /// otherwise swallow every click and hover meant for the bar.
        /// </summary>
        public static readonly DependencyProperty RatingsBarMarginProperty = DependencyProperty.Register(
            nameof(RatingsBarMargin),
            typeof(Thickness),
            typeof(PreviewPane),
            new PropertyMetadata(new Thickness(0))
        );

        public Thickness RatingsBarMargin
        {
            get => (Thickness)GetValue(RatingsBarMarginProperty);
            set => SetValue(RatingsBarMarginProperty, value);
        }

        /// <summary>
        /// The ratings bar follows the "show tags" toggle, and optionally gets out of the way while
        /// a video is actually playing so it doesn't sit over the picture.
        /// </summary>
        public bool IsRatingsBarVisible
        {
            get
            {
                if (MainModel is not { ShowTags: true }) return false;

                if (!ServiceLocator.ExtendedSettings.HideRatingsBarWhileVideoPlays) return true;

                return !(HasPlayer && isPlaying);
            }
        }

        private void NotifyPlaybackStateChanged()
        {
            OnPropertyChanged(nameof(HasPlayer));
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsRatingsBarVisible));
        }

        public TimeSpan Position
        {
            get => _decoded?.Position ?? Player?.Position ?? TimeSpan.Zero;
            set
            {
                if (_decoded != null)
                {
                    _decoded.Position = value;
                    isEnded = false;
                    return;
                }

                if (Player == null) return;

                Player.Position = value;
                isEnded = false;
            }
        }

        public TimeSpan Duration => _decoded != null
            ? _decoded.Duration
            : Player is { NaturalDuration.HasTimeSpan: true }
                ? Player.NaturalDuration.TimeSpan
                : TimeSpan.Zero;

        /// <summary>
        /// In the docked pane this follows the "mute side panel video" setting, so browsing the
        /// grid doesn't blast audio. In the popped out viewer it follows the mute button.
        /// </summary>
        public bool IsMuted
        {
            get => IsPopout ? _userMuted : ServiceLocator.ExtendedSettings.MuteSidePanelVideo;
            set
            {
                if (IsPopout)
                {
                    _userMuted = value;
                }
                else
                {
                    ServiceLocator.ExtendedSettings.MuteSidePanelVideo = value;
                }

                ApplyMuteState();

                OnPropertyChanged();
            }
        }

        public void ToggleMute()
        {
            IsMuted = !IsMuted;
        }

        public void TogglePlayPause()
        {
            if (_decoded != null)
            {
                if (_decoded.IsPlaying)
                {
                    _decoded.Pause();
                }
                else
                {
                    // Starts again from the beginning if it had played through
                    _decoded.Play();
                    isEnded = false;
                }

                isPlaying = _decoded.IsPlaying;

                NotifyPlaybackStateChanged();
                return;
            }

            if (Player == null) return;

            if (isPlaying)
            {
                Player.Pause();
            }
            else
            {
                if (isEnded)
                {
                    Player.Position = TimeSpan.FromMilliseconds(1);
                    isEnded = false;
                }
                Player.Play();
            }

            isPlaying = !isPlaying;

            NotifyPlaybackStateChanged();
        }

        private void ApplyMuteState()
        {
            if (Player == null) return;

            Player.IsMuted = IsMuted;
        }

        private void MediaElement_OnMediaOpened(object sender, RoutedEventArgs e)
        {
            StopOpenWatchdog();

            // Opened late, after the watchdog had already handed over to the decoder
            if (_decoded != null || _decodingPath != null)
            {
                Player?.Stop();
                return;
            }

            // Opened, but with no picture: the container was readable and the video in it was
            // not, which is what a missing codec looks like when the file also has sound
            if (Image is { Type: ImageType.Video } && Player is { HasVideo: false })
            {
                Logger.Log($"Windows opened {Image.Path} but could not decode its video; decoding it directly instead");
                StartDecodedPlayback();
                return;
            }

            if (Image.Type == ImageType.Video)
            {
                ApplyMuteState();

                if (IsPopout || !MainModel.IsPreviewOpen)
                {
                    isPlaying = true;
                    Player?.Play();
                }
                else if (!IsPopout && MainModel.IsPreviewOpen)
                {
                    isPlaying = true;
                    Player?.Play();
                    Task.Delay(10).ContinueWith((t) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            isPlaying = false;
                            Player?.Stop();

                            NotifyPlaybackStateChanged();
                        });
                    });
                }

                NotifyPlaybackStateChanged();
                OnPropertyChanged(nameof(IsMuted));

                MediaOpened?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Player_OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            StopOpenWatchdog();

            // Usually a missing codec - HEVC without the extension from the Store, AV1, 10-bit
            // or 4:4:4 H.264 - which Windows reports by refusing the file outright. This used to
            // go unhandled, leaving the pane black and the viewer with no controls at all.
            Logger.Log($"Windows could not play {Image?.Path}: {e.ErrorException?.Message}; decoding it directly instead");

            StartDecodedPlayback();
        }

        private void DecodedFrame_OnLoaded(object sender, RoutedEventArgs e)
        {
            _decodedFrame = (System.Windows.Controls.Image)sender;

            // Back on a page whose video was already found unplayable: no need to wait for
            // Windows to fail at it a second time
            if (_unplayablePath != null && string.Equals(_unplayablePath, Image?.Path, StringComparison.OrdinalIgnoreCase))
            {
                StartDecodedPlayback();
            }
        }

        private void WatchForOpen(ImageViewModel? model)
        {
            StopOpenWatchdog();

            if (model is not { Type: ImageType.Video }) return;

            _openWatchdog = new DispatcherTimer { Interval = OpenTimeout };

            _openWatchdog.Tick += (_, _) =>
            {
                StopOpenWatchdog();

                // Only for the video it was started for, and only if anyone can see the pane -
                // a hidden pane never gets its media element going in the first place
                if (!ReferenceEquals(Image, model) || !IsVisible || Player == null) return;

                // Opened after all, with the event already gone by
                if (Player.HasVideo || Player.NaturalDuration.HasTimeSpan) return;

                Logger.Log($"Windows did not open {model.Path} within {OpenTimeout.TotalSeconds:0}s; decoding it directly instead");

                StartDecodedPlayback();
            };

            _openWatchdog.Start();
        }

        private void StopOpenWatchdog()
        {
            _openWatchdog?.Stop();
            _openWatchdog = null;
        }

        /// <summary>
        /// Plays the current video from frames decoded by the app, in place of the media element.
        /// </summary>
        private async void StartDecodedPlayback()
        {
            var model = Image;
            var path = model?.Path;

            if (model is not { Type: ImageType.Video } || string.IsNullOrEmpty(path)) return;

            // The media element can report the same file more than once
            if (string.Equals(_decodingPath, path, StringComparison.OrdinalIgnoreCase)) return;

            StopDecodedPlayback(forget: true);

            _decodingPath = path;
            _unplayablePath = path;

            try
            {
                // Nothing to play there; stopping it keeps it from trying again
                Player?.Stop();
            }
            catch (Exception)
            {
                // A media element that failed to open can object to being stopped
            }

            // Opening probes the file, which takes a moment
            var source = await Task.Run(() => VideoFrameSource.Open(path));

            // Moved on to something else while that was happening
            if (!string.Equals(_decodingPath, path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Image?.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                source?.Dispose();
                return;
            }

            if (source == null || _decodedFrame == null)
            {
                source?.Dispose();
                _decodingPath = null;

                var current = Image ?? model;

                current.Message = GetLocalizedText("Preview.CannotPlay");
                current.IsMessageVisible = true;

                return;
            }

            _decoded = new DecodedVideoPlayer(source, _decodedFrame);

            _decoded.Ended += (_, _) =>
            {
                isEnded = true;
                isPlaying = false;

                NotifyPlaybackStateChanged();
            };

            if (Player != null)
            {
                Player.Visibility = Visibility.Collapsed;
            }

            _decodedFrame.Visibility = Visibility.Visible;

            // Starts the way a video Windows can play does: playing, unless this is the docked
            // pane while the viewer is open, where it shows the first frame and waits
            var autoPlay = IsPopout || !MainModel.IsPreviewOpen;

            isEnded = false;
            isPlaying = autoPlay;

            if (autoPlay)
            {
                _decoded.Play();
            }

            NotifyPlaybackStateChanged();

            // The viewer builds its control bar on this, the same as for any other video
            MediaOpened?.Invoke(this, EventArgs.Empty);
        }

        private static string GetLocalizedText(string key)
        {
            return global::Diffusion.Toolkit.Localization.JsonLocalizationProvider.Instance.GetLocalizedObject(key, null, System.Globalization.CultureInfo.InvariantCulture) as string ?? key;
        }

        /// <summary>
        /// Hands the pane back to the media element. <paramref name="forget"/> also drops the
        /// note that the current video is unplayable - wanted when moving to another video, not
        /// when the pane is only being unloaded for a while.
        /// </summary>
        private void StopDecodedPlayback(bool forget)
        {
            _decodingPath = null;

            if (forget)
            {
                _unplayablePath = null;
            }

            if (_decoded != null)
            {
                _decoded.Dispose();
                _decoded = null;
            }

            if (_decodedFrame != null)
            {
                _decodedFrame.Visibility = Visibility.Collapsed;
            }

            if (Player != null)
            {
                Player.Visibility = Visibility.Visible;
            }
        }
    }
}
