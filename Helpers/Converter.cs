using System;
using System.Globalization;
using System.Windows.Data;

namespace MMONavigator.Helpers;

public class CenterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double containerSize && parameter != null)
        {
            // Parse the element's size from the ConverterParameter
            if (double.TryParse(parameter.ToString(), out double elementSize))
            {
                return (containerSize - elementSize) / 2;
            }
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}