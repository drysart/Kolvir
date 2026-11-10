using SkiaSharp;

namespace XPlatUI;

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
/// The drop-down is the library's own menu - <see cref="XPlatMenuView"/> in an
/// <see cref="XPlatPopup"/> through <see cref="XPlatMenuPresenter"/> - so submenus, mnemonics,
/// keyboard navigation and the platform's ability to let the menu leave the window all come with it.
/// The popup is created when the menu opens; a button that is never clicked builds nothing.
/// </para>
/// <para>
/// Enter and Space raise the default action, and Down opens the menu. Once the menu is open it owns
/// the keyboard, so its own arrow keys, Enter and Escape are the ones in force.
/// </para>
/// </remarks>
public class XPlatSplitButton : XPlatButton
{
    private float _arrowWidth = 24f;
    private bool _arrowHot;
    private bool _arrowPressed;
    private bool _clickOpensMenu;
    private bool _clickIsCancelled;
    private XPlatPopup? _dropDown;
    private bool _dropDownOpen;
    private bool _disposed;

    public XPlatSplitButton()
    {
        Menu = new XPlatContextMenu();
    }

    /// <summary>Creates a split button with <paramref name="text"/> as its default action's label.</summary>
    public XPlatSplitButton(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>The items the arrow opens, top to bottom. Add to it before the menu is first opened.</summary>
    public XPlatContextMenu Menu { get; }

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
            var content = XPlatGeometry.Deflate(ClientRect, Padding);
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
        var view = new XPlatMenuView(Menu);
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
        _dropDown = XPlatMenuPresenter.ShowMenu(host, view, location, OnDropDownClosed, scale);
        Invalidate();
    }

    /// <summary>Closes the menu, if it is open.</summary>
    public void CloseDropDown() => _dropDown?.Close();

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var measured = XPlatTextRenderer.Measure(Text, Font, float.PositiveInfinity, wrap: false);
        return new SKSize(MathF.Ceiling(measured.Width) + _arrowWidth, MathF.Ceiling(measured.Height));
    }

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
            XPlatButtonStyle.Primary => XPlatTheme.AccentPressed,
            _ => XPlatTheme.SecondaryPressed,
        };

        var arrow = XPlatGeometry.Deflate(ArrowBounds, 3f);
        if (arrow.Width > 0f && arrow.Height > 0f)
        {
            XPlatPainter.Fill(canvas, arrow, fill, 4f);
        }
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var (_, textColor) = ResolveColors();
        var arrow = ArrowBounds;

        if (ShowsArrowDivider)
        {
            var divider = Enabled
                ? Style == XPlatButtonStyle.Primary
                    ? XPlatTheme.AccentForeground.WithAlpha(70)
                    : XPlatTheme.Border
                : XPlatTheme.DisabledBackground;

            XPlatPainter.Fill(
                canvas,
                new SKRect(arrow.Left - 0.5f, rect.Top + 7f, arrow.Left + 0.5f, rect.Bottom - 7f),
                divider);
        }

        var text = TextBounds;
        if (!string.IsNullOrEmpty(Text) && text.Width > 0f)
        {
            XPlatTextRenderer.Draw(canvas, Text, Font, textColor, text, XPlatContentAlignment.MiddleCenter);
        }

        PaintChevron(canvas, arrow, textColor);
    }

    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        _arrowPressed = e.Button == XPlatMouseButtons.Left &&
                        IsInArrow(e.Location) &&
                        !Menu.IsEmpty;

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        bool startedInArrow = _arrowPressed;
        _arrowPressed = false;

        // A press that started in one half and was released over the other is neither action: the
        // base class decides whether the release is inside the control at all, and the flags below
        // decide what that press means when it is.
        bool inside = IsPressed && XPlatGeometry.Contains(ClientRect, e.Location);
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

    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        bool hot = IsInArrow(e.Location);
        if (hot != _arrowHot)
        {
            _arrowHot = hot;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave()
    {
        if (_arrowHot)
        {
            _arrowHot = false;
        }

        _arrowPressed = false;
        base.OnMouseLeave();
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !Enabled)
        {
            return;
        }

        if (e.Key == XPlatKey.Down && !Menu.IsEmpty)
        {
            OpenDropDown();
            e.Handled = true;
        }
    }

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

    protected override void OnVisibleChanged()
    {
        if (!Visible)
        {
            CloseDropDown();
        }

        base.OnVisibleChanged();
    }

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
        _arrowWidth > 0f && XPlatGeometry.Contains(ArrowBounds, point);

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
/// <see cref="XPlatControl.AddClickHandler"/> still runs, which mirrors the WinForms control: a
/// handler that wants to know the menu was asked for can subscribe to it. Applications that treat
/// the button purely as a menu usually subscribe to nothing.
/// </remarks>
public class XPlatDropDownButton : XPlatSplitButton
{
    public XPlatDropDownButton()
    {
    }

    /// <summary>Creates a drop-down button with <paramref name="text"/> as its label.</summary>
    public XPlatDropDownButton(string text)
        : base(text)
    {
    }

    /// <summary>A drop-down button is one face, so no divider separates a half that does not exist.</summary>
    protected override bool ShowsArrowDivider => false;

    protected override void OnClick()
    {
        OpenDropDown();
        base.OnClick();
    }
}
