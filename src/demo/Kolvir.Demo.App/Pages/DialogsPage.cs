using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The dialogs page: the message box, the open and save file dialogs, and the owned window.
/// </summary>
internal sealed class DialogsPage : DemoPage
{
    private enum DialogKind
    {
        Open,
        Save,
        Folder,
    }
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public DialogsPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var section = new Panel
        {
            // The buttons are as tall as a button is, so the row keeps a height of its own rather
            // than stretching to the height of the stage.
            Dock = XDock.Top,
            Height = 44f,
            Padding = new Thickness(0f),
        };

        var row = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
        };

        var messageBox = DialogButton("Message box", () => ShowMessageBox());
        row.Controls.Add(messageBox);
        row.Controls.Add(DialogButton("Open file", () => ShowFileDialog(DialogKind.Open)));
        row.Controls.Add(DialogButton("Save file", () => ShowFileDialog(DialogKind.Save)));
        row.Controls.Add(DialogButton("Pick folder", () => ShowFileDialog(DialogKind.Folder)));

        section.Controls.Add(row);
        return section;
    }

    private static Button DialogButton(string text, Action onClick)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => onClick());
        return button;
    }

    private void ShowMessageBox()
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var result = owner.Application.Dialogs.ShowMessage(
            new MessageBoxOptions
            {
                Message = "This box is drawn by the platform, not by Kolvir.\n\n" +
                          "It is modal to the window and the sample keeps painting behind it.",
                Title = "Kolvir sample",
                Buttons = MessageBoxButtons.YesNoCancel,
                Icon = MessageBoxIcon.Information,
                DefaultResult = DialogResult.Yes,
            },
            owner);

        SetStatus($"Message box returned {result}.");
    }

    private void ShowFileDialog(DialogKind kind)
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var dialogs = owner.Application.Dialogs;

        var options = new FileDialogOptions
        {
            Title = kind switch
            {
                DialogKind.Open => "Open a file",
                DialogKind.Save => "Save a file",
                _ => "Pick a folder",
            },
            FileName = kind == DialogKind.Save ? "untitled.txt" : null,
            OverwritePrompt = true,
        };

        if (kind != DialogKind.Folder)
        {
            options.Filters.Add(new FileFilter("Text files", "*.txt", "*.md"));
            options.Filters.Add(new FileFilter("All files", "*.*"));
        }

        var path = kind switch
        {
            DialogKind.Open => dialogs.OpenFile(options, owner),
            DialogKind.Save => dialogs.SaveFile(options, owner),
            _ => dialogs.PickFolder(options, owner),
        };

        SetStatus(path is null ? "Dialog cancelled." : $"Chose {path}");
    }
}
