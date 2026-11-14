using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A button that is both a command and a menu: pressing the main area runs the default action, and
/// the arrow at its right edge opens <see cref="Menu"/> in a popup.
/// </summary>
/// <remarks>
/// <para>
/// The two halves are one control rather than a button beside a second button because native split
/// buttons are one: they take one tab stop, draw one focus ring and grey out together. Which half
/// was pressed is decided from the press location, and a press that starts in one half and is
/// released over the other does nothing, the way a press that wanders off a button does.
/// </para>
/// <para>
/// The drop-down is the library's own menu - <see cref="MenuView"/> in an
/// <see cref="Popup"/> through <see cref="MenuPresenter"/> - so submenus, mnemonics,
/// keyboard navigation and the platform's ability to let the menu leave the window all come with it.
/// The popup is created when the menu opens; a button that is never clicked builds nothing.
/// </para>
/// <para>
/// Enter and Space raise the default action, and Down opens the menu. Once the menu is open it owns
/// the keyboard, so its own arrow keys, Enter and Escape are the ones in force.
/// </para>
/// </remarks>
public class SplitButton : Button
{
    private float _arrowWidth = 24f;
    private bool _arrowHot;
    private bool _arrowPressed;
    private bool _clickOpensMenu;
    private bool _clickIsCancelled;
    private Popup? _dropDown;
    private bool _dropDownOpen;
    private bool _disposed;

    /// <summary>Creates a split button with no text and an empty <see cref="Menu"/>.</summary>
    public SplitButton()
    {
        Menu = new ContextMenu();
    }

