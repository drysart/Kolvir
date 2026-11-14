namespace Kolvir.Undo;

/// <summary>
/// The undo and redo members a text box gains by opting into the history: turning it on, the two
/// commands, whether each one is available, and the size of each side.
/// </summary>
/// <remarks>
/// <para>
/// These are extension members rather than members of <see cref="TextBox"/>, the way
/// FEATURE-COST-POLICY.md §5 asks, and the surface still reads as part of the control:
/// <code>
/// using Kolvir.Undo;
///
/// var name = new TextBox
/// {
///     UndoEnabled = true,
///     Text = "Ada Lovelace",
/// };
/// </code>
/// </para>
/// <para>
/// <b>The contract.</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Coalescing.</b> Ordinary character entry is recorded as a run: each character joins the
/// entry before it while it arrives within five seconds of the <em>previous character</em>, and a
/// longer pause starts a new entry. Backspace and Delete coalesce by the same rule, on runs of
/// their own, so a burst of typing undoes as one step and a burst of deleting undoes as one step,
/// and the two never merge. A change made while a non-zero selection is active is always one entry
/// of its own, and it ends any run, so undo restores the replaced run rather than un-replacing one
/// character at a time.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>The insertion point travels with the entry.</b> Each entry records the caret and the
/// selection on both sides of the change, and undo and redo put them back, including the direction
/// of a selection made right to left. Undoing therefore leaves the caret where the user was typing
/// rather than at the start or the end of the value. An assignment that shortens the value is
/// recorded against the caret and selection the clamp left - the end of the new text is as far as
/// they can go - so a redo lands the insertion point where the assignment did.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A programmatic <c>Text</c> assignment is undoable when it changes the value, and free when it
/// does not.</b> The setter already ignores an assignment of the value the box holds, so a two-way
/// binding that echoes each keystroke back writes the same string and records nothing. An
/// assignment that really changes the value - loading a document, a form resetting a field, a
/// derived value the box does not already hold - is recorded as one entry that restores the
/// previous value, because it is a change the user did not make but will expect Ctrl+Z to take
/// back. A binding that <em>transforms</em> the value it echoes (trimming, upper-casing) is the one
/// case that produces an entry per write; call <see cref="ClearUndoStack"/> after loading data, or
/// do not enable the history on such a field.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>The context menu follows.</b> A box with the history leads the editing block it contributes to
/// a right-click with <b>Undo</b> and <b>Redo</b>, enabled by <c>CanUndo</c> and <c>CanRedo</c> as
/// the menu is built, and a row runs the same operation a key press does.
/// A box that never turned the history on contributes no rows for it: the history is opt-in, so a
/// menu the application designed is not handed rows for a feature it never asked for.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Read-only.</b> A read-only field records nothing, <c>CanUndo</c> and <c>CanRedo</c> report
/// false, and <see cref="Undo"/> and <see cref="Redo"/> do nothing.
/// The history is kept rather than dropped, so a field that is locked for a while gets it back when
/// it is unlocked again.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Depth and memory.</b> The history keeps the newest 100 entries, and drops the oldest whenever
/// that or a one-million-character budget for the text the two stacks pin is exceeded. An entry is
/// a delta - the run it replaced, the run it inserted and two insertion points - so a large
/// multi-line document does not make the history large: typing in it costs a few bytes an entry.
/// A paste bigger than the budget keeps its own entry and drops everything older, so the history
/// can never pin an unbounded amount of memory.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Multi-line.</b> The history spans line breaks like any other character, and an entry also
/// records the scroll offsets of a multi-line field. Undo restores the caret and scrolls it into
/// view; the recorded offsets are then put back, so the view returns to where the edit was made.
/// Turning <c>Multiline</c> on or off clears the history, because the value changes shape - line
/// breaks are added to or dropped from it - and an entry from one shape cannot be applied to the
/// other.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Spans are the caller's, as they always are.</b> Undo restores text, not annotations: the box
/// never rewrites a <c>Spans</c> list, so the list the application handed over keeps describing
/// the text it was computed for. A checker re-marks after the change, which is exactly what it
/// already does for typing - the undo arrives as an ordinary text change - and a stale span is
/// clamped where it is drawn rather than throwing.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Autocomplete.</b> Undo and redo are text changes the autocomplete feature did not see a
/// keystroke for, so it treats them the way it treats an assignment: it adopts the restored value,
/// drops any inline completion that was pending, and closes the suggestion list. There is no
/// half-applied completion left behind for the next keystroke to extend.
/// </description>
/// </item>
/// </list>
/// <para>
/// The history runs no timer. The five-second window is measured from
/// <see cref="UndoClock.Current"/> at the moment of each edit, so an idle application has
/// nothing ticking for it, and a test can move the clock by hand instead of waiting.
/// </para>
/// </remarks>
public static class TextBoxUndoExtensions
{
    extension(TextBox textBox)
    {
        /// <summary>
        /// When true the box keeps an undo history. Off by default, and turning it off again - or
        /// turning it on and off - drops whatever it held. A box with a history also leads the
        /// editing block of its context menu with Undo and Redo; one without it contributes no rows
        /// for them.
        /// </summary>
        public bool UndoEnabled
        {
            get => textBox.UndoFeature is not null;
            set
            {
                if (value == (textBox.UndoFeature is not null))
                {
                    return;
                }

                if (value)
                {
                    TextBoxUndoFeature.Attach(textBox);
                }
                else
                {
                    TextBoxUndoFeature.Detach(textBox);
                }
            }
        }

