namespace Kolvir.Undo;

/// <summary>
/// The time source the undo history reads when it decides whether a keystroke continues the entry
/// before it or starts a new one.
/// </summary>
/// <remarks>
/// <para>
/// The history is the only part of the toolkit that has to measure a <em>gap</em> between two
/// events: a character typed within five seconds of the previous one joins that character's entry.
/// A repeating <see cref="Timer"/> cannot answer that question - it fires on its own schedule
/// and would have to run for five seconds after every keystroke, which is exactly the idle timer
/// <c>CONTROLS-TODO.md</c> forbids - so the feature asks a clock instead, and runs no timer at all.
/// </para>
/// <para>
/// The clock is an ambient service in the shape <see cref="Clipboard.Current"/> and
/// <see cref="SystemTheme.Current"/> already have: the default is
/// <see cref="TimeProvider.System"/>, and an application or a test may replace it. Replacing it is
/// what makes the five-second window testable without sleeping: a harness installs a
/// <see cref="TimeProvider"/> whose timestamps it advances by hand and drives a whole history,
/// gaps and all, in a millisecond of wall time.
/// </para>
/// <para>
/// It is named only by <c>Kolvir.Undo</c> code, so an application that does not use the feature
/// neither carries it nor imports it - FEATURE-COST-POLICY.md §4's first rung.
/// </para>
/// </remarks>
public static class UndoClock
{
    private static TimeProvider _current = TimeProvider.System;

    /// <summary>
    /// The time source the history measures gaps with. Assigning null restores
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    public static TimeProvider Current
    {
        get => _current;
        set => _current = value ?? TimeProvider.System;
    }
}
