using Microsoft.UI.Xaml.Data;

namespace Meridian.Converters;

public class TimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTime dt ? dt.ToString("HH:mm") : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
