using Meridian.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class MonthEventChip : UserControl
{
    public MonthEventChip()
    {
        InitializeComponent();
    }

    public void Apply(EventChipData data)
    {
        Root.Background = new SolidColorBrush(data.Color);
        var fg = new SolidColorBrush(data.TextColor ?? EventColorPicker.PickReadable(data.Color));
        TitleText.Foreground = fg;
        TimeText.Foreground = fg;
        TitleText.Text = data.Title;

        if (data.StartTime.HasValue)
        {
            TimeText.Text = data.StartTime.Value.ToString("HH:mm");
            TimeText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            TimeText.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    // Needed by DayCellControl.Refit() to probe chip height
    internal static MonthEventChip MakeProbe(Color color)
    {
        var chip = new MonthEventChip();
        chip.Apply(new EventChipData("x", color, null, null, true));
        return chip;
    }
}
