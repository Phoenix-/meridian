using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Meridian.Views;

/// <summary>
/// Represents one day in the month grid.
/// Visual parts (DateCircle, ChipsStack) are placed into the parent WeekRowControl's
/// grid via AddToGrid() so they can occupy separate rows while sharing the same column.
/// </summary>
public sealed partial class DayCellControl : UserControl
{
    public readonly Border DateCircle;
    public readonly StackPanel ChipsStack;

    private readonly TextBlock _dateText;
    private List<Border> _chips = [];
    private TextBlock? _overflowLabel;
    private int _totalCount;
    private double _lastFitHeight = -1;

    public DayCellControl()
    {
        InitializeComponent();

        _dateText = new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        DateCircle = new Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 2, 2, 0),
            Child = _dateText,
        };

        ChipsStack = new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(2, 1, 2, 2),
        };

        ChipsStack.SizeChanged += (_, _) => Refit();
        ChipsStack.Loaded += (_, _) =>
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Refit);
    }

    public void SetDate(DateTime date, bool isCurrentMonth, bool isToday)
    {
        _dateText.Text = date.Day.ToString();

        if (isToday)
        {
            DateCircle.Background = new SolidColorBrush(Color.FromArgb(255, 26, 115, 232));
            _dateText.Foreground = new SolidColorBrush(Colors.White);
        }
        else
        {
            DateCircle.Background = new SolidColorBrush(Colors.Transparent);
            _dateText.Foreground = isCurrentMonth
                ? (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"]
                : (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumLowBrush"];
        }
    }

    public void SetItems(IReadOnlyList<(string title, Color color)> items)
    {
        ChipsStack.Children.Clear();
        _chips = new List<Border>(items.Count);
        _totalCount = items.Count;
        _lastFitHeight = -1;

        foreach (var (title, color) in items)
        {
            var chip = MakeChip(title, color);
            _chips.Add(chip);
            ChipsStack.Children.Add(chip);
        }

        _overflowLabel = new TextBlock
        {
            Text = "+0 ещё",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            Margin = new Thickness(2, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        ChipsStack.Children.Add(_overflowLabel);
    }

    private void Refit()
    {
        if (_overflowLabel == null) return;

        double available = ChipsStack.ActualHeight - ChipsStack.Margin.Top - ChipsStack.Margin.Bottom;
        if (available <= 0) return;
        if (available == _lastFitHeight) return;
        _lastFitHeight = available;

        var inf = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);

        var probeChip = MakeChip("x", Colors.Transparent);
        probeChip.Measure(inf);
        double chipHeight = probeChip.DesiredSize.Height + ChipsStack.Spacing;

        var probeOverflow = new TextBlock { Text = "+0 ещё", FontSize = 11, Margin = new Thickness(2, 0, 0, 0) };
        probeOverflow.Measure(inf);
        double overflowHeight = probeOverflow.DesiredSize.Height + ChipsStack.Spacing;

        if (chipHeight <= 0)
        {
            foreach (var chip in _chips) chip.Visibility = Visibility.Collapsed;
            _overflowLabel.Visibility = Visibility.Collapsed;
            return;
        }

        int shown = (int)(available / chipHeight);
        if (shown < _totalCount)
            shown = Math.Max(0, (int)((available - overflowHeight) / chipHeight));
        else
            shown = _totalCount;

        for (int i = 0; i < _chips.Count; i++)
            _chips[i].Visibility = i < shown ? Visibility.Visible : Visibility.Collapsed;

        int overflow = _totalCount - shown;
        if (overflow > 0)
        {
            _overflowLabel.Text = $"+{overflow} ещё";
            _overflowLabel.Visibility = Visibility.Visible;
        }
        else
        {
            _overflowLabel.Visibility = Visibility.Collapsed;
        }
    }

    internal static Border MakeChip(string title, Color color)
    {
        return new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = title,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            },
        };
    }
}
