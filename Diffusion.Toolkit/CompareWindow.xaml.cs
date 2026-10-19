using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Diffusion.Common;
using Diffusion.Toolkit.Localization;
using Diffusion.Toolkit.Services;
using Diffusion.Video;
using FontAwesome.WPF;

namespace Diffusion.Toolkit
{
    /// <summary>
    /// Full screen A/B comparison of two images or videos: a wipe with a draggable divider, or a
    /// straight toggle between the two.
    /// </summary>
    /// <remarks>
    /// Videos run on one shared clock - both media timelines are children of a single parallel
    /// timeline - so they are frame locked by construction rather than nudged back into step,
    /// and pausing, seeking or stepping the parent moves both.
    ///
    /// Different lengths are handled by holding the last frame. The parent timeline lasts as long
    /// as the longer clip; the shorter one ends early and, with FillBehavior.HoldEnd, freezes on
    /// its final frame until the parent finishes. Then the parent repeats and both start again
    /// together. Every moment of the overlap is compared like for like, and neither clip drifts.
    /// </remarks>
    public partial class CompareWindow : Window
    {
        public sealed record Side(string Path, bool IsVideo);

        /// <summary>One side of the comparison and the layer that draws it.</summary>
        private sealed class Slot
        {
            public required Side Side { get; set; }
            public required Grid Layer { get; init; }
            public required Image Image { get; init; }
            public required MediaElement Video { get; init; }
            public TimeSpan? NaturalDuration { get; set; }
        }

        /// <summary>A, drawn underneath and shown on the left of the wipe.</summary>
        private Slot _left;

        /// <summary>B, drawn on top and clipped to the right of the divider.</summary>
        private Slot _right;

        private bool _wipe = true;
        private double _split = 0.5;
        private bool _showingB;
        private bool _dragging;
        private bool _muted;

        private ClockGroup? _clock;
        private List<Slot> _clockSlots = new();
        private TimeSpan _duration;
        private bool _durationConfirmed;

        /// <summary>
        /// The faster of the two frame rates. Stepping by the smaller interval means neither clip
        /// can skip a frame; the slower one simply repeats a frame now and then.
        /// </summary>
        private double _frameRate = 30;

        private DispatcherTimer? _ticker;
        private bool _scrubbing;
        private bool _updatingScrub;

        private bool _bottomBarShown;

        /// <summary>
        /// Hides the control bar a while after the pointer leaves it, as the viewer's does.
        /// </summary>
        private readonly DispatcherTimer _hideBottomBar = new() { Interval = TimeSpan.FromSeconds(2) };

        public CompareWindow(Side a, Side b)
        {
            InitializeComponent();

            _left = new Slot { Side = a, Layer = LayerA, Image = ImageA, Video = VideoA };
            _right = new Slot { Side = b, Layer = LayerB, Image = ImageB, Video = VideoB };

            // Below the divider, labels and transport, whichever side is on top
            Panel.SetZIndex(_left.Layer, -2);
            Panel.SetZIndex(_right.Layer, -1);

            _hideBottomBar.Tick += (_, _) =>
            {
                _hideBottomBar.Stop();
                SetBottomBarShown(false);
            };

            Loaded += OnLoaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // By now the window has been placed over its owner, so maximising fills that monitor
            WindowState = WindowState.Maximized;

            // The same width rule as the viewer's bar, so the two are the same size
            VideoBar.MaxWidth = PreviewWindow.GetControlBarMaxWidth(new WindowInteropHelper(this).Handle);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Activate();
            Focus();

            ShowNames();
            ApplyMode();

            await LoadImages();

            if (_left.Side.IsVideo || _right.Side.IsVideo)
            {
                await StartVideos();
            }
        }

        #region Loading

        private async Task LoadImages()
        {
            foreach (var slot in new[] { _left, _right })
            {
                if (slot.Side.IsVideo)
                {
                    slot.Image.Visibility = Visibility.Collapsed;
                    continue;
                }

                slot.Video.Visibility = Visibility.Collapsed;

                var path = slot.Side.Path;

                var bitmap = await Task.Run(() => TryLoadImage(path));

                if (bitmap == null)
                {
                    ReportFailure(slot);
                    continue;
                }

                slot.Image.Source = bitmap;
            }
        }

