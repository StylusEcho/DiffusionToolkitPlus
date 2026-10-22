using System.Windows;
using System.Windows.Controls;

namespace Diffusion.Toolkit.Controls
{
    /// <summary>
    /// The speaker on a video control bar's mute button: sound waves when audible, a cross when
    /// muted. Shared by the full screen viewer and the compare view so the two bars match.
    /// </summary>
    public partial class VolumeIcon : UserControl
    {
        public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
            nameof(IsMuted), typeof(bool), typeof(VolumeIcon),
            new PropertyMetadata(false, (d, e) => ((VolumeIcon)d).UpdateState()));

        public bool IsMuted
        {
            get => (bool)GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        public VolumeIcon()
        {
            InitializeComponent();
            UpdateState();
        }

        private void UpdateState()
        {
            Waves.Visibility = IsMuted ? Visibility.Collapsed : Visibility.Visible;
            Cross.Visibility = IsMuted ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
