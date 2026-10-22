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
using Diffusion.Common;
using Diffusion.Toolkit.Localization;
using Diffusion.Toolkit.Services;
using FontAwesome.WPF;

namespace Diffusion.Toolkit
{
    /// <summary>
    /// Full screen A/B comparison of two images or videos: a wipe with a draggable divider, or a
    /// straight toggle between the two.
    /// </summary>
    /// <remarks>
    /// Videos are not played, they are decoded - each on its own thread, by frame index - and
    /// shown against one playhead. On every screen refresh the playhead says which frame each
    /// side should be on, and the pair is shown only when both of those frames are ready. If
    /// either side falls behind, the playhead waits for it rather than letting the other run on,
    /// so the two are never a frame apart. A media player cannot promise that: each one runs its
    /// own decoder on its own clock, and a shared timeline only keeps the clocks together, not
    /// the pictures.
    ///
    /// Different lengths are handled by holding the last frame. The playhead runs to the end of
    /// the longer clip; the shorter one stays on its final frame until then. Then both start
    /// again together. Different frame rates are lined up by time: each side shows whichever of
    /// its frames covers the playhead's moment.
    ///
    /// Sound, where there is any, comes from a media player kept close to the playhead. It is
    /// only ever heard from one side, so it does not need to be locked to the frame.
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

            /// <summary>The decoded frames, for a video that could be opened.</summary>
            public CompareVideoSource? Video { get; set; }

            /// <summary>The sound, for a video that has any.</summary>
            public MediaPlayer? Audio { get; set; }

            public bool AudioPlaying { get; set; }

            /// <summary>The frame index on screen, or -1 before the first.</summary>
            public int Shown { get; set; } = -1;

            public bool FailureReported { get; set; }
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

        /// <summary>The sides with a video that is decoding, A and B in either order.</summary>
        private List<Slot> _videoSlots = new();

        /// <summary>
        /// The playhead, in seconds. Only moved once both sides have the frames for where it
        /// moves to.
        /// </summary>
        private double _position;

        private bool _playing;

        /// <summary>Played through to the end with looping off, and holding there.</summary>
        private bool _ended;

        private readonly System.Diagnostics.Stopwatch _stopwatch = new();
        private TimeSpan _lastTick;
        private bool _rendering;

        /// <summary>
        /// The faster of the two frame rates. Stepping by the smaller interval means neither clip
        /// can skip a frame; the slower one simply repeats a frame now and then.
        /// </summary>
        private double _frameRate = 30;

        private bool _scrubbing;
        private bool _updatingScrub;

        private bool _bottomBarShown;

        public CompareWindow(Side a, Side b)
        {
            InitializeComponent();

            _left = new Slot { Side = a, Layer = LayerA, Image = ImageA };
            _right = new Slot { Side = b, Layer = LayerB, Image = ImageB };

            // Below the divider, labels and transport, whichever side is on top
            Panel.SetZIndex(_left.Layer, -2);
            Panel.SetZIndex(_right.Layer, -1);

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
                // Video frames arrive in the same image element once decoding starts
                if (slot.Side.IsVideo) continue;

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
            var slots = new[] { _left, _right }.Where(s => s.Side.IsVideo).ToList();

            // Opening probes the file, which can take a moment; the decoding itself then runs on
            // each source's own thread
            var sources = await Task.Run(() => slots.Select(s => CompareVideoSource.Open(s.Side.Path)).ToList());

            if (_closed)
            {
                foreach (var source in sources) source?.Dispose();
                return;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                if (sources[i] == null)
                {
                    ReportFailure(slots[i]);
                    continue;
                }

                slots[i].Video = sources[i];
                _videoSlots.Add(slots[i]);

                OpenAudio(slots[i]);
            }

            if (_videoSlots.Count == 0) return;

            _frameRate = _videoSlots.Max(s => s.Video!.FrameRate);

            BottomBar.Visibility = Visibility.Visible;

            UpdateLoopIcon();

            _playing = true;

            _stopwatch.Start();
            _lastTick = _stopwatch.Elapsed;

            // Once per screen refresh, which is as often as a new pair of frames could be shown
            CompositionTarget.Rendering += OnRendering;
            _rendering = true;

            UpdateAudio();
        }

        /// <summary>
        /// A player for the sound only. Kept only if there is sound to play - most generated
        /// clips are silent, and an idle player would still be decoding the picture for nothing.
        /// </summary>
        private void OpenAudio(Slot slot)
        {
            var player = new MediaPlayer();

            player.MediaOpened += (_, _) =>
            {
                if (_closed || !player.HasAudio)
                {
                    player.Close();
                    return;
                }

                slot.Audio = player;
                UpdateAudio();
            };

            player.MediaFailed += (_, args) =>
            {
                Logger.Log($"Compare could not play the sound of {slot.Side.Path}: {args.ErrorException?.Message}");
                player.Close();
            };

            player.Open(new Uri(slot.Side.Path));
        }

