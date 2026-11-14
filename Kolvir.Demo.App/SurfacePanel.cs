namespace Kolvir.Demo;

/// <summary>
/// The sample's card: a surface with a hairline border and a rounded corner.
/// </summary>
/// <remarks>
/// <see cref="Panel"/> brings no look of its own, which is what makes it a container rather than a
/// card. The sample wants its panels to be cards, so it says so once, here - as a class default
/// rather than an assigned colour, so that a control inside a card keeps its own background instead
/// of inheriting the card's. That is the same division the library draws: a default is the look of a
/// kind of control, an assigned background is a value that cascades.
/// </remarks>
internal sealed class SurfacePanel : Panel
{
    public SurfacePanel()
    {
        // The sample's cards were laid out with the frame sharing the gutter rather than reserving
        // room for it, so the card declares a frame that takes none: reserving a pixel would have
        // moved the contents of every page by one.
        Border = SimpleBorder.Thin.WithCornerRadius(8f).WithInsets(Thickness.Empty);
    }

    protected override Background DefaultBackground => Background.FromThemeColor(ThemeColor.Surface);
}

/// <summary>The same card look for a stack, which the sample frames on the stack panel page.</summary>
internal sealed class SurfaceStackPanel : StackPanel
{
    public SurfaceStackPanel()
    {
        Border = SimpleBorder.Thin.WithCornerRadius(8f).WithInsets(Thickness.Empty);
    }

    protected override Background DefaultBackground => Background.FromThemeColor(ThemeColor.Surface);
}
