using SkiaSharp;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// What a window's tracked properties looked like before any node touched them.
/// </summary>
/// <remarks>
/// Captured from the real window immediately after it is created rather than from a freshly
/// constructed one held statically, because a window's defaults are not wholly static: the icon and
/// menu bar start from the application's options, and the client size and location come from the
/// backend. Reading them once per window is the same rule the generated control nodes use, with the
/// window itself as the source of its own pristine values.
/// </remarks>
internal readonly record struct WindowPristine(
    string Title,
    SKSize ClientSize,
    SKPoint Location,
    WindowState State,
    bool Resizable,
    bool TopMost,
    Icon? Icon,
    MenuBar? MenuBar,
    SKSize MinimumClientSize,
    SKSize MaximumClientSize);
