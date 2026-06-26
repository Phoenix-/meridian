using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Meridian.Services;
using Meridian.ViewModels;

namespace Meridian.Views;

// The settings window. Single-instance: ShowOrActivate() reuses the open
// window instead of stacking a second one, mirroring how a typical app's
// Settings entry behaves.
//
// NavigationView hosts the sections. Only "Уведомления" exists today; the
// SelectionChanged switch is the seam where future sections plug in — each
// gets a panel and a case here, no structural change.
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _current;

    public SettingsViewModel ViewModel { get; } = new();

    private SettingsWindow()
    {
        InitializeComponent();

        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Packaged (MSIX) builds register their notification identity through the
        // package manifest, so the "register in notifications" toggle is a no-op
        // there (ToastSetup.EnsureRegistered short-circuits for IsPackaged). Hide
        // the row entirely — it only matters for unpackaged dev builds that may
        // want to bow out and leave the field to the installed app.
        if (AppPaths.IsPackaged)
            RegisterForNotificationsRow.Visibility = Visibility.Collapsed;

        // Caption-button glyphs aren't auto-themed under ExtendsContentIntoTitleBar;
        // theme them now and on every OS light/dark switch (shared with MainWindow).
        TitleBarTheming.ApplyCaptionButtonColors(this);
        if (Content is FrameworkElement fe)
            fe.ActualThemeChanged += (_, _) => TitleBarTheming.ApplyCaptionButtonColors(this);

        // A settings dialog doesn't need to be calendar-sized. Set a floor
        // first so the initial Resize isn't clamped below it, then size to a
        // comfortable default. PreferredMinimum* (WinAppSDK 1.7+) stops the
        // user squeezing the window narrow enough to collapse the text column
        // into a one-letter-per-line ribbon. Values are in DIPs.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Floor wide enough that the 200-DIP nav pane plus the content row
            // both fit: text column MinWidth 180 + its 16 right-margin + the
            // (label-less, MinWidth=0) ToggleSwitch ~44 + the ScrollViewer's
            // 24+24 padding ≈ 488. Round up to 500 for a little breathing room.
            presenter.PreferredMinimumWidth = 500;
            presenter.PreferredMinimumHeight = 400;
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 560));

        Closed += (_, _) =>
        {
            ViewModel.Dispose();
            _current = null;
        };
    }

    // Centers this window over the owner. Without this the OS drops the window
    // at some default spot — jarring on a multi-monitor setup where it can land
    // on a different screen than the one the user is working on. We only do this
    // at creation time (see ShowOrActivate): once it's open, the user may have
    // dragged it aside, and re-activating shouldn't yank it back.
    private void CenterOver(Window owner)
    {
        var ownerArea = owner.AppWindow;
        var size = AppWindow.Size;

        var x = ownerArea.Position.X + (ownerArea.Size.Width - size.Width) / 2;
        var y = ownerArea.Position.Y + (ownerArea.Size.Height - size.Height) / 2;

        // Clamp to the owner's display work area so the centered window can't
        // spill off-screen when the main window sits near a monitor edge.
        var work = DisplayArea.GetFromWindowId(ownerArea.Id, DisplayAreaFallback.Nearest).WorkArea;
        x = System.Math.Clamp(x, work.X, work.X + work.Width - size.Width);
        y = System.Math.Clamp(y, work.Y, work.Y + work.Height - size.Height);

        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    // Opens the settings window, or brings the existing one to the front.
    // UI-thread only: the single-instance check below isn't synchronized, and
    // every caller (the gear button) is already on the dispatcher.
    public static void ShowOrActivate(Window owner)
    {
        if (_current is null)
        {
            _current = new SettingsWindow();
            // Center over the owner before the first Activate so it appears in
            // place — no visible jump from a default spot to its real position.
            _current.CenterOver(owner);
        }
        _current.Activate();
    }

    // Closes the settings window if it's open. Called when the main window
    // closes so settings can't outlive it (the two are independent top-level
    // windows; without this, the process would stay alive on a now-orphaned
    // settings window after the user "quit" via the main window). Close()
    // triggers our own Closed handler, which clears _current and disposes the VM.
    public static void CloseIfOpen() => _current?.Close();

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Single section today; the tag switch is here so adding a section is
        // a localized change (new panel + new case) rather than a rewrite.
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "developer":
                NotificationsPanel.Visibility = Visibility.Collapsed;
                DeveloperPanel.Visibility = Visibility.Visible;
                break;
            case "notifications":
            default:
                DeveloperPanel.Visibility = Visibility.Collapsed;
                NotificationsPanel.Visibility = Visibility.Visible;
                break;
        }
    }
}
