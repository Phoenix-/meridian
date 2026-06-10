using Meridian.Models;
using Meridian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace Meridian.Views;

public sealed partial class MonthEventChip : UserControl
{
    private CalendarEvent? _event;

    public MonthEventChip()
    {
        InitializeComponent();
        Tapped += OnTapped;
        RightTapped += OnRightTapped;
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
            TimeText.Visibility = Visibility.Visible;
        }
        else
        {
            TimeText.Visibility = Visibility.Collapsed;
        }

        _event = data.Source;
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
        ShowContextMenu(this, _event, e.GetPosition(this));
        e.Handled = true;
    }

    internal static void ShowContextMenu(FrameworkElement target, CalendarEvent ev, Windows.Foundation.Point position)
    {
        var menu = new MenuFlyout();

        AddRsvpItems(menu, ev);

        if (!string.IsNullOrEmpty(ev.HtmlLink))
        {
            var openItem = new MenuFlyoutItem { Text = "Открыть в Google Calendar" };
            openItem.Click += async (_, _) =>
            {
                var url = ev.HtmlLink!;
                if (!string.IsNullOrEmpty(ev.AccountEmail))
                {
                    var sep = url.Contains('?') ? '&' : '?';
                    url = $"{url}{sep}authuser={System.Uri.EscapeDataString(ev.AccountEmail)}";
                }
                if (System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
                    await Windows.System.Launcher.LaunchUriAsync(uri);
            };
            menu.Items.Add(openItem);

            var copyLink = new MenuFlyoutItem { Text = "Копировать ссылку" };
            copyLink.Click += (_, _) =>
            {
                var dp = new DataPackage();
                dp.SetText(ev.HtmlLink!);
                Clipboard.SetContent(dp);
            };
            menu.Items.Add(copyLink);
        }

        var copyTitle = new MenuFlyoutItem { Text = "Копировать заголовок" };
        copyTitle.Click += (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(ev.Title);
            Clipboard.SetContent(dp);
        };
        menu.Items.Add(copyTitle);

        menu.XamlRoot = target.XamlRoot;
        menu.ShowAt(target, position);
    }

    // Prepends Yes/No/Maybe RSVP items (with a check on the current choice) plus
    // a separator, but only when the user can respond — i.e. they're an attendee
    // on a writable calendar and the feature is wired. Mirrors the flyout's
    // affordance for a quicker right-click path. The cache handles the optimistic
    // update + PATCH + reconcile, so here we just fire and forget.
    private static void AddRsvpItems(MenuFlyout menu, CalendarEvent ev)
    {
        if (EventActions.CanRespond is not { } canRespond
            || EventActions.Respond is not { } respond
            || !canRespond(ev))
            return;

        var current = EventActions.SelfAttendee(ev)?.ResponseStatus;

        menu.Items.Add(MakeRsvpItem("Принять", EventActions.Accepted, current, ev, respond));
        menu.Items.Add(MakeRsvpItem("Отклонить", EventActions.Declined, current, ev, respond));
        menu.Items.Add(MakeRsvpItem("Возможно", EventActions.Tentative, current, ev, respond));
        menu.Items.Add(new MenuFlyoutSeparator());
    }

    private static ToggleMenuFlyoutItem MakeRsvpItem(
        string text, string status, string? current,
        CalendarEvent ev, Func<CalendarEvent, string, System.Threading.Tasks.Task<bool>> respond)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = current == status };
        item.Click += (_, _) =>
        {
            // Clicking the active choice is a no-op; the cache short-circuits it.
            if (current == status) return;
            _ = respond(ev, status);
        };
        return item;
    }

    // Needed by DayCellControl.Refit() to probe chip height
    internal static MonthEventChip MakeProbe(Color color)
    {
        var chip = new MonthEventChip();
        chip.Apply(new EventChipData("x", color, null, null, true));
        return chip;
    }
}
