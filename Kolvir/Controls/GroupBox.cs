using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A labelled frame around a group of controls: the WinForms group box. <see cref="Control.Text"/>
/// is the caption, which sits in a band along the top edge; the frame is drawn around what is left
/// and the children are laid out inside it.
/// </summary>
/// <remarks>
/// The caption band is taken out of the content area rather than out of the padding, so a child can
/// never be arranged under the caption, and content that scrolls is clipped below it. A group box
/// with no text is just a plain bordered container.
/// </remarks>
public class GroupBox : Panel
{
    public GroupBox()
    {
        // The panel draws no chrome of its own, and the caption needs a break in the top edge, so
        // the frame is drawn here instead.
        BorderWidth = 1f;
        CornerRadius = 8f;
        Padding = new Thickness(12f, 0f, 12f, 12f);
        ForeColorRole = ThemeColor.MutedForeground;
        TabStop = false;
    }

    public GroupBox(string caption)
        : this()
    {
        Text = caption;
    }

    /// <summary>The caption is emphasized until the theme or an ancestor says otherwise.</summary>
    protected override Font DefaultFont => Theme.EmphasizedFont;

    /// <summary>The height of the band the caption sits in, and the top of the content area.</summary>
    protected float CaptionBand => string.IsNullOrEmpty(Text) ? 0f : MathF.Ceiling(Font.LineHeight) + 2f;

    protected override SKRect ContentBounds =>
        new(ClientRect.Left, ClientRect.Top + CaptionBand, ClientRect.Right, ClientRect.Bottom);

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        float band = CaptionBand;
        var content = base.MeasureContent(new SKSize(availableSize.Width, MathF.Max(0f, availableSize.Height - band)));
        return new SKSize(content.Width, content.Height + band);
    }

    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        float band = CaptionBand;
        var frame = new SKRect(rect.Left + 0.5f, rect.Top + (band / 2f), rect.Right - 0.5f, rect.Bottom - 0.5f);
        if (frame.Width <= 0f || frame.Height <= 0f)
        {
            return;
        }

        float width = BorderWidth <= 0f ? 1f : BorderWidth;

        if (band <= 0f)
        {
            Painter.DrawBorder(canvas, frame, BorderColor, width, CornerRadius);
            return;
        }

        var measured = TextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false);
        float left = rect.Left + Padding.Left;
        var caption = new SKRect(left, rect.Top, left + MathF.Ceiling(measured.Width), rect.Top + band);

        // The break in the top edge is cut out of the frame rather than painted over, so the
        // caption reads the same on any background - there is no colour to match.
        canvas.Save();
        canvas.ClipRect(
            new SKRect(caption.Left - 4f, rect.Top - 1f, caption.Right + 4f, rect.Top + band),
            SKClipOperation.Difference,
            antialias: true);
        Painter.DrawBorder(canvas, frame, BorderColor, width, CornerRadius);
        canvas.Restore();

        TextRenderer.Draw(
            canvas,
            Text,
            Font,
            Enabled ? ForeColor : Theme.DisabledForeground,
            caption,
            ContentAlignment.MiddleLeft);
    }
}
