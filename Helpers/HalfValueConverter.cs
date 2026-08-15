using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MMONavigator.Helpers;

public class HalfValueConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        try {
            // Guard against null or WPF UnsetValue during binding/layout initialization
            if (value == null || value == DependencyProperty.UnsetValue) {
                return 0.0;
            }

            if (value is double d) {
                // Guard against invalid geometry double states
                if (double.IsNaN(d) || double.IsInfinity(d)) {
                    return 0.0;
                }

                double half = d / 2.0;

                // Optionally pass a parameter to apply an offset (e.g., "-40")
                if (parameter != null) {
                    string? paramString = parameter.ToString();
                    if (!string.IsNullOrWhiteSpace(paramString)) {
                        if (double.TryParse(paramString, NumberStyles.Float, CultureInfo.InvariantCulture, out double offset)) {
                            if (!double.IsNaN(offset) && !double.IsInfinity(offset)) {
                                return half + offset;
                            }
                        }
                        else {
                            Log.Warning("HalfValueConverter: Failed to parse ConverterParameter '{Parameter}' as double.", parameter);
                        }
                    }
                }

                return half;
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Unexpected exception in HalfValueConverter during conversion.");
        }

        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        // Return Binding.DoNothing for one-way bindings to prevent WPF binding trace exceptions
        return System.Windows.Data.Binding.DoNothing;
    }
}