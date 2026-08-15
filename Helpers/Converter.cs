using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MMONavigator.Helpers;

public class CenterConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        try {
            // Guard against WPF UnsetValue or null during layout initialization
            if (value == null || value == DependencyProperty.UnsetValue) {
                return 0.0;
            }

            if (value is double containerSize && parameter != null) {
                // Guard against invalid geometry double states
                if (double.IsNaN(containerSize) || double.IsInfinity(containerSize)) {
                    return 0.0;
                }

                string? paramString = parameter.ToString();
                if (string.IsNullOrWhiteSpace(paramString)) {
                    return 0.0;
                }

                if (double.TryParse(paramString, NumberStyles.Float, CultureInfo.InvariantCulture, out double elementSize)) {
                    if (double.IsNaN(elementSize) || double.IsInfinity(elementSize)) {
                        return 0.0;
                    }

                    double centeredOffset = (containerSize - elementSize) / 2.0;
                    return centeredOffset;
                }

                Log.Warning("CenterConverter: Failed to parse ConverterParameter '{Parameter}' as a valid double.", parameter);
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected error in CenterConverter during conversion.");
        }

        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        // Return Binding.DoNothing for one-way bindings to avoid throwing WPF binding exceptions
        return System.Windows.Data.Binding.DoNothing;
    }
}