using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The list box page: one list per selection mode, and what the selection reports.
/// </summary>
internal sealed class ListsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ListsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// An <see cref="ListBox"/> in <see cref="SelectionMode.MultiExtended"/>: click to
    /// select, Ctrl+click to toggle, Shift+click to extend. The button cycles the mode so all four
    /// behave the same way as the WinForms control.
    /// </summary>
    private Panel Build()
    {
        var card = Card();

        var modeCaption = ScrollCaption("MultiExtended");
        card.Controls.Add(modeCaption);

        var list = new ListBox
        {
            Dock = XDock.Top,
            Height = 108f,
            SelectionMode = SelectionMode.MultiExtended,
            VerticalScroll = ScrollStyle.Automatic,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        list.Items.AddRange(new object[]
        {
                "Overview", "Labels", "Buttons", "Text input", "Rich text", "Images",
                "Scrolling", "Lists", "Dialogs", "Split container", "Selection", "Drag and drop",
                "Menus", "Owned windows",
        });

        list.SelectedIndex = 0;
        card.Controls.Add(list);

        var selectionLabel = ScrollCaption("1 selected");
        selectionLabel.Margin = new Thickness(0f, 0f, 0f, 6f);
        card.Controls.Add(selectionLabel);

        list.AddSelectedIndexChangedHandler(_ =>
        {
            int count = list.SelectedIndices.Count;
            selectionLabel.Text = count switch
            {
                0 => "no selection",
                1 => $"\"{list.GetItemText(list.SelectedIndex)}\" selected",
                _ => $"{count} rows",
            };
        });

        list.AddItemActivatedHandler(_ =>
            SetStatus($"Activated \"{list.GetItemText(list.SelectedIndex)}\"."));

        var modeButton = new Button("Next mode")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };

        modeButton.AddClickHandler(_ =>
        {
            var mode = list.SelectionMode switch
            {
                SelectionMode.None => SelectionMode.One,
                SelectionMode.One => SelectionMode.MultiSimple,
                SelectionMode.MultiSimple => SelectionMode.MultiExtended,
                _ => SelectionMode.None,
            };

            list.SelectionMode = mode;
            modeCaption.Text = mode.ToString();
            SetStatus($"List selection mode is {mode}.");
        });

        card.Controls.Add(modeButton);

        return card;
    }

    /// <summary>
    /// Two list views over the same shape of data: one in <see cref="View.Details"/> with
    /// sortable columns and grid lines, one in <see cref="View.List"/>. Both only paint the
    /// rows on screen, so the row count is there to make that claim checkable.
    /// </summary>
}
