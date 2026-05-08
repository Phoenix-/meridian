using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class WeekEventBlock : UserControl
{
    public WeekEventBlock()
    {
        InitializeComponent();
    }

    public void Apply(string title, DateTime start, DateTime end, Color color, double height)
    {
        Root.Background = new SolidColorBrush(color);
        Root.BorderBrush = (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];

        var startStr = start.ToString("HH:mm");
        var timeRange = $"{startStr}–{end:HH:mm}";

        // Decide layout: two lines (title + time) or one line (title, HH:mm)
        var measureTitle = new TextBlock { Text = title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
        var measureTime  = new TextBlock { Text = timeRange, FontSize = 10 };
        measureTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        measureTime.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        const double vertPadding = 4; // Padding top + bottom
        bool twoLines = height >= measureTitle.DesiredSize.Height + measureTime.DesiredSize.Height + vertPadding;

        if (twoLines)
        {
            TitleText.Text = title;
            TimeText.Text = timeRange;
            TimeText.Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            TimeText.Visibility = Visibility.Visible;
        }
        else
        {
            TitleText.Text = $"{title}, {startStr}";
            TimeText.Visibility = Visibility.Collapsed;
        }
    }
}
