using System.Windows;
using System.Windows.Data;
using System.Globalization;

namespace MMONavigator;

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Invert the boolean value
            boolValue = !boolValue; 
            
            if (boolValue)
            {
                return Visibility.Visible;
            }
            else
            {
                // Default to Collapsed, can be adjusted to Hidden if needed
                return Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed; // Default value for non-boolean input
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Conversion from Visibility back to Boolean is generally not needed for visibility bindings
        throw new NotImplementedException();
    }
}