using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The tool tips page: what each control asks for, and where it appears.
/// </summary>
internal sealed class ToolTipsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ToolTipsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// One <see cref="ToolTip"/> shared by every control on the card, which is the shape the
    /// Windows Forms component has: the tooltip is not a child of anything, it follows the pointer.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Tool tips"));

        var toolTip = new ToolTip { InitialDelay = 400, AutoPopDelay = 5000 };

        card.Controls.Add(ScrollCaption("Hover a control to see its tip"));

        var primary = new Button("Save")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        toolTip.SetToolTip(primary, "Write the document to disk.");
        card.Controls.Add(primary);

        var secondary = new Button("Save as")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        toolTip.SetToolTip(secondary, "Write the document to a new file.");
        card.Controls.Add(secondary);

        var longTip = new Button("A long tip")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Ghost,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        // The tooltip measures itself and wraps at MaximumWidth, so a long string stays readable
        // instead of becoming one very wide line.
        toolTip.SetToolTip(longTip, "This tip is long enough that it has to wrap onto more than one line, which is what MaximumWidth is for.");
        card.Controls.Add(longTip);

        var field = new TextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "Hover me",
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };
        toolTip.SetToolTip(field, "Any control can carry a tip, not only buttons.");
        card.Controls.Add(field);

        var toggled = new CheckBox("Disable the tips")
        {
            Dock = XDock.Top,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };
        toggled.AddCheckedChangedHandler(_ => toolTip.Active = !toggled.Checked);
        card.Controls.Add(toggled);

        card.Controls.Add(new Label(
            "A tip is shown in a popup that never takes the pointer, so the control under it keeps the hover.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>Shows <paramref name="message"/> in the footer's status line.</summary>
    /// <param name="message">The text to show.</param>
    /// <remarks>
    /// Public so a host can report something the sample UI did not cause itself, such as a second
    /// launch being handed to the running window.
    /// </remarks>
    /// <summary>
    /// Publishes the live presentation path for the footer and the measured-rendering page. The
    /// backend only knows it once the window really exists, so the harness calls this from
    /// <see cref="Window.AddShownHandler"/> rather than before <c>Show</c>.
    /// </summary>
    /// <param name="path"><c>gl</c> or <c>cpu (...)</c>, from the Win32 backend.</param>
    /// <param name="renderer">The vendor and renderer string, or null on the CPU path.</param>
}