        /// <summary>True when <see cref="Undo"/> would change the text: there is an entry and the box is editable.</summary>
        public bool CanUndo => !textBox.ReadOnly && textBox.UndoFeature?.CanUndo == true;

        /// <summary>True when <see cref="Redo"/> would change the text: there is an undone entry and the box is editable.</summary>
        public bool CanRedo => !textBox.ReadOnly && textBox.UndoFeature?.CanRedo == true;

        /// <summary>How many entries <see cref="Undo"/> can walk back through.</summary>
        public int UndoCount => textBox.UndoFeature?.UndoCount ?? 0;

        /// <summary>How many undone entries <see cref="Redo"/> can walk forward through.</summary>
        public int RedoCount => textBox.UndoFeature?.RedoCount ?? 0;

        /// <summary>
        /// Takes the newest entry back out of the value and puts the caret where it was when the
        /// entry was made. Returns false when there is nothing to undo or the box is read-only.
        /// </summary>
        public bool Undo() => textBox.UndoFeature?.Undo(textBox) ?? false;

        /// <summary>
        /// Re-applies the newest undone entry and puts the caret where it was left after it. Returns
        /// false when there is nothing to redo or the box is read-only. Any new edit in between
        /// drops the redo side, so a redo entry only ever applies to the text it was recorded
        /// against.
        /// </summary>
        public bool Redo() => textBox.UndoFeature?.Redo(textBox) ?? false;

        /// <summary>
        /// Drops the history, including everything undone, and leaves the value and the insertion
        /// point alone. This is the call to make after loading a document into a field whose history
        /// should not reach back past the load, and it is a no-op on a box that never turned
        /// <c>UndoEnabled</c> on, so it allocates nothing for an application that does not
        /// use the feature.
        /// </summary>
        public void ClearUndoStack() => textBox.UndoFeature?.ClearHistory();

        /// <summary>
        /// Adds a handler that runs after an entry is recorded, undone, redone or dropped - which is
        /// every moment <c>CanUndo</c> or <c>CanRedo</c> can change. Subscribe to it
        /// to keep an Undo/Redo menu or a pair of buttons in step with the field:
        /// <code>
        /// using var subscription = field.AddUndoStateChangedHandler(_ => RefreshUndoCommands());
        /// </code>
        /// It runs on the UI thread, in subscription order, after the text and the insertion point
        /// have already been put back, so a handler may read the field. A box that has not turned
        /// <c>UndoEnabled</c> on has no history to subscribe to, and the token is inert.
        /// </summary>
        public IDisposable AddUndoStateChangedHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            textBox.UndoFeature?.AddChangedHandler(handler);
            return new HandlerToken<Action<object>>(
                _ => textBox.UndoFeature?.RemoveChangedHandler(handler),
                handler);
        }

        /// <summary>Removes a handler that <see cref="AddUndoStateChangedHandler"/> added.</summary>
        public void RemoveUndoStateChangedHandler(Action<object> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            textBox.UndoFeature?.RemoveChangedHandler(handler);
        }
    }
}
