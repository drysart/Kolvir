using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The combo boxes page: a plain one, an editable one, and one that is disabled.
/// </summary>
internal sealed class ComboBoxesPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ComboBoxesPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// Drop-down lists, including the two cases the popup layer exists for: a list longer than its
    /// owner and one near the bottom of the window, which hangs past the edge of the window instead
    /// of being clipped by it.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Combo boxes"));

        card.Controls.Add(ScrollCaption("A few items"));

        var sizes = new ComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        foreach (string size in new[] { "Small", "Medium", "Large", "Extra large" })
        {
            sizes.Items.Add(size);
        }

        sizes.SelectedIndex = 1;
        sizes.AddSelectedIndexChangedHandler(_ => SetStatus($"Size is {sizes.SelectedText}."));
        card.Controls.Add(sizes);

        card.Controls.Add(ScrollCaption("No selection, so the empty text shows"));

        var optional = new ComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            EmptyText = "Nothing selected",
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        optional.Items.Add("Alpha");
        optional.Items.Add("Beta");
        optional.Items.Add("Gamma");
        card.Controls.Add(optional);

        card.Controls.Add(ScrollCaption("Longer than its owner, so the list scrolls"));

        var languages = new ComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            MaxDropDownHeight = 160f,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        foreach (string language in new[]
                 {
                         "C#", "F#", "Rust", "Go", "Swift", "Kotlin", "TypeScript", "Python",
                         "Ruby", "Elixir", "Haskell", "OCaml", "Zig", "Nim", "D", "Julia",
                     })
        {
            languages.Items.Add(language);
        }

        languages.SelectedIndex = 0;
        card.Controls.Add(languages);

        card.Controls.Add(ScrollCaption("Disabled"));

        var disabled = new ComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Enabled = false,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        disabled.Items.Add("Cannot be opened");
        disabled.SelectedIndex = 0;
        card.Controls.Add(disabled);

        card.Controls.Add(new Label(
            "A drop-down is drawn in a window of its own when the platform layer provides one, so it can " +
            "hang past the edges of the window. Without one it is painted inside the window instead.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 54f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="DateTimePicker"/>: the four formats, a bounded one, an empty one and a
    /// disabled one, plus a readout so committing typed text can be watched.
    /// </summary>
}
