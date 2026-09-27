using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Poe2StashPricer.App.ViewModels;

/// <summary>Picks one of two colours from a flag, so the rows can say what they mean without code-behind.</summary>
public class BoolInk : IValueConverter
{
    public string WhenTrue { get; set; } = "#FFFFFF";
    public string WhenFalse { get; set; } = "#FFFFFF";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new SolidColorBrush(Color.Parse(value is bool flag && flag ? WhenTrue : WhenFalse));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
