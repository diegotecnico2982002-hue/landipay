using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Landipay.Theme.Conversores;

/// <summary>Visível quando o valor booleano é falso (oposto do BooleanToVisibilityConverter).</summary>
public sealed class VisibilidadeInversa : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
