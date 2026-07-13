using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FemBoy_Account_Manager.Converters;

public class InvertedBoolToVisibilityConverter : IValueConverter
{
    // Shows the element when value is FALSE (used for the "invalid cookie" red dot)
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        return b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}