using System;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Diffusion.Toolkit.Converters;

public class MultiBoolToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType,
        object parameter, System.Globalization.CultureInfo culture)
    {return values.Select(d => d is bool ? d : false).Cast<bool>().Aggregate(true, (a, b) =>  a && b) ? Visibility.Visible : Visibility.Hidden;
    }
    public object[] ConvertBack(object value, Type[] targetTypes,
        object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException("Cannot convert back");
    }
}


public class MultiBoolToVisibilityCollapsedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType,
        object parameter, System.Globalization.CultureInfo culture)
    {
        return values.Select(d => d is bool ? d : false).Cast<bool>().Aggregate(true, (a, b) => a && b) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object[] ConvertBack(object value, Type[] targetTypes,
        object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException("Cannot convert back");
    }
}

/// <summary>
/// Visible when any of the bound booleans is true. Used where an element should appear if either
/// of two independent conditions holds, such as a search or a filter being active.
/// </summary>
public class AnyBoolToVisibilityCollapsedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType,
        object parameter, System.Globalization.CultureInfo culture)
    {
        return values.Any(d => d is true) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes,
        object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException("Cannot convert back");
    }
}