    /// <summary>Creates a split button with <paramref name="text"/> as its default action's label.</summary>
    public SplitButton(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>The items the arrow opens, top to bottom. Add to it before the menu is first opened.</summary>
    public ContextMenu Menu { get; }

    /// <summary>Width of the arrow region at the right edge, in logical pixels. Defaults to 24.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public float ArrowWidth
    {
        get => _arrowWidth;
        set
        {
            if (value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The arrow width cannot be negative.");
            }

            if (MathF.Abs(_arrowWidth - value) < 0.01f)
            {
                return;
            }

            _arrowWidth = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>True while the arrow's menu is showing.</summary>
    public bool IsDropDownOpen => _dropDownOpen;

    /// <summary>
    /// True when the arrow is drawn with a divider between it and the main area. A drop-down button
    /// has one label rather than two halves, so it draws no divider.
    /// </summary>
    protected virtual bool ShowsArrowDivider => true;

    /// <summary>The rectangle the arrow occupies, in client coordinates.</summary>
    protected SKRect ArrowBounds
    {
        get
        {
            var client = ClientRect;
            return new SKRect(MathF.Max(client.Left, client.Right - _arrowWidth), client.Top, client.Right, client.Bottom);
        }
    }

    /// <summary>The rectangle the label is drawn in, in client coordinates.</summary>
    protected SKRect TextBounds
    {
        get
        {
            var content = Geometry.Deflate(ClientRect, Padding);
            return new SKRect(content.Left, content.Top, MathF.Max(content.Left, content.Right - _arrowWidth), content.Bottom);
        }
    }

    /// <summary>Opens <see cref="Menu"/> below the button, or above it when there is no room below.</summary>
    public void OpenDropDown()
    {
        if (_dropDownOpen || !Enabled || Host is not { } host || Menu.IsEmpty)
        {
            return;
        }

        // The view is built per open, because the presenter owns and disposes it with the popup.
        var view = new MenuView(Menu);
        var scale = EffectiveScaleMultiplier;
        var origin = GetPositionInHost();
        var measured = view.Measure(new SKSize(host.ClientSize.Width / scale, host.ClientSize.Height / scale));
        var onScreen = new SKSize(measured.Width * scale, measured.Height * scale);
        var location = new SKPoint(origin.X, origin.Y + (Height * scale));

        // Flipping above is what a split button at the bottom of a window does. The presenter places
        // the menu inside the display's work area as well, but it does not know which control the
        // menu belongs to.
        if (location.Y + onScreen.Height > host.ClientSize.Height && origin.Y - onScreen.Height >= 0f)
        {
            location = new SKPoint(origin.X, origin.Y - onScreen.Height);
        }

        _dropDownOpen = true;
        _dropDown = MenuPresenter.ShowMenu(host, view, location, OnDropDownClosed, scale);
        Invalidate();
    }

    /// <summary>Closes the menu, if it is open.</summary>
    public void CloseDropDown() => _dropDown?.Close();

    /// <summary>
    /// The label on one line plus the arrow region: as wide as the text unwrapped and
    /// <see cref="ArrowWidth"/> wider, rounded up to whole pixels.
    /// </summary>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var measured = TextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false);
        return new SKSize(MathF.Ceiling(measured.Width) + _arrowWidth, MathF.Ceiling(measured.Height));
    }

    /// <summary>
    /// Fills the button as a <see cref="Button"/> does, then darkens the arrow region while the
    /// pointer or a press is over that half.
    /// </summary>
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (!Enabled || (!_arrowHot && !_arrowPressed))
        {
            return;
        }

        // The arrow region darkens under the pointer the way a split button's does, so it is clear
        // which half a press is about to hit.
        var fill = Style switch
        {
            ButtonStyle.Primary => Theme.AccentPressed,
            _ => Theme.SecondaryPressed,
        };

        var arrow = Geometry.Deflate(ArrowBounds, 3f);
        if (arrow.Width > 0f && arrow.Height > 0f)
        {
            Painter.Fill(canvas, arrow, fill, 4f);
        }
    }

    /// <summary>
    /// Draws the divider, if <see cref="ShowsArrowDivider"/> asks for one, the label centred in the
    /// text region and the chevron in the arrow region.
    /// </summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var (_, textColor) = ResolveColors();
        var arrow = ArrowBounds;

        if (ShowsArrowDivider)
        {
            var divider = Enabled
                ? Style == ButtonStyle.Primary
                    ? Theme.AccentForeground.WithAlpha(70)
                    : Theme.Border
                : Theme.DisabledBackground;

            Painter.Fill(
                canvas,
                new SKRect(arrow.Left - 0.5f, rect.Top + 7f, arrow.Left + 0.5f, rect.Bottom - 7f),
                divider);
        }

        var text = TextBounds;
        if (!string.IsNullOrEmpty(Text) && text.Width > 0f)
        {
            TextRenderer.Draw(canvas, Text, Font, textColor, text, ContentAlignment.MiddleCenter);
        }

        PaintChevron(canvas, arrow, textColor);
    }

    /// <summary>
    /// Records whether the press began in the arrow region, then starts the press the way a button
    /// does.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _arrowPressed = e.Button == MouseButtons.Left &&
                        IsInArrow(e.Location) &&
                        !Menu.IsEmpty;

