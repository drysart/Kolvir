namespace Kolvir.Multiline;

/// <summary>
/// The members a multi-line editor gains by opting into the feature: <c>Multiline</c>,
/// <c>WordWrap</c> and the state of its scrollbars.
/// </summary>
/// <remarks>
/// <para>
/// These are extension members rather than members of <see cref="TextBox"/>, the way
/// FEATURE-COST-POLICY.md §5 asks. The surface still reads as part of the control, object
/// initializers included:
/// <code>
/// var notes = new TextBox
/// {
///     Multiline = true,
///     WordWrap = true,
///     Dock = XDock.Fill,
/// };
/// </code>
/// </para>
/// <para>
/// Turning it on is what creates the state; a text box that never turns it on has none, and the
/// implementation behind it is reachable only from here. Turning it off again collapses the text
/// to one line and releases the state.
/// </para>
/// </remarks>
public static class TextBoxMultilineExtensions
{
    extension(TextBox textBox)
    {
        /// <summary>
        /// When true the field edits several lines: <c>Enter</c> inserts a line break, the text
        /// wraps or scrolls sideways, and a scrollbar appears when the content is taller than the
        /// box. The default, false, is the original single-line behaviour.
        /// </summary>
        public bool Multiline
        {
            get => textBox.MultilineFeature is not null;
            set
            {
                if (value == textBox.MultilineFeature is not null)
                {
                    return;
                }

                if (value)
                {
                    TextBoxMultiline.Attach(textBox);
                }
                else
                {
                    TextBoxMultiline.Detach(textBox);
                }
            }
        }

        /// <summary>
        /// When true (the default) a multi-line field wraps at word boundaries and never scrolls
        /// sideways. When false each logical line stays on one visual line and the field scrolls
        /// horizontally when it is wider than the box. Ignored while <c>Multiline</c> is false.
        /// </summary>
        public bool WordWrap
        {
            get => textBox.MultilineFeature?.WordWrap ?? true;
            set
            {
                if (textBox.MultilineFeature is { } feature)
                {
                    feature.WordWrap = value;
                }
            }
        }

        /// <summary>The vertical scrollbar, or null until a multi-line field first needs one.</summary>
        public ScrollBar? VerticalScrollBar => textBox.MultilineFeature?.VerticalScrollBar;

        /// <summary>The horizontal scrollbar, or null until an unwrapped field first needs one.</summary>
        public ScrollBar? HorizontalScrollBar => textBox.MultilineFeature?.HorizontalScrollBar;

        /// <summary>The current vertical scroll offset, in pixels. Zero when not scrolled.</summary>
        public float VerticalOffset => textBox.MultilineFeature?.VerticalOffset ?? 0f;

        /// <summary>The current horizontal scroll offset, in pixels. Zero when not scrolled.</summary>
        public float HorizontalOffset => textBox.MultilineFeature?.HorizontalOffset ?? 0f;
    }
}
