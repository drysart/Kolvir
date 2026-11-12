using System.Globalization;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A spectrum colour picker: a square you pick a colour from by clicking and dragging, a hue strip
/// beside it, three numeric component fields and a hex field.
/// </summary>
/// <remarks>
/// <para>
/// The square's horizontal axis is saturation, from zero at the left to full at the right. Its
/// vertical axis is value in <see cref="ColorMode.Rgb"/> and <see cref="ColorMode.Hsv"/>,
/// and lightness in <see cref="ColorMode.Hsl"/>; both run from full at the top to zero at the
/// bottom. The hue strip to the right of the square carries the third axis, with 0 degrees (red) at
/// the top and 359 degrees at the bottom. <see cref="ColorMode.Rgb"/> and
/// <see cref="ColorMode.Hsv"/> therefore share one square, because RGB has no two-axis shape of
/// its own; only the three fields differ between them.
/// </para>
/// <para>
/// A mode change is a display change, never a value change. The colour is held as 8-bit RGB and is
/// never round-tripped through the components when the mode changes, so <see cref="Color"/> survives
/// any number of mode switches exactly. The component fields show integers and are consequently a
/// rounded view of that colour; writing back the values the picker displayed can move a channel by a
/// few levels for a colour that is not representable at that precision.
/// </para>
/// <para>
/// The three component fields take the units and ranges of the current mode: R, G and B are 0-255,
/// while H is 0-359 degrees and S, L and V are 0-100 percent. Typing is parsed as it changes, so a
/// valid value updates the picker immediately; out-of-range values are clamped, the text is
/// rewritten from the value on Enter or when the focus leaves, and a value that cannot be parsed is
/// left alone until then and then put back. That is the convention
/// <see cref="NumericUpDown"/> uses. Enter commits and moves the focus on rather than staying
/// in the field, Escape puts the field back, and the arrow keys step by one while Page Up and Page
/// Down step by ten.
/// </para>
/// <para>
/// The hex field takes an HTML/CSS colour - <c>#RRGGBB</c> or the <c>#RGB</c> shorthand, with or
/// without the leading <c>#</c> and in either case - and writes it back as uppercase
/// <c>#RRGGBB</c>. An alpha form is not accepted: the picker edits the three colour channels, so
/// the alpha the colour already has is preserved and never shown or changed. A complete valid value
/// is applied while it is typed; an incomplete or malformed one leaves the colour alone, and on
/// Enter or when the focus leaves the field is rewritten from the current colour.
/// </para>
/// <para>
/// The spectrum is drawn from the hue rather than blitted from a fixed bitmap: the square's image is
/// generated at the device resolution the canvas is painting at, and it is cached and regenerated
/// only when the hue, the space or the physical size changes. Painting the square, moving the marker,
/// hovering or focusing it reuses that image, so a repaint is a draw of one cached bitmap and a
/// marker; only dragging the hue strip pays for regeneration. The hue strip itself is a linear
/// gradient shader and needs no cache.
/// </para>
/// <para>
/// Pressing in the square focuses it and starts a drag; the value follows the pointer, and a drag
/// that leaves the square is clamped to its edge and continues until the button is released. The
/// arrow keys move saturation by one percent and value or lightness by one percent, Page Up and
/// Page Down move value or lightness by ten, and Home and End put saturation at its ends. The hue
/// strip moves one degree per arrow press, ten per page press, and Home and End jump to 0 and 359.
/// Tab visits the square, the hue strip, the three component fields and then the hex field.
/// </para>
/// </remarks>
public class ColorPicker : Control
{
    private const float HueStripWidth = 18f;
    private const float Gap = 6f;
    private const float RowHeight = 30f;
    private const float LabelWidth = 30f;
    private const float SectionGap = 8f;
    private const float PreferredSquare = 168f;
    private const float NarrowLayoutWidth = 210f;
    private const int HueMaximum = 359;

    private static readonly string[] RgbLabels = ["R", "G", "B"];
    private static readonly string[] HslLabels = ["H\u00B0", "S%", "L%"];
    private static readonly string[] HsvLabels = ["H\u00B0", "S%", "V%"];

    private readonly SKRect[] _labelRects = new SKRect[3];
    private readonly SpectrumView _spectrum;
    private readonly HueSlider _hueSlider;
    private readonly ComponentField[] _components = new ComponentField[3];
    private readonly HexField _hex;

    private Action<object>? _colorChanged;

    private SKColor _color = new(255, 0, 0);
    private int _hue;
    private ColorMode _mode = ColorMode.Rgb;
    private bool _isReadOnly;
    private bool _updatingFields;

