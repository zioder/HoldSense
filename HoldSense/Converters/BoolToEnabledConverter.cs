using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace _HoldSense.Converters;

public class BoolToEnabledConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool enabled)
        {
            return enabled ? "Enabled" : "Disabled";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}






