namespace Kolvir;

/// <summary>
/// The keyboard a shown popup borrows from the window that has it: Escape closes the popup or is
/// swallowed, Tab stays inside it, and every other key and typed character goes to the control the
/// popup's own tree has focused.
/// </summary>
/// <remarks>
/// <para>
/// It exists because no backend popup takes keyboard focus - Win32 shows the window without
/// activating it, AppKit's popup window cannot become key or main, and GTK4's popover hands keys
/// back to the window that owns them. The keys therefore keep arriving at the owner, and this sink
/// is what carries them into the popup's tree: without it a <see cref="TextBox"/> inside a popup
/// would be a picture of a text box.
/// </para>
/// <para>
/// It follows <see cref="MenuView"/>, which pushes itself onto the owning window for the same
/// reason: the sink is registered when the popup opens and disposed when it closes, by every route,
/// so a window whose popup has gone stops hearing about it.
/// </para>
/// <para>
/// A key the popup's focused control does not take is answered with false rather than swallowed, so
/// that the platform still turns it into typed text - Win32 skips <c>TranslateMessage</c> for a key
/// the library keeps, and a swallowed letter is a letter nobody can type. The price is that such a
/// key still reaches the window's menu bar and shortcuts, which is the same price
/// <see cref="MenuView"/> pays for a control it hosts in a row. Every key the popup does take, and
/// all typed text, stops here.
/// </para>
/// </remarks>
internal sealed class PopupInputSink : IInputSink, IDisposable
{
    private readonly PopupSession _session;
    private readonly Control _content;
    private readonly Control? _anchor;
    private readonly bool _escapeCloses;
    private bool _disposed;

    /// <summary>Creates the bridge for one shown popup.</summary>
    /// <param name="session">The session whose result Escape completes.</param>
    /// <param name="content">The control the popup shows, which is the root of its tree.</param>
    /// <param name="anchor">The control the popup hangs from, which takes the focus back when it can.</param>
    /// <param name="escapeCloses">Whether Escape closes the popup.</param>
    public PopupInputSink(PopupSession session, Control content, Control? anchor, bool escapeCloses)
    {
        _session = session;
        _content = content;
        _anchor = anchor;
        _escapeCloses = escapeCloses;
    }

    /// <summary>
    /// Puts the keyboard into the popup's tree, which is what makes the popup usable at all: the
    /// first tab stop in it takes the focus, and a content control with no tab stops in it takes
    /// the focus itself when it can.
    /// </summary>
    public void FocusContent() => Target();

    /// <inheritdoc />
    public bool ProcessKeyDown(Key key, Modifiers modifiers)
    {
        if (_disposed)
        {
            return false;
        }

        // A popup opened inside this one owns the keyboard while it is open. Its sink was
        // registered after this one, so it has already had the key; taking it here as well would
        // deliver it to the popup underneath as well.
        if (_session.Surface.Current is { } nested && !nested.Completion.IsCompleted)
        {
            return false;
        }

        // Escape belongs to the popup showing, whether or not it closes on it: a popup that decided
        // to stay open must not hand the key on to the control behind it, which would dismiss or
        // cancel something the user did not see.
        if (key == Key.Escape)
        {
            if (_escapeCloses)
            {
                _session.Complete(PopupResult.Dismissed(PopupCloseReason.Escape));
            }

            return true;
        }

        // Tab cycles the popup's own tree and wraps at both ends, the way the window's Tab walk
        // does - so a popup with one control keeps the focus on it instead of leaking it to the
        // window behind.
        if (key == Key.Tab)
        {
            MoveFocus((modifiers & Modifiers.Shift) != 0 ? -1 : 1);
            return true;
        }

        return Target()?.RaiseKeyDown(new KeyEventArgs(key, modifiers)) == true;
    }

    /// <inheritdoc />
    public bool ProcessTextInput(string text)
    {
        if (_disposed || text.Length == 0)
        {
            return false;
        }

        if (_session.Surface.Current is { } nested && !nested.Completion.IsCompleted)
        {
            return false;
        }

        // Text goes into the popup's tree or nowhere: the popup is showing, so the control the
        // window had focused must not receive what the user typed at the popup.
        Target()?.RaiseTextInput(new TextInputEventArgs(text));
        return true;
    }

    /// <summary>
    /// Stops carrying the keyboard and hands the focus back out of the popup, because the focus the
    /// popup took belongs to a tree that is about to go away.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_content.Host is not { } host || host.FocusedControl is not { } focused || !Contains(focused))
        {
            return;
        }

        // The control that opened the popup takes the focus back when it still can, which is what a
        // drop-down does; with nothing to give it to, the window keeps no focus rather than one
        // pointing into a control nobody can see any more.
        if (_anchor is { } anchor && anchor.Focusable && anchor.IsReachable)
        {
            anchor.Focus();
        }

        if (_content.Host is { } remaining && remaining.FocusedControl is { } still && Contains(still))
        {
            remaining.SetFocus(null);
        }
    }

    /// <summary>
    /// The control inside the popup that holds the keyboard, taking the focus into the popup when it
    /// is somewhere else - which is the case on open, and after a popup inside this one closed.
    /// </summary>
    private Control? Target()
    {
        if (_disposed || _content.Host is not { } host)
        {
            return null;
        }

        if (host.FocusedControl is { } focused && Contains(focused))
        {
            return focused;
        }

        var stops = UiHost.CollectTabStops(_content);

        if (stops.Count > 0)
        {
            stops[0].Focus();
            return stops[0];
        }

        if (_content.Focusable && _content.IsReachable)
        {
            _content.Focus();
            return _content;
        }

        return null;
    }

    /// <summary>Moves the focus one stop forward or back inside the popup, wrapping at both ends.</summary>
    private void MoveFocus(int direction)
    {
        var stops = UiHost.CollectTabStops(_content);

        if (stops.Count == 0)
        {
            // Nothing in the popup is a tab stop, so Tab has nowhere to go - including out of the
            // popup, which is the one place it must not go.
            Target();
            return;
        }

        int current = _content.Host is { FocusedControl: { } focused } ? stops.IndexOf(focused) : -1;

        int next = current < 0
            ? (direction >= 0 ? 0 : stops.Count - 1)
            : ((current + direction) % stops.Count + stops.Count) % stops.Count;

        stops[next].Focus();
    }

    /// <summary>Whether <paramref name="control"/> is the popup's content or inside it.</summary>
    private bool Contains(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, _content))
            {
                return true;
            }
        }

        return false;
    }
}
