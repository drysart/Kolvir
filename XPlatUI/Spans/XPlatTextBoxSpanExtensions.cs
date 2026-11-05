using SkiaSharp;

namespace XPlatUI.Spans;

/// <summary>
/// The members a text box gains by opting into annotated spans: the list of spans it draws and
/// answers questions about.
/// </summary>
/// <remarks>
/// <para>
/// These are extension members rather than members of <see cref="XPlatTextBox"/>, the way
/// FEATURE-COST-POLICY.md §5 asks, and the surface still reads as part of the control, object
/// initializers included:
/// <code>
/// var field = new XPlatTextBox
/// {
///     Dock = XDock.Fill,
///     Spans = [new XPlatTextBoxSpan(4, 7)],
/// };
/// </code>
/// </para>
/// <para>
/// Assigning a list is what creates the state; a text box that never assigns one has none, and the
/// implementation behind it is reachable only from here.
/// </para>
/// <para>
/// <b>The contract.</b> The box keeps the list by reference - it does not copy, reorder, merge or
/// mutate it - and it never rewrites or drops a span when the text changes underneath. Spans are
/// therefore caller-owned, and a checker that re-runs on every keystroke can clear its own
/// <see cref="List{T}"/>, refill it and assign it again with no allocation on the box's side and no
/// work for the box beyond one repaint.
/// </para>
/// <para>
/// That means a set of spans can go stale, and stale spans are handled by clamping rather than by
/// throwing: a span that starts past the end of the text paints nothing and resolves no
/// right-click, and one that runs past the end is drawn up to the last character. An empty span (a
/// length of zero) is skipped the same way. Overlaps are allowed and are not an error: both spans
/// paint, in list order, and a right-click inside both resolves to the last one in the list -
/// the one drawn on top.
/// </para>
/// </remarks>
public static class XPlatTextBoxSpanExtensions
{
    extension(XPlatTextBox textBox)
    {
        /// <summary>
        /// The spans drawn in the field, or null when it has none. Each is marked with a squiggly
        /// underline under the characters it covers, one segment per visual line, so a span that
        /// crosses a soft wrap in a multi-line field is marked on every line it touches.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Assigning replaces the whole set: the list is stored by reference and the field
        /// repaints, so mutating it and assigning it again is the cheap way to update annotations
        /// after an edit. Assigning null or an empty list removes the state, which is what makes a
        /// form that has no annotations pay nothing for the feature.
        /// </para>
        /// <para>
        /// The squiggle is drawn over the selection highlight and under the caret, in the band
        /// below the text, so a marked word stays visible while it is selected rather than being
        /// painted out by its own highlight.
        /// </para>
        /// </remarks>
        public IReadOnlyList<XPlatTextBoxSpan>? Spans
        {
            get => textBox.SpanFeature?.Spans;
            set
            {
                if (value is null || value.Count == 0)
                {
                    if (textBox.SpanFeature is { } existing)
                    {
                        existing.Dispose();
                        textBox.SpanFeature = null;
                        textBox.Invalidate();
                    }

                    return;
                }

                if (textBox.SpanFeature is { } feature)
                {
                    // The same list again is the common case: a checker refilled the list it handed
                    // over last time. Only the reference moves, and the repaint is what makes the
                    // new contents visible.
                    feature.Spans = value;
                }
                else
                {
                    XPlatTextBoxSpanFeature.Attach(textBox, value);
                }

                textBox.Invalidate();
            }
        }
    }
}

/// <summary>
/// The context-menu member a span annotation adds: which span, if any, the right-click landed in.
/// </summary>
/// <remarks>
/// <para>
/// It is an extension member on the existing <see cref="XPlatContextMenuEventArgs"/> rather than a
/// property of it, so an application that never imports this namespace pays nothing for the
/// feature even though the event args are everywhere. Code reads it as if it were a member, inside
/// the handler of the text box the span belongs to:
/// </para>
/// <code>
/// using XPlatUI.Spans;
///
/// field.AddContextMenuOpeningHandler(args =>
/// {
///     if (args.Span is { } span &amp;&amp; args.Menu is { } menu)
///     {
///         // In front of the Cut/Copy/Paste the box contributed.
///         menu.Insert(0, new XPlatMenuItem("&amp;Add to dictionary", () => Ignore(span)));
///     }
/// });
/// </code>
/// <para>
/// The text box resolves the span itself, before any handler runs, from its own hit test at the
/// moment of the press. The answer therefore follows the layout - a right-click after the text has
/// been scrolled, and after it has been re-wrapped by a resize, resolves against where the span is
/// drawn on the frame the user is looking at - and it does not change under a handler that scrolls
/// the field before reading it.
/// </para>
/// </remarks>
public static class XPlatContextMenuSpanExtensions
{
    extension(XPlatContextMenuEventArgs args)
    {
        /// <summary>
        /// The span whose characters the right-click landed in, or null when it landed outside
        /// every span - next to one, on a line between two, on the scrollbar, or in a control that
        /// has no spans at all.
        /// </summary>
        public XPlatTextBoxSpan? Span => args.Hit as XPlatTextBoxSpan;
    }
}
