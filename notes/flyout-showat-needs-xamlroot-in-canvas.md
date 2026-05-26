# Flyout/MenuFlyout.ShowAt throws ArgumentException for targets inside a Canvas

## Trap

`flyout.ShowAt(target)` (and `menu.ShowAt(target, position)`) throws

```
System.ArgumentException: 'Value does not fall within the expected range.'
  at ABI...IFlyoutBaseMethods.ShowAt(...)
  at Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAt(FrameworkElement)
```

when the placement `target` is an element positioned inside a `Canvas`
(our event blocks live on `DayCanvas` in Day/Week views). The flyout never
appears — the very first click on an event blew up here.

## Fix

Set the flyout's `XamlRoot` explicitly from the target before showing, and
use the `FlyoutShowOptions` overload:

```csharp
var flyout = new Flyout { Content = content, XamlRoot = target.XamlRoot };
flyout.ShowAt(target, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Auto });
```

Same for `MenuFlyout`: `menu.XamlRoot = target.XamlRoot;` before
`menu.ShowAt(target, position)`.

- [Meridian/Views/EventDetailsFlyout.xaml.cs](../Meridian/Views/EventDetailsFlyout.xaml.cs) → `Show(...)`
- [Meridian/Views/MonthEventChip.xaml.cs](../Meridian/Views/MonthEventChip.xaml.cs) → `ShowContextMenu(...)`

## Rule of thumb

For elements that aren't part of the normal layout tree (anything placed in
a `Canvas`, or otherwise positioned manually), don't trust `ShowAt` to infer
the `XamlRoot` — set it from `target.XamlRoot`. If `target.XamlRoot` could be
null (element not yet in the tree), fall back to the window's root.
