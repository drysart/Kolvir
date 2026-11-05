using XPlatUI.Multiline;

namespace XPlatUI.Undo;

/// <summary>What a recorded change was, which is what decides whether the next one may join it.</summary>
internal enum XPlatUndoEditKind
{
    /// <summary>
    /// An entry of its own: a paste, a cut, a multi-character insert, a delete of more than one
    /// character, or any change made while a non-zero selection was active.
    /// </summary>
    Replace = 0,

    /// <summary>A character or two typed in a row, which coalesce while they arrive within the window.</summary>
    Typing = 1,

    /// <summary>A single character removed with Backspace or Delete, coalescing the same way.</summary>
    Deleting = 2,
}

/// <summary>
/// The whole seam between <see cref="XPlatTextBox"/> and its undo history.
/// </summary>
/// <remarks>
/// <para>
/// A text box holds one nullable reference to this interface and nothing else. Every edit the
/// control makes passes through <see cref="ReplaceRange"/>, and a programmatic
/// <c>Text</c> assignment passes through the property's setter, so those two places are the only
/// ones that have to ask the history what to record - and the keys that undo are the only other
/// thing the control has to be able to ask. Everything else - the entries, the coalescing rule, the
/// depth cap - is behind the interface and named only by the extension property that installs it.
/// </para>
/// <para>
/// That is what keeps FEATURE-COST-POLICY.md §4's first rung reachable, the way
/// <c>IXPlatTextBoxMultiline</c> and <c>IXPlatTextBoxSpanFeature</c> do: an application that never
/// writes <c>using XPlatUI.Undo;</c> and never turns <c>UndoEnabled</c> on publishes without any of
/// this. What it does pay for is the null reference every text box carries - eight bytes - plus one
/// null check on each of the three call sites, which is the same accepted seam cost the other two
/// text-box features pay for the same reason.
/// </para>
/// </remarks>
internal interface IXPlatTextBoxUndoFeature
{
    /// <summary>
    /// Called by the control immediately before it applies an edit, while the text, the caret and
    /// the selection are still the state the user made the edit from.
    /// </summary>
    /// <param name="textBox">The box being edited.</param>
    /// <param name="start">Where the replaced run begins.</param>
    /// <param name="removedLength">How many characters the run replaces.</param>
    /// <param name="inserted">What replaces them, after <c>MaxLength</c> has been applied.</param>
    /// <param name="selectionLength">How much of the value was selected when the edit arrived.</param>
    void BeginEdit(XPlatTextBox textBox, int start, int removedLength, string inserted, int selectionLength);

    /// <summary>
    /// Called by the control immediately before a programmatic <c>Text</c> assignment that actually
    /// changes the value. The entry it starts is completed by <see cref="EndEdit"/> like any other.
    /// </summary>
    void BeginAssign(XPlatTextBox textBox, string previousText);

    /// <summary>
    /// Called once the edit has been applied and the caret has been brought into view, so the entry
    /// can record where the user was left.
    /// </summary>
    void EndEdit(XPlatTextBox textBox);

    /// <summary>Handles Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z. False leaves the key alone.</summary>
    bool OnKeyDown(XPlatTextBox textBox, XPlatKeyEventArgs e);

    /// <summary>True when there is an entry to undo.</summary>
    bool CanUndo { get; }

    /// <summary>True when there is an undone entry to redo.</summary>
    bool CanRedo { get; }

    /// <summary>How many entries can be undone, and how many have been undone.</summary>
    int UndoCount { get; }

    /// <inheritdoc cref="UndoCount" />
    int RedoCount { get; }

    /// <summary>Applies the newest entry. False when there is nothing to undo, or the box is read-only.</summary>
    bool Undo(XPlatTextBox textBox);

    /// <summary>Re-applies the newest undone entry. False when there is nothing to redo, or the box is read-only.</summary>
    bool Redo(XPlatTextBox textBox);

    /// <summary>Drops both sides of the history. The text box keeps everything else.</summary>
    void ClearHistory();

    /// <summary>Adds a handler that runs whenever an entry is recorded, undone, redone or dropped.</summary>
    void AddChangedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddChangedHandler"/> added.</summary>
    void RemoveChangedHandler(Action<object> handler);

    /// <summary>Releases what the feature owns. The control keeps no history afterwards.</summary>
    void Dispose();
}

