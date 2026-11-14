using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A labelled frame around a group of controls: the WinForms group box. <see cref="Control.Text"/>
/// is the caption, which sits in a band along the top edge; the frame is drawn around what is left
/// and the children are laid out inside it.
/// </summary>
/// <remarks>
/// The frame is a <see cref="SimpleBorder"/> that shows the control's text, so the caption band is
/// taken out of the content by the border rather than by the control: a child can never be arranged
/// under the caption, and content that scrolls is clipped below it. A group box with no text is a
/// plain bordered container.
/// </remarks>
public class GroupBox : Panel
{
    /// <summary>One pixel, an 8-pixel corner radius, and the caption in a break in the top edge.</summary>
    private static readonly SimpleBorder DefaultBorder =
        SimpleBorder.Thin.WithCornerRadius(8f).WithControlText();

    /// <summary>
    /// Creates a group box with no caption: a one-pixel frame, an 8-pixel corner radius, a 12-pixel
    /// gutter and no tab stop of its own.
    /// </summary>
    public GroupBox()
    {
        // The gutter is one less than the usual twelve because the frame's own pixel is the outer
        // one of it, so the content lands where it always has.
        Border = DefaultBorder;
        Padding = new Thickness(11f, 0f, 11f, 11f);
        ForeColorRole = ThemeColor.MutedForeground;
        TabStop = false;
    }

    /// <summary>Creates a group box whose caption is <paramref name="caption"/>.</summary>
    /// <param name="caption">The caption, drawn in a break in the top edge.</param>
    public GroupBox(string caption)
        : this()
    {
        Text = caption;
    }

    /// <summary>The caption is emphasized until the theme or an ancestor says otherwise.</summary>
    protected override Font DefaultFont => Theme.EmphasizedFont;
}