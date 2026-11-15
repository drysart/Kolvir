using Kolvir;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// A control with no chrome whose only property is a nullable string, used to test the difference
/// between "not set", "set to a value" and "set to null".
/// </summary>
/// <remarks>
/// Its initial value is not the type's default on purpose: the reconciler restores a property that a
/// render stops setting to what the control's constructor left, not to <c>default(T)</c>, and a test
/// that could not tell those apart would not be testing anything.
/// </remarks>
internal sealed class NoteControl : Control
{
    /// <summary>What a freshly constructed control's <see cref="Note"/> reads as.</summary>
    internal const string Pristine = "pristine";

    /// <summary>A nullable property, so that null is a value rather than an absence.</summary>
    public string? Note { get; set; } = Pristine;
}

/// <summary>A hand-written node for <see cref="Label"/>, standing in for what the generator emits.</summary>
internal sealed class VLabel : VNode
{
    private static readonly NodeType Kind = new(typeof(Label), static () => new Label());

    private static readonly string PristineText = new Label().Text;
    private static readonly Thickness PristinePadding = new Label().Padding;

    private Specified<string> _text;
    private Specified<Thickness> _padding;

    public string Text
    {
        get => _text.Value;
        set => _text = value;
    }

    public Thickness Padding
    {
        get => _padding.Value;
        set => _padding = value;
    }

    public override NodeType NodeType => Kind;

    public override void ApplyTo(Control control)
    {
        var label = (Label)control;

        if (_text.IsSpecified)
        {
            label.Text = _text.Value;
        }

        if (_padding.IsSpecified)
        {
            label.Padding = _padding.Value;
        }
    }

    public override void PatchFrom(VNode previous, Control control)
    {
        var old = (VLabel)previous;
        var label = (Label)control;

        if (_text != old._text)
        {
            label.Text = _text.IsSpecified ? _text.Value : PristineText;
        }

        if (_padding != old._padding)
        {
            label.Padding = _padding.IsSpecified ? _padding.Value : PristinePadding;
        }
    }
}

/// <summary>A hand-written node for <see cref="NoteControl"/>, whose property is nullable.</summary>
internal sealed class VNote : VNode
{
    private static readonly NodeType Kind = new(typeof(NoteControl), static () => new NoteControl());
    private static readonly string? PristineNote = new NoteControl().Note;

    private Specified<string?> _note;

    public string? Note
    {
        get => _note.Value;
        set => _note = value;
    }

    public override NodeType NodeType => Kind;

    public override void ApplyTo(Control control)
    {
        if (_note.IsSpecified)
        {
            ((NoteControl)control).Note = _note.Value;
        }
    }

    public override void PatchFrom(VNode previous, Control control)
    {
        var old = (VNote)previous;

        if (_note != old._note)
        {
            ((NoteControl)control).Note = _note.IsSpecified ? _note.Value : PristineNote;
        }
    }
}

/// <summary>A hand-written node for <see cref="Button"/>, including the click handler.</summary>
internal sealed class VButton : VNode
{
    private static readonly NodeType Kind = new(typeof(Button), static () => new Button());
    private static readonly string PristineText = new Button().Text;
    private static readonly DockStyle PristineDock = new Button().Dock;

    private Specified<string> _text;
    private Specified<DockStyle> _dock;
    private Specified<Action<object>?> _click;

    public string Text
    {
        get => _text.Value;
        set => _text = value;
    }

    public DockStyle Dock
    {
        get => _dock.Value;
        set => _dock = value;
    }

    public Action<object>? Click
    {
        get => _click.Value;
        set => _click = value;
    }

    public override NodeType NodeType => Kind;

    public override void ApplyTo(Control control)
    {
        var button = (Button)control;

        if (_text.IsSpecified)
        {
            button.Text = _text.Value;
        }

        if (_dock.IsSpecified)
        {
            button.Dock = _dock.Value;
        }

        if (_click.IsSpecified && _click.Value is not null)
        {
            button.AddClickHandler(_click.Value);
        }
    }

    public override void PatchFrom(VNode previous, Control control)
    {
        var old = (VButton)previous;
        var button = (Button)control;

        if (_text != old._text)
        {
            button.Text = _text.IsSpecified ? _text.Value : PristineText;
        }

        if (_dock != old._dock)
        {
            button.Dock = _dock.IsSpecified ? _dock.Value : PristineDock;
        }

        if (_click != old._click)
        {
            if (old._click.IsSpecified && old._click.Value is { } detached)
            {
                button.RemoveClickHandler(detached);
            }

            if (_click.IsSpecified && _click.Value is { } attached)
            {
                button.AddClickHandler(attached);
            }
        }
    }
}

/// <summary>A hand-written container node for <see cref="Panel"/>, with a dock and children.</summary>
internal sealed class VPanel : VNode
{
    private static readonly NodeType Kind = new(typeof(Panel), static () => new Panel());
    private static readonly DockStyle PristineDock = new Panel().Dock;

    private Specified<DockStyle> _dock;

    public DockStyle Dock
    {
        get => _dock.Value;
        set => _dock = value;
    }

    public override NodeType NodeType => Kind;

    public override void ApplyTo(Control control)
    {
        if (_dock.IsSpecified)
        {
            control.Dock = _dock.Value;
        }
    }

    public override void PatchFrom(VNode previous, Control control)
    {
        var old = (VPanel)previous;

        if (_dock != old._dock)
        {
            control.Dock = _dock.IsSpecified ? _dock.Value : PristineDock;
        }
    }
}
