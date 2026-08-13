using Diffusion.Common;
using Diffusion.Common.Query;
using Diffusion.Database.Models;
using Diffusion.Toolkit.Configuration;
using Diffusion.Toolkit.Models;
using Diffusion.Toolkit.Services;
using Diffusion.Toolkit.Localization;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Diffusion.Toolkit.Controls
{
    /// <summary>
    /// Interaction logic for MetadataPanel.xaml
    /// </summary>
    public partial class MetadataPanel : UserControl
    {
        public static readonly DependencyProperty CurrentImageProperty = DependencyProperty.Register(
            nameof(CurrentImage),
            typeof(ImageViewModel),
            typeof(MetadataPanel),
            new PropertyMetadata(default(ImageEntry), PropertyChangedCallback)
        );

        private static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MetadataPanel panel)
            {
                panel.CurrentImage.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(ImageViewModel.ImageTags))
                    {
                        panel.UpdateFilter();
                    }
                };
                panel.UpdateFilter();
            }
        }

        public ImageViewModel CurrentImage
        {
            get => (ImageViewModel)GetValue(CurrentImageProperty);
            set => SetValue(CurrentImageProperty, value);
        }

        public static readonly DependencyProperty MetadataSectionProperty = DependencyProperty.Register(
            nameof(MetadataSection),
            typeof(MetadataSection),
            typeof(MetadataPanel),
            new PropertyMetadata(default(ImageEntry))
        );

        public MetadataSection MetadataSection
        {
            get => (MetadataSection)GetValue(MetadataSectionProperty);
            set => SetValue(MetadataSectionProperty, value);
        }

        /// <summary>
        /// Fork-only settings, exposed so the video section can bind its state and field toggles.
        /// Not a DependencyProperty because it is a process wide singleton rather than something
        /// a host hands in.
        /// </summary>
        public ExtendedSettings ExtendedSettings => ServiceLocator.ExtendedSettings;

        public static readonly DependencyProperty TextForegroundProperty = DependencyProperty.Register(
            nameof(TextForeground),
            typeof(Brush),
            typeof(MetadataPanel),
            new PropertyMetadata(Brushes.White)
        );

        /// <summary>
        /// Colour of the metadata text. The overlays sit on a dark backdrop whatever the theme, so
        /// they keep the default light text; the docked pane passes the theme's foreground brush.
        /// </summary>
        public Brush TextForeground
        {
            get => (Brush)GetValue(TextForegroundProperty);
            set => SetValue(TextForegroundProperty, value);
        }

        public MetadataPanel()
        {
            InitializeComponent();
        }

        private DispatcherTimer? _copiedPopupTimer;

        /// <summary>
        /// Confirms a copy next to the pointer. Runs alongside the button's own copy command, which
        /// also raises an application toast - but that toast is anchored to the main window and is
        /// hidden behind the full screen viewer, where this panel is most often used.
        /// </summary>
        private void Copy_OnClick(object sender, RoutedEventArgs e)
        {
            CopiedPopupText.Text = GetLocalizedText("Metadata.Buttons.Copied");

            // Reopening moves the popup to the current pointer position
            CopiedPopup.IsOpen = false;
            CopiedPopup.IsOpen = true;

            _copiedPopupTimer ??= CreateCopiedPopupTimer();

            _copiedPopupTimer.Stop();
            _copiedPopupTimer.Start();
        }

        private static string GetLocalizedText(string key)
        {
            return (string)JsonLocalizationProvider.Instance.GetLocalizedObject(key, null, CultureInfo.InvariantCulture);
        }

        private DispatcherTimer CreateCopiedPopupTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                CopiedPopup.IsOpen = false;
            };

            return timer;
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            SetMetadataState(AccordionState.Collapsed);
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            SetMetadataState(AccordionState.Expanded);
        }

        private void SetMetadataState(AccordionState state)
        {
            PromptMetadata.State = state;
            NegativePromptMetadata.State = state;
            SeedMetadata.State = state;
            SamplerMetadata.State = state;
            OtherMetadata.State = state;
            ModelMetadata.State = state;
            PathMetadata.State = state;
            AlbumMetadata.State = state;
            DateMetadata.State = state;
        }

        private void AlbumName_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            var album = ((Album)((TextBox)sender).DataContext);
            CurrentImage.OpenAlbumCommand?.Execute(album);
        }

        private void UIElement_OnGotFocus(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
        }

        private void AddTagButton_OnClick(object sender, RoutedEventArgs e)
        {
            AddTag();
        }

        private void AddTagText_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddTag();
            }
        }

        private void AddTag()
        {
            var tagName = AddTagText.Text.Trim();
            if (tagName.Length > 0)
            {
                ServiceLocator.DataStore.CreateTag(tagName);
                AddTagText.Text = "";
                CurrentImage.ImageTags = ServiceLocator.TagService.GetImageTagViews(CurrentImage.Id);
                _ = ServiceLocator.TagService.LoadTags();
                UpdateFilter();
            }
        }

        private void UpdateFilter()
        {
            if (CurrentImage.ImageTags == null)
            {
                CurrentImage.FilteredTags = null;
                return;
            }
            if (TagFilter.Text is { Length: > 0 })
            {
                var filter = TagFilter.Text.ToLower().Trim();

                CurrentImage.FilteredTags = CurrentImage.ImageTags.Where(d => d.Name.ToLower().Contains(filter)).ToList();
            }
            else
            {
                CurrentImage.FilteredTags = CurrentImage.ImageTags.ToList();
            }
        }

        private void ClearFilter_OnClick(object sender, RoutedEventArgs e)
        {
            TagFilter.Text = "";
            UpdateFilter();
        }

        private void TagFilter_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilter();
        }

        private void UIElement_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true; // Mark the event as handled to stop tunneling

            // Create a new MouseWheelEventArgs for the bubbling event
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };

            // Raise the new bubbling event on the ListView itself
            if (VisualTreeHelper.GetParent((FrameworkElement)sender) is UIElement parent)
            {
                parent.RaiseEvent(eventArg);
            }
        }

        private void TagFilter_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                TagFilter.Text = "";
                UpdateFilter();
            }
        }
    }
}
