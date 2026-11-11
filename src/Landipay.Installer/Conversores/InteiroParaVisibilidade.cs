using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Landipay.Installer.Conversores;

/// <summary>Visível quando o índice do passo atual é igual ao parâmetro (ex.: "2").</summary>
public sealed class InteiroParaVisibilidade : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var atual = value is int i ? i : -1;
        var alvo = int.TryParse(parameter?.ToString(), out var p) ? p : -1;
        return atual == alvo ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Verdadeiro → Collapsed, falso → Visible. Para "mostrar enquanto ainda não terminou".</summary>
public sealed class BoolParaVisibilidadeInversa : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Texto preenchido → Visible.</summary>
public sealed class TextoParaVisibilidade : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
