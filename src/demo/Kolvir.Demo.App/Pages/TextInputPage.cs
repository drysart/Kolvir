using Kolvir.Feature.DragDrop;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The text input page: a single-line field per state, and the dialog that takes one.
/// </summary>
internal sealed class TextInputPage : DemoPage
{
    private CompositionTextBox? _nameBox;
    private Label? _readout;
    private string _preedit = string.Empty;
    private int _preeditCaret;
    private string _lastCommit = string.Empty;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public TextInputPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var section = new Panel
        {
            // A row of fields is a caption plus a field tall, so the stage below it stays empty
            // rather than stretching the inputs to the height of the window.
            Dock = XDock.Top,
            Height = 54f,
            Padding = new Thickness(0f),
        };

        var nameBox = new CompositionTextBox(this)
        {
            PlaceholderText = "Type your name",
            MaxLength = 24,
            AllowDrop = true,
        };
        _nameBox = nameBox;
        nameBox.AddTextChangedHandler(_ =>
        {
            SetStatus(nameBox.Text.Length == 0
                ? "Name cleared."
                : $"Name: {nameBox.Text} ({nameBox.Text.Length}/{nameBox.MaxLength})");
            UpdateReadout();
        });

        var passwordBox = new TextBox
        {
            IsPassword = true,
            PlaceholderText = "Password",
        };
        passwordBox.AddTextChangedHandler(_ => SetStatus($"Password length: {passwordBox.Text.Length}"));

        var readOnlyBox = new TextBox
        {
            Text = "Read-only - select, then Ctrl+C",
            ReadOnly = true,
        };

        var row = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
        };
        row.Controls.Add(Field("Name", nameBox, 320f));
        row.Controls.Add(Field("Password", passwordBox, 280f));
        row.Controls.Add(Field("Read only", readOnlyBox, 0f));
        section.Controls.Add(row);

        // The evidence a headed run is judged by: what the input method has not committed, where its
        // caret is inside it, what it last committed, and what the field's value says. A screenshot
        // of this line, before and after a dead key, is what proves the GTK wiring.
        _readout = new Label(string.Empty)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            WrapText = true,
        };

        var body = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
        };

        body.Controls.Add(section);
        body.Controls.Add(_readout);
        UpdateReadout();

        return body;
    }

    /// <summary>Reports the preedit the field currently holds, and where its caret is.</summary>
    private void ShowComposition(string preedit, int caretIndex)
    {
        _preedit = preedit;
        _preeditCaret = caretIndex;
        UpdateReadout();
    }

    /// <summary>Reports the last piece of text the field inserted, which is what a commit becomes.</summary>
    private void ShowCommit(string text)
    {
        _lastCommit = text;
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        if (_readout is null)
        {
            return;
        }

        var value = _nameBox?.Text ?? string.Empty;
        _readout.Text =
            $"Preedit: \"{_preedit}\"  |  preedit caret: {_preeditCaret}  |  " +
            $"last commit: \"{_lastCommit}\"  |  value: \"{value}\"";
    }

    /// <summary>
    /// The name field, with the composition readout hung off the protected hooks. Nothing here is
    /// public API: an application control author observes composition by overriding the same three
    /// hooks, which is also what makes this a fair test of their shape.
    /// </summary>
    private sealed class CompositionTextBox : TextBox
    {
        private readonly TextInputPage _page;

        public CompositionTextBox(TextInputPage page) => _page = page;

        protected override bool OnCompositionStarted()
        {
            bool taken = base.OnCompositionStarted();
            _page.ShowComposition(string.Empty, 0);
            return taken;
        }

        protected override bool OnCompositionUpdated(string text, int caretIndex)
        {
            bool taken = base.OnCompositionUpdated(text, caretIndex);
            _page.ShowComposition(text, caretIndex);
            return taken;
        }

        protected override void OnCompositionCancelled()
        {
            base.OnCompositionCancelled();
            _page.ShowComposition(string.Empty, 0);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            if (e.Handled)
            {
                _page.ShowCommit(e.Text);
            }
        }
    }

    /// <summary>A captioned input: a small label above the control. A width of 0 docks the field to fill.</summary>
    private static Panel Field(string caption, TextBox input, float width)
    {
        bool fill = width <= 0f;
        var field = new Panel
        {
            Dock = fill ? XDock.Fill : XDock.Left,
            Width = fill ? 0f : width,
            Padding = new Thickness(0f, 0f, fill ? 0f : 16f, 0f),
        };

        field.Controls.Add(new Label(caption)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        input.Dock = XDock.Fill;
        field.Controls.Add(input);

        return field;
    }
}
