using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A modal layer over a container: it dims what is under it, takes every press, and swallows every
/// key so that nothing underneath can be driven while it is showing. Put a control inside it - a
/// wait spinner, a progress row, a confirmation panel - and the layer is the "not now" around it.
/// </summary>
/// <remarks>
/// <para>
/// The layer is an ordinary <see cref="Panel"/> in the tree, not a window: an application docks
/// it over the panel - or the whole page - that has to be held still, and the panel's own child
/// ordering makes it the topmost thing in that subtree, which is what takes the presses. Keyboard
/// input does not go to the topmost control, it goes to the focused one, so the layer also joins the
/// window as an <see cref="IInputSink"/> while it is on screen: every key and every typed
/// character stops there before the focused control sees it. That is the same seam an open menu uses
/// to own the keyboard without taking the focus.
/// </para>
/// <para>
/// A child with <see cref="DockStyle.None"/> is centred - or placed by
/// <see cref="ChildAlignment"/> - in the room the layer has, which is what makes "a spinner in the
/// middle of the window" one line:
/// <c>overlay.Controls.Add(new BusyIndicator())</c>. A docked child keeps its edge, so a
/// message can sit at the top while the spinner takes the middle.
/// </para>
/// <para>
/// The dimming is painted with <see cref="Theme.Background"/> at <see cref="Dim"/> alpha rather
/// than a fixed grey, so it darkens a light window and lightens a dark one when the palette changes.
/// </para>
/// </remarks>
public class Overlay : Panel, IInputSink
{
    private float _dim = 0.55f;
    private ContentAlignment _childAlignment = ContentAlignment.MiddleCenter;
    private IDisposable? _sink;

    /// <summary>
    /// Creates a layer that dims what is under it, takes every press and swallows every key.
    /// It fills the container it is put in, keeps no chrome of its own and is not a tab stop.
    /// </summary>
    public Overlay()
    {
        Padding = Thickness.Empty;
        TabStop = false;
        Focusable = false;
        Cursor = Cursor.Default;
        BackColor = null;

        // A layer is meant to cover the region it is put in, so filling it is the default the way a
        // tool strip defaults to docking at the top.
        Dock = DockStyle.Fill;
    }

    /// <summary>
    /// How much the layer covers what is under it, 0 to 1. Defaults to 0.55; 0 leaves a layer that
    /// only blocks input, and 1 hides the content completely.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 1.</exception>
    public float Dim
    {
        get => _dim;
        set
        {
            if (value is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The dimming must be between 0 and 1.");
            }

            if (MathF.Abs(_dim - value) < 0.001f)
            {
                return;
            }

            _dim = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Where a child that is not docked is placed in the layer. Defaults to
    /// <see cref="ContentAlignment.MiddleCenter"/>.
    /// </summary>
    public ContentAlignment ChildAlignment
    {
        get => _childAlignment;
        set
        {
            if (_childAlignment == value)
            {
                return;
            }

            _childAlignment = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>Every key stops at the layer while it is on screen and enabled.</summary>
    public bool ProcessKeyDown(Key key, Modifiers modifiers) => BlocksInput;

    /// <summary>Every typed character stops at the layer too, so a field underneath cannot fill up.</summary>
    public bool ProcessTextInput(string text) => BlocksInput && text.Length > 0;

    /// <summary>True while the layer is really able to hold the window still.</summary>
    private bool BlocksInput => IsDisplayed && Enabled;

    /// <summary>
    /// Paints the dimming: <see cref="Theme.Background"/> at <see cref="Dim"/> alpha, read from the
    /// palette at paint time so a palette change recolours a live layer with no rebuild.
    /// </summary>
    /// <param name="canvas">Canvas to paint into, in this control's client space.</param>
    /// <param name="rect">The area to paint.</param>
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        // The panel's own surface is off, so the only fill is the dimming, and it is a palette role
        // read now: a palette swap recolours a live layer with no rebuild.
        if (_dim > 0f)
        {
            var shade = Theme.Background.WithAlpha((byte)MathF.Round(Math.Clamp(_dim, 0f, 1f) * 255f));
            Painter.Fill(canvas, rect, shade, Border.CornerRadius);
        }

        base.OnPaintBackground(canvas, rect);
    }

    /// <summary>
    /// Draws nothing: a modal layer has no chrome of its own, because a border would read as a panel
    /// sitting on top of the content it is meant to hold still.
    /// </summary>
    /// <param name="canvas">Canvas to paint into, in this control's client space.</param>
    /// <param name="rect">The area the border would be drawn in.</param>
    protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
    {
        // A modal layer has no chrome of its own; the border would read as a panel on top.
    }

    /// <inheritdoc />
    /// <remarks>
    /// Docked children keep their edge; every other visible child is sized to its content - or kept
    /// at its own size - and placed by <see cref="ChildAlignment"/> in what the docked children left.
    /// A layer that has no room yet arranges nothing, so the children keep the size they were given.
    /// </remarks>
    protected override SKSize ArrangeChildren(SKRect area)
    {
        var remaining = LayoutDockedChildren(area);

        // A layer that has not been given an area yet - it was just built, or its container is
        // collapsed - has nothing to centre in, and arranging into nothing would write a zero size
        // onto a child that a later pass would then take as the size the application asked for.
        if (remaining.Width <= 0f || remaining.Height <= 0f)
        {
            return MeasureExtent(area);
        }

        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock != DockStyle.None)
            {
                continue;
            }

            var room = Geometry.Deflate(remaining, child.Margin);
            var size = child.AutoSize ? child.Measure(new SKSize(room.Width, room.Height)) : child.Size;

            child.Bounds = Geometry.Align(size, room, _childAlignment);
        }

        return MeasureExtent(area);
    }

    /// <summary>Takes the window's keyboard while the layer is on screen, and gives it back when it is not.</summary>
    protected override void OnDisplayedChanged()
    {
        base.OnDisplayedChanged();
        SyncInputSink();
    }

    /// <summary>
    /// Takes the window's keyboard when the layer is enabled and gives it back when it is not, so a
    /// disabled layer leaves the focused control underneath answering the keys again.
    /// </summary>
    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();
        SyncInputSink();
    }

    /// <summary>Removes the layer from the window's input sinks, if it had joined them.</summary>
    /// <param name="disposing">True when the control is being disposed rather than finalized.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Takes the window's keyboard while the layer can be seen and gives it back when it cannot, so
    /// a hidden or detached overlay does not leave the application answering nothing.
    /// </summary>
    private void SyncInputSink()
    {
        if (BlocksInput && Host is { } host)
        {
            _sink ??= host.AddInputSink(this);
            return;
        }

        if (_sink is not null)
        {
            _sink.Dispose();
            _sink = null;
        }
    }
}
