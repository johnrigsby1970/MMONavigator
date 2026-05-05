using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;

namespace MMONavigator.Helpers;

public class ComparisonConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return false;
        Debug.WriteLine($"Comparing map {values[0]} and viewer {values[1]}");
        // values[0] = Raw Canvas Height
        // values[1] = Current Zoom Level (1.0, 0.5, etc)
        // values[2] = ScrollViewer Height
        if (values[0] is double rawHeight && 
            values[1] is double zoom && 
            values[2] is double viewHeight)
        {
            double scaledMapHeight = rawHeight * zoom;
            return scaledMapHeight < viewHeight;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }

    public object[] ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}