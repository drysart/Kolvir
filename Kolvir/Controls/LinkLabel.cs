using SkiaSharp;

namespace Kolvir;

/// <summary>
/// What a click on an <see cref="LinkLabel"/> did: which span was clicked, which URI the
/// label was going to open, and whether the platform accepted it.
/// </summary>
public sealed class LinkClickedEventArgs : EventArgs
{
    public LinkClickedEventArgs(string? url, TextSpan? span)
    {
        Url = url;
        Span = span;
    }

    /// <summary>The URI the label carried when it was clicked, or null when it carries none.</summary>
    public string? Url { get; }

    /// <summary>
    /// The span that was clicked, or null when the link was activated from the keyboard - a key
    /// press belongs to the whole label rather than to one run of text.
    /// </summary>
    public TextSpan? Span { get; }

    /// <summary>
    /// True when the label handed <see cref="Url"/> to the desktop. False when it had no URI, when
    /// <see cref="LinkLabel.AutoLaunch"/> is off, or when the platform could not open it - in
    /// which case a handler is the only thing that can still navigate.
    /// </summary>
    public bool Opened { get; internal set; }
}

/// <summary>
/// A hyperlink: an <see cref="RichLabel"/> whose text is styled as a link and which opens a
/// URI in the user's own handler when it is clicked or activated from the keyboard. The styling is
/// inherited from <see cref="RichLabel.AddLink"/>, so extra spans can be added around the link
/// to build a sentence.
/// </summary>
/// <example>
/// <code>
/// var link = new LinkLabel("Project home", "https://github.com/drysart/Kolvir");
/// link.AddLinkClickedHandler((_, e) => SetStatus(e.Opened ? "Opened" : "Could not open"));
/// </code>
/// </example>
public class LinkLabel : RichLabel
{
    private Action<object, LinkClickedEventArgs>? _linkClicked;
    private TextSpan? _keyPressedSpan;
    private string? _url;

    public LinkLabel()
    {
        TabStop = true;
        Cursor = Cursor.Hand;
        Padding = new Thickness(4f, 2f);
        MinimumSize = new SKSize(0f, 0f);
        Own(AddSpanClickHandler(static (label, e) => ((LinkLabel)label).Activate(e.Span)));
    }

    public LinkLabel(string text)
        : this()
    {
        Text = text;
    }

    public LinkLabel(string text, string? url)
        : this()
    {
        _url = url;
        Text = text;
    }

    /// <summary>Adds a handler that runs when the link is activated, before or instead of the URI opening.</summary>
    public IDisposable AddLinkClickedHandler(Action<object, LinkClickedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _linkClicked += handler;
        return new HandlerToken<Action<object, LinkClickedEventArgs>>(RemoveLinkClickedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddLinkClickedHandler"/> added.</summary>
    public void RemoveLinkClickedHandler(Action<object, LinkClickedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _linkClicked -= handler;
    }

    /// <summary>
    /// The URI to open. Null leaves the label a plain link that only raises
    /// <see cref="AddLinkClickedHandler"/>, which is what a link that navigates inside the
    /// application itself wants.
    /// </summary>
    public string? Url
    {
        get => _url;
        set => _url = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Whether activating the label opens <see cref="Url"/> through <see cref="Launcher"/>.
    /// Defaults to true; turn it off to navigate the URI yourself from a
    /// <see cref="AddLinkClickedHandler"/> handler - to open it in an in-app browser, say.
    /// </summary>
    public bool AutoLaunch { get; set; } = true;

    /// <summary>
    /// The label text, which is the link. Assigning replaces every span with one link span, so a
    /// label built from several spans should be filled through <see cref="RichLabel.AddLink"/>
    /// instead.
    /// </summary>
    public override string Text
    {
        get => base.Text;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(text, base.Text, StringComparison.Ordinal))
            {
                return;
            }

            ClearSpans();
            if (text.Length > 0)
            {
                AddLink(text);
            }

            OnTextChanged();
        }
    }

    /// <summary>Raises the link-clicked event. Override to react to activation without subscribing.</summary>
    protected virtual void OnLinkClicked(LinkClickedEventArgs e) => _linkClicked?.Invoke(this, e);

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (IsFocused && Enabled)
        {
            // A link has no fill of its own, so the ring is the only thing marking it as the
            // focused control. It is drawn thin to stay inside the padding rather than across
            // the text.
            Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 1.5f, 4f);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is not (Key.Enter or Key.Space) || _keyPressedSpan is not null)
        {
            return;
        }

        e.Handled = true;
        _keyPressedSpan = FirstSpan();
        if (_keyPressedSpan is not null)
        {
            _keyPressedSpan.IsPressed = true;
            Invalidate();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key is not (Key.Enter or Key.Space) || _keyPressedSpan is null)
        {
            return;
        }

        e.Handled = true;
        _keyPressedSpan.IsPressed = false;
        _keyPressedSpan = null;
        Invalidate();
        Activate(null);
    }

    protected override void OnLostFocus()
    {
        ClearKeyPress();
        base.OnLostFocus();
    }

    protected override void OnEnabledChanged()
    {
        ClearKeyPress();
        base.OnEnabledChanged();
    }

    /// <summary>
    /// Opens the URI and raises the event. <paramref name="span"/> is the span that was clicked,
    /// or null when the activation came from the keyboard.
    /// </summary>
    private void Activate(TextSpan? span)
    {
        if (!Enabled)
        {
            return;
        }

        bool opened = AutoLaunch && Launcher.OpenUrl(_url ?? string.Empty);
        OnLinkClicked(new LinkClickedEventArgs(_url, span) { Opened = opened });
    }

    private TextSpan? FirstSpan() => Spans.Count > 0 ? Spans[0] : null;

    private void ClearKeyPress()
    {
        if (_keyPressedSpan is null)
        {
            return;
        }

        _keyPressedSpan.IsPressed = false;
        _keyPressedSpan = null;
        Invalidate();
    }
}