        /// <summary>How long the longer clip runs, as far as is known yet.</summary>
        private double Duration()
        {
            return _videoSlots.Count == 0 ? 0 : _videoSlots.Max(s => s.Video!.DurationSeconds);
        }

        private bool IsLooping => ServiceLocator.Settings?.LoopVideo != false;

        /// <summary>
        /// The frame one side shows at a moment: whichever of its frames covers it, or its last
        /// once it has run out.
        /// </summary>
        private static int FrameAt(Slot slot, double seconds)
        {
            var video = slot.Video!;

            var count = video.FrameCount;

            if (count <= 0) return 0;

            // The nudge keeps a moment that is exactly on a frame boundary, give or take
            // floating point, on the frame that starts there rather than the one before
            var index = (int)Math.Floor(seconds * video.FrameRate + 1e-6);

            return Math.Clamp(index, 0, count - 1);
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = _stopwatch.Elapsed;
            var elapsed = (now - _lastTick).TotalSeconds;

            _lastTick = now;

            var duration = Duration();

            var target = _position;
            var reachedEnd = false;

            if (_playing)
            {
                target += elapsed;

                if (duration > 0 && target >= duration)
                {
                    if (IsLooping)
                    {
                        target %= duration;
                    }
                    else
                    {
                        target = duration;
                        reachedEnd = true;
                    }
                }
            }

            // Moves only when both sides can show where it moves to. Otherwise the time is let
            // go - the playhead waits for the slower decoder rather than leaving it behind
            if (TryShow(target))
            {
                _position = target;

                if (reachedEnd)
                {
                    _playing = false;
                    _ended = true;
                }
            }

            SyncAudio();
            UpdateTransport(duration);
        }

        /// <summary>
        /// Puts both sides on their frames for a moment, together or not at all.
        /// </summary>
        private bool TryShow(double seconds)
        {
            var ready = true;
            var frames = new List<(Slot Slot, int Index, BitmapSource? Frame)>(_videoSlots.Count);

            foreach (var slot in _videoSlots)
            {
                var video = slot.Video!;

                // A side that cannot be decoded would otherwise hold the other up forever
                if (video.Failed)
                {
                    if (!slot.FailureReported)
                    {
                        slot.FailureReported = true;
                        ReportFailure(slot);
                    }

                    continue;
                }

                var index = FrameAt(slot, seconds);

                if (index == slot.Shown)
                {
                    video.Want(index);
                    continue;
                }

                var frame = video.Get(index);

                if (frame == null)
                {
                    ready = false;
                    continue;
                }

                frames.Add((slot, index, frame));
            }

            if (!ready) return false;

            foreach (var (slot, index, frame) in frames)
            {
                slot.Image.Source = frame;
                slot.Shown = index;
            }

            return true;
        }

        /// <summary>
        /// Keeps the sound with the picture. Nudged back only when it has wandered further than
        /// can be heard, since every correction is a small skip.
        /// </summary>
        private void SyncAudio()
        {
            const double tolerance = 0.15;

            foreach (var slot in _videoSlots)
            {
                if (slot.Audio is not { } audio) continue;

                var shouldPlay = _playing && _position < slot.Video!.DurationSeconds;

                if (!shouldPlay)
                {
                    if (slot.AudioPlaying)
                    {
                        audio.Pause();
                        slot.AudioPlaying = false;
                    }

                    continue;
                }

                var at = TimeSpan.FromSeconds(_position);

                if (!slot.AudioPlaying)
                {
                    audio.Position = at;
                    audio.Play();
                    slot.AudioPlaying = true;
                }
                else if (Math.Abs((audio.Position - at).TotalSeconds) > tolerance)
                {
                    audio.Position = at;
                }
            }
        }

        private void TogglePlay()
        {
            if (_videoSlots.Count == 0) return;

            if (_ended)
            {
                // Played through with looping off - play again from the start, as the viewer does
                _ended = false;
                _position = 0;
                _playing = true;
            }
            else
            {
                _playing = !_playing;
            }

            UpdatePlayIcon();
        }

        /// <summary>
        /// Moves both clips together by whole frames. Pauses first: stepping only means anything
        /// with the picture standing still.
        /// </summary>
        private void Step(int frames)
        {
            if (_videoSlots.Count == 0) return;

            _playing = false;

            // From the start of the frame on screen, so a step lands on a frame boundary of the
            // faster clip every time instead of carrying a fraction along
            var current = (int)Math.Floor(_position * _frameRate + 1e-6);

            SeekTo((current + frames) / _frameRate);
        }

