using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// What a click on an <see cref="XPlatLinkLabel"/> did: which span was clicked, which URI the
/// label was going to open, and whether the platform accepted it.
/// </summary>
public sealed class XPlatLinkClickedEventArgs : EventArgs
{
    public XPlatLinkClickedEventArgs(string? url, XPlatTextSpan? span)
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
    public XPlatTextSpan? Span { get; }

    /// <summary>
    /// True when the label handed <see cref="Url"/> to the desktop. False when it had no URI, when
    /// <see cref="XPlatLinkLabel.AutoLaunch"/> is off, or when the platform could not open it - in
    /// which case a handler is the only thing that can still navigate.
    /// </summary>
    public bool Opened { get; internal set; }
}

/// <summary>
/// A hyperlink: an <see cref="XPlatRichLabel"/> whose text is styled as a link and which opens a
/// URI in the user's own handler when it is clicked or activated from the keyboard. The styling is
/// inherited from <see cref="XPlatRichLabel.AddLink"/>, so extra spans can be added around the link
/// to build a sentence.
/// </summary>
/// <example>
/// <code>
/// var link = new XPlatLinkLabel("Project home", "https://github.com/drysart/XPlatUI");
/// link.AddLinkClickedHandler((_, e) => SetStatus(e.Opened ? "Opened" : "Could not open"));
/// </code>
/// </example>
public class XPlatLinkLabel : XPlatRichLabel
{
    private Action<object, XPlatLinkClickedEventArgs>? _linkClicked;
    private XPlatTextSpan? _keyPressedSpan;
    private string? _url;

    public XPlatLinkLabel()
    {
        TabStop = true;
        Cursor = XPlatCursor.Hand;
        Padding = new XPlatThickness(4f, 2f);
        MinimumSize = new SKSize(0f, 0f);
        Own(AddSpanClickHandler(static (label, e) => ((XPlatLinkLabel)label).Activate(e.Span)));
    }

    public XPlatLinkLabel(string text)
        : this()
    {
        Text = text;
    }

    public XPlatLinkLabel(string text, string? url)
        : this()
    {
        _url = url;
        Text = text;
    }

    /// <summary>Adds a handler that runs when the link is activated, before or instead of the URI opening.</summary>
    public IDisposable AddLinkClickedHandler(Action<object, XPlatLinkClickedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _linkClicked += handler;
        return new XPlatHandlerToken<Action<object, XPlatLinkClickedEventArgs>>(RemoveLinkClickedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddLinkClickedHandler"/> added.</summary>
    public void RemoveLinkClickedHandler(Action<object, XPlatLinkClickedEventArgs> handler)
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
    /// Whether activating the label opens <see cref="Url"/> through <see cref="XPlatLauncher"/>.
    /// Defaults to true; turn it off to navigate the URI yourself from a
    /// <see cref="AddLinkClickedHandler"/> handler - to open it in an in-app browser, say.
    /// </summary>
    public bool AutoLaunch { get; set; } = true;

    /// <summary>
    /// The label text, which is the link. Assigning replaces every span with one link span, so a
    /// label built from several spans should be filled through <see cref="XPlatRichLabel.AddLink"/>
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
    protected virtual void OnLinkClicked(XPlatLinkClickedEventArgs e) => _linkClicked?.Invoke(this, e);

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (IsFocused && Enabled)
        {
            // A link has no fill of its own, so the ring is the only thing marking it as the
            // focused control. It is drawn thin to stay inside the padding rather than across
            // the text.
            XPlatPainter.DrawFocusRing(canvas, rect, XPlatTheme.FocusRing, 1.5f, 4f);
        }
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is not (XPlatKey.Enter or XPlatKey.Space) || _keyPressedSpan is not null)
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

    protected override void OnKeyUp(XPlatKeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key is not (XPlatKey.Enter or XPlatKey.Space) || _keyPressedSpan is null)
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
    private void Activate(XPlatTextSpan? span)
    {
        if (!Enabled)
        {
            return;
        }

        bool opened = AutoLaunch && XPlatLauncher.OpenUrl(_url ?? string.Empty);
        OnLinkClicked(new XPlatLinkClickedEventArgs(_url, span) { Opened = opened });
    }

    private XPlatTextSpan? FirstSpan() => Spans.Count > 0 ? Spans[0] : null;

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
