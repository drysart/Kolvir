using SkiaSharp;

namespace Kolvir.Backend.Win32;

/// <summary>
/// Works out what colour the window should erase to when Windows asks, which is the colour a viewer
/// would see at the window's edge.
/// </summary>
/// <remarks>
/// The window asks for an erase when it grows, and answering it means filling a strip of client area
/// that the next frame has not painted yet. Black is always available and always wrong somewhere;
/// the window's own background - the host's clear colour, which follows the palette - is right at
/// the edge by construction, because that is what is painted behind everything else.
///
/// A control that paints can still be what is visible at the edge: a root, or a page inside it, that
/// covers the whole client area with a colour of its own hides the clear colour entirely, and erasing
/// to the clear colour would then leave a seam of the wrong shade. So the walk follows the controls
/// that cover the window and takes the innermost colour it finds, falling back to the clear colour
/// wherever the chain runs out.
/// </remarks>
internal static class Win32EraseColor
{
    /// <summary>Half a logical pixel: layout rounds, and a control that fills the window still fills it.</summary>
    private const float Tolerance = 0.5f;

    /// <summary>
    /// The colour to erase to: the innermost control that covers the whole client area and paints,
    /// or <paramref name="clearColor"/> when no control does.
    /// </summary>
    /// <param name="root">The top of the control tree; null before one is assigned.</param>
    /// <param name="client">The client area in logical pixels.</param>
    /// <param name="clearColor">The host's clear colour, which is what the window paints behind the tree.</param>
    internal static SKColor Resolve(Control? root, SKSize client, SKColor clearColor)
    {
        if (root is null || client.Width <= 0f || client.Height <= 0f)
        {
            return clearColor;
        }

        var color = clearColor;
        var control = root;

        while (control is not null && Covers(control.Bounds, client))
        {
            if (control.Visible && control.Background.Color is { } painted)
            {
                color = painted;
            }

            control = FullBleedChild(control, client);
        }

        return color;
    }

    /// <summary>
    /// The first visible child that covers the client area on its own. Only the first: a second
    /// full-bleed child is in front of the first and would be the one visible, but picking between
    /// them means knowing the paint order, and the walk stops being a thumbnail of the window.
    /// </summary>
    private static Control? FullBleedChild(Control parent, SKSize client)
    {
        foreach (var child in parent.Controls)
        {
            if (child.Visible && Covers(child.Bounds, client))
            {
                return child;
            }
        }

        return null;
    }

    private static bool Covers(SKRect bounds, SKSize client) =>
        bounds.Left <= Tolerance
        && bounds.Top <= Tolerance
        && bounds.Right >= client.Width - Tolerance
        && bounds.Bottom >= client.Height - Tolerance;
}
