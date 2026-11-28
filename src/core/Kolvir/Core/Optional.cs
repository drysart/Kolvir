namespace Kolvir;

/// <summary>
/// An argument that may have been omitted: the value it carries, and the record of whether the
/// caller supplied it at all.
/// </summary>
/// <typeparam name="T">
/// The argument's type. A nullable type argument - <c>Optional&lt;string?&gt;</c> - is what lets a
/// supplied <c>null</c> be a value of its own, distinct from an omitted argument.
/// </typeparam>
/// <remarks>
/// <para>
/// A method that takes a plain <c>T</c> cannot tell an omitted argument from one passed as
/// <c>default</c>, and a method that takes a <c>T?</c> cannot tell either of those from a supplied
/// <c>null</c>. This type answers all three: an omitted argument is <c>default(Optional&lt;T&gt;)</c>,
/// a supplied value arrives through the implicit conversion below, and a supplied <c>null</c> arrives
/// as a populated <see cref="Optional{T}"/> whose <see cref="Value"/> is null. The flag is what
/// separates the last two, so <see cref="IsSpecified"/> - not a null check - is the thing to read.
/// </para>
/// <para>
/// The parameter has to be this non-nullable struct, not <c>Optional&lt;T&gt;?</c>. On a nullable
/// parameter the natural default, <c>default(Optional&lt;T&gt;)</c>, is not expressible: CS1770 says
/// it "is not a simple type". The spellings that do compile collapse states - <c>= default</c> and
/// <c>= null</c> are the same value for <c>Optional&lt;T&gt;?</c> - so an omitted argument and an
/// explicit <c>null</c> would be indistinguishable, which is the one distinction the type exists to
/// make. With a non-nullable <see cref="Optional{T}"/> there is no fourth state to lose.
/// </para>
/// <para>
/// It is the argument wrapper for <c>Font.With</c>, and its purpose is narrow: a complete font has
/// no inherited state, so its arguments are only supplied or not. The partial types -
/// <c>FontOverride.With</c> and <c>InheritedFont.With</c> - take
/// <c>Union&lt;T?, InheritedValue&gt;</c> instead, because they can also say "clear this member back
/// to inherited". This type is not <c>Specified&lt;T&gt;</c> in <c>Kolvir.Feature.VirtualDom</c>:
/// that type is machinery a generated node class holds per tracked property and is always
/// constructed explicitly, while this one is a calling convention whose whole job is the implicit
/// conversion at a call site. The two share the word for the flag on purpose and are otherwise not
/// interchangeable; see section 6.1 of <c>dev-docs/VIRTUAL-DOM.md</c> for the comparison.
/// </para>
/// </remarks>
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    private readonly T? _value;

    /// <summary>Records that the caller supplied an argument, and the value it supplied.</summary>
    /// <param name="value">The supplied value, which may be null when <typeparamref name="T"/> permits it.</param>
    public Optional(T value)
    {
        _value = value;
        IsSpecified = true;
    }

    /// <summary>The supplied value, or <c>default</c> when the argument was omitted.</summary>
    /// <remarks>
    /// A supplied <c>null</c> answers null, so <see cref="IsSpecified"/> is what tells the two apart:
    /// reading this without checking it turns "omitted" into an ordinary null.
    /// </remarks>
    public T Value => _value!;

    /// <summary>
    /// True when the caller supplied the argument, including when it supplied <c>null</c>.
    /// </summary>
    public bool IsSpecified { get; }

    /// <summary>Populates an argument with <paramref name="value"/>.</summary>
    /// <param name="value">The value the caller supplied.</param>
    /// <returns>A specified <see cref="Optional{T}"/> holding the value.</returns>
    /// <remarks>
    /// This is what makes <c>With(weight: 700)</c> and <c>With(family: null)</c> read like ordinary
    /// arguments: the value is converted implicitly, and a null literal converts when the type
    /// argument is nullable and does not convert when it is not.
    /// </remarks>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <summary>Compares two arguments, treating "omitted" as a state rather than as a value.</summary>
    /// <param name="other">The other argument.</param>
    /// <returns>
    /// True when both were omitted, or both were supplied the same value.
    /// </returns>
    public bool Equals(Optional<T> other) =>
        IsSpecified == other.IsSpecified
            && EqualityComparer<T>.Default.Equals(_value!, other._value!);

    /// <summary>Whether <paramref name="obj"/> is the same argument.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is an equal <see cref="Optional{T}"/>.</returns>
    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    /// <summary>A hash code over the flag and the value.</summary>
    /// <returns>The hash code, which agrees with <see cref="Equals(Optional{T})"/>.</returns>
    public override int GetHashCode() => IsSpecified ? HashCode.Combine(true, _value) : 0;

    /// <summary>Compares two arguments.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they are the same argument.</returns>
    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    /// <summary>Compares two arguments.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they are different arguments.</returns>
    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);

    /// <summary>The argument, for debuggers and log lines.</summary>
    /// <returns>
    /// <c>unspecified</c> when the argument was omitted, otherwise the value's text - and
    /// <c>null</c> for a supplied null.
    /// </returns>
    public override string ToString() =>
        !IsSpecified ? "unspecified" : _value is null ? "null" : _value.ToString()!;
}
