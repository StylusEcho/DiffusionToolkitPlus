using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Diffusion.Toolkit.Converters;

/// <summary>
/// Collapses an element when its bound value carries no information - null, blank text, zero, or
/// an empty collection. Used to keep empty rows out of the metadata pane.
/// </summary>
public class HasValueVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return HasValue(value) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool HasValue(object? value)
    {
        switch (value)
        {
            case null:
                return false;
            case string text:
                return !string.IsNullOrWhiteSpace(text);
            case ICollection collection:
                return collection.Count > 0;
            case int number:
                return number != 0;
            case long number:
                return number != 0;
            case double number:
                return number != 0;
            case float number:
                return number != 0;
            case decimal number:
                return number != 0;
            default:
                return true;
        }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
