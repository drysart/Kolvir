using System.Runtime.CompilerServices;

namespace Kolvir.Observables;

/// <summary>
/// The expression factory: a function whose result is cached and kept up to date by what it read.
/// </summary>
/// <remarks>
/// <para>
/// Two overloads, and the split exists to keep a file path out of an application that did not ask for
/// one. Passing a name selects the named overload, which has no caller attributes at all, so nothing is
/// emitted at that call site; the other overload takes the creation site from the compiler and uses it
/// in diagnostics (dev-docs/OBSERVABLES.md section 3.8.1).
/// </para>
/// <code>
/// // A name, for an expression whose messages an application will read.
/// var total = Observable.CreateExpression(() =&gt; _lines.Sum(line =&gt; line.Amount), "total");
///
/// // No name: the compiler supplies "OrderView.cs:88" instead, which is what a diagnostic needs and
/// // what an application that never looks at one should not pay a path for.
/// var label = Observable.CreateExpression(() =&gt; $"{_count.Value} clicks");
/// </code>
/// </remarks>
public static partial class Observable
{
    /// <summary>Creates an expression from a function and a name.</summary>
    /// <typeparam name="T">What the function returns.</typeparam>
    /// <param name="function">The function to evaluate. It is not run here.</param>
    /// <param name="name">
    /// How this expression is described in a diagnostic message - the name a refused step in a chain
    /// names as its source. Nothing looks it up; it is a label.
    /// </param>
    /// <param name="valueComparer">
    /// How two results are compared to decide whether the result <em>changed</em>. Null means
    /// <see cref="EqualityComparer{T}.Default"/>, which is the type's own equality: a record's value
    /// semantics work with no ceremony, and a type that wants identity semantics gets them by not
    /// overriding <see cref="object.Equals(object)"/>.
    /// </param>
    /// <param name="exceptionComparer">
    /// How two exceptions are compared to decide whether a *failure* changed. Null means two exceptions
    /// are never equal, so a function that keeps failing raises on every re-run - the safe default,
    /// because a reader may branch on the exception it finds. Supplying a comparer is how an application
    /// says "still the same failure, do not tell me again"; it suppresses the notification only, never
    /// the replacement of the cached exception.
    /// </param>
    /// <returns>The expression, seen as a read-only value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    /// <remarks>
    /// This overload carries no caller attributes on purpose: a call site that named itself should not
    /// compile a file path into the application.
    /// </remarks>
    public static IObservableValue<T> CreateExpression<T>(
        Func<T> function,
        string? name,
        IEqualityComparer<T>? valueComparer = null,
        IEqualityComparer<Exception>? exceptionComparer = null)
    {
        ArgumentNullException.ThrowIfNull(function);

        return new ObservableExpression<T>(function, valueComparer, exceptionComparer, Describe(name, null, 0));
    }

    /// <summary>Creates an expression from a function, named after its creation site.</summary>
    /// <typeparam name="T">What the function returns.</typeparam>
    /// <param name="function">The function to evaluate. It is not run here.</param>
    /// <param name="valueComparer">
    /// How two results are compared to decide whether the result changed. Null means
    /// <see cref="EqualityComparer{T}.Default"/>.
    /// </param>
    /// <param name="exceptionComparer">
    /// How two exceptions are compared to decide whether a failure changed. Null means two exceptions are
    /// never equal.
    /// </param>
    /// <param name="file">Supplied by the compiler: which file this expression was created in.</param>
    /// <param name="line">Supplied by the compiler: which line of it.</param>
    /// <returns>The expression, seen as a read-only value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is null.</exception>
    /// <remarks>
    /// The name this expression reports is the caller's file name and line, reduced to a file name when a
    /// message is built. Recording the path costs one string per file that creates an expression this way,
    /// which is the trade section 11 records: name the expressions an application cares about and this
    /// overload is not used at all.
    /// </remarks>
    public static IObservableValue<T> CreateExpression<T>(
        Func<T> function,
        IEqualityComparer<T>? valueComparer = null,
        IEqualityComparer<Exception>? exceptionComparer = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        ArgumentNullException.ThrowIfNull(function);

        return new ObservableExpression<T>(
            function,
            valueComparer,
            exceptionComparer,
            Describe(null, file, line));
    }

    /// <summary>The label an expression carries into a diagnostic message.</summary>
    /// <remarks>
    /// A given name, or the creation site as <c>File.cs:88</c>. Only the file name is kept, never the
    /// directory: the path a build machine had is of no use to anyone reading a diagnostic that names
    /// the creation site.
    /// </remarks>
    private static string Describe(string? name, string? file, int line)
    {
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (string.IsNullOrEmpty(file))
        {
            return "expression";
        }

        var fileName = Path.GetFileName(file);

        return line > 0 ? $"{fileName}:{line}" : fileName;
    }
}
