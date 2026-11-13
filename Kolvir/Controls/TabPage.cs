using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A page of an <see cref="TabControl"/>: a panel whose <see cref="Control.Text"/> is the
/// caption on its tab. It paints no frame of its own, because the tab control draws the surface the
/// page sits on.
/// </summary>
public class TabPage : Panel
{
    public TabPage()
    {
        BorderWidth = 0f;
        CornerRadius = 0f;
        Padding = new Thickness(10f);
    }

    public TabPage(string text)
        : this()
    {
        Text = text;
    }
}
