using System;
using System.Globalization;
using System.Windows.Data;

namespace MMONavigator.Helpers;

public class HalfValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            // Optionally pass a parameter to subtract an offset (e.g., "-40")
            if (parameter != null && double.TryParse(parameter.ToString(), out double offset))
            {
                return (d / 2) + offset;
            }
            return d / 2;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}