/// <summary>
/// The undo history of one text box: the entries, the run that may still be extended, and the
/// depth and memory caps.
/// </summary>
/// <remarks>
/// <para>
/// One instance exists per text box that turned <c>UndoEnabled</c> on, is created by that property,
/// and is installed into the control as <c>XPlatTextBox.UndoFeature</c>. Nothing else in the
/// library names this type, so it is trimmed along with the property that creates it.
/// </para>
/// <para>
/// An entry is a <em>delta</em>, not a snapshot: the run it replaced, the run it inserted, and the
/// insertion point on either side of it. Undo and redo are therefore cheap in the middle of a large
/// document - an entry for one keystroke is a couple of characters and four integers - and the
/// history as a whole is bounded by <see cref="Capacity"/>, by
/// <see cref="RetainedCharacterBudget"/>, and by the fact that a programmatic assignment is stored
/// as the two strings it already had rather than as a copy of the document.
/// </para>
/// <para>
/// The two stacks are plain lists with the newest entry at the end. Any new edit clears the redo
/// side, which is what makes redo safe: a redo entry is only ever applied to the text it was
/// recorded against.
/// </para>
/// </remarks>
internal sealed class XPlatTextBoxUndoFeature : IXPlatTextBoxUndoFeature
{
    /// <summary>How many entries the history keeps before the oldest is dropped.</summary>
    internal const int Capacity = 100;

    /// <summary>
    /// How many characters of removed and inserted text the two stacks may hold between them. A
    /// paste of more than this keeps its own entry and drops everything older, so a very large
    /// document cannot pin an unbounded amount of memory through the history.
    /// </summary>
    internal const int RetainedCharacterBudget = 1_000_000;

    /// <summary>How long a run of typing or deleting stays open for the next character to join.</summary>
    internal static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(5);

    private readonly XPlatTextBox _textBox;
    private readonly List<UndoEntry> _undo = [];
    private readonly List<UndoEntry> _redo = [];

    /// <summary>The entry the next character may extend, or null when the next change starts a new one.</summary>
    private UndoEntry? _open;

    /// <summary>The entry between <c>Begin</c> and <c>End</c>, which is the one being built.</summary>
    private UndoEntry? _pending;

    /// <summary>True while this feature is applying an entry, so the recording calls are ignored.</summary>
    private bool _applying;

    /// <summary>Handlers that run when the history or its availability changed.</summary>
    private Action<object>? _changed;

    private XPlatTextBoxUndoFeature(XPlatTextBox textBox) => _textBox = textBox;

    /// <summary>Turns the history on for <paramref name="textBox"/>, which must not have one yet.</summary>
    public static void Attach(XPlatTextBox textBox) => textBox.UndoFeature = new XPlatTextBoxUndoFeature(textBox);

    /// <summary>Releases the history of <paramref name="textBox"/> and forgets it.</summary>
    public static void Detach(XPlatTextBox textBox)
    {
        if (textBox.UndoFeature is { } feature)
        {
            textBox.UndoFeature = null;
            feature.Dispose();
        }
    }

    /// <inheritdoc />
    public bool CanUndo => _undo.Count > 0;

    /// <inheritdoc />
    public bool CanRedo => _redo.Count > 0;

    /// <inheritdoc />
    public int UndoCount => _undo.Count;

    /// <inheritdoc />
    public int RedoCount => _redo.Count;

