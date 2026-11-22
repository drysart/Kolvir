using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Kolvir;

/// <summary>
/// A value that holds exactly one of two case types at a time: the first, the second, or - until
/// something is assigned - neither.
/// </summary>
/// <typeparam name="T1">The first case type.</typeparam>
/// <typeparam name="T2">The second case type.</typeparam>
/// <remarks>
/// <para>
/// Assign a value of either case type directly to a variable or property of this type, or construct
/// it explicitly with <see cref="Union{T1, T2}(T1)"/> or <see cref="Union{T1, T2}(T2)"/>. Read the
/// value back with <see cref="TryGetValue{T}"/>, which answers only for the case type that is
/// stored, and dispatch on it with <see cref="Switch(Action{T1}, Action{T2})"/>,
/// <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2})"/> or
/// <see cref="Match{TState, TResult}(TState, Func{TState, T1, TResult}, Func{TState, T2, TResult})"/>.
/// </para>
/// <para>
/// The two case types have to be distinct: two identical case types would mean two constructors and
/// two conversion operators with the same signature, which does not compile.
/// </para>
/// <para>
/// This type knows nothing about inheritance: a case such as <see cref="InheritedValue"/> is a case
/// like any other here, and deciding that it means "the inherited value" is the caller's job.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Union&lt;SKColor, ThemeColor&gt; colour = ThemeColor.MutedForeground;
///
/// string text = colour.Match(
///     0,
///     static (_, SKColor direct) =&gt; direct.ToString(),
///     static (_, ThemeColor role) =&gt; role.ToString());
/// </code>
/// </example>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct Union<T1, T2> : IEquatable<Union<T1, T2>>
{
    private const byte ArmFirst = 1;
    private const byte ArmSecond = 2;

    private readonly byte _arm;
    private readonly T1 _first;
    private readonly T2 _second;

    /// <summary>A union holding <paramref name="value"/> as its first case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T1 value)
    {
        this = default;
        _arm = ArmFirst;
        _first = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its second case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T2 value)
    {
        this = default;
        _arm = ArmSecond;
        _second = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its first case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the first case.</returns>
    public static implicit operator Union<T1, T2>(T1 value) => new(value);

    /// <summary>A union holding <paramref name="value"/> as its second case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the second case.</returns>
    public static implicit operator Union<T1, T2>(T2 value) => new(value);

    /// <summary>
    /// Whether this union was never assigned, so it holds none of its case types.
    /// </summary>
    /// <remarks>
    /// A union that has never been assigned is <c>default</c>, and a <c>default</c> union has no
    /// case. This property is how a caller checks for the case before reading anything;
    /// <see cref="ThrowIfUnspecified"/> is the way to refuse it.
    /// </remarks>
    /// <value><c>true</c> for <c>default</c>; otherwise <c>false</c>.</value>
    public bool IsUnspecified => _arm == 0;

    /// <summary>Throws when this union was never assigned, and therefore holds no case.</summary>
    /// <remarks>
    /// Call this at the top of anything that cannot do without a case - a paint method, a handler -
    /// so a property that was never set fails where it is used rather than silently doing nothing.
    /// <see cref="Switch(Action{T1}, Action{T2})"/>,
    /// <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2})"/> and
    /// <see cref="Match{TState, TResult}(TState, Func{TState, T1, TResult}, Func{TState, T2, TResult})"/>
    /// call it themselves.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This union is unspecified, which means it is default-initialised, so it is not one of its
    /// case types.
    /// </exception>
    public void ThrowIfUnspecified()
    {
        if (_arm == 0)
        {
            throw new InvalidOperationException(
                $"{this} has no case: it is default-initialised, and was never assigned. Assign a value before reading it.");
        }
    }

    /// <summary>Reads the held value when it is of exactly type <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// <para>
    /// The check is against each case type exactly, never against a type it is assignable to. In
    /// <c>Union&lt;object, string&gt;</c> holding a <c>string</c>, this returns <c>true</c> only for
    /// <c>TryGetValue&lt;string&gt;</c>; <c>TryGetValue&lt;object&gt;</c> is not a case type and
    /// throws. The answer therefore never depends on the order of the cases.
    /// </para>
    /// <para>
    /// Asking for a type that is not one of the union's case types is a coding error and throws,
    /// rather than quietly returning <c>false</c>, which would hide a mistake in a handler. Asking
    /// for a case type while a different case is stored returns <c>false</c>, which is an ordinary
    /// answer.
    /// </para>
    /// <para>
    /// A case may hold a null reference - assigning <c>null</c> to a reference-typed case is a valid
    /// assignment - so <paramref name="value"/> can be <c>null</c> while this returns <c>true</c>.
    /// </para>
    /// <para>
    /// This method does not box or allocate.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The case type to read. It has to be one of this union's case types.</typeparam>
    /// <param name="value">The held value when this returns <c>true</c>; otherwise <c>default</c>.</param>
    /// <returns><c>true</c> when the stored case is exactly type <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="T"/> is not one of this union's case types.
    /// </exception>
    public bool TryGetValue<T>([MaybeNullWhen(false)] out T value)
    {
        if (typeof(T) == typeof(T1))
        {
            if (_arm == ArmFirst)
            {
                value = Unsafe.As<T1, T>(ref Unsafe.AsRef(in _first));
                return true;
            }
        }
        else if (typeof(T) == typeof(T2))
        {
            if (_arm == ArmSecond)
            {
                value = Unsafe.As<T2, T>(ref Unsafe.AsRef(in _second));
                return true;
            }
        }
        else
        {
            throw new ArgumentException(
                $"Union<{typeof(T1).Name}, {typeof(T2).Name}> has no case of type {typeof(T).Name}, so TryGetValue<{typeof(T).Name}> cannot answer. Ask for one of {typeof(T1).Name}, {typeof(T2).Name}.");
        }

        value = default;
        return false;
    }

    /// <summary>Runs the handler for the case that is stored.</summary>
    /// <remarks>
    /// One handler per case type, so a handler that is missing fails to compile rather than being
    /// forgotten. Use the state-carrying overload when a handler needs a captured value and the
    /// handler should be a <c>static</c> lambda.
    /// </remarks>
    /// <param name="first">The handler for a value of the first case type.</param>
    /// <param name="second">The handler for a value of the second case type.</param>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to dispatch on.
    /// </exception>
    public void Switch(Action<T1> first, Action<T2> second)
    {
        ThrowIfUnspecified();

        switch (_arm)
        {
            case ArmFirst:
                first(_first);
                break;
            case ArmSecond:
                second(_second);
                break;
        }
    }

    /// <summary>Runs the handler for the case that is stored, passing it <paramref name="state"/>.</summary>
    /// <param name="state">The value passed to whichever handler runs.</param>
    /// <param name="first">The handler for a value of the first case type.</param>
    /// <param name="second">The handler for a value of the second case type.</param>
    /// <typeparam name="TState">The type of the value passed to the handler.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to dispatch on.
    /// </exception>
    public void Switch<TState>(TState state, Action<TState, T1> first, Action<TState, T2> second)
    {
        ThrowIfUnspecified();

        switch (_arm)
        {
            case ArmFirst:
                first(state, _first);
                break;
            case ArmSecond:
                second(state, _second);
                break;
        }
    }

    /// <summary>Computes a result from the case that is stored, passing it <paramref name="state"/>.</summary>
    /// <remarks>
    /// The returning form of <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2})"/>:
    /// one function per case type, and the stored case picks which one runs.
    /// </remarks>
    /// <typeparam name="TState">The type of the value passed to the function.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="state">The value passed to whichever function runs.</param>
    /// <param name="first">The function for a value of the first case type.</param>
    /// <param name="second">The function for a value of the second case type.</param>
    /// <returns>The result of the function for the stored case.</returns>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to match on.
    /// </exception>
    public TResult Match<TState, TResult>(TState state, Func<TState, T1, TResult> first, Func<TState, T2, TResult> second)
    {
        ThrowIfUnspecified();

        return _arm switch
        {
            ArmFirst => first(state, _first),
            _ => second(state, _second),
        };
    }

    /// <summary>Whether two unions are the same case holding equal payloads.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> when both are the same case and their values are equal.</returns>
    public static bool operator ==(Union<T1, T2> left, Union<T1, T2> right) => left.Equals(right);

    /// <summary>Whether two unions are not the same case, or their payloads differ.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> when either side differs.</returns>
    public static bool operator !=(Union<T1, T2> left, Union<T1, T2> right) => !left.Equals(right);

    /// <inheritdoc />
    /// <remarks>
    /// Two unions are equal when they hold the same case and the payloads compare equal with
    /// <see cref="EqualityComparer{T}.Default"/>; two default-initialised unions are equal to each
    /// other.
    /// </remarks>
    public bool Equals(Union<T1, T2> other)
    {
        if (_arm != other._arm)
        {
            return false;
        }

        return _arm switch
        {
            ArmFirst => EqualityComparer<T1>.Default.Equals(_first, other._first),
            ArmSecond => EqualityComparer<T2>.Default.Equals(_second, other._second),
            _ => true,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Union<T1, T2> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _arm switch
    {
        ArmFirst => HashCode.Combine(_arm, _first),
        ArmSecond => HashCode.Combine(_arm, _second),
        _ => 0,
    };

    /// <summary>The stored case and its value as text.</summary>
    /// <remarks>
    /// The first case reads as its type name, a dot and the value, which is how a named constant
    /// reads; every other case as its type name and the value in parentheses. A default-initialised
    /// union reads as <c>Union&lt;T1, T2&gt;(unspecified)</c>.
    /// </remarks>
    /// <returns>A string such as <c>ThemeColor.MutedForeground</c>, <c>SKColor(#FF404040)</c> or
    /// <c>Union&lt;SKColor, ThemeColor&gt;(unspecified)</c>.</returns>
    public override string ToString() => _arm switch
    {
        ArmFirst => $"{typeof(T1).Name}.{_first}",
        ArmSecond => $"{typeof(T2).Name}({_second})",
        _ => $"Union<{typeof(T1).Name}, {typeof(T2).Name}>(unspecified)",
    };
}

/// <summary>
/// A value that holds exactly one of three case types at a time: the first, the second, the third,
/// or - until something is assigned - neither.
/// </summary>
/// <typeparam name="T1">The first case type.</typeparam>
/// <typeparam name="T2">The second case type.</typeparam>
/// <typeparam name="T3">The third case type.</typeparam>
/// <remarks>
/// <para>
/// Assign a value of any case type directly to a variable or property of this type, or construct it
/// explicitly with <see cref="Union{T1, T2, T3}(T1)"/>,
/// <see cref="Union{T1, T2, T3}(T2)"/> or <see cref="Union{T1, T2, T3}(T3)"/>. Read the value back
/// with <see cref="TryGetValue{T}"/>, which answers only for the case type that is stored, and
/// dispatch on it with <see cref="Switch(Action{T1}, Action{T2}, Action{T3})"/>,
/// <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2}, Action{TState, T3})"/>
/// or <see cref="Match{TState, TResult}(TState, Func{TState, T1, TResult}, Func{TState, T2, TResult}, Func{TState, T3, TResult})"/>.
/// </para>
/// <para>
/// The three case types have to be distinct: two identical case types would mean two constructors
/// and two conversion operators with the same signature, which does not compile.
/// </para>
/// <para>
/// This type knows nothing about inheritance: a case such as <see cref="InheritedValue"/> is a case
/// like any other here, and deciding that it means "the inherited value" is the caller's job.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Union&lt;SKColor, ThemeColor, InheritedValue&gt; forerunner;
/// forerunner = InheritedValue.Instance;
///
/// if (forerunner.TryGetValue&lt;InheritedValue&gt;(out _))
/// {
///     // the property inherits its value
/// }
/// </code>
/// </example>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct Union<T1, T2, T3> : IEquatable<Union<T1, T2, T3>>
{
    private const byte ArmFirst = 1;
    private const byte ArmSecond = 2;
    private const byte ArmThird = 3;

    private readonly byte _arm;
    private readonly T1 _first;
    private readonly T2 _second;
    private readonly T3 _third;

    /// <summary>A union holding <paramref name="value"/> as its first case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T1 value)
    {
        this = default;
        _arm = ArmFirst;
        _first = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its second case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T2 value)
    {
        this = default;
        _arm = ArmSecond;
        _second = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its third case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T3 value)
    {
        this = default;
        _arm = ArmThird;
        _third = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its first case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the first case.</returns>
    public static implicit operator Union<T1, T2, T3>(T1 value) => new(value);

    /// <summary>A union holding <paramref name="value"/> as its second case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the second case.</returns>
    public static implicit operator Union<T1, T2, T3>(T2 value) => new(value);

    /// <summary>A union holding <paramref name="value"/> as its third case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the third case.</returns>
    public static implicit operator Union<T1, T2, T3>(T3 value) => new(value);

    /// <summary>
    /// Whether this union was never assigned, so it holds none of its case types.
    /// </summary>
    /// <remarks>
    /// A union that has never been assigned is <c>default</c>, and a <c>default</c> union has no
    /// case. This property is how a caller checks for the case before reading anything;
    /// <see cref="ThrowIfUnspecified"/> is the way to refuse it.
    /// </remarks>
    /// <value><c>true</c> for <c>default</c>; otherwise <c>false</c>.</value>
    public bool IsUnspecified => _arm == 0;

    /// <summary>Throws when this union was never assigned, and therefore holds no case.</summary>
    /// <remarks>
    /// Call this at the top of anything that cannot do without a case - a paint method, a handler -
    /// so a property that was never set fails where it is used rather than silently doing nothing.
    /// <see cref="Switch(Action{T1}, Action{T2}, Action{T3})"/>,
    /// <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2}, Action{TState, T3})"/>
    /// and <see cref="Match{TState, TResult}(TState, Func{TState, T1, TResult}, Func{TState, T2, TResult}, Func{TState, T3, TResult})"/>
    /// call it themselves.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This union is unspecified, which means it is default-initialised, so it is not one of its
    /// case types.
    /// </exception>
    public void ThrowIfUnspecified()
    {
        if (_arm == 0)
        {
            throw new InvalidOperationException(
                $"{this} has no case: it is default-initialised, and was never assigned. Assign a value before reading it.");
        }
    }

    /// <summary>Reads the held value when it is of exactly type <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// <para>
    /// The check is against each case type exactly, never against a type it is assignable to. In
    /// <c>Union&lt;object, string, InheritedValue&gt;</c> holding a <c>string</c>, this returns
    /// <c>true</c> only for <c>TryGetValue&lt;string&gt;</c>; <c>TryGetValue&lt;object&gt;</c> is not
    /// a case type and throws. The answer therefore never depends on the order of the cases.
    /// </para>
    /// <para>
    /// Asking for a type that is not one of the union's case types is a coding error and throws,
    /// rather than quietly returning <c>false</c>, which would hide a mistake in a handler. Asking
    /// for a case type while a different case is stored returns <c>false</c>, which is an ordinary
    /// answer.
    /// </para>
    /// <para>
    /// A case may hold a null reference - assigning <c>null</c> to a reference-typed case is a valid
    /// assignment - so <paramref name="value"/> can be <c>null</c> while this returns <c>true</c>.
    /// </para>
    /// <para>
    /// This method does not box or allocate.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The case type to read. It has to be one of this union's case types.</typeparam>
    /// <param name="value">The held value when this returns <c>true</c>; otherwise <c>default</c>.</param>
    /// <returns><c>true</c> when the stored case is exactly type <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="T"/> is not one of this union's case types.
    /// </exception>
    public bool TryGetValue<T>([MaybeNullWhen(false)] out T value)
    {
        if (typeof(T) == typeof(T1))
        {
            if (_arm == ArmFirst)
            {
                value = Unsafe.As<T1, T>(ref Unsafe.AsRef(in _first));
                return true;
            }
        }
        else if (typeof(T) == typeof(T2))
        {
            if (_arm == ArmSecond)
            {
                value = Unsafe.As<T2, T>(ref Unsafe.AsRef(in _second));
                return true;
            }
        }
        else if (typeof(T) == typeof(T3))
        {
            if (_arm == ArmThird)
            {
                value = Unsafe.As<T3, T>(ref Unsafe.AsRef(in _third));
                return true;
            }
        }
        else
        {
            throw new ArgumentException(
                $"Union<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}> has no case of type {typeof(T).Name}, so TryGetValue<{typeof(T).Name}> cannot answer. Ask for one of {typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}.");
        }

        value = default;
        return false;
    }

    /// <summary>Runs the handler for the case that is stored.</summary>
    /// <remarks>
    /// One handler per case type, so a handler that is missing fails to compile rather than being
    /// forgotten. Use the state-carrying overload when a handler needs a captured value and the
    /// handler should be a <c>static</c> lambda.
    /// </remarks>
    /// <param name="first">The handler for a value of the first case type.</param>
    /// <param name="second">The handler for a value of the second case type.</param>
    /// <param name="third">The handler for a value of the third case type.</param>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to dispatch on.
    /// </exception>
    public void Switch(Action<T1> first, Action<T2> second, Action<T3> third)
    {
        ThrowIfUnspecified();

        switch (_arm)
        {
            case ArmFirst:
                first(_first);
                break;
            case ArmSecond:
                second(_second);
                break;
            case ArmThird:
                third(_third);
                break;
        }
    }

    /// <summary>Runs the handler for the case that is stored, passing it <paramref name="state"/>.</summary>
    /// <param name="state">The value passed to whichever handler runs.</param>
    /// <param name="first">The handler for a value of the first case type.</param>
    /// <param name="second">The handler for a value of the second case type.</param>
    /// <param name="third">The handler for a value of the third case type.</param>
    /// <typeparam name="TState">The type of the value passed to the handler.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to dispatch on.
    /// </exception>
    public void Switch<TState>(TState state, Action<TState, T1> first, Action<TState, T2> second, Action<TState, T3> third)
    {
        ThrowIfUnspecified();

        switch (_arm)
        {
            case ArmFirst:
                first(state, _first);
                break;
            case ArmSecond:
                second(state, _second);
                break;
            case ArmThird:
                third(state, _third);
                break;
        }
    }

    /// <summary>Computes a result from the case that is stored, passing it <paramref name="state"/>.</summary>
    /// <remarks>
    /// The returning form of <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2}, Action{TState, T3})"/>:
    /// one function per case type, and the stored case picks which one runs.
    /// </remarks>
    /// <typeparam name="TState">The type of the value passed to the function.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="state">The value passed to whichever function runs.</param>
    /// <param name="first">The function for a value of the first case type.</param>
    /// <param name="second">The function for a value of the second case type.</param>
    /// <param name="third">The function for a value of the third case type.</param>
    /// <returns>The result of the function for the stored case.</returns>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to match on.
    /// </exception>
    public TResult Match<TState, TResult>(
        TState state,
        Func<TState, T1, TResult> first,
        Func<TState, T2, TResult> second,
        Func<TState, T3, TResult> third)
    {
        ThrowIfUnspecified();

        return _arm switch
        {
            ArmFirst => first(state, _first),
            ArmSecond => second(state, _second),
            _ => third(state, _third),
        };
    }

    /// <summary>Whether two unions are the same case holding equal payloads.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> when both are the same case and their values are equal.</returns>
    public static bool operator ==(Union<T1, T2, T3> left, Union<T1, T2, T3> right) => left.Equals(right);

    /// <summary>Whether two unions are not the same case, or their payloads differ.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> when either side differs.</returns>
    public static bool operator !=(Union<T1, T2, T3> left, Union<T1, T2, T3> right) => !left.Equals(right);

    /// <inheritdoc />
    /// <remarks>
    /// Two unions are equal when they hold the same case and the payloads compare equal with
    /// <see cref="EqualityComparer{T}.Default"/>; two default-initialised unions are equal to each
    /// other.
    /// </remarks>
    public bool Equals(Union<T1, T2, T3> other)
    {
        if (_arm != other._arm)
        {
            return false;
        }

        return _arm switch
        {
            ArmFirst => EqualityComparer<T1>.Default.Equals(_first, other._first),
            ArmSecond => EqualityComparer<T2>.Default.Equals(_second, other._second),
            ArmThird => EqualityComparer<T3>.Default.Equals(_third, other._third),
            _ => true,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Union<T1, T2, T3> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _arm switch
    {
        ArmFirst => HashCode.Combine(_arm, _first),
        ArmSecond => HashCode.Combine(_arm, _second),
        ArmThird => HashCode.Combine(_arm, _third),
        _ => 0,
    };

    /// <summary>The stored case and its value as text.</summary>
    /// <remarks>
    /// The first case reads as its type name, a dot and the value, which is how a named constant
    /// reads; every other case as its type name and the value in parentheses. A default-initialised
    /// union reads as <c>Union&lt;T1, T2, T3&gt;(unspecified)</c>.
    /// </remarks>
    /// <returns>A string such as <c>ThemeColor.MutedForeground</c>, <c>SKColor(#FF404040)</c>,
    /// <c>InheritedValue(Inherit)</c> or <c>Union&lt;SKColor, ThemeColor, InheritedValue&gt;(unspecified)</c>.</returns>
    public override string ToString() => _arm switch
    {
        ArmFirst => $"{typeof(T1).Name}.{_first}",
        ArmSecond => $"{typeof(T2).Name}({_second})",
        ArmThird => $"{typeof(T3).Name}({_third})",
        _ => $"Union<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}>(unspecified)",
    };
}

/// <summary>
/// A value that holds exactly one of four case types at a time: the first, the second, the third,
/// the fourth, or - until something is assigned - neither.
/// </summary>
/// <typeparam name="T1">The first case type.</typeparam>
/// <typeparam name="T2">The second case type.</typeparam>
/// <typeparam name="T3">The third case type.</typeparam>
/// <typeparam name="T4">The fourth case type.</typeparam>
/// <remarks>
/// <para>
/// Assign a value of any case type directly to a variable or property of this type, or construct it
/// explicitly with <see cref="Union{T1, T2, T3, T4}(T1)"/>,
/// <see cref="Union{T1, T2, T3, T4}(T2)"/>, <see cref="Union{T1, T2, T3, T4}(T3)"/> or
/// <see cref="Union{T1, T2, T3, T4}(T4)"/>. Read the value back with <see cref="TryGetValue{T}"/>,
/// which answers only for the case type that is stored, and dispatch on it with
/// <see cref="Switch(Action{T1}, Action{T2}, Action{T3}, Action{T4})"/>,
/// <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2}, Action{TState, T3}, Action{TState, T4})"/>
/// or <see cref="Match{TState, TResult}(TState, Func{TState, T1, TResult}, Func{TState, T2, TResult}, Func{TState, T3, TResult}, Func{TState, T4, TResult})"/>.
/// </para>
/// <para>
/// The four case types have to be distinct: two identical case types would mean two constructors
/// and two conversion operators with the same signature, which does not compile.
/// </para>
/// <para>
/// This type knows nothing about inheritance: a case such as <see cref="InheritedValue"/> is a case
/// like any other here, and deciding that it means "the inherited value" is the caller's job.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Union&lt;SKColor, ThemeColor, InheritedValue, int&gt; value;
/// value = 12;
///
/// int thickness = value.Match(
///     0,
///     static (_, SKColor direct) =&gt; 0,
///     static (_, ThemeColor role) =&gt; 0,
///     static (_, InheritedValue inherit) =&gt; 0,
///     static (_, int number) =&gt; number);
/// </code>
/// </example>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct Union<T1, T2, T3, T4> : IEquatable<Union<T1, T2, T3, T4>>
{
    private const byte ArmFirst = 1;
    private const byte ArmSecond = 2;
    private const byte ArmThird = 3;
    private const byte ArmFourth = 4;

    private readonly byte _arm;
    private readonly T1 _first;
    private readonly T2 _second;
    private readonly T3 _third;
    private readonly T4 _fourth;

    /// <summary>A union holding <paramref name="value"/> as its first case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T1 value)
    {
        this = default;
        _arm = ArmFirst;
        _first = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its second case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T2 value)
    {
        this = default;
        _arm = ArmSecond;
        _second = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its third case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T3 value)
    {
        this = default;
        _arm = ArmThird;
        _third = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its fourth case.</summary>
    /// <param name="value">The value to hold. A null reference is a value like any other.</param>
    public Union(T4 value)
    {
        this = default;
        _arm = ArmFourth;
        _fourth = value;
    }

    /// <summary>A union holding <paramref name="value"/> as its first case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the first case.</returns>
    public static implicit operator Union<T1, T2, T3, T4>(T1 value) => new(value);

    /// <summary>A union holding <paramref name="value"/> as its second case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the second case.</returns>
    public static implicit operator Union<T1, T2, T3, T4>(T2 value) => new(value);

    /// <summary>A union holding <paramref name="value"/> as its third case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the third case.</returns>
    public static implicit operator Union<T1, T2, T3, T4>(T3 value) => new(value);

    /// <summary>A union holding <paramref name="value"/> as its fourth case, by assignment.</summary>
    /// <param name="value">The value to hold.</param>
    /// <returns>The value as a union of the fourth case.</returns>
    public static implicit operator Union<T1, T2, T3, T4>(T4 value) => new(value);

    /// <summary>
    /// Whether this union was never assigned, so it holds none of its case types.
    /// </summary>
    /// <remarks>
    /// A union that has never been assigned is <c>default</c>, and a <c>default</c> union has no
    /// case. This property is how a caller checks for the case before reading anything;
    /// <see cref="ThrowIfUnspecified"/> is the way to refuse it.
    /// </remarks>
    /// <value><c>true</c> for <c>default</c>; otherwise <c>false</c>.</value>
    public bool IsUnspecified => _arm == 0;

    /// <summary>Throws when this union was never assigned, and therefore holds no case.</summary>
    /// <remarks>
    /// Call this at the top of anything that cannot do without a case - a paint method, a handler -
    /// so a property that was never set fails where it is used rather than silently doing nothing.
    /// <see cref="Switch(Action{T1}, Action{T2}, Action{T3}, Action{T4})"/>,
    /// <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2}, Action{TState, T3}, Action{TState, T4})"/>
    /// and <see cref="Match{TState, TResult}(TState, Func{TState, T1, TResult}, Func{TState, T2, TResult}, Func{TState, T3, TResult}, Func{TState, T4, TResult})"/>
    /// call it themselves.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This union is unspecified, which means it is default-initialised, so it is not one of its
    /// case types.
    /// </exception>
    public void ThrowIfUnspecified()
    {
        if (_arm == 0)
        {
            throw new InvalidOperationException(
                $"{this} has no case: it is default-initialised, and was never assigned. Assign a value before reading it.");
        }
    }

    /// <summary>Reads the held value when it is of exactly type <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// <para>
    /// The check is against each case type exactly, never against a type it is assignable to. In
    /// <c>Union&lt;object, string, InheritedValue, int&gt;</c> holding a <c>string</c>, this returns
    /// <c>true</c> only for <c>TryGetValue&lt;string&gt;</c>; <c>TryGetValue&lt;object&gt;</c> is not
    /// a case type and throws. The answer therefore never depends on the order of the cases.
    /// </para>
    /// <para>
    /// Asking for a type that is not one of the union's case types is a coding error and throws,
    /// rather than quietly returning <c>false</c>, which would hide a mistake in a handler. Asking
    /// for a case type while a different case is stored returns <c>false</c>, which is an ordinary
    /// answer.
    /// </para>
    /// <para>
    /// A case may hold a null reference - assigning <c>null</c> to a reference-typed case is a valid
    /// assignment - so <paramref name="value"/> can be <c>null</c> while this returns <c>true</c>.
    /// </para>
    /// <para>
    /// This method does not box or allocate.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The case type to read. It has to be one of this union's case types.</typeparam>
    /// <param name="value">The held value when this returns <c>true</c>; otherwise <c>default</c>.</param>
    /// <returns><c>true</c> when the stored case is exactly type <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="T"/> is not one of this union's case types.
    /// </exception>
    public bool TryGetValue<T>([MaybeNullWhen(false)] out T value)
    {
        if (typeof(T) == typeof(T1))
        {
            if (_arm == ArmFirst)
            {
                value = Unsafe.As<T1, T>(ref Unsafe.AsRef(in _first));
                return true;
            }
        }
        else if (typeof(T) == typeof(T2))
        {
            if (_arm == ArmSecond)
            {
                value = Unsafe.As<T2, T>(ref Unsafe.AsRef(in _second));
                return true;
            }
        }
        else if (typeof(T) == typeof(T3))
        {
            if (_arm == ArmThird)
            {
                value = Unsafe.As<T3, T>(ref Unsafe.AsRef(in _third));
                return true;
            }
        }
        else if (typeof(T) == typeof(T4))
        {
            if (_arm == ArmFourth)
            {
                value = Unsafe.As<T4, T>(ref Unsafe.AsRef(in _fourth));
                return true;
            }
        }
        else
        {
            throw new ArgumentException(
                $"Union<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}, {typeof(T4).Name}> has no case of type {typeof(T).Name}, so TryGetValue<{typeof(T).Name}> cannot answer. Ask for one of {typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}, {typeof(T4).Name}.");
        }

        value = default;
        return false;
    }

    /// <summary>Runs the handler for the case that is stored.</summary>
    /// <remarks>
    /// One handler per case type, so a handler that is missing fails to compile rather than being
    /// forgotten. Use the state-carrying overload when a handler needs a captured value and the
    /// handler should be a <c>static</c> lambda.
    /// </remarks>
    /// <param name="first">The handler for a value of the first case type.</param>
    /// <param name="second">The handler for a value of the second case type.</param>
    /// <param name="third">The handler for a value of the third case type.</param>
    /// <param name="fourth">The handler for a value of the fourth case type.</param>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to dispatch on.
    /// </exception>
    public void Switch(Action<T1> first, Action<T2> second, Action<T3> third, Action<T4> fourth)
    {
        ThrowIfUnspecified();

        switch (_arm)
        {
            case ArmFirst:
                first(_first);
                break;
            case ArmSecond:
                second(_second);
                break;
            case ArmThird:
                third(_third);
                break;
            case ArmFourth:
                fourth(_fourth);
                break;
        }
    }

    /// <summary>Runs the handler for the case that is stored, passing it <paramref name="state"/>.</summary>
    /// <param name="state">The value passed to whichever handler runs.</param>
    /// <param name="first">The handler for a value of the first case type.</param>
    /// <param name="second">The handler for a value of the second case type.</param>
    /// <param name="third">The handler for a value of the third case type.</param>
    /// <param name="fourth">The handler for a value of the fourth case type.</param>
    /// <typeparam name="TState">The type of the value passed to the handler.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to dispatch on.
    /// </exception>
    public void Switch<TState>(
        TState state,
        Action<TState, T1> first,
        Action<TState, T2> second,
        Action<TState, T3> third,
        Action<TState, T4> fourth)
    {
        ThrowIfUnspecified();

        switch (_arm)
        {
            case ArmFirst:
                first(state, _first);
                break;
            case ArmSecond:
                second(state, _second);
                break;
            case ArmThird:
                third(state, _third);
                break;
            case ArmFourth:
                fourth(state, _fourth);
                break;
        }
    }

    /// <summary>Computes a result from the case that is stored, passing it <paramref name="state"/>.</summary>
    /// <remarks>
    /// The returning form of
    /// <see cref="Switch{TState}(TState, Action{TState, T1}, Action{TState, T2}, Action{TState, T3}, Action{TState, T4})"/>:
    /// one function per case type, and the stored case picks which one runs.
    /// </remarks>
    /// <typeparam name="TState">The type of the value passed to the function.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="state">The value passed to whichever function runs.</param>
    /// <param name="first">The function for a value of the first case type.</param>
    /// <param name="second">The function for a value of the second case type.</param>
    /// <param name="third">The function for a value of the third case type.</param>
    /// <param name="fourth">The function for a value of the fourth case type.</param>
    /// <returns>The result of the function for the stored case.</returns>
    /// <exception cref="InvalidOperationException">
    /// This union is default-initialised, so there is no case to match on.
    /// </exception>
    public TResult Match<TState, TResult>(
        TState state,
        Func<TState, T1, TResult> first,
        Func<TState, T2, TResult> second,
        Func<TState, T3, TResult> third,
        Func<TState, T4, TResult> fourth)
    {
        ThrowIfUnspecified();

        return _arm switch
        {
            ArmFirst => first(state, _first),
            ArmSecond => second(state, _second),
            ArmThird => third(state, _third),
            _ => fourth(state, _fourth),
        };
    }

    /// <summary>Whether two unions are the same case holding equal payloads.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> when both are the same case and their values are equal.</returns>
    public static bool operator ==(Union<T1, T2, T3, T4> left, Union<T1, T2, T3, T4> right) => left.Equals(right);

    /// <summary>Whether two unions are not the same case, or their payloads differ.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><c>true</c> when either side differs.</returns>
    public static bool operator !=(Union<T1, T2, T3, T4> left, Union<T1, T2, T3, T4> right) => !left.Equals(right);

    /// <inheritdoc />
    /// <remarks>
    /// Two unions are equal when they hold the same case and the payloads compare equal with
    /// <see cref="EqualityComparer{T}.Default"/>; two default-initialised unions are equal to each
    /// other.
    /// </remarks>
    public bool Equals(Union<T1, T2, T3, T4> other)
    {
        if (_arm != other._arm)
        {
            return false;
        }

        return _arm switch
        {
            ArmFirst => EqualityComparer<T1>.Default.Equals(_first, other._first),
            ArmSecond => EqualityComparer<T2>.Default.Equals(_second, other._second),
            ArmThird => EqualityComparer<T3>.Default.Equals(_third, other._third),
            ArmFourth => EqualityComparer<T4>.Default.Equals(_fourth, other._fourth),
            _ => true,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Union<T1, T2, T3, T4> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _arm switch
    {
        ArmFirst => HashCode.Combine(_arm, _first),
        ArmSecond => HashCode.Combine(_arm, _second),
        ArmThird => HashCode.Combine(_arm, _third),
        ArmFourth => HashCode.Combine(_arm, _fourth),
        _ => 0,
    };

    /// <summary>The stored case and its value as text.</summary>
    /// <remarks>
    /// The first case reads as its type name, a dot and the value, which is how a named constant
    /// reads; every other case as its type name and the value in parentheses. A default-initialised
    /// union reads as <c>Union&lt;T1, T2, T3, T4&gt;(unspecified)</c>.
    /// </remarks>
    /// <returns>A string such as <c>ThemeColor.MutedForeground</c>, <c>SKColor(#FF404040)</c>,
    /// <c>InheritedValue(Inherit)</c>, <c>Int32(12)</c> or
    /// <c>Union&lt;SKColor, ThemeColor, InheritedValue, int&gt;(unspecified)</c>.</returns>
    public override string ToString() => _arm switch
    {
        ArmFirst => $"{typeof(T1).Name}.{_first}",
        ArmSecond => $"{typeof(T2).Name}({_second})",
        ArmThird => $"{typeof(T3).Name}({_third})",
        ArmFourth => $"{typeof(T4).Name}({_fourth})",
        _ => $"Union<{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}, {typeof(T4).Name}>(unspecified)",
    };
}
