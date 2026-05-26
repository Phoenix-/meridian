# There is no built-in WinRT HTML DOM parser for arbitrary strings

## Trap

When rendering Google event descriptions (a small HTML subset: `<b>`,
`<strong>`, `<i>`, `<em>`, `<u>`, `<a href>`, `<br>`, `<p>`), the obvious
move is to parse the HTML into a DOM and walk it. `Windows.Data.Html` looks
like it should do this — but it only exposes `HtmlUtilities.ConvertToText()`,
which returns **plain text** and throws away all formatting. There is no
instantiable `HtmlDocument` / `IHtmlElement` / node tree you can load an
arbitrary string into. (The only real DOM lives inside `WebView2`, which is
far too heavy for a popover and conflicts with NativeAOT goals.)

NuGet `HtmlAgilityPack` is a real parser but uses reflection in places — risky
under NativeAOT, so it was rejected.

## Fix

Hand-rolled tokenizer that HTML-decodes once (Google sometimes double-encodes
markup as `&lt;b&gt;`), then walks tags maintaining a style flag stack and
emits XAML inlines (`Bold`/`Italic`/`Underline`/`Hyperlink`/`Run`). Bare URLs
are auto-linked. AOT-safe, zero dependencies.

- [Meridian/Views/EventDetailsFlyout.xaml.cs](../Meridian/Views/EventDetailsFlyout.xaml.cs) → `BuildDescription` / `EmitText` / `StyleRun`

## Rule of thumb

Need to turn HTML into formatted XAML text? Don't go looking for a WinRT DOM
parser — there isn't one. Either tokenize the known tag subset by hand, or (if
the markup is genuinely arbitrary and complex) reconsider whether plain text
via `HtmlUtilities.ConvertToText` is good enough.
