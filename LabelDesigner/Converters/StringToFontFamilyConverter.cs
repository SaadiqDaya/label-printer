using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LabelDesigner.Converters;

public class StringToFontFamilyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrWhiteSpace(name))
        {
            try { return new FontFamily(name); }
            catch { }
        }
        return new FontFamily("Arial");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is FontFamily ff ? ff.Source : "Arial";
}
