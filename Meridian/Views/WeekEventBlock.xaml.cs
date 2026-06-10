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

        var fg = textColor ?? EventColorPicker.PickReadable(color);
        var status = RsvpChipStyle.StatusFor(ev);

        // RSVP look (fill/border + text foreground + strikethrough). For the
        // filled/muted states this paints both text blocks in `fg`; we re-dim
        // the time line just below. For needsAction it themes the text and the
        // border becomes the shared pulse — leave those as the helper set them.
        RsvpChipStyle.Apply(status, Root, color, fg, TitleText, TimeText);

        // Keep the 2px separator border in the filled/muted states (needsAction
        // owns the border for its outline, so don't stomp it there).
        if (!RsvpChipStyle.IsNeedsAction(status))
        {
            Root.BorderThickness = new Thickness(2);
            Root.BorderBrush = (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        }

        var fgDim = new SolidColorBrush(Color.FromArgb(200, fg.R, fg.G, fg.B));

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
            // For needsAction the helper themed the time text for contrast over
            // the cell background — don't override it with the event-color dim.
            if (!RsvpChipStyle.IsNeedsAction(status))
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
