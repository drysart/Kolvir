using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Kolvir.VirtualDom;

/// <summary>
/// A property value together with the record of whether it was assigned at all.
/// </summary>
/// <typeparam name="T">The property's type.</typeparam>
/// <remarks>
/// <para>
/// This is the machinery behind a node's tracked properties, not something an application writes:
/// generated node classes hold one of these per property, and exposing it publicly is what lets
/// generated code in a consuming assembly use the same type the library does. It is hidden from
/// IntelliSense for that reason.
/// </para>
/// <para>
/// The zero value means "not assigned", which matters more than it looks: <c>default</c> is what a
/// struct field, an array element and an uninitialised local all produce, so a flag whose zero value
/// meant "assigned" would misread every one of them. Since <see cref="Value"/> is meaningless until
/// the property has been assigned, <see cref="IsSpecified"/> is what a reader checks first.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Specified<T> : IEquatable<Specified<T>>
{
    private readonly T? _value;

    /// <summary>Records that a property was assigned a value.</summary>
    /// <param name="value">The value it was assigned.</param>
    public Specified(T value)
    {
        _value = value;
        IsSpecified = true;
    }

    /// <summary>The assigned value, or <c>default</c> when the property was not assigned.</summary>
    public T Value => _value!;

    /// <summary>True when the property was assigned, including assigned <c>null</c>.</summary>
    public bool IsSpecified { get; }

    /// <summary>
    /// Answers whether two tracked properties hold the same thing, treating "not assigned" as a
    /// distinct state rather than as the default value.
    /// </summary>
    /// <param name="other">The other tracked property.</param>
    /// <returns>True when both were assigned the same value, or neither was assigned.</returns>
    public bool Equals(Specified<T> other) =>
        IsSpecified == other.IsSpecified
            && EqualityComparer<T>.Default.Equals(_value!, other._value!);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) =>
        obj is Specified<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        IsSpecified ? HashCode.Combine(true, _value) : 0;

    /// <summary>Records that a property was assigned, so a generated setter stays one assignment.</summary>
    /// <param name="value">The value being assigned, which may be <c>null</c> where the property allows it.</param>
    public static implicit operator Specified<T>(T value) => new(value);

    /// <summary>Compares two tracked properties.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they track the same thing.</returns>
    public static bool operator ==(Specified<T> left, Specified<T> right) => left.Equals(right);

    /// <summary>Compares two tracked properties.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they track different things.</returns>
    public static bool operator !=(Specified<T> left, Specified<T> right) => !left.Equals(right);
}
