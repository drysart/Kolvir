using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The tab order page: what Tab does, and how a control leaves it.
/// </summary>
internal sealed class TabOrderPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public TabOrderPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// A page whose only job is to be checked by hand. Creation order and <c>TabIndex</c> order
    /// disagree on purpose, a nested container orders its own children, a focusable container is
    /// entered as a unit, two controls are interleaved by index but meant to be skipped, two share
    /// an index, and the readout names whatever has the focus so a person can Tab and read the
    /// order rather than guess it.
    /// </summary>
    private Panel Build()
    {
        var section = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Padding = new Thickness(0f),
        };

        // The stops in the order Tab should reach them, filled in after the form is built. The
        // readout reads this list, so if the walk ever disagrees with it the page says "it is meant
        // to be skipped" instead of quietly describing whatever the library did.
        var expected = new List<(Control Control, string Name)>();
        var skipped = new List<(Control Control, string Name)>();

        var readout = new Label("Focus: nothing yet - press Tab to land on 1 Country.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        void Refresh()
        {
            foreach (var (control, name) in skipped)
            {
                if (control.IsFocused)
                {
                    readout.Text = $"Focus: {name} - it is meant to be skipped!";
                    return;
                }
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i].Control.IsFocused)
                {
                    readout.Text =
                        $"Focus: {expected[i].Name} - TabIndex {expected[i].Control.TabIndex} - " +
                        $"tab stop {i + 1} of {expected.Count}";
                    return;
                }
            }

            readout.Text = "Focus: nothing on this page - press Tab to land on 1 Country.";
        }

        void Watch(Control control, string name, bool isSkipped = false)
        {
            (isSkipped ? skipped : expected).Add((control, name));
            control.AddGotFocusHandler(_ => Refresh());
            control.AddLostFocusHandler(_ => Refresh());
        }

        // A label and its control on one line, both placed by hand. A docked row panel would be a
        // container of its own, and its index - not the control's - would decide the order, which is
        // exactly the thing this page exists to show correctly.
        static void Place(Panel parent, string caption, Control control, float y)
        {
            parent.Controls.Add(new Label(caption)
            {
                AutoSize = false,
                Bounds = new SKRect(0f, y, 330f, y + 28f),
                Font = Theme.CaptionFont,
                ForeColor = ThemeColor.MutedForeground,
                TextAlign = ContentAlignment.MiddleLeft,
            });

            control.Bounds = new SKRect(338f, y, 628f, y + 28f);
            parent.Controls.Add(control);
        }

        var form = Card();
        form.Dock = XDock.Left;
        form.Width = 660f;
        form.Margin = new Thickness(0f, 0f, 16f, 0f);
        form.Controls.Add(Heading("Tab order"));
        form.Controls.Add(new Label(
            "Press Tab repeatedly and watch the readout - and Shift+Tab to walk the same order " +
            "backwards. The order is Country, Street, Check address, Email, Standard shipping, Full " +
            "name, the scrollable container, Inside the container, Save. That is not the order these " +
            "controls were created in: they were created top to bottom, and Tab follows TabIndex. " +
            "After Save the walk leaves the page for the window's own controls and wraps back to Country.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 60f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });
        form.Controls.Add(readout);

        // Every stop is a direct child of this panel, placed by hand: layout must not invent
        // containers that the Tab walk would then have to order.
        var rows = new Panel
        {
            Dock = XDock.Top,
            Height = 436f,
            BackColor = null,
            Padding = new Thickness(0f),
        };
        form.Controls.Add(rows);

        // Created first, visited sixth.
        var fullName = new TextBox { TabIndex = 30 };
        Place(rows, "Created 1st - tab 6 - Full name (TabIndex 30)", fullName, 0f);

        // Created second, visited first.
        var country = new ComboBox { TabIndex = 10 };
        country.Items.Add("United Kingdom");
        country.Items.Add("United States");
        country.Items.Add("Germany");
        Place(rows, "Created 2nd - tab 1 - Country (TabIndex 10)", country, 34f);

        // Equal indices: this one is created before the radio button, so the tie-break puts it first.
        var email = new CheckBox("Email me a receipt") { AutoSize = false, TabIndex = 20 };
        Place(rows, "Created 3rd - tab 4 - Email (TabIndex 20, tie)", email, 68f);

        var standard = new RadioButton("Standard shipping") { AutoSize = false, TabIndex = 20 };
        Place(rows, "Created 4th - tab 5 - Standard (TabIndex 20, tie)", standard, 102f);

        // A nested container: not a stop itself, so it takes the place its own index gives it in the
        // outer order (15) and its children order among themselves.
        var addressPanel = new SurfacePanel
        {
            TabIndex = 15,
            Bounds = new SKRect(0f, 136f, 628f, 232f),
            Border = Framed(8f),
            Padding = new Thickness(8f),
        };
        rows.Controls.Add(addressPanel);
        addressPanel.Controls.Add(new Label(
            "Created 5th - TabIndex 15 - a nested container: its children order among themselves, 10 then 20.")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 0f, 612f, 18f),
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        // Created in the reverse of the order they are visited, so the container's own indices - not
        // creation order - explain the result.
        var checkAddress = new Button("Check address") { AutoSize = false, TabIndex = 20 };
        var street = new TextBox { TabIndex = 10 };

        addressPanel.Controls.Add(new Label("Created 5a - tab 3 - Check address (TabIndex 20)")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 22f, 300f, 50f),
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        checkAddress.Bounds = new SKRect(308f, 22f, 540f, 50f);
        addressPanel.Controls.Add(checkAddress);

        addressPanel.Controls.Add(new Label("Created 5b - tab 2 - Street (TabIndex 10)")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 56f, 300f, 84f),
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        street.Bounds = new SKRect(308f, 56f, 540f, 84f);
        addressPanel.Controls.Add(street);

        // Created here, but their indices put them between real stops - and they are skipped, which
        // is the point. One line so the page still fits above the fold.
        var notATabStop = new Button("Not a tab stop") { AutoSize = false, TabStop = false, TabIndex = 25 };
        var disabled = new Button("Disabled") { AutoSize = false, Enabled = false, TabIndex = 45 };
        rows.Controls.Add(new Label("Created 6th and 7th - never - skipped (TabIndex 25, 45)")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 236f, 330f, 264f),
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        notATabStop.Bounds = new SKRect(338f, 236f, 470f, 264f);
        disabled.Bounds = new SKRect(478f, 236f, 610f, 264f);
        rows.Controls.Add(notATabStop);
        rows.Controls.Add(disabled);

        // A focusable container: a tab stop of its own, and its children are walked straight after it.
        rows.Controls.Add(new Label(
            "Created 8th - tabs 7 and 8 - a scrollable panel that is itself a tab stop (TabIndex 40): " +
            "Tab lands on the container, and its child comes straight after it with TabIndex 0.")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 268f, 628f, 298f),
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        var scroller = new FocusRingPanel
        {
            TabIndex = 40,
            TabStop = true,
            Bounds = new SKRect(0f, 300f, 628f, 396f),
            VerticalScroll = ScrollStyle.Automatic,
            Border = Framed(8f),
            Padding = new Thickness(8f),
        };
        rows.Controls.Add(scroller);

        for (int i = 0; i < 4; i++)
        {
            scroller.Controls.Add(new Label($"Filler row {i + 1} - the wheel scrolls this container")
            {
                AutoSize = false,
                Bounds = new SKRect(4f, 38f + (i * 22f), 420f, 58f + (i * 22f)),
                Font = Theme.CaptionFont,
                ForeColor = ThemeColor.MutedForeground,
            });
        }

        var inside = new Button("Inside the container") { AutoSize = false, TabIndex = 0 };
        inside.Bounds = new SKRect(4f, 4f, 260f, 30f);

        // The rows below the fold are what make the panel scroll, and focusing one is the page's
        // demonstration of what an application does with a scrolling panel.
        inside.AddGotFocusHandler(_ => scroller.ScrollControlIntoView(inside));
        scroller.Controls.Add(inside);

        // Created last, visited last: the one row whose creation and tab position agree.
        var save = new Button("Save") { AutoSize = false, Style = ButtonStyle.Primary, TabIndex = 50 };
        Place(rows, "Created 9th - tab 9 - Save (TabIndex 50)", save, 408f);

        // The order this page expects, written in the order the walk should produce it. Nothing
        // else on the page derives from this list.
        Watch(country, "Country");
        Watch(street, "Street");
        Watch(checkAddress, "Check address");
        Watch(email, "Email me a receipt");
        Watch(standard, "Standard shipping");
        Watch(fullName, "Full name");
        Watch(scroller, "the scrollable container");
        Watch(inside, "Inside the container");
        Watch(save, "Save");
        Watch(notATabStop, "Not a tab stop", isSkipped: true);
        Watch(disabled, "Disabled", isSkipped: true);

        var notes = Card();
        notes.Controls.Add(Heading("What the order shows"));
        notes.Controls.Add(Caption("Click any control and press Tab: the walk continues from there."));
        notes.Controls.Add(Note(
            "Creation order is top to bottom; Tab follows TabIndex, so it starts at Country, the " +
            "lowest index, not at the first control created."));
        notes.Controls.Add(Note(
            "Per container: the address panel's children order among themselves (10, then 20), " +
            "where the panel's own index, 15, puts them."));
        notes.Controls.Add(Note(
            "Equal indices keep tree order: the check box and the radio button are both 20, and the " +
            "check box was created first."));
        notes.Controls.Add(Note(
            "A focused container is one step: Tab stops on the scrollable panel (40), then enters " +
            "it - the child at index 0 - then continues to Save."));
        notes.Controls.Add(Note(
            "Not a tab stop (25) and Disabled (45) sit between real stops by index and never take " +
            "focus; the readout says so if one does."));

        section.Controls.Add(form);
        section.Controls.Add(notes);
        return section;
    }

    private sealed class FocusRingPanel : Panel
    {
        protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
        {
            base.OnPaintBorder(canvas, rect);

            if (IsFocused)
            {
                Painter.DrawFocusRing(canvas, rect, Theme.FocusRing, 2f, Border.CornerRadius);
            }
        }
    }
}
