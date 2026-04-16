using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FastEdit.Infrastructure.Converters;

public class StringToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try { return new BrushConverter().ConvertFromString(hex) as Brush; }
            catch { return null; }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class FileNameToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string fileName && !string.IsNullOrEmpty(fileName))
            return FastEdit.Helpers.FileIconHelper.GetIcon(fileName);
        return "\uE8A5";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