        base.OnMouseDown(e);
    }

    /// <summary>
    /// Works out which half the press was for - arrow, main area, or neither when it moved between
    /// the two - so that <see cref="OnClick"/> can act on it.
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool startedInArrow = _arrowPressed;
        _arrowPressed = false;

        // A press that started in one half and was released over the other is neither action: the
        // base class decides whether the release is inside the control at all, and the flags below
        // decide what that press means when it is.
        bool inside = IsPressed && Geometry.Contains(ClientRect, e.Location);
        bool inArrow = IsInArrow(e.Location);

        _clickOpensMenu = startedInArrow && inside && inArrow;
        _clickIsCancelled = startedInArrow && inside && !inArrow;

        try
        {
            base.OnMouseUp(e);
        }
        finally
        {
            _clickOpensMenu = false;
            _clickIsCancelled = false;
        }
    }

    /// <summary>
    /// Opens the menu when the click came from the arrow region, swallows a click that started in
    /// one half and ended in the other, and otherwise runs the default action.
    /// </summary>
    protected override void OnClick()
    {
        bool opensMenu = _clickOpensMenu;
        bool cancelled = _clickIsCancelled;
        _clickOpensMenu = false;
        _clickIsCancelled = false;

        if (opensMenu)
        {
            OpenDropDown();
            return;
        }

        if (cancelled)
        {
            return;
        }

        base.OnClick();
    }

    /// <summary>Tracks whether the pointer is over the arrow, which highlights that half.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hot = IsInArrow(e.Location);
        if (hot != _arrowHot)
        {
            _arrowHot = hot;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    /// <summary>Clears the arrow's hover and press states when the pointer leaves the button.</summary>
    protected override void OnMouseLeave()
    {
        if (_arrowHot)
        {
            _arrowHot = false;
        }

        _arrowPressed = false;
        base.OnMouseLeave();
    }

    /// <summary>Opens the menu on Down, when the menu has items, and claims the key.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !Enabled)
        {
            return;
        }

        if (e.Key == Key.Down && !Menu.IsEmpty)
        {
            OpenDropDown();
            e.Handled = true;
        }
    }

    /// <summary>Clears the arrow's states and closes the menu when the button is disabled.</summary>
    protected override void OnEnabledChanged()
    {
        if (!Enabled)
        {
            _arrowHot = false;
            _arrowPressed = false;
            CloseDropDown();
        }

        base.OnEnabledChanged();
    }

    /// <summary>Closes the menu when the button stops being visible, so no popup outlives it.</summary>
    protected override void OnVisibleChanged()
    {
        if (!Visible)
        {
            CloseDropDown();
        }

        base.OnVisibleChanged();
    }

    /// <summary>
    /// Closes and disposes the popup the menu was shown in, and marks the button disposed so that a
    /// late close does not repaint it.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _dropDown?.Dispose();
            _dropDown = null;
            _dropDownOpen = false;
        }

        base.Dispose(disposing);
    }

    private bool IsInArrow(SKPoint point) =>
        _arrowWidth > 0f && Geometry.Contains(ArrowBounds, point);

    private void OnDropDownClosed()
    {
        _dropDown = null;
        _dropDownOpen = false;

        // A popup that closes while the owner is being torn down must not repaint a disposed tree.
        if (!_disposed)
        {
            Invalidate();
        }
    }

    private void PaintChevron(SKCanvas canvas, SKRect arrow, SKColor color)
    {
        if (arrow.Width < 8f)
        {
            return;
        }

        float centerX = arrow.MidX;
        float centerY = arrow.MidY;
        float half = 4f;
        float height = 4f;

        using var builder = new SKPathBuilder();
        builder.MoveTo(centerX - half, centerY - (height * 0.5f));
        builder.LineTo(centerX + half, centerY - (height * 0.5f));
        builder.LineTo(centerX, centerY + (height * 0.5f));
        builder.Close();

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawPath(path, paint);
    }
}

/// <summary>
/// A button whose whole face opens a menu: the WinForms <c>ToolStripDropDownButton</c>. There is no
/// default action to run separately, so pressing anywhere opens the popup.
/// </summary>
/// <remarks>
/// <see cref="Control.AddClickHandler"/> still runs, which mirrors the WinForms control: a
/// handler that wants to know the menu was asked for can subscribe to it. Applications that treat
/// the button purely as a menu usually subscribe to nothing.
/// </remarks>
public class DropDownButton : SplitButton
{
    /// <summary>Creates a drop-down button with no text and an empty menu.</summary>
    public DropDownButton()
    {
    }

    /// <summary>Creates a drop-down button with <paramref name="text"/> as its label.</summary>
    public DropDownButton(string text)
        : base(text)
    {
    }

    /// <summary>A drop-down button is one face, so no divider separates a half that does not exist.</summary>
    protected override bool ShowsArrowDivider => false;

    /// <summary>Opens the menu, then raises <see cref="Control.AddClickHandler"/>.</summary>
    protected override void OnClick()
    {
        OpenDropDown();
        base.OnClick();
    }
}
