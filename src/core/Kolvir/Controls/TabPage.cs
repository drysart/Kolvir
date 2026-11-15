using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A page of an <see cref="TabControl"/>: a panel whose <see cref="Control.Text"/> is the
/// caption on its tab. It paints no frame of its own, because the tab control draws the surface the
/// page sits on.
/// </summary>
public class TabPage : Panel
{
    /// <summary>Creates an empty page with no frame and a 10-pixel gutter.</summary>
    public TabPage()
    {
        Padding = new Thickness(10f);
    }

    /// <summary>Creates a page whose tab caption is <paramref name="text"/>.</summary>
    public TabPage(string text)
        : this()
    {
        Text = text;
    }
}
