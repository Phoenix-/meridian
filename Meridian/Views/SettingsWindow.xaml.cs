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

    // Opens the settings window, or brings the existing one to the front.
    // UI-thread only: the single-instance check below isn't synchronized, and
    // every caller (the gear button) is already on the dispatcher.
    public static void ShowOrActivate()
    {
        _current ??= new SettingsWindow();
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
