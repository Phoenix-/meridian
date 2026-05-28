using Meridian.Models;
using Meridian.Theme;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Meridian.Views;

/// <summary>
/// Represents one day in the month grid.
/// Visual parts (DateCircle, BodyPanel) are placed into the parent WeekRowControl's
/// grid: DateCircle in the date row, BodyPanel in the body (Star) row, sharing one column.
/// BodyPanel is a 2-row grid: a transparent band-reserve spacer on top (its height is set
/// by the parent so this day's chips land below its own multi-day bands), then the chip
/// stack filling the rest. The visible bands themselves are drawn by the parent as an
/// overlay; the spacer only reserves vertical space.
/// </summary>
public sealed partial class DayCellControl : UserControl
{
    public readonly Border DateCircle;
    public readonly Grid BodyPanel;
    public readonly StackPanel ChipsStack;

    private readonly Border _bandReserve;
    private readonly TextBlock _dateText;
    private List<MonthEventChip> _chips = [];
    private TextBlock? _overflowLabel;
    private int _totalCount;
    private int _baseHiddenCount;   // multi-day bands covering this day that the parent hid
    private double _lastFitHeight = -1;
    private DateTime _date;

    /// Invoked when the user taps "+N ещё" — opens the full day. Set by the parent
    /// week row; the cell only ever references up (no static subscription → no leak).
    public Action<DateTime>? OnOverflowTap;

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

        // Transparent spacer reserving this day's own band-block height (set by the parent
        // via SetBandReserve). Height 0 by default → chips sit right under the date circle.
        _bandReserve = new Border { Height = 0 };

        BodyPanel = new Grid();
        BodyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BodyPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_bandReserve, 0);
        Grid.SetRow(ChipsStack, 1);
        BodyPanel.Children.Add(_bandReserve);
        BodyPanel.Children.Add(ChipsStack);

        // ChipsStack sits in the Star row, so its ActualHeight is the space left after the
        // band reserve — exactly the budget Refit needs.
        ChipsStack.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Height > 0) Refit();
        };
        ChipsStack.Loaded += (_, _) =>
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Refit);
    }

    /// Called by the parent week row once the cross-day band fit is known: reserves
    /// <paramref name="reserveHeight"/> px above the chips for this day's visible bands,
    /// and seeds the "+N ещё" count with bands the parent decided to hide for this day.
    public void SetBandReserve(double reserveHeight, int baseHiddenCount)
    {
        _baseHiddenCount = baseHiddenCount;
        if (_bandReserve.Height != reserveHeight)
            _bandReserve.Height = reserveHeight;   // triggers a re-layout → ChipsStack.SizeChanged → Refit
        _lastFitHeight = -1;
        Refit();
    }

    public void SetDate(DateTime date, bool isCurrentMonth, bool isToday)
    {
        _date = date;
        _dateText.Text = date.Day.ToString();

        if (isToday)
        {
            DateCircle.Background = new SolidColorBrush(AppColors.Accent);
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

    public void SetItems(IReadOnlyList<EventChipData> items)
    {
        ChipsStack.Children.Clear();
        _chips = new List<MonthEventChip>(items.Count);
        _totalCount = items.Count;
        _lastFitHeight = -1;

        foreach (var data in items)
        {
            var chip = new MonthEventChip();
            chip.Apply(data);
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
        _overflowLabel.Tapped += (_, e) =>
        {
            OnOverflowTap?.Invoke(_date);
            e.Handled = true;
        };
        ChipsStack.Children.Add(_overflowLabel);
    }

    private void Refit()
    {
        if (_overflowLabel == null) return;

        double available = ChipsStack.ActualHeight - ChipsStack.Margin.Top - ChipsStack.Margin.Bottom;
        if (available <= 0)
        {
            // Not laid out yet — show all chips, SizeChanged will refit when height arrives
            foreach (var chip in _chips) chip.Visibility = Visibility.Visible;
            if (_overflowLabel != null) _overflowLabel.Visibility = Visibility.Collapsed;
            return;
        }
        if (available == _lastFitHeight) return;
        _lastFitHeight = available;

        var inf = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);

        var probeChip = MonthEventChip.MakeProbe(Colors.Transparent);
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
        // An overflow line is needed if some chips won't fit OR the parent already hid
        // multi-day bands for this day — in both cases reserve a row for "+N ещё".
        bool needOverflowLine = shown < _totalCount || _baseHiddenCount > 0;
        if (needOverflowLine)
            shown = Math.Max(0, (int)((available - overflowHeight) / chipHeight));
        else
            shown = _totalCount;

        for (int i = 0; i < _chips.Count; i++)
            _chips[i].Visibility = i < shown ? Visibility.Visible : Visibility.Collapsed;

        // "+N ещё" = hidden bands (decided by the parent) + chips that didn't fit.
        int overflow = _baseHiddenCount + (_totalCount - shown);
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
}
