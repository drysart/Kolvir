namespace Kolvir.Feature.Observables;

/// <summary>
/// Creates observables, and is the one static surface the package has.
/// </summary>
/// <remarks>
/// <para>
/// A value is created where the state lives and read where it is used:
/// <see cref="CreateValue{T}"/> hands back the mutable type for the owner of the state, and everything
/// downstream sees it as <see cref="IObservableValue{T}"/> or as an
/// <see cref="IObservableEventSource"/>.
/// </para>
/// <code>
/// internal sealed class TaskItem
/// {
///     private readonly IObservableMutableValue&lt;string&gt; _text = Observable.CreateValue("New task");
///     private readonly IObservableMutableValue&lt;bool&gt; _done = Observable.CreateValue(false);
///
///     // The model's surface, not the observable's: a consumer that cares the property is observable
///     // is collecting dependencies over it anyway.
///     public string Text { get =&gt; _text.Value; set =&gt; _text.Value = value; }
///     public bool Done { get =&gt; _done.Value; set =&gt; _done.Value = value; }
/// }
/// </code>
/// <para>
/// There is no <c>AddObservables()</c> and nothing to register: the package has no process-wide state
/// that needs a container, and what it does keep - the named table and the collection scope in flight -
/// is created on first use.
/// </para>
/// </remarks>
public static partial class Observable
{
    /// <summary>Creates a standalone observable value.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="initialValue">What the value holds until it is written.</param>
    /// <returns>
    /// The writable view of the new value, for the piece of code that owns the state. Everything else
    /// should see it as <see cref="IObservableValue{T}"/>.
    /// </returns>
    /// <remarks>
    /// Always a fresh value, unrelated to any other observable: there is no name to collide on and
    /// nothing to share. Writing an equal value raises nothing; writing a different one raises once
    /// (see <see cref="IObservableMutableValue{T}"/>).
    /// </remarks>
    public static IObservableMutableValue<T> CreateValue<T>(T initialValue) => new ObservableValue<T>(initialValue);

    /// <summary>
    /// What a collection starts life configured as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults, not process-wide settings: a collection copies the value it cares about when it is
    /// created and keeps its own from then on, so changing one here decides what collections created
    /// afterwards start at and never reaches back into one that already exists. Every default is
    /// therefore also a property on the interface of the collection that uses it.
    /// </para>
    /// </remarks>
    public static class Default
    {
        /// <summary>
        /// How many records one event may carry before it stops describing the changes and says so
        /// instead.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A batch collects a record per change, and a caller is free to make a great many of them
        /// between <see cref="IObservableList{T}.BeginChangeBatch"/> and
        /// <see cref="IObservableList{T}.EndChangeBatch"/> - a bulk load, a refresh, a loop over a file.
        /// Past this many, the records are dropped and the event carries one
        /// <see cref="ObservableListChangeType.UntrackedChanges"/> record instead, which tells an
        /// observer to re-read the collection rather than replay what it was given.
        /// </para>
        /// <para>
        /// The trade is explicit and it is the caller's to avoid: fidelity up to this many changes per
        /// event, and one rescan beyond it. Fifty is the point where replaying the records stops being
        /// cheaper for an observer than reading the collection again.
        /// </para>
        /// <para>
        /// A list copies this when it is created and exposes its own copy as
        /// <see cref="IObservableList{T}.MaxCollectionBatchSize"/>. Set it there to tune one list for
        /// its own observers; set it here to decide what lists created afterwards start at.
        /// </para>
        /// </remarks>
        public const int MaxCollectionBatchSize = 50;
    }

    /// <summary>Records that the caller now depends on <paramref name="source"/>.</summary>
    /// <param name="source">The source that was read.</param>
    /// <remarks>
    /// <para>
    /// The primitive behind <see cref="IObservableEventSource.DependUpon"/>, and public because a
    /// hand-written source in another assembly needs it: its read members have the same protocol to
    /// keep as a value's, and the interface cannot hand it anything more than this.
    /// </para>
    /// <para>
    /// It appends to the innermost active collection scope and does nothing outside one, which is the
    /// entire cost of every ordinary property read in an application that never uses an expression: a
    /// null check on a static field. Nested scopes are isolated - the outer run never sees what the
    /// inner run read - because an outer function that builds an inner observable and reads its
    /// <see cref="IObservableValue{T}.Value"/> is depending on that observable, not on what it depends
    /// on, and hears about any inner change through it (dev-docs/OBSERVABLES.md section 3.3).
    /// </para>
    /// </remarks>
    public static void PublishRead(IObservableEventSource source) => ReadScope.Publish(source);
}
