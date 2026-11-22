using SkiaSharp;

namespace Kolvir.Spans;

/// <summary>
/// One annotated run of characters in an <see cref="TextBox"/>: the range it covers and the
/// squiggly underline that marks it in the field, the way a spell or grammar checker marks a word.
/// </summary>
/// <remarks>
/// <para>
/// The base type holds what every annotation has to have - a start index, a length, and how the
/// squiggle is drawn - and is deliberately not sealed, so a consumer hangs its own payload on a
/// subclass: a suggestion list, a rule identifier, a severity.
/// </para>
/// <code>
/// public sealed class SpellingError(int start, int length, IReadOnlyList&lt;string&gt; suggestions)
///     : TextBoxSpan(start, length)
/// {
///     public IReadOnlyList&lt;string&gt; Suggestions { get; } = suggestions;
/// }
/// </code>
/// <para>
/// A span is a plain value holder that belongs to whoever created it, and it is <em>not</em> a
/// control: it never paints by itself, it holds no state of its own beyond what is declared here,
/// and the text box only ever reads it. See the <c>Spans</c> member of
/// <see cref="TextBoxSpanExtensions"/> for the contract on what happens when the text
/// changes under a set of spans.
/// </para>
/// <para>
/// <see cref="Start"/> and <see cref="Length"/> are fixed at construction so that a span can be
/// handed out as an identity - a popup that remembers the span it is offering suggestions for can
/// compare references - and so that a stale span cannot be quietly rewritten by the control.
/// </para>
/// </remarks>
public class TextBoxSpan
{
    /// <summary>
    /// The red a spelling check conventionally marks a word with. A span that assigns nothing
    /// inherits the field's text colour instead, so assign this to keep the familiar red.
    /// </summary>
    public static readonly SKColor DefaultColor = new(0xFFD32F2F);

    private const float DefaultThickness = 1f;
    private const float DefaultAmplitude = 1.5f;
    private const float DefaultWavelength = 4f;

    private Union<SKColor, ThemeColor, InheritedValue> _color = PropertyValue.Inherited;
    private float _thickness = DefaultThickness;
    private float _amplitude = DefaultAmplitude;
    private float _wavelength = DefaultWavelength;

    /// <summary>
    /// Marks <paramref name="length"/> characters from <paramref name="start"/>.
    /// </summary>
    /// <param name="start">
    /// The index of the first marked character. It may be past the end of the current text: a span
    /// is clamped where it is drawn, never rejected.
    /// </param>
    /// <param name="length">How many characters are marked. Zero marks nothing but is allowed.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="start"/> or <paramref name="length"/> is negative.
    /// </exception>
    public TextBoxSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Start = start;
        Length = length;
    }

    /// <summary>The index of the first character the span marks.</summary>
    public int Start { get; }

    /// <summary>How many characters the span marks.</summary>
    public int Length { get; }

    /// <summary>
    /// The index just past the last marked character, i.e. <see cref="Start"/> + <see cref="Length"/>.
    /// </summary>
    public int End => (int)Math.Min(int.MaxValue, (long)Start + Length);

    /// <summary>
    /// The squiggle's colour: a literal <see cref="SKColor"/>, a <see cref="ThemeColor"/> role, or
    /// <see cref="PropertyValue.Inherited"/> to draw in the field's
    /// <see cref="Control.EffectiveForeColor"/>.
    /// </summary>
    /// <remarks>
    /// The property reports what was assigned. Read <see cref="EffectiveColor"/> for the colour the
    /// squiggle is actually drawn in. The initial value is
    /// <see cref="PropertyValue.Inherited"/>; a checker that wants the conventional red assigns
    /// <see cref="DefaultColor"/>.
    /// </remarks>
    public Union<SKColor, ThemeColor, InheritedValue> Color
    {
        get => _color;
        set => _color = value;
    }

    /// <summary>
    /// The colour the squiggle is drawn in, resolved when it is read so a theme change applies to
    /// the next frame rather than the next time the span is built.
    /// </summary>
    /// <param name="owner">The field the span is drawn in, whose text colour an inherited span takes.</param>
    /// <returns>The colour, alpha included.</returns>
    /// <remarks>
    /// A literal is returned as it is, a role is resolved through <see cref="Theme.ColorOf"/> on
    /// every read, and <see cref="PropertyValue.Inherited"/> answers with the field's own
    /// <see cref="Control.EffectiveForeColor"/>. The field is passed in rather than remembered
    /// because a span is caller-owned and may be handed to more than one box.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Color"/> was assigned <c>default</c>, so it holds no case.
    /// </exception>
    public SKColor EffectiveColor(TextBox owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _color.ThrowIfUnspecified();

        if (_color.TryGetValue<SKColor>(out var literal))
        {
            return literal;
        }

        return _color.TryGetValue<ThemeColor>(out var role)
            ? Theme.ColorOf(role)
            : owner.EffectiveForeColor;
    }

    /// <summary>
    /// The width of the squiggle's stroke, in logical pixels. One is the usual weight; a warning
    /// that has to be quieter than an error can be thinner.
    /// </summary>
    public float Thickness
    {
        get => _thickness;
        set => _thickness = float.IsFinite(value) ? MathF.Max(0.5f, value) : DefaultThickness;
    }

    /// <summary>
    /// How far the squiggle rises above and falls below its own centre line, in logical pixels. A
    /// peak-to-peak height of twice this value is what gives the wave its shape; zero draws a
    /// straight underline instead, which is how some editors mark a warning rather than an error.
    /// </summary>
    /// <remarks>
    /// The wave is drawn in the space the font reserves under the baseline, so a value too large to
    /// fit there at the current size is reduced rather than allowed to climb into the letters.
    /// </remarks>
    public float Amplitude
    {
        get => _amplitude;
        set => _amplitude = float.IsFinite(value) ? MathF.Max(0f, value) : DefaultAmplitude;
    }

    /// <summary>
    /// The horizontal distance of one full up-and-down cycle of the wave, in logical pixels. A
    /// smaller value gives a tighter, more obviously "spelling" squiggle; a larger one reads as a
    /// gentler ripple.
    /// </summary>
    public float Wavelength
    {
        get => _wavelength;
        set => _wavelength = float.IsFinite(value) ? MathF.Max(1f, value) : DefaultWavelength;
    }

    /// <summary>Arbitrary data the consumer associates with the span.</summary>
    public object? Tag { get; set; }
}
