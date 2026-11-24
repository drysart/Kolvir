namespace Kolvir.Backend.AppKit;

/// <summary>
/// The marked text of a macOS input method, held in step with the core composition contract.
/// </summary>
/// <remarks>
/// <para>
/// macOS reports a composition as <em>marked text</em>: the input manager calls
/// <c>setMarkedText:selectedRange:replacementRange:</c> while the text is still a proposal, and
/// <c>insertText:replacementRange:</c> when it is final. This class holds the half of that a
/// control-author cannot see: it opens the composition once, keeps the marked string and the caret
/// inside it, and ends it either as a commit or as a cancel.
/// </para>
/// <para>
/// It is deliberately free of AppKit types. The view supplies
/// <c>NSTextInputContext.DiscardMarkedText</c> as a callback, which is how macOS is told that the
/// field refused the composition - the equivalent of Win32's
/// <c>ImmNotifyIME(NI_COMPOSITIONSTR, CPS_CANCEL)</c> - and a test supplies a spy instead, so the
/// state machine is exercised on the process's main thread under <c>dotnet test</c> where no
/// <c>NSView</c> can be created.
/// </para>
/// <para>
/// The <c>replacementRange</c> a composition carries is ignored on purpose: v1 always commits at
/// the caret, which is the same decision section 3 of <c>dev-docs/IME.md</c> records for every
/// platform.
/// </para>
/// </remarks>
internal sealed class AppKitComposition
{
    private readonly UiHost _host;
    private readonly Action _discardMarkedText;

    private string _marked = string.Empty;
    private int _selected;
    private bool _composing;

    /// <summary>
    /// True while the marked text is being taken down, so the <c>unmarkText</c> AppKit sends in
    /// answer to <see cref="Discard"/> cannot re-enter the host and start the exchange again.
    /// </summary>
    private bool _discarding;

    /// <summary>Creates the composition state for <paramref name="host"/>.</summary>
    /// <param name="host">The control tree a commit and a preedit are routed through.</param>
    /// <param name="discardMarkedText">
    /// How to tell the input manager its marked text is going away. On the view this is
    /// <c>NSTextInputContext.DiscardMarkedText</c>; in a test it is a spy that records the call.
    /// </param>
    public AppKitComposition(UiHost host, Action discardMarkedText)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(discardMarkedText);

        _host = host;
        _discardMarkedText = discardMarkedText;
    }

    /// <summary>True while the input method holds marked text this class is mirroring.</summary>
    public bool HasMarkedText => _marked.Length > 0;

    /// <summary>The marked string, which is never part of a control's value.</summary>
    public string MarkedText => _marked;

    /// <summary>How long the marked string is, which is the length of its range.</summary>
    public int MarkedLength => _marked.Length;

    /// <summary>Where the input method's caret sits inside <see cref="MarkedText"/>.</summary>
    public int SelectedLocation => _selected;

    /// <summary>True once a control accepted the composition, whether or not text is marked yet.</summary>
    public bool IsComposing => _composing;

    /// <summary>How many times the input method has been asked to drop its marked text.</summary>
    public int Discards { get; private set; }

    /// <summary>
    /// Replaces the marked text, opening the composition the first time. Empty text is the input
    /// method's own way of saying the composition ended without a commit.
    /// </summary>
    /// <param name="text">The preedit string.</param>
    /// <param name="selectedLocation">The caret inside <paramref name="text"/>, in UTF-16 code units.</param>
    public void SetMarkedText(string text, int selectedLocation)
    {
        ArgumentNullException.ThrowIfNull(text);

        // AppKit answers a discard by re-offering an empty mark; anything arriving while the
        // discard is unwinding belongs to the composition being taken down.
        if (_discarding)
        {
            return;
        }

        if (text.Length == 0)
        {
            UnmarkText();
            return;
        }

        if (!_composing)
        {
            if (!_host.BeginComposition())
            {
                // A read-only or disabled field declines, and leaving it there would keep the input
                // method's own underline over a field that will never take the text.
                Discard();
                return;
            }

            _composing = true;
        }

        if (!_host.UpdateComposition(text, selectedLocation))
        {
            // The control stopped taking it between two updates - disabled mid-composition, or
            // made read-only. Drop whatever preedit it still holds and tell the input method.
            _host.CancelComposition();
            Discard();
            return;
        }

        _marked = text;
        _selected = Math.Clamp(selectedLocation, 0, text.Length);
    }

    /// <summary>
    /// Ends the composition with <paramref name="text"/>, which goes through the ordinary text
    /// path exactly as typing would. This is the only commit route for a composed character.
    /// </summary>
    /// <param name="text">The final text.</param>
    public void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_discarding)
        {
            return;
        }

        Clear();

        // The host drops the control's preedit itself, so a commit and a cancel cannot both arrive.
        _host.CommitComposition(text);
    }

    /// <summary>Drops the composition without inserting anything, and without an undo entry.</summary>
    public void UnmarkText()
    {
        if (_discarding || !HoldsAnything)
        {
            Clear();
            return;
        }

        Clear();
        _host.CancelComposition();
    }

    /// <summary>
    /// Ends a composition the library itself just acted on. The library sees a key before the
    /// input method does, so a key it consumes never reaches the input manager; this is the macOS
    /// spelling of GTK's <c>IMContext.Reset()</c> and Win32's cancel on a consumed key.
    /// </summary>
    public void Reset()
    {
        if (_discarding || !HoldsAnything)
        {
            return;
        }

        Clear();
        _host.CancelComposition();
        Discard();
    }

    /// <summary>True while the control holds a preedit or the input method holds a mark.</summary>
    private bool HoldsAnything => _composing || _marked.Length > 0;

    private void Clear()
    {
        _marked = string.Empty;
        _selected = 0;
        _composing = false;
    }

    /// <summary>
    /// Takes the marked text down and tells the input manager, once, under a guard that makes the
    /// resulting <c>unmarkText</c> a no-op. Without the guard a refused composition and AppKit's
    /// answer to the refusal call each other until the stack ends.
    /// </summary>
    private void Discard()
    {
        if (_discarding)
        {
            return;
        }

        Clear();
        Discards++;
        _discarding = true;

        try
        {
            _discardMarkedText();
        }
        finally
        {
            _discarding = false;
        }
    }
}
