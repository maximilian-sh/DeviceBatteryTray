using System;
using System.Globalization;
using System.Windows.Data;

namespace LGSTrayUI;

public class BoolToInverseBoolConverter : IValueConverter
{
    public static readonly BoolToInverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

