using Microsoft.UI.Xaml.Data;

namespace Meridian.Converters;

public partial class NotNullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string s ? !string.IsNullOrEmpty(s) : value != null;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
