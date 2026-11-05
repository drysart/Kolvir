using SkiaSharp;

namespace XPlatUI.Multiline;

/// <summary>
/// The geometry a text box hands its multi-line feature: the client area it was given, and the
/// padding its content is inset by. Passing both keeps the feature out of the control's fields
/// while still letting it lay text out where a single-line box would have drawn it.
/// </summary>
internal readonly struct XPlatTextBoxViewport
{
    public XPlatTextBoxViewport(SKRect client, XPlatThickness padding)
    {
        Client = client;
        Padding = padding;
    }

    /// <summary>The control's client area, with its own origin.</summary>
    public SKRect Client { get; }

    /// <summary>The padding the text is inset by.</summary>
    public XPlatThickness Padding { get; }

    /// <summary>The area the text is laid out in.</summary>
    public SKRect Text => XPlatGeometry.Deflate(Client, Padding);
}

/// <summary>
/// The whole seam between <see cref="XPlatTextBox"/> and multi-line editing.
/// </summary>
/// <remarks>
/// <para>
/// A text box holds one nullable reference to this interface and nothing else. Every multi-line
/// behaviour - measuring, painting, hit testing, the scrollbars, the keys that differ from
/// single-line editing - is reached through it, so the control's own code only ever calls an
/// interface it cannot see an implementation of.
/// </para>
/// <para>
/// That is what keeps FEATURE-COST-POLICY.md §4's first rung reachable: the implementation
/// (<c>XPlatTextBoxMultiline</c>) is named only by the extension property that turns the feature
/// on, so an application that never writes <c>using XPlatUI.Multiline;</c> and never sets
/// <c>Multiline</c> publishes without it. What such an application does pay for is the null
/// reference every text box carries - eight bytes and a null check - which is the accepted cost of
/// a seam that lets every line behind it go away.
/// </para>
/// </remarks>
internal interface IXPlatTextBoxMultiline
{
    /// <summary>Whether long lines wrap, or stay on one visual line and scroll sideways.</summary>
    bool WordWrap { get; set; }

    /// <summary>Normalises line breaks and expands tabs, without touching anything else.</summary>
    string SanitizeText(string text);

    /// <summary>The preferred size of a multi-line box: a capped width and a few lines of height.</summary>
    SKSize Measure(XPlatTextBox textBox, XPlatTextBoxViewport viewport, SKSize availableSize);

    /// <summary>Positions the scrollbars and rebuilds the visual lines for the current width.</summary>
    void Layout(XPlatTextBox textBox, XPlatTextBoxViewport viewport);

    /// <summary>
    /// Paints the text. The control paints the selection and any squiggles before this and the
    /// caret after it, so the order is the same on a one-line field.
    /// </summary>
    void Paint(XPlatTextBox textBox, SKCanvas canvas, XPlatTextBoxViewport viewport, string placeholderText);

    /// <summary>Where the caret is drawn, in the control's client coordinates.</summary>
    SKRect GetCaretBounds(XPlatTextBox textBox, XPlatTextBoxViewport viewport);

    /// <summary>The character index nearest a point in the control's client coordinates.</summary>
    int GetIndexFromPoint(XPlatTextBox textBox, XPlatTextBoxViewport viewport, SKPoint point);

    /// <summary>Adds one rectangle per visual line the selection covers.</summary>
    void GetSelectionRects(
        XPlatTextBox textBox,
        XPlatTextBoxViewport viewport,
        int start,
        int length,
        List<SKRect> output);

    /// <summary>Handles the keys that mean something else once text spans lines. False leaves the key alone.</summary>
    bool OnKeyDown(XPlatTextBox textBox, XPlatTextBoxViewport viewport, XPlatKeyEventArgs e);

    /// <summary>Turns the wheel over the text. False leaves the turn for the container above.</summary>
    bool OnMouseWheel(XPlatTextBox textBox, XPlatMouseEventArgs e);

    /// <summary>Called after the text changed, so the view can follow it.</summary>
    void OnTextChanged(XPlatTextBox textBox, XPlatTextBoxViewport viewport);

    /// <summary>Scrolls so the caret is inside the text area.</summary>
    void EnsureCaretVisible(XPlatTextBox textBox, XPlatTextBoxViewport viewport);

    /// <summary>Clamps the scroll offsets to the range the content now has.</summary>
    void ClampScroll(XPlatTextBox textBox, XPlatTextBoxViewport viewport);

    /// <summary>
    /// Moves the view to the given offsets, clamped to what the content allows. The undo history
    /// uses it to put the view back where an edit was made; a single-line box scrolls itself from
    /// the caret instead and has no equivalent.
    /// </summary>
    void ScrollTo(XPlatTextBox textBox, XPlatTextBoxViewport viewport, float horizontal, float vertical);

    /// <summary>The vertical scrollbar, or null until the content first needs one.</summary>
    XPlatScrollBar? VerticalScrollBar { get; }

    /// <summary>The horizontal scrollbar, or null until an unwrapped field first needs one.</summary>
    XPlatScrollBar? HorizontalScrollBar { get; }

    /// <summary>How far the text is scrolled vertically, in pixels.</summary>
    float VerticalOffset { get; }

    /// <summary>How far the text is scrolled horizontally, in pixels.</summary>
    float HorizontalOffset { get; }

    /// <summary>Paints the scrollbars over the text, after the children so they are never clipped away.</summary>
    void RenderBars(SKCanvas canvas);

    /// <summary>The scrollbar containing a point in the control's client coordinates, or null.</summary>
    XPlatControl? HitTestBar(SKPoint pointInLocal);

    /// <summary>Releases what the feature owns. The control stays single-line afterwards.</summary>
    void Dispose();
}