    /// <inheritdoc />
    public void AddChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed += handler;
    }

    /// <inheritdoc />
    public void RemoveChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed -= handler;
    }

    /// <inheritdoc />
    public void BeginEdit(XPlatTextBox textBox, int start, int removedLength, string inserted, int selectionLength)
    {
        if (_applying)
        {
            return;
        }

        _pending = null;
        ClearRedo();

        var kind = Classify(removedLength, inserted, selectionLength);
        long now = XPlatUndoClock.Current.GetTimestamp();

        if (_open is { } open && ReferenceEquals(open, _undo[^1]) && TryExtend(open, kind, start, removedLength, inserted, now))
        {
            // The run grows. What it removes is taken from the text as it is now, which is still the
            // state before this edit: a Backspace adds to the front of the run, a Delete to the back.
            if (removedLength > 0)
            {
                string removed = textBox.Text.Substring(start, removedLength);
                open.RemovedText = start < open.Start ? removed + open.RemovedText : open.RemovedText + removed;
            }

            if (inserted.Length > 0)
            {
                open.InsertedText += inserted;
            }

            open.Start = Math.Min(open.Start, start);
            _pending = open;
            return;
        }

        var (scrollX, scrollY) = ScrollOf(textBox);
        var entry = new UndoEntry
        {
            Start = start,
            InsertedText = inserted,
            RemovedText = removedLength > 0 ? textBox.Text.Substring(start, removedLength) : string.Empty,
            Kind = kind,
            AnchorBefore = textBox.SelectionAnchor,
            CaretBefore = textBox.CaretIndex,
            ScrollXBefore = scrollX,
            ScrollYBefore = scrollY,
        };

        _undo.Add(entry);
        _open = entry;
        _pending = entry;
        Trim();
    }

    /// <inheritdoc />
    public void BeginAssign(XPlatTextBox textBox, string previousText)
    {
        if (_applying)
        {
            return;
        }

        _pending = null;
        ClearRedo();

        // The new value is read in EndEdit, once the box actually holds it. Nothing is copied:
        // both strings are the ones the assignment already had.
        var (scrollX, scrollY) = ScrollOf(textBox);
        var entry = new UndoEntry
        {
            Start = 0,
            InsertedText = string.Empty,
            ReadsInsertedFromBox = true,
            RemovedText = previousText,
            Kind = XPlatUndoEditKind.Replace,
            AnchorBefore = textBox.SelectionAnchor,
            CaretBefore = textBox.CaretIndex,
            ScrollXBefore = scrollX,
            ScrollYBefore = scrollY,
        };

        _undo.Add(entry);
        _pending = entry;
    }

    /// <inheritdoc />
    public void EndEdit(XPlatTextBox textBox)
    {
        if (_applying || _pending is not { } entry)
        {
            return;
        }

        _pending = null;

        // Only an assignment leaves the inserted run unknown until here, and it always replaces the
        // whole value, so the box's text is exactly what it inserted.
        if (entry.ReadsInsertedFromBox)
        {
            entry.InsertedText = textBox.Text;
            entry.ReadsInsertedFromBox = false;
        }

        // An assignment's entry has to carry the selection the clamp left, not just a collapsed
        // caret: the box may still have room for the selection it had, and a redo of the assignment
        // that dropped it would not match what the assignment actually did.
        entry.AnchorAfter = textBox.SelectionAnchor;
        entry.CaretAfter = textBox.CaretIndex;
        entry.ScrollXAfter = ScrollOf(textBox).X;
        entry.ScrollYAfter = ScrollOf(textBox).Y;
        entry.Timestamp = XPlatUndoClock.Current.GetTimestamp();

        _open = entry;
        Trim();
        RaiseChanged();
    }

    /// <inheritdoc />
    public bool OnKeyDown(XPlatTextBox textBox, XPlatKeyEventArgs e)
    {
        bool control = (e.Modifiers & XPlatModifiers.Control) != 0;
        bool shift = (e.Modifiers & XPlatModifiers.Shift) != 0;
        if (!control)
        {
            return false;
        }

        switch (e.Key)
        {
            case XPlatKey.Z when !shift:
                e.Handled = true;
                Undo(textBox);
                return true;

            case XPlatKey.Z when shift:
            case XPlatKey.Y:
                // Both spellings are redo: Ctrl+Shift+Z is the GTK and modern Windows one, Ctrl+Y
                // the one Windows has always had. macOS spells both with Command through the same
                // Control modifier the platform maps it to.
                e.Handled = true;
                Redo(textBox);
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public bool Undo(XPlatTextBox textBox)
    {
        // A read-only field is not editable, so it has no undo: an entry applied to it would change
        // text the application said the user may not change. The history is kept, not dropped, so
        // a field that is locked and unlocked again gets its history back.
        if (textBox.ReadOnly || _undo.Count == 0)
        {
            return false;
        }

        // The entry moves between the stacks before it is applied, so anything watching the value -
        // a readout, a menu that enables itself - sees the history and the text agree even when it
        // looks from inside the change the apply raises.
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        CloseRun();
        Apply(textBox, entry, undoing: true);
        RaiseChanged();
        return true;
    }

    /// <inheritdoc />
    public bool Redo(XPlatTextBox textBox)
    {
        if (textBox.ReadOnly || _redo.Count == 0)
        {
            return false;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        CloseRun();
        Apply(textBox, entry, undoing: false);
        RaiseChanged();
        return true;
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
        CloseRun();
        RaiseChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _undo.Clear();
        _redo.Clear();
        CloseRun();
        _changed = null;
    }

    /// <summary>Tells the handlers that the history, or what it can do, changed.</summary>
    private void RaiseChanged() => _changed?.Invoke(_textBox);

    /// <summary>
    /// What the edit was. A change made over a selection is always its own entry - undo has to
    /// restore the replaced run in one step rather than un-replacing one character at a time - and
    /// so is anything that is not a single character in or out.
    /// </summary>
    private static XPlatUndoEditKind Classify(int removedLength, string inserted, int selectionLength)
    {
        if (selectionLength > 0)
        {
            return XPlatUndoEditKind.Replace;
        }

        if (removedLength == 0 && IsOneCodePoint(inserted))
        {
            return XPlatUndoEditKind.Typing;
        }

        return removedLength == 1 && inserted.Length == 0
            ? XPlatUndoEditKind.Deleting
            : XPlatUndoEditKind.Replace;
    }

    /// <summary>
    /// Whether <paramref name="entry"/> may absorb this edit: same kind, still inside the window,
    /// and next to what it already covers. The gap is measured to the <em>previous character</em>,
    /// so a run survives a pause shorter than the window and ends at the first longer one.
    /// </summary>
    private static bool TryExtend(
        UndoEntry entry,
        XPlatUndoEditKind kind,
        int start,
        int removedLength,
        string inserted,
        long now)
    {
        if (kind != entry.Kind || entry.Kind == XPlatUndoEditKind.Replace)
        {
            return false;
        }

        var gap = XPlatUndoClock.Current.GetElapsedTime(entry.Timestamp, now);
        if (gap < TimeSpan.Zero || gap > CoalesceWindow)
        {
            return false;
        }

        return entry.Kind switch
        {
            XPlatUndoEditKind.Typing =>
                removedLength == 0 &&
                IsOneCodePoint(inserted) &&
                start == entry.Start + entry.InsertedText.Length,

            // Backspace removes the character before the run and moves its start; Delete removes the
            // one after it and leaves the start alone. Both extend the same run, so a burst of either
            // key - or of them alternating - undoes as one step.
            XPlatUndoEditKind.Deleting =>
                inserted.Length == 0 &&
                removedLength == 1 &&
                (start == entry.Start - 1 || start == entry.Start),

            _ => false,
        };
    }

    /// <summary>A single character, or a surrogate pair, which is one character to the user.</summary>
    private static bool IsOneCodePoint(string text) =>
        text.Length == 1 || (text.Length == 2 && char.IsSurrogatePair(text[0], text[1]));

    private void Apply(XPlatTextBox textBox, UndoEntry entry, bool undoing)
    {
        string present = undoing ? entry.InsertedText : entry.RemovedText;
        string restored = undoing ? entry.RemovedText : entry.InsertedText;

        _applying = true;
        try
        {
            textBox.ApplyHistoryEdit(entry.Start, present.Length, restored);

            // The insertion point is part of the entry: the caret goes back to where the user left
            // it, and a selection that was replaced is restored with its own direction.
            textBox.SetSelection(
                undoing ? entry.AnchorBefore : entry.AnchorAfter,
                undoing ? entry.CaretBefore : entry.CaretAfter);

            // SetSelection scrolls the caret into view, which is the right answer for a one-line
            // field and the minimum for a multi-line one. Restoring the recorded offsets afterwards
            // puts a multi-line view back exactly where the edit was made, and since those offsets
            // were taken with the caret in this position, it stays in view.
            if (textBox.MultilineFeature is { } multiline)
            {
                multiline.ScrollTo(
                    textBox,
                    textBox.Viewport,
                    undoing ? entry.ScrollXBefore : entry.ScrollXAfter,
                    undoing ? entry.ScrollYBefore : entry.ScrollYAfter);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Ends the run, so the next edit cannot join the entry that was just undone or redone.</summary>
    private void CloseRun()
    {
        _open = null;
        _pending = null;
    }

    private void ClearRedo()
    {
        _redo.Clear();
    }

    /// <summary>Drops the oldest entries until the depth and the memory budget are satisfied.</summary>
    private void Trim()
    {
        while (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        while (_undo.Count > 1 && RetainedCharacters() > RetainedCharacterBudget)
        {
            _undo.RemoveAt(0);
        }

        if (_open is not null && !_undo.Contains(_open))
        {
            _open = null;
        }
    }

    private int RetainedCharacters()
    {
        int total = 0;
        foreach (var entry in _undo)
        {
            total += entry.Characters;
        }

        foreach (var entry in _redo)
        {
            total += entry.Characters;
        }

        return total;
    }

    private static (float X, float Y) ScrollOf(XPlatTextBox textBox) =>
        textBox.MultilineFeature is { } multiline
            ? (multiline.HorizontalOffset, multiline.VerticalOffset)
            : (0f, 0f);

    /// <summary>
    /// One change: what it replaced, what it inserted, and the insertion point on both sides of it.
    /// Mutable, because a run of typing or deleting grows the entry rather than adding another.
    /// </summary>
    private sealed class UndoEntry
    {
        /// <summary>Where the replaced run begins.</summary>
        public int Start;

        /// <summary>The run this change replaced.</summary>
        public string RemovedText = string.Empty;

        /// <summary>The run it inserted.</summary>
        public string InsertedText = string.Empty;

        /// <summary>True for an assignment, whose inserted run is only known once the box holds it.</summary>
        public bool ReadsInsertedFromBox;

        public XPlatUndoEditKind Kind;

        /// <summary>When the run last grew, which is what the next character's gap is measured from.</summary>
        public long Timestamp;

        public int AnchorBefore;
        public int CaretBefore;
        public int AnchorAfter;
        public int CaretAfter;
        public float ScrollXBefore;
        public float ScrollYBefore;
        public float ScrollXAfter;
        public float ScrollYAfter;

        /// <summary>How much text this entry pins, for the memory budget.</summary>
        public int Characters => RemovedText.Length + InsertedText.Length;
    }
}