    private SKRect _hexLabelRect;

    public ColorPicker()
    {
        TabStop = false;
        Focusable = false;
        Cursor = Cursor.Default;
        Padding = new Thickness(8f);
        MinimumSize = new SKSize(150f, 270f);
        BorderWidth = 1f;
        CornerRadius = 8f;

        _spectrum = new SpectrumView(this) { TabIndex = 0, Name = "Spectrum" };
        _hueSlider = new HueSlider(this) { TabIndex = 1, Name = "Hue" };

        for (int index = 0; index < _components.Length; index++)
        {
            _components[index] = new ComponentField(this, index)
            {
                TabIndex = 2 + index,
                Name = "Component" + (index + 1).ToString(CultureInfo.InvariantCulture),
            };
        }

        _hex = new HexField(this) { TabIndex = 5, Name = "Hex" };

        Controls.Add(_spectrum);
        Controls.Add(_hueSlider);
        foreach (var component in _components)
        {
            Controls.Add(component);
        }

        Controls.Add(_hex);

        SyncFields();
    }

    /// <summary>Creates a picker holding <paramref name="color"/>.</summary>
    public ColorPicker(SKColor color)
        : this()
    {
        Color = color;
    }

    /// <summary>Adds a handler that runs after <see cref="Color"/> has changed.</summary>
    /// <remarks>
    /// A mode change does not raise it: the colour did not change, only its representation did.
    /// </remarks>
    public IDisposable AddColorChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _colorChanged += handler;
        return new HandlerToken<Action<object>>(RemoveColorChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddColorChangedHandler"/> added.</summary>
    public void RemoveColorChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _colorChanged -= handler;
    }

    /// <summary>The selected colour. Defaults to opaque red.</summary>
    /// <remarks>
    /// The picker edits the three colour channels only; the alpha of the colour it is given is
    /// preserved through every mouse, keyboard, component and hex edit, and is never displayed.
    /// </remarks>
    public SKColor Color
    {
        get => _color;
        set
        {
            if (value == _color)
            {
                return;
            }

            _color = value;
            if (HasChroma(value))
            {
                _hue = HueFromRgb(value);
            }

            AfterStateChange(colorChanged: true, except: null);
        }
    }

