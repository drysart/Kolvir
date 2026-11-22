using Kolvir.DragDrop;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The text input page: a single-line field per state, and the dialog that takes one.
/// </summary>
internal sealed class TextInputPage : DemoPage
{
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

        var nameBox = new TextBox
        {
            PlaceholderText = "Type your name",
            MaxLength = 24,
            AllowDrop = true,
        };
        nameBox.AddTextChangedHandler(_ => SetStatus(nameBox.Text.Length == 0
            ? "Name cleared."
            : $"Name: {nameBox.Text} ({nameBox.Text.Length}/{nameBox.MaxLength})"));

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

        return section;
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
