using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Meridian.Views;

// Shared title-bar caption-button theming for windows that extend content into
// the title bar over an acrylic backdrop. When ExtendsContentIntoTitleBar is
// on, the min/max/close glyphs are NOT auto-themed — without this they go
// nearly invisible in light mode (grey glyphs on a light backdrop).
//
// Both MainWindow and SettingsWindow share this so the two can't drift apart.
// Call ApplyCaptionButtonColors on construction and again from the content
// root's ActualThemeChanged.
internal static class TitleBarTheming
{
    public static void ApplyCaptionButtonColors(Window window)
    {
        var theme = (window.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;
        var tb = window.AppWindow.TitleBar;

        // Backdrop is acrylic — keep the bands transparent so it shows through.
        tb.ButtonBackgroundColor         = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;

        var fg         = ResolveThemeBrushColor("WindowCaptionForeground",         theme);
        var fgInactive = ResolveThemeBrushColor("WindowCaptionForegroundDisabled", theme);
        var hoverBg    = ResolveThemeBrushColor("WindowCaptionButtonBackgroundPointerOver", theme);
        var pressedBg  = ResolveThemeBrushColor("WindowCaptionButtonBackgroundPressed",     theme);

        tb.ButtonForegroundColor         = fg;
        tb.ButtonHoverForegroundColor    = fg;
        tb.ButtonPressedForegroundColor  = fg;
        tb.ButtonInactiveForegroundColor = fgInactive;

        tb.ButtonHoverBackgroundColor    = hoverBg;
        tb.ButtonPressedBackgroundColor  = pressedBg;
    }

    // Application.Resources.ThemeDictionaries holds Light/Dark/HighContrast variants
    // that ThemeResource lookup would pick from. Code-behind lookup via Resources[key]
    // does not follow Window.ActualTheme, so resolve the dictionary explicitly.
    private static Color ResolveThemeBrushColor(string key, ElementTheme theme)
    {
        var dicts = Application.Current.Resources.ThemeDictionaries;
        var themeKey = theme == ElementTheme.Light ? "Light" : "Default"; // Default == Dark in WinUI
        if (dicts.TryGetValue(themeKey, out var d) && d is ResourceDictionary rd && rd[key] is SolidColorBrush b)
            return b.Color;
        // Fallback to whatever Resources[key] resolves to — at worst, the Default theme.
        if (Application.Current.Resources[key] is SolidColorBrush fallback)
            return fallback.Color;
        return theme == ElementTheme.Light ? Colors.Black : Colors.White;
    }
}