        private static BitmapSource? TryLoadImage(string path)
        {
            try
            {
                return Pages.Search.GetBitmapImage(path);
            }
            catch (Exception e)
            {
                Logger.Log($"Compare could not load {path}: {e.Message}");
                return null;
            }
        }

        private void ReportFailure(Slot slot)
        {
            var label = slot == _left ? NameA : NameB;

            label.Text = $"{System.IO.Path.GetFileName(slot.Side.Path)} - {GetLocalizedText("Compare.LoadFailed")}";
        }

        #endregion

        #region Video

        private async Task StartVideos()
        {
            _clockSlots = new[] { _left, _right }.Where(s => s.Side.IsVideo).ToList();

            foreach (var slot in new[] { _left, _right }.Where(s => !s.Side.IsVideo))
            {
                slot.Video.Visibility = Visibility.Collapsed;
            }

            // Read up front so the shared clock can be sized before anything plays. The media
            // player's own durations arrive later and correct this if it was off
            var paths = _clockSlots.Select(s => s.Side.Path).ToList();

            var infos = await Task.Run(() => paths.Select(TryReadInfo).ToList());

            _duration = infos.Select(i => i?.Duration ?? TimeSpan.Zero).DefaultIfEmpty(TimeSpan.Zero).Max();

            var rates = infos.Select(i => i?.FrameRate ?? 0).Where(r => r > 0).ToList();

            if (rates.Count > 0)
            {
                _frameRate = rates.Max();
            }

            BuildClock(TimeSpan.Zero, paused: false);

            BottomBar.Visibility = Visibility.Visible;

            UpdateLoopIcon();

            _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            _ticker.Tick += (_, _) => UpdateTransport();
            _ticker.Start();

            UpdateAudio();
        }