    /// <summary>
    /// Which colour space the component fields show. Defaults to <see cref="ColorMode.Rgb"/>.
    /// </summary>
    /// <remarks>
    /// This changes the three fields' labels, units, ranges and values, and it changes the square's
    /// vertical axis between value and lightness when moving to or from
    /// <see cref="ColorMode.Hsl"/>. It never changes <see cref="Color"/>, and it raises no
    /// <see cref="AddColorChangedHandler"/> notification.
    /// </remarks>
    public ColorMode ColorMode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            SyncFields();
            Invalidate();
        }
    }

    /// <summary>
    /// When true the picker displays its colour but refuses to change it. Defaults to false.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Control.Enabled"/>, a read-only picker still looks live: the spectrum
    /// and the fields paint in their normal colours, the fields can be focused and copied from, and
    /// programmatic assignment to <see cref="Color"/> still works. Only the user's edits are
    /// refused. A disabled picker paints the disabled palette, takes no input and is skipped by Tab.
    /// </remarks>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value)
            {
                return;
            }

            _isReadOnly = value;
            foreach (var component in _components)
            {
                component.ReadOnly = value;
            }

            _hex.ReadOnly = value;
        }
    }

    /// <summary>How many times the square's image has been generated, for the caching test.</summary>
    internal int SpectrumGenerationCount => _spectrum.GenerationCount;

    /// <summary>The label the three component fields show in the current mode.</summary>
    internal string ComponentLabelAt(int index) => LabelsFor(_mode)[index];

    protected override Background DefaultBackground =>
        Background.FromThemeColor(ThemeColor.Surface);

    protected override SKSize MeasureContent(SKSize availableSize) =>
        new(PreferredSquare + HueStripWidth + Gap + 50f, PreferredSquare + SectionGap + RowHeight + Gap + RowHeight);

    protected override void OnLayout()
    {
        var content = ContentRect;
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        bool stacked = content.Width < NarrowLayoutWidth;
        float fieldsHeight = stacked
            ? (RowHeight * 4f) + (Gap * 3f)
            : RowHeight + Gap + RowHeight;
        float squareWidth = content.Width - HueStripWidth - Gap;
        float squareHeight = content.Height - fieldsHeight - SectionGap;
        float side = MathF.Max(0f, MathF.Min(squareWidth, squareHeight));

        var square = new SKRect(content.Left, content.Top, content.Left + side, content.Top + side);
        _spectrum.Bounds = square;
        _hueSlider.Bounds = new SKRect(
            square.Right + Gap,
            content.Top,
            square.Right + Gap + HueStripWidth,
            content.Top + side);

        float top = square.Bottom + SectionGap;
        if (stacked)
        {
            for (int index = 0; index < _components.Length; index++)
            {
                PlaceRow(index, content.Left, content.Right, top);
                top += RowHeight + Gap;
            }

            PlaceHexRow(content.Left, content.Right, top);
        }
        else
        {
            float cell = content.Width / 3f;
            for (int index = 0; index < _components.Length; index++)
            {
                float left = content.Left + (cell * index);
                PlaceRow(index, left, left + cell - Gap, top);
            }

            PlaceHexRow(content.Left, content.Right, top + RowHeight + Gap);
        }
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        var labels = LabelsFor(_mode);
        var color = Enabled ? Theme.MutedForeground : Theme.DisabledForeground;

        for (int index = 0; index < labels.Length; index++)
        {
            if (_labelRects[index].Width > 0f)
            {
                TextRenderer.Draw(canvas, labels[index], Font, color, _labelRects[index], ContentAlignment.MiddleLeft);
            }
        }

        if (_hexLabelRect.Width > 0f)
        {
            TextRenderer.Draw(canvas, "Hex", Font, color, _hexLabelRect, ContentAlignment.MiddleLeft);
        }
    }

    /// <summary>The saturation and the vertical axis the current colour occupies, both 0-1.</summary>
    private (float Saturation, float Axis) SaturationAndAxis()
    {
        if (_mode == ColorMode.Hsl)
        {
            var hsl = ToHsl(_color);
            return (hsl.Saturation, hsl.Lightness);
        }

        var hsv = ToHsv(_color);
        return (hsv.Saturation, hsv.Value);
    }

    private void PlaceRow(int index, float left, float right, float top)
    {
        _labelRects[index] = new SKRect(left, top, left + LabelWidth, top + RowHeight);
        _components[index].Bounds = new SKRect(
            left + LabelWidth,
            top,
            MathF.Max(left + LabelWidth, right),
            top + RowHeight);
    }

    private void PlaceHexRow(float left, float right, float top)
    {
        _hexLabelRect = new SKRect(left, top, left + LabelWidth, top + RowHeight);
        _hex.Bounds = new SKRect(
            left + LabelWidth,
            top,
            MathF.Max(left + LabelWidth, right),
            top + RowHeight);
    }

    private void SetFromSquare(float saturation, float axis)
    {
        var color = ColorFromComponents(_hue, Math.Clamp(saturation, 0f, 1f), Math.Clamp(axis, 0f, 1f));
        ApplyColor(color, updateHue: false);
    }

    private SKColor ColorFromComponents(int hue, float saturation, float axis) =>
        _mode == ColorMode.Hsl
            ? FromHsl(hue, saturation, axis, _color.Alpha)
            : FromHsv(hue, saturation, axis, _color.Alpha);

    private void SetHue(int hue)
    {
        int normalized = NormalizeHue(hue);
        if (normalized == _hue)
        {
            return;
        }

        int previous = _hue;
        _hue = normalized;

        var (saturation, axis) = SaturationAndAxis();
        var color = ColorFromComponents(_hue, saturation, axis);
        bool colorChanged = color != _color;
        bool hueChanged = _hue != previous;
        _color = color;

        if (!colorChanged && !hueChanged)
        {
            return;
        }

        AfterStateChange(colorChanged, except: null);
    }

    private void ApplyColor(SKColor color, bool updateHue, TextBox? except = null)
    {
        bool hueChanged = false;
        if (updateHue && HasChroma(color))
        {
            int hue = HueFromRgb(color);
            if (hue != _hue)
            {
                _hue = hue;
                hueChanged = true;
            }
        }

        bool colorChanged = color != _color;
        if (!colorChanged && !hueChanged)
        {
            return;
        }

        _color = color;
        AfterStateChange(colorChanged, except);
    }

    private void AfterStateChange(bool colorChanged, TextBox? except)
    {
        SyncFields(except);
        Invalidate();

        if (colorChanged)
        {
            _colorChanged?.Invoke(this);
        }
    }

    private void SyncFields(TextBox? except = null)
    {
        _updatingFields = true;
        try
        {
            for (int index = 0; index < _components.Length; index++)
            {
                if (!ReferenceEquals(_components[index], except))
                {
                    _components[index].Sync(ComponentValue(index));
                }
            }

            if (!ReferenceEquals(_hex, except))
            {
                _hex.Sync(HexText);
            }
        }
        finally
        {
            _updatingFields = false;
        }
    }

    private int ComponentValue(int index)
    {
        if (_mode == ColorMode.Rgb)
        {
            return index switch
            {
                0 => _color.Red,
                1 => _color.Green,
                _ => _color.Blue,
            };
        }

        if (index == 0)
        {
            return _hue;
        }

        var (saturation, axis) = SaturationAndAxis();
        return index == 1 ? ToPercent(saturation) : ToPercent(axis);
    }

    private int ComponentMinimum(int index) => 0;

    private int ComponentMaximum(int index) =>
        _mode == ColorMode.Rgb ? 255 : (index == 0 ? HueMaximum : 100);

    private string HexText => $"#{_color.Red:X2}{_color.Green:X2}{_color.Blue:X2}";

    private void OnComponentTextChanged(ComponentField field, int index)
    {
        if (_updatingFields)
        {
            return;
        }

        if (!TryParseComponent(field.Text, out int value))
        {
            return;
        }

        ApplyComponent(index, value, field);
    }

    private void ApplyComponent(int index, int value, TextBox except)
    {
        value = Math.Clamp(value, ComponentMinimum(index), ComponentMaximum(index));

        if (_mode == ColorMode.Rgb)
        {
            int red = index == 0 ? value : _color.Red;
            int green = index == 1 ? value : _color.Green;
            int blue = index == 2 ? value : _color.Blue;
            ApplyColor(new SKColor((byte)red, (byte)green, (byte)blue, _color.Alpha), updateHue: true, except);
            return;
        }

        int previousHue = _hue;
        var (saturation, axis) = SaturationAndAxis();

        if (index == 0)
        {
            _hue = NormalizeHue(value);
        }
        else if (index == 1)
        {
            saturation = value / 100f;
        }
        else
        {
            axis = value / 100f;
        }

        var color = ColorFromComponents(_hue, saturation, axis);
        bool colorChanged = color != _color;
        bool hueChanged = _hue != previousHue;
        if (!colorChanged && !hueChanged)
        {
            return;
        }

        _color = color;
        AfterStateChange(colorChanged, except);
    }

    private void CommitComponent(ComponentField field, int index, bool apply = true)
    {
        if (apply && TryParseComponent(field.Text, out int value))
        {
            ApplyComponent(index, value, field);
        }

        field.Sync(ComponentValue(index));
        if (field.IsFocused)
        {
            field.SelectAll();
        }
    }

    private void StepComponent(ComponentField field, int index, int delta)
    {
        if (IsReadOnly || !Enabled)
        {
            return;
        }

        int value = Math.Clamp(
            ComponentValue(index) + delta,
            ComponentMinimum(index),
            ComponentMaximum(index));
        ApplyComponent(index, value, field);
        field.Sync(ComponentValue(index));
        if (field.IsFocused)
        {
            field.SelectAll();
        }
    }

    private void OnHexTextChanged()
    {
        if (_updatingFields)
        {
            return;
        }

        if (!TryParseHex(_hex.Text, out var color))
        {
            return;
        }

        ApplyColor(new SKColor(color.Red, color.Green, color.Blue, _color.Alpha), updateHue: true, except: _hex);
    }

    private void CommitHex()
    {
        if (TryParseHex(_hex.Text, out var color))
        {
            ApplyColor(new SKColor(color.Red, color.Green, color.Blue, _color.Alpha), updateHue: true, except: _hex);
        }

        _hex.Sync(HexText);
        if (_hex.IsFocused)
        {
            _hex.SelectAll();
        }
    }

    private static bool TryParseComponent(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);

    private static bool TryParseHex(string? text, out SKColor color)
    {
        color = default;
        if (text is null)
        {
            return false;
        }

        string value = text.Trim();
        if (value.Length > 0 && value[0] == '#')
        {
            value = value[1..];
        }

        if (value.Length == 3)
        {
            value = $"{value[0]}{value[0]}{value[1]}{value[1]}{value[2]}{value[2]}";
        }

        if (value.Length != 6 ||
            !int.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int red) ||
            !int.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int green) ||
            !int.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int blue))
        {
            return false;
        }

        color = new SKColor((byte)red, (byte)green, (byte)blue);
        return true;
    }

    private static string[] LabelsFor(ColorMode mode) => mode switch
    {
        ColorMode.Hsl => HslLabels,
        ColorMode.Hsv => HsvLabels,
        _ => RgbLabels,
    };

    private static bool HasChroma(SKColor color) =>
        color.Red != color.Green || color.Green != color.Blue;

    private static int NormalizeHue(int hue) => ((hue % 360) + 360) % 360;

    private static int HueFromRgb(SKColor color)
    {
        int max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        int min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        int delta = max - min;
        if (delta == 0)
        {
            return 0;
        }

        float hue;
        if (max == color.Red)
        {
            hue = (color.Green - color.Blue) / (float)delta;
        }
        else if (max == color.Green)
        {
            hue = ((color.Blue - color.Red) / (float)delta) + 2f;
        }
        else
        {
            hue = ((color.Red - color.Green) / (float)delta) + 4f;
        }

        hue *= 60f;
        if (hue < 0f)
        {
            hue += 360f;
        }

        return NormalizeHue((int)MathF.Round(hue));
    }

    private static (int Hue, float Saturation, float Value) ToHsv(SKColor color)
    {
        float red = color.Red / 255f;
        float green = color.Green / 255f;
        float blue = color.Blue / 255f;
        float max = MathF.Max(red, MathF.Max(green, blue));
        float min = MathF.Min(red, MathF.Min(green, blue));
        float delta = max - min;

        float saturation = max <= 0f ? 0f : delta / max;
        return (HueFromRgb(color), saturation, max);
    }

    private static (int Hue, float Saturation, float Lightness) ToHsl(SKColor color)
    {
        float red = color.Red / 255f;
        float green = color.Green / 255f;
        float blue = color.Blue / 255f;
        float max = MathF.Max(red, MathF.Max(green, blue));
        float min = MathF.Min(red, MathF.Min(green, blue));
        float delta = max - min;
        float lightness = (max + min) / 2f;
        float saturation = delta <= 0f
            ? 0f
            : delta / (1f - MathF.Abs((2f * lightness) - 1f));

        return (HueFromRgb(color), saturation, lightness);
    }

    private static SKColor FromHsv(int hue, float saturation, float value, byte alpha = 255)
    {
        float h = NormalizeHue(hue) / 60f;
        int sector = (int)MathF.Floor(h) % 6;
        float fraction = h - MathF.Floor(h);
        float p = value * (1f - saturation);
        float q = value * (1f - (saturation * fraction));
        float t = value * (1f - (saturation * (1f - fraction)));

        (float red, float green, float blue) = sector switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };

        return new SKColor(ToByte(red), ToByte(green), ToByte(blue), alpha);
    }

    private static SKColor FromHsl(int hue, float saturation, float lightness, byte alpha = 255)
    {
        float h = NormalizeHue(hue) / 60f;
        float chroma = (1f - MathF.Abs((2f * lightness) - 1f)) * saturation;
        float second = chroma * (1f - MathF.Abs((h % 2f) - 1f));
        float match = lightness - (chroma / 2f);

        (float red, float green, float blue) = ((int)MathF.Floor(h) % 6) switch
        {
            0 => (chroma, second, 0f),
            1 => (second, chroma, 0f),
            2 => (0f, chroma, second),
            3 => (0f, second, chroma),
            4 => (second, 0f, chroma),
            _ => (chroma, 0f, second),
        };

        return new SKColor(ToByte(red + match), ToByte(green + match), ToByte(blue + match), alpha);
    }

    /// <summary>Rounds a 0-1 channel to a byte, with a half rounding up rather than to even.</summary>
    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Floor((value * 255f) + 0.5f), 0, 255);

    private static int ToPercent(float value) =>
        Math.Clamp((int)MathF.Floor((value * 100f) + 0.5f), 0, 100);

    /// <summary>The square: saturation across, value or lightness down, with the selected marker.</summary>
    private sealed class SpectrumView : Control
    {
        private readonly ColorPicker _owner;

        private SKBitmap? _bitmap;
        private SKColor[]? _pixels;
        private int _keyWidth;
        private int _keyHeight;
        private int _keyHue = -1;
        private bool _keyHsl;
        private bool _dragging;

        public SpectrumView(ColorPicker owner)
        {
            _owner = owner;
            TabStop = true;
            Cursor = Cursor.Cross;
            Padding = new Thickness(0f);
            MinimumSize = new SKSize(40f, 40f);
        }

        internal int GenerationCount { get; private set; }

        public override Cursor GetCursorAt(SKPoint pointInLocal) => Cursor.Cross;

        protected override void OnPaint(SKCanvas canvas, SKRect rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                return;
            }

            if (!Enabled)
            {
                Painter.Fill(canvas, rect, Theme.DisabledBackground);
                Painter.DrawBorder(canvas, rect, Theme.DisabledForeground, 1f);
                return;
            }

            EnsureImage(canvas, rect);

            if (_bitmap is not null)
            {
                using var paint = new SKPaint { IsAntialias = false };
                canvas.DrawBitmap(
                    _bitmap,
                    rect,
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
                    paint);
            }

            PaintMarker(canvas, rect);
            Painter.DrawBorder(canvas, rect, Theme.Border, 1f);

            if (IsFocused)
            {
                Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 2f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left || !Enabled)
            {
                return;
            }

            Focus();
            if (_owner.IsReadOnly)
            {
                return;
            }

            _dragging = true;
            IsPressed = true;
            CaptureMouse();
            SetFromPoint(e.Location);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragging && Enabled && !_owner.IsReadOnly)
            {
                SetFromPoint(e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_dragging)
            {
                _dragging = false;
                IsPressed = false;
                ReleaseMouseCapture();
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || !Enabled || _owner.IsReadOnly)
            {
                return;
            }

            var (saturation, axis) = _owner.SaturationAndAxis();
            const float step = 0.01f;
            bool handled = true;

            switch (e.Key)
            {
                case Key.Left:
                    saturation -= step;
                    break;

                case Key.Right:
                    saturation += step;
                    break;

                case Key.Up:
                    axis += step;
                    break;

                case Key.Down:
                    axis -= step;
                    break;

                case Key.Home:
                    saturation = 0f;
                    break;

                case Key.End:
                    saturation = 1f;
                    break;

                case Key.PageUp:
                    axis += step * 10f;
                    break;

                case Key.PageDown:
                    axis -= step * 10f;
                    break;

                default:
                    handled = false;
                    break;
            }

            if (!handled)
            {
                return;
            }

            _owner.SetFromSquare(saturation, axis);
            e.Handled = true;
        }

        protected override void OnLostFocus()
        {
            base.OnLostFocus();
            EndDrag();
        }

        protected override void OnEnabledChanged()
        {
            if (!Enabled)
            {
                EndDrag();
            }

            base.OnEnabledChanged();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bitmap?.Dispose();
                _bitmap = null;
                _pixels = null;
            }

            base.Dispose(disposing);
        }

        private void EndDrag()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            IsPressed = false;
            ReleaseMouseCapture();
            Invalidate();
        }

        private void SetFromPoint(SKPoint point)
        {
            var rect = ClientRect;
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                return;
            }

            float saturation = Math.Clamp(
                (point.X - rect.Left) / MathF.Max(1f, rect.Width - 1f),
                0f,
                1f);
            float t = Math.Clamp(
                (point.Y - rect.Top) / MathF.Max(1f, rect.Height - 1f),
                0f,
                1f);

            _owner.SetFromSquare(saturation, 1f - t);
        }

        /// <summary>
        /// Generates the square's image at the canvas's device resolution when the hue, the space
        /// or the physical size changed, and otherwise reuses the image the last paint generated.
        /// </summary>
        private void EnsureImage(SKCanvas canvas, SKRect rect)
        {
            float scaleX = MathF.Abs(canvas.TotalMatrix.ScaleX);
            float scaleY = MathF.Abs(canvas.TotalMatrix.ScaleY);
            if (scaleX <= 0f)
            {
                scaleX = 1f;
            }

            if (scaleY <= 0f)
            {
                scaleY = 1f;
            }

            int width = Math.Max(1, (int)MathF.Round(rect.Width * scaleX));
            int height = Math.Max(1, (int)MathF.Round(rect.Height * scaleY));
            bool hsl = _owner._mode == ColorMode.Hsl;
            int hue = _owner._hue;

            if (_bitmap is not null &&
                width == _keyWidth &&
                height == _keyHeight &&
                hue == _keyHue &&
                hsl == _keyHsl)
            {
                return;
            }

            if (_bitmap is null || _bitmap.Width != width || _bitmap.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                _pixels = new SKColor[width * height];
            }

            var pixels = _pixels!;
            for (int y = 0; y < height; y++)
            {
                float t = height == 1 ? 0f : y / (float)(height - 1);
                float axis = 1f - t;
                int row = y * width;

                for (int x = 0; x < width; x++)
                {
                    float saturation = width == 1 ? 0f : x / (float)(width - 1);
                    pixels[row + x] = hsl
                        ? FromHsl(hue, saturation, axis)
                        : FromHsv(hue, saturation, axis);
                }
            }

            _bitmap.Pixels = pixels;
            _keyWidth = width;
            _keyHeight = height;
            _keyHue = hue;
            _keyHsl = hsl;
            GenerationCount++;
        }

        private void PaintMarker(SKCanvas canvas, SKRect rect)
        {
            var (saturation, axis) = _owner.SaturationAndAxis();
            float radius = IsPressed ? 7f : IsMouseOver ? 6f : 5f;
            radius = MathF.Min(radius, (MathF.Min(rect.Width, rect.Height) / 2f) - 1f);
            if (radius <= 0f)
            {
                return;
            }

            float travelX = MathF.Max(0f, rect.Width - (radius * 2f));
            float travelY = MathF.Max(0f, rect.Height - (radius * 2f));
            float centerX = rect.Left + radius + (Math.Clamp(saturation, 0f, 1f) * travelX);
            float centerY = rect.Top + radius + ((1f - Math.Clamp(axis, 0f, 1f)) * travelY);

            Painter.Fill(
                canvas,
                new SKRect(centerX - radius - 1f, centerY - radius - 1f, centerX + radius + 1f, centerY + radius + 1f),
                new SKColor(0, 0, 0, 0x99),
                radius + 1f);
            Painter.Fill(
                canvas,
                new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius),
                SKColors.White,
                radius);
        }
    }

    /// <summary>The third axis: a vertical hue strip with a movable marker.</summary>
    private sealed class HueSlider : Control
    {
        private static readonly SKColor[] HueStops =
        [
            SKColors.Red,
            SKColors.Yellow,
            SKColors.Green,
            SKColors.Cyan,
            SKColors.Blue,
            SKColors.Magenta,
            SKColors.Red,
        ];

        private readonly ColorPicker _owner;
        private bool _dragging;

        public HueSlider(ColorPicker owner)
        {
            _owner = owner;
            TabStop = true;
            Cursor = Cursor.SizeNS;
            Padding = new Thickness(0f);
            MinimumSize = new SKSize(10f, 40f);
        }

        public override Cursor GetCursorAt(SKPoint pointInLocal) => Cursor.SizeNS;

        protected override void OnPaint(SKCanvas canvas, SKRect rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                return;
            }

            if (!Enabled)
            {
                Painter.Fill(canvas, rect, Theme.DisabledBackground);
                Painter.DrawBorder(canvas, rect, Theme.DisabledForeground, 1f);
                return;
            }

            using (var shader = SKShader.CreateLinearGradient(
                new SKPoint(rect.MidX, rect.Top),
                new SKPoint(rect.MidX, rect.Bottom),
                HueStops,
                null,
                SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { Shader = shader, IsAntialias = false })
            {
                canvas.DrawRect(rect, paint);
            }

            PaintMarker(canvas, rect);
            Painter.DrawBorder(canvas, rect, Theme.Border, 1f);

            if (IsFocused)
            {
                Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 2f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left || !Enabled)
            {
                return;
            }

            Focus();
            if (_owner.IsReadOnly)
            {
                return;
            }

            _dragging = true;
            IsPressed = true;
            CaptureMouse();
            SetFromPoint(e.Location);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragging && Enabled && !_owner.IsReadOnly)
            {
                SetFromPoint(e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_dragging)
            {
                _dragging = false;
                IsPressed = false;
                ReleaseMouseCapture();
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || !Enabled || _owner.IsReadOnly)
            {
                return;
            }

            int hue = _owner._hue;
            bool handled = true;

            switch (e.Key)
            {
                case Key.Left:
                case Key.Down:
                    _owner.SetHue(hue - 1);
                    break;

                case Key.Right:
                case Key.Up:
                    _owner.SetHue(hue + 1);
                    break;

                case Key.PageDown:
                    _owner.SetHue(hue - 10);
                    break;

                case Key.PageUp:
                    _owner.SetHue(hue + 10);
                    break;

                case Key.Home:
                    _owner.SetHue(0);
                    break;

                case Key.End:
                    _owner.SetHue(HueMaximum);
                    break;

                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
            }
        }

        protected override void OnLostFocus()
        {
            base.OnLostFocus();
            EndDrag();
        }

        protected override void OnEnabledChanged()
        {
            if (!Enabled)
            {
                EndDrag();
            }

            base.OnEnabledChanged();
        }

        private void EndDrag()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            IsPressed = false;
            ReleaseMouseCapture();
            Invalidate();
        }

        private void SetFromPoint(SKPoint point)
        {
            var rect = ClientRect;
            if (rect.Height <= 0f)
            {
                return;
            }

            float t = Math.Clamp(
                (point.Y - rect.Top) / MathF.Max(1f, rect.Height - 1f),
                0f,
                1f);
            _owner.SetHue((int)MathF.Round(t * HueMaximum));
        }

        private void PaintMarker(SKCanvas canvas, SKRect rect)
        {
            float y = rect.Top + ((_owner._hue / (float)HueMaximum) * MathF.Max(1f, rect.Height - 1f));
            y = Math.Clamp(y, rect.Top + 2f, rect.Bottom - 2f);

            Painter.Fill(
                canvas,
                new SKRect(rect.Left, y - 2f, rect.Right, y + 2f),
                new SKColor(0, 0, 0, 0x99));
            Painter.Fill(
                canvas,
                new SKRect(rect.Left + 1f, y - 1f, rect.Right - 1f, y + 1f),
                SKColors.White);
        }
    }

    /// <summary>One of the three 0-255 or 0-359/0-100 component fields.</summary>
    private sealed class ComponentField : TextBox
    {
        private readonly ColorPicker _owner;
        private readonly int _index;
        private bool _syncing;

        public ComponentField(ColorPicker owner, int index)
        {
            _owner = owner;
            _index = index;
            TabStop = true;
            BorderWidth = 1f;
            CornerRadius = 5f;
            Padding = new Thickness(4f, 4f);
            MinimumSize = new SKSize(0f, 28f);

            // One character more than a valid value holds, so an out-of-range paste is clamped
            // rather than silently cut down to a different number.
            MaxLength = 4;
        }

        internal void Sync(int value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(Text, text, StringComparison.Ordinal))
            {
                return;
            }

            _syncing = true;
            try
            {
                Text = text;
            }
            finally
            {
                _syncing = false;
            }
        }

        protected override void OnTextChanged()
        {
            base.OnTextChanged();

            if (!_syncing)
            {
                _owner.OnComponentTextChanged(this, _index);
            }
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            if (e.Handled || ReadOnly)
            {
                base.OnTextInput(e);
                return;
            }

            foreach (char character in e.Text)
            {
                if (character is < '0' or > '9')
                {
                    e.Handled = true;
                    return;
                }
            }

            base.OnTextInput(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    _owner.CommitComponent(this, _index);
                    Host?.ProcessKeyDown(Key.Tab, Modifiers.None);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    _owner.CommitComponent(this, _index, apply: false);
                    e.Handled = true;
                    break;

                case Key.Up:
                    _owner.StepComponent(this, _index, 1);
                    e.Handled = true;
                    break;

                case Key.Down:
                    _owner.StepComponent(this, _index, -1);
                    e.Handled = true;
                    break;

                case Key.PageUp:
                    _owner.StepComponent(this, _index, 10);
                    e.Handled = true;
                    break;

                case Key.PageDown:
                    _owner.StepComponent(this, _index, -10);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnGotFocus()
        {
            base.OnGotFocus();
            SelectAll();
        }

        protected override void OnLostFocus()
        {
            base.OnLostFocus();
            _owner.CommitComponent(this, _index);
        }
    }

    /// <summary>The HTML/CSS hex field.</summary>
    private sealed class HexField : TextBox
    {
        private readonly ColorPicker _owner;
        private bool _syncing;

        public HexField(ColorPicker owner)
        {
            _owner = owner;
            TabStop = true;
            BorderWidth = 1f;
            CornerRadius = 5f;
            Padding = new Thickness(4f, 4f);
            MinimumSize = new SKSize(0f, 28f);

            // One more than "#RRGGBB" holds, so a pasted alpha form reaches the parser and is
            // rejected rather than being cut down to a different, valid colour.
            MaxLength = 9;
            PlaceholderText = "#RRGGBB";
        }

        internal void Sync(string text)
        {
            if (string.Equals(Text, text, StringComparison.Ordinal))
            {
                return;
            }

            _syncing = true;
            try
            {
                Text = text;
            }
            finally
            {
                _syncing = false;
            }
        }

        protected override void OnTextChanged()
        {
            base.OnTextChanged();

            if (!_syncing)
            {
                _owner.OnHexTextChanged();
            }
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            if (e.Handled || ReadOnly)
            {
                base.OnTextInput(e);
                return;
            }

            foreach (char character in e.Text)
            {
                if (!char.IsAsciiHexDigit(character) && character != '#')
                {
                    e.Handled = true;
                    return;
                }
            }

            base.OnTextInput(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    _owner.CommitHex();
                    Host?.ProcessKeyDown(Key.Tab, Modifiers.None);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    Sync(_owner.HexText);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnGotFocus()
        {
            base.OnGotFocus();
            SelectAll();
        }

        protected override void OnLostFocus()
        {
            base.OnLostFocus();
            _owner.CommitHex();
        }
    }
}
