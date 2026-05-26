using Meridian.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class WeekEventBlock : UserControl
{
    private CalendarEvent? _event;

    public WeekEventBlock()
    {
        InitializeComponent();
        Tapped += OnTapped;
        RightTapped += OnRightTapped;
    }

    public void Apply(CalendarEvent ev, Color color, Color? textColor, double height)
    {
        _event = ev;

        Root.Background = new SolidColorBrush(color);
        Root.BorderBrush = (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];

        var fg = textColor ?? EventColorPicker.PickReadable(color);
        var fgBrush = new SolidColorBrush(fg);
        var fgDim = new SolidColorBrush(Color.FromArgb(200, fg.R, fg.G, fg.B));
        TitleText.Foreground = fgBrush;

        var startStr = ev.Start.ToString("HH:mm");
        var timeRange = $"{startStr}–{ev.End:HH:mm}";

        // Decide layout: two lines (title + time) or one line (title, HH:mm)
        var measureTitle = new TextBlock { Text = ev.Title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
        var measureTime  = new TextBlock { Text = timeRange, FontSize = 10 };
        measureTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        measureTime.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        const double vertPadding = 4; // Padding top + bottom
        bool twoLines = height >= measureTitle.DesiredSize.Height + measureTime.DesiredSize.Height + vertPadding;

        if (twoLines)
        {
            TitleText.Text = ev.Title;
            TimeText.Text = timeRange;
            TimeText.Foreground = fgDim;
            TimeText.Visibility = Visibility.Visible;
        }
        else
        {
            TitleText.Text = $"{ev.Title}, {startStr}";
            TimeText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_event is null) return;
        EventDetailsFlyout.Show(this, _event);
        e.Handled = true;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_event is null) return;
        MonthEventChip.ShowContextMenu(this, _event, e.GetPosition(this));
        e.Handled = true;
    }
}
