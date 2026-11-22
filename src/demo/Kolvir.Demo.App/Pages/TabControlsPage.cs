using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The tab controls page: the strips, the overflow, and the pages underneath.
/// </summary>
internal sealed class TabControlsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public TabControlsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// A tab control whose pages hold real controls, plus a second one with more tabs than fit so
    /// the strip has to scroll.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Tab controls"));

        var readout = ScrollCaption("Selected: General");
        card.Controls.Add(readout);

        var tabs = new TabControl
        {
            Dock = XDock.Top,
            Height = 220f,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        };

        var general = new TabPage("General");
        general.Controls.Add(new CheckBox("Show the welcome screen")
        {
            Dock = XDock.Top,
            Checked = true,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });
        general.Controls.Add(new CheckBox("Reopen the last page")
        {
            Dock = XDock.Top,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });
        general.Controls.Add(new Label("Pages are ordinary panels: they dock, scroll and lay out like any other.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        var appearance = new TabPage("Appearance");
        appearance.Controls.Add(new ComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });
        appearance.Controls.Add(new ProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 45f,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });
        appearance.Controls.Add(DemoButton("A button on a page", () => SetStatus("A button on the Appearance page.")));

        var advanced = new TabPage("Advanced");
        advanced.Controls.Add(new TextBox { Dock = XDock.Top, Height = 32f, Margin = new Thickness(0f, 0f, 0f, 8f) });
        advanced.Controls.Add(new Label("Tab and the arrow keys move between pages.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        tabs.TabPages.Add(general);
        tabs.TabPages.Add(appearance);
        tabs.TabPages.Add(advanced);
        tabs.SelectedIndex = 0;
        tabs.AddSelectedIndexChangedHandler(_ => readout.Text = $"Selected: {tabs.SelectedTab?.Text ?? "none"}");
        card.Controls.Add(tabs);

        card.Controls.Add(ScrollCaption("More tabs than fit, so the strip scrolls with the wheel"));

        var crowded = new TabControl
        {
            Dock = XDock.Fill,
            Margin = new Thickness(0f, 0f, 0f, 0f),
        };

        foreach (string name in new[]
                 {
                         "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight",
                         "Nine", "Ten", "Eleven", "Twelve",
                     })
        {
            var page = new TabPage(name);
            page.Controls.Add(new Label($"Page {name}")
            {
                Dock = XDock.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
            });
            crowded.TabPages.Add(page);
        }

        card.Controls.Add(crowded);
        return card;
    }

    /// <summary>
    /// One <see cref="ToolTip"/> shared by every control on the card, which is the shape the
    /// Windows Forms component has: the tooltip is not a child of anything, it follows the pointer.
    /// </summary>
}
