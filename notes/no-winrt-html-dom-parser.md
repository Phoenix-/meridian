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

## Update 2026-07-25: ConvertToText is a landmine too

`HtmlUtilities.ConvertToText()` rides the deprecated IE stack: calling it from
a background (sync) thread on Win11 fail-fasted the whole process —
`0xc0000409` inside **iertutil.dll**, no catchable CLR exception (seen live
converting Outlook event bodies in `GraphApiClient`). Do not use it at all.
Plain-text extraction is now hand-rolled as well:

- [Meridian/Services/GraphApiClient.cs](../Meridian/Services/GraphApiClient.cs) → `HtmlToText`
  (drop `style/script/head` with content, newline on block closers, strip
  remaining tags, `WebUtility.HtmlDecode`).

## Rule of thumb

Need anything HTML-related? `Windows.Data.Html` has nothing for you: no DOM
parser exists, and `ConvertToText` can fail-fast the process off the UI
thread. Tokenize / strip the markup by hand.