        private void SeekTo(double seconds)
        {
            if (_videoSlots.Count == 0) return;

            var duration = Duration();

            // The start of the longer clip's last frame at the latest; the end itself would roll
            // straight over into the next loop
            var last = Math.Max(0, duration - 1.0 / _frameRate);

            _position = Math.Clamp(seconds, 0, last);
            _ended = false;

            // Shown on the next refresh, once both frames are in. Asking now gets both decoders
            // started on them straight away
            foreach (var slot in _videoSlots)
            {
                slot.Video!.Want(FrameAt(slot, _position));

                if (slot.Audio != null && slot.AudioPlaying)
                {
                    slot.Audio.Position = TimeSpan.FromSeconds(_position);
                }
            }

            UpdateTransport(duration);
        }

        private void UpdateTransport(double duration)
        {
            if (_videoSlots.Count == 0) return;

            _updatingScrub = true;

            // The length firms up as decoding finds where each clip really ends
            var maximum = Math.Max(duration, 0.001);

            if (Math.Abs(Scrub.Maximum - maximum) > 1e-6)
            {
                Scrub.Maximum = maximum;
            }

            if (!_scrubbing)
            {
                Scrub.Value = Math.Min(_position, Scrub.Maximum);
            }

            _updatingScrub = false;

            // Written the way the viewer writes it
            TimeText.Text = $"{PreviewModel.FormatTime(_position)} / {PreviewModel.FormatTime(duration)}";

            // Reaching the end with looping off stops playback without anyone pressing anything
            UpdatePlayIcon();
        }

        private void UpdatePlayIcon()
        {
            var playing = _playing && !_ended;

            PlayPauseIcon.Icon = playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        }

        private void UpdateLoopIcon()
        {
            LoopIcon.Opacity = ServiceLocator.Settings?.LoopVideo == false ? 0.35 : 1.0;
        }

        private void UpdateMuteIcon()
        {
            MuteIcon.IsMuted = _muted;
        }

        /// <summary>
        /// Flips the same setting as the viewer's loop button. Read afresh on every refresh, so it
        /// takes effect at the next end without disturbing anything now.
        /// </summary>
        private void Loop_OnClick(object sender, RoutedEventArgs e)
        {
            if (ServiceLocator.Settings == null) return;

            ServiceLocator.Settings.LoopVideo = !ServiceLocator.Settings.LoopVideo;

            UpdateLoopIcon();
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
            SetBottomBarShown(true);
        }

        private void BottomBar_OnMouseLeave(object sender, MouseEventArgs e)
        {
            // Leaving the window altogether raises no further mouse moves, so this is the last
            // chance to put the bar away
            UpdateBottomBar(e.GetPosition(this));
        }

        /// <summary>
        /// Shows the bar while the pointer is near the bottom edge and hides it the moment it is
        /// not, over the same strip as the viewer. A drag on the seek bar keeps it up.
        /// </summary>
        private void UpdateBottomBar(Point position)
        {
            SetBottomBarShown(_videoSlots.Count > 0 && (_scrubbing || PreviewWindow.IsInBottomHoverZone(this, position)));
        }

        private void Window_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            UpdateBottomBar(e.GetPosition(this));
        }

        private void PlayPause_OnClick(object sender, RoutedEventArgs e) => TogglePlay();

        private void Scrub_OnDragStarted(object sender, DragStartedEventArgs e) => _scrubbing = true;

        private void Scrub_OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            _scrubbing = false;

            // The drag held the bar up; let go away from it and it goes
            UpdateBottomBar(Mouse.GetPosition(this));
        }

        private void Scrub_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingScrub) return;

            SeekTo(e.NewValue);
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

                // Opacity rather than visibility, so the side out of view keeps its frame and
                // comes back without a flash of nothing
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

            if (_left.Audio != null)
            {
                _left.Audio.Volume = volume;
                _left.Audio.IsMuted = _muted || hearB;
            }

            if (_right.Audio != null)
            {
                _right.Audio.Volume = volume;
                _right.Audio.IsMuted = _muted || !hearB;
            }

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
            var hasVideo = _videoSlots.Count > 0;

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
                    SeekTo(0);
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
            _closed = true;

            if (_rendering)
            {
                CompositionTarget.Rendering -= OnRendering;
                _rendering = false;
            }

            // Releases the files and the decoded frames, which for a short clip is all of them
            foreach (var slot in new[] { _left, _right })
            {
                slot.Video?.Dispose();
                slot.Video = null;

                slot.Audio?.Close();
                slot.Audio = null;
            }

            _videoSlots.Clear();
        }

        /// <summary>Set once the window has gone, for loading that finishes after it.</summary>
        private bool _closed;

        private static string GetLocalizedText(string key)
        {
            return JsonLocalizationProvider.Instance.GetLocalizedObject(key, null, System.Globalization.CultureInfo.InvariantCulture) as string ?? key;
        }
    }
}
