using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Diffusion.Toolkit.Themes;

namespace Diffusion.Toolkit.Converters;

/// <summary>
/// Turns the accent colour setting into a brush for the swatch beside the input. An empty or
/// malformed value resolves to the theme's own accent, which is what the application will actually
/// use, so the swatch always shows the colour that is in effect rather than going blank as the user
/// types a partial value.
/// </summary>
public class AccentColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = ThemeManager.ParseAccent(value as string);

        if (color.HasValue)
        {
            return new SolidColorBrush(color.Value);
        }

        return Application.Current?.TryFindResource("AccentBrush") as Brush
               ?? new SolidColorBrush(Colors.Cyan);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
