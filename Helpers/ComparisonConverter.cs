using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MMONavigator.Helpers;

public class ComparisonConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
        try {
            if (values == null || values.Length < 3) {
                return false;
            }

            // values[0] = Raw Canvas Height
            // values[1] = Current Zoom Level (1.0, 0.5, etc)
            // values[2] = ScrollViewer Height
            // Guard against WPF UnsetValue during binding initialization phases
            if (values[0] == DependencyProperty.UnsetValue ||
                values[1] == DependencyProperty.UnsetValue ||
                values[2] == DependencyProperty.UnsetValue) {
                return false;
            }

            if (values[0] is double rawHeight &&
                values[1] is double zoom &&
                values[2] is double viewHeight) {

                // Guard against invalid geometry double states
                if (double.IsNaN(rawHeight) || double.IsNaN(zoom) || double.IsNaN(viewHeight) ||
                    double.IsInfinity(rawHeight) || double.IsInfinity(zoom) || double.IsInfinity(viewHeight)) {
                    return false;
                }

                double scaledMapHeight = rawHeight * zoom;
                bool isSmaller = scaledMapHeight < viewHeight;

                Log.Verbose("ComparisonConverter: ScaledMapHeight ({Scaled}) < ViewHeight ({View}) = {Result}", 
                    scaledMapHeight, viewHeight, isSmaller);

                return isSmaller;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing ComparisonConverter.");
        }

        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
        // Return null for one-way multi-bindings to satisfy WPF engine cleanly
        return Array.Empty<object>();
    }
}