        private static VideoInfo? TryReadInfo(string path)
        {
            try
            {
                return VideoInfoReader.Read(path);
            }
            catch (Exception e)
            {
                Logger.Log($"Compare could not read video details for {path}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// (Re)creates the shared clock and hands each video its child of it.
        /// </summary>
        private void BuildClock(TimeSpan position, bool paused)
        {
            StopClock();

            // Follows the viewer's loop setting. Off, the pair plays through once and stops with
            // both on their last frames
            var group = new ParallelTimeline
            {
                RepeatBehavior = ServiceLocator.Settings?.LoopVideo == false
                    ? new RepeatBehavior(1)
                    : RepeatBehavior.Forever
            };

            // Sized to the longer clip. Left automatic only when nothing could be read, in which
            // case the timeline works it out from the media once they open
            if (_duration > TimeSpan.Zero)
            {
                group.Duration = new Duration(_duration);
            }

            foreach (var slot in _clockSlots)
            {
                group.Children.Add(new MediaTimeline(new Uri(slot.Side.Path))
                {
                    // The shorter clip freezes on its last frame until the longer one finishes
                    FillBehavior = FillBehavior.HoldEnd
                });
            }

            _clock = group.CreateClock();

            for (var i = 0; i < _clockSlots.Count; i++)
            {
                _clockSlots[i].Video.Clock = (MediaClock)_clock.Children[i];
            }

            // Shrinking the range can coerce the value, which would otherwise read as a seek
            _updatingScrub = true;
            Scrub.Maximum = Math.Max(_duration.TotalSeconds, 0.001);
            _updatingScrub = false;

            var controller = _clock.Controller!;

            controller.Begin();

            // Queued rather than aligned to the last tick: interactive calls are applied in order
            // on the next tick, and an aligned seek would land before the Begin above took effect
            if (position > TimeSpan.Zero)
            {
                controller.Seek(position, TimeSeekOrigin.BeginTime);
            }

            if (paused)
            {
                controller.Pause();
            }

            UpdatePlayIcon();
            UpdateAudio();
        }

        private void StopClock()
        {
            if (_clock != null)
            {
                _clock.Controller?.Stop();
            }

            foreach (var slot in new[] { _left, _right })
            {
                slot.Video.Clock = null;
            }

            _clock = null;
        }

        /// <summary>
        /// The duration read up front comes from frame counts and can be a little off. Once both
        /// clips have opened their real lengths are known, and if the longer one disagrees by more
        /// than a frame the clock is rebuilt at the same place - otherwise the end of the longer
        /// clip would be cut, or the hold at the end run long.
        /// </summary>
        private void Video_OnMediaOpened(object sender, RoutedEventArgs e)
        {
            var slot = _clockSlots.FirstOrDefault(s => s.Video == sender);

            if (slot == null) return;

            if (slot.Video.NaturalDuration.HasTimeSpan)
            {
                slot.NaturalDuration = slot.Video.NaturalDuration.TimeSpan;
            }

            if (_durationConfirmed || _clockSlots.Any(s => s.NaturalDuration == null)) return;

            _durationConfirmed = true;

            var actual = _clockSlots.Max(s => s.NaturalDuration!.Value);

            var frame = TimeSpan.FromSeconds(1.0 / _frameRate);

            if ((actual - _duration).Duration() <= frame) return;

            var position = CurrentPosition();
            var paused = _clock?.IsPaused == true;

            _duration = actual;

            BuildClock(position, paused);
        }

        private void Video_OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            var slot = _clockSlots.FirstOrDefault(s => s.Video == sender);

            Logger.Log($"Compare could not play {slot?.Side.Path}: {e.ErrorException?.Message}");

            if (slot != null)
            {
                ReportFailure(slot);
            }
        }

        private TimeSpan CurrentPosition() => _clock?.CurrentTime ?? TimeSpan.Zero;

        /// <summary>Played through to the end with looping off, and holding there.</summary>
        private bool HasEnded() => _clock?.CurrentState == ClockState.Filling;

        private void TogglePlay()
        {
            if (_clock?.Controller == null) return;

            if (HasEnded())
            {
                // Played through with looping off - play again from the start, as the viewer does
                _clock.Controller.Begin();
            }
            else if (_clock.IsPaused)
            {
                _clock.Controller.Resume();
            }
            else
            {
                _clock.Controller.Pause();
            }

            UpdatePlayIcon();
        }

        /// <summary>
        /// Moves both clips together by whole frames. Pauses first: stepping only means anything
        /// with the picture standing still.
        /// </summary>
        private void Step(int frames)
        {
            if (_clock?.Controller == null) return;

            if (!_clock.IsPaused)
            {
                _clock.Controller.Pause();
                UpdatePlayIcon();
            }

            var frame = TimeSpan.FromSeconds(1.0 / _frameRate);

            var target = CurrentPosition() + TimeSpan.FromTicks(frame.Ticks * frames);

            SeekTo(target);
        }

        private void SeekTo(TimeSpan target)
        {
            if (_clock?.Controller == null) return;

            var frame = TimeSpan.FromSeconds(1.0 / _frameRate);

            if (target < TimeSpan.Zero) target = TimeSpan.Zero;

            // Short of the very end, which would roll over into the next repeat
            if (_duration > frame && target > _duration - frame) target = _duration - frame;

            // Applied immediately rather than on the next tick, so stepping with a held key reads
            // back the position it just set rather than a stale one
            _clock.Controller.SeekAlignedToLastTick(target, TimeSeekOrigin.BeginTime);

            UpdateTransport();
        }

        private void UpdateTransport()
        {
            if (_clock == null) return;

            var position = CurrentPosition();

            if (!_scrubbing)
            {
                _updatingScrub = true;
                Scrub.Value = Math.Min(position.TotalSeconds, Scrub.Maximum);
                _updatingScrub = false;
            }

            // Written the way the viewer writes it
            TimeText.Text = $"{PreviewModel.FormatTime(position.TotalSeconds)} / {PreviewModel.FormatTime(_duration.TotalSeconds)}";

            // Reaching the end with looping off stops playback without anyone pressing anything
            UpdatePlayIcon();
        }

        private void UpdatePlayIcon()
        {
            var playing = _clock != null && !_clock.IsPaused && !HasEnded();

            PlayPauseIcon.Icon = playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        }

        private void UpdateLoopIcon()
        {
            LoopIcon.Opacity = ServiceLocator.Settings?.LoopVideo == false ? 0.35 : 1.0;
        }

        private void UpdateMuteIcon()
        {
            MuteIcon.Icon = _muted ? FontAwesomeIcon.VolumeOff : FontAwesomeIcon.VolumeUp;
            MuteIcon.Opacity = _muted ? 0.5 : 1.0;
        }

        /// <summary>
        /// Flips the same setting as the viewer's loop button, then rebuilds the clock in place so
        /// the change takes effect without losing the position.
        /// </summary>
        private void Loop_OnClick(object sender, RoutedEventArgs e)
        {
            if (ServiceLocator.Settings == null) return;

            ServiceLocator.Settings.LoopVideo = !ServiceLocator.Settings.LoopVideo;

            UpdateLoopIcon();

            if (_clock != null)
            {
                BuildClock(CurrentPosition(), _clock.IsPaused || HasEnded());
            }
        }

        private void Mute_OnClick(object sender, RoutedEventArgs e)
        {
            _muted = !_muted;
            UpdateAudio();
        }

        private void SetBottomBarShown(bool shown)
        {
            if (_bottomBarShown == shown) return;

            _bottomBarShown = shown;

            // The viewer's slide: the same distance, the same time
            BottomBarOffset.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(shown ? 80 : 0, shown ? 0 : 80, TimeSpan.FromSeconds(0.1)));
        }

        private void BottomBar_OnMouseEnter(object sender, MouseEventArgs e)
        {
            _hideBottomBar.Stop();
            SetBottomBarShown(true);
        }

        private void BottomBar_OnMouseLeave(object sender, MouseEventArgs e)
        {
            _hideBottomBar.Stop();
            _hideBottomBar.Start();
        }

        /// <summary>
        /// Reveals the bar when the pointer comes near the bottom edge, over the same distance as
        /// the viewer.
        /// </summary>
        private void Window_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_clock == null) return;

            if (e.GetPosition(this).Y >= ActualHeight - PreviewWindow.BottomHoverZone)
            {
                SetBottomBarShown(true);
            }
        }

        private void PlayPause_OnClick(object sender, RoutedEventArgs e) => TogglePlay();

        private void Scrub_OnDragStarted(object sender, DragStartedEventArgs e) => _scrubbing = true;

        private void Scrub_OnDragCompleted(object sender, DragCompletedEventArgs e) => _scrubbing = false;

        private void Scrub_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingScrub) return;

            SeekTo(TimeSpan.FromSeconds(e.NewValue));
        }

        /// <summary>Clicks on the transport bar must not also move the divider underneath it.</summary>
        private void VideoBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        #endregion

        #region Wipe and toggle

        private void ApplyMode()
        {
            if (_wipe)
            {
                _left.Layer.Opacity = 1;
                _right.Layer.Opacity = 1;

                Overlay.Visibility = Visibility.Visible;

                LabelA.Opacity = 1;
                LabelB.Opacity = 1;

                UpdateWipe();
            }
            else
            {
                _right.Layer.Clip = null;

                // Opacity rather than visibility, so a hidden video keeps playing in step instead
                // of the media pipeline having anything to catch up on when it comes back
                _left.Layer.Opacity = _showingB ? 0 : 1;
                _right.Layer.Opacity = _showingB ? 1 : 0;

                Overlay.Visibility = Visibility.Collapsed;

                LabelA.Opacity = _showingB ? 0.35 : 1;
                LabelB.Opacity = _showingB ? 1 : 0.35;
            }

            UpdateAudio();
        }

        private void UpdateWipe()
        {
            if (!_wipe) return;

            var width = Root.ActualWidth;
            var height = Root.ActualHeight;

            if (width <= 0 || height <= 0) return;

            var x = _split * width;

            _right.Layer.Clip = new RectangleGeometry(new Rect(x, 0, Math.Max(0, width - x), height));

            Divider.Height = height;
            Canvas.SetLeft(Divider, x - Divider.Width / 2);

            Canvas.SetLeft(Handle, x - Handle.Width / 2);
            Canvas.SetTop(Handle, height / 2 - Handle.Height / 2);
        }

        private void SetSplit(double x)
        {
            if (Root.ActualWidth <= 0) return;

            _split = Math.Clamp(x / Root.ActualWidth, 0, 1);

            UpdateWipe();
        }

        /// <summary>Exchanges which file is A and which is B, without reloading either.</summary>
        private void Swap()
        {
            _left.Layer.Clip = null;
            _right.Layer.Clip = null;

            (_left, _right) = (_right, _left);

            Panel.SetZIndex(_left.Layer, -2);
            Panel.SetZIndex(_right.Layer, -1);

            ShowNames();
            ApplyMode();
        }

        private void ShowNames()
        {
            NameA.Text = System.IO.Path.GetFileName(_left.Side.Path);
            NameB.Text = System.IO.Path.GetFileName(_right.Side.Path);
        }

        /// <summary>
        /// Two soundtracks at once is noise, so only one is heard: whichever is showing when
        /// toggling, and A during a wipe.
        /// </summary>
        private void UpdateAudio()
        {
            var volume = ServiceLocator.Settings?.Volume ?? 1.0f;

            var hearB = !_wipe && _showingB;

            _left.Video.Volume = volume;
            _right.Video.Volume = volume;

            _left.Video.IsMuted = _muted || hearB;
            _right.Video.IsMuted = _muted || !hearB;

            UpdateMuteIcon();
        }

        private void Root_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateWipe();

        private void Root_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_wipe)
            {
                _dragging = true;
                Root.CaptureMouse();
                SetSplit(e.GetPosition(Root).X);
            }
            else
            {
                _showingB = !_showingB;
                ApplyMode();
            }

            e.Handled = true;
        }

        private void Root_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                SetSplit(e.GetPosition(Root).X);
            }
        }

        private void Root_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;

            _dragging = false;
            Root.ReleaseMouseCapture();
        }

        #endregion

        private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var hasVideo = _clock != null;

            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    break;

                case Key.W:
                    _wipe = !_wipe;
                    ApplyMode();
                    break;

                case Key.Tab:
                    _wipe = false;
                    _showingB = !_showingB;
                    ApplyMode();
                    break;

                case Key.A:
                    _wipe = false;
                    _showingB = false;
                    ApplyMode();
                    break;

                case Key.B:
                    _wipe = false;
                    _showingB = true;
                    ApplyMode();
                    break;

                case Key.S:
                    Swap();
                    break;

                case Key.Space when hasVideo:
                    TogglePlay();
                    break;

                case Key.Left when hasVideo:
                    Step(-1);
                    break;

                case Key.Right when hasVideo:
                    Step(1);
                    break;

                // Without video the arrows nudge the divider instead
                case Key.Left when _wipe:
                    _split = Math.Clamp(_split - 0.02, 0, 1);
                    UpdateWipe();
                    break;

                case Key.Right when _wipe:
                    _split = Math.Clamp(_split + 0.02, 0, 1);
                    UpdateWipe();
                    break;

                case Key.Home when hasVideo:
                    SeekTo(TimeSpan.Zero);
                    break;

                case Key.M:
                    _muted = !_muted;
                    UpdateAudio();
                    break;

                case Key.H:
                    Help.Visibility = Help.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                    break;

                default:
                    return;
            }

            e.Handled = true;
        }

        private void Window_OnClosed(object? sender, EventArgs e)
        {
            _ticker?.Stop();
            _hideBottomBar.Stop();

            // Releases the files - a media element otherwise keeps them open
            StopClock();
        }

        private static string GetLocalizedText(string key)
        {
            return JsonLocalizationProvider.Instance.GetLocalizedObject(key, null, System.Globalization.CultureInfo.InvariantCulture) as string ?? key;
        }
    }
}
