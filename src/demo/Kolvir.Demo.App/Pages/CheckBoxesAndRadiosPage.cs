using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The check boxes and radios page: the three check states and two radio groups.
/// </summary>
internal sealed class CheckBoxesAndRadiosPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public CheckBoxesAndRadiosPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// Check boxes on the left, radio buttons on the right: the checks cover the three states and
    /// the disabled look, and the radios show two groups on one card separated only by
    /// <see cref="RadioButton.GroupName"/> rather than by a container each.
    /// </summary>
    private Panel Build()
    {
        var section = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Padding = new Thickness(0f),
        };

        var checks = Card();
        checks.Dock = XDock.Left;
        checks.Width = 320f;
        checks.Margin = new Thickness(0f, 0f, 16f, 0f);
        checks.Controls.Add(Heading("Check boxes"));

        foreach (string caption in new[] { "Auto-save", "Show line numbers", "Wrap long lines" })
        {
            var box = new CheckBox(caption)
            {
                Dock = XDock.Top,
                Margin = new Thickness(0f, 0f, 0f, 6f),
            };
            box.AddCheckedChangedHandler(_ => SetStatus($"{box.Text} is {Describe(box.CheckState)}."));
            checks.Controls.Add(box);
        }

        // ThreeState is set before CheckState because the initializer runs in order: a check box
        // that cannot hold the indeterminate state would drop it on the way in.
        var threeState = new CheckBox("Three state")
        {
            Dock = XDock.Top,
            ThreeState = true,
            CheckState = CheckState.Indeterminate,
            Margin = new Thickness(0f, 12f, 0f, 2f),
        };
        checks.Controls.Add(threeState);

        var stateReadout = ScrollCaption("Indeterminate");
        checks.Controls.Add(stateReadout);
        threeState.AddCheckedChangedHandler(_ => stateReadout.Text = Describe(threeState.CheckState));

        checks.Controls.Add(new CheckBox("Disabled, checked")
        {
            Dock = XDock.Top,
            Checked = true,
            Enabled = false,
            Margin = new Thickness(0f, 12f, 0f, 0f),
        });

        var radios = Card();
        radios.Controls.Add(Heading("Radio buttons"));
        radios.Controls.Add(new Label("Both groups are on this card: GroupName decides which radio buttons answer to each other.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });

        AddRadioGroup(radios, "Size", "size", ["Small", "Medium", "Large"], "Medium");
        AddRadioGroup(radios, "Alignment", "align", ["Left", "Centre", "Right"], "Left");

        radios.Controls.Add(new RadioButton("Disabled")
        {
            Dock = XDock.Top,
            Enabled = false,
            Margin = new Thickness(0f, 12f, 0f, 0f),
        });

        section.Controls.Add(checks);
        section.Controls.Add(radios);
        return section;
    }

    /// <summary>Adds a captioned run of radio buttons that form one group by name.</summary>
    private void AddRadioGroup(Panel card, string caption, string groupName, string[] options, string selected)
    {
        var heading = ScrollCaption(caption);
        heading.Margin = new Thickness(0f, 10f, 0f, 2f);
        card.Controls.Add(heading);

        foreach (string option in options)
        {
            var radio = new RadioButton(option)
            {
                Dock = XDock.Top,
                GroupName = groupName,
                Checked = option == selected,
                Margin = new Thickness(0f, 0f, 0f, 4f),
            };
            radio.AddCheckedChangedHandler(_ =>
            {
                if (radio.Checked)
                {
                    SetStatus($"{caption} is {option}.");
                }
            });
            card.Controls.Add(radio);
        }
    }

    private static string Describe(CheckState state) => state switch
    {
        CheckState.Checked => "checked",
        CheckState.Indeterminate => "indeterminate",
        _ => "unchecked",
    };

    /// <summary>
    /// The three <see cref="ProgressBarStyle"/>s, a disabled bar, and a Step button, so the
    /// fill is watched rather than described. Only the marquee bar animates, and only while the
    /// window is running a frame loop to tick it.
    /// </summary>
}
