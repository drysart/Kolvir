using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A page of an <see cref="XPlatTabControl"/>: a panel whose <see cref="XPlatControl.Text"/> is the
/// caption on its tab. It paints no frame of its own, because the tab control draws the surface the
/// page sits on.
/// </summary>
public class XPlatTabPage : XPlatPanel
{
    public XPlatTabPage()
    {
        ShowFrame = false;
        IsCard = false;
        BorderWidth = 0f;
        CornerRadius = 0f;
        Padding = new XPlatThickness(10f);
    }

    public XPlatTabPage(string text)
        : this()
    {
        Text = text;
    }
}
