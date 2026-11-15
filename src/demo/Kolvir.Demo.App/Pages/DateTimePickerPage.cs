using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The date and time page: the three pickers and the calendar they open.
/// </summary>
internal sealed class DateTimePickerPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public DateTimePickerPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="DateTimePicker"/>: the four formats, a bounded one, an empty one and a
    /// disabled one, plus a readout so committing typed text can be watched.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Date and time"));

        var readout = ScrollCaption("Value: 2026-11-04 09:30");
        card.Controls.Add(readout);

        var shortPicker = new DateTimePicker(new DateTime(2026, 11, 4, 9, 30, 0));
        shortPicker.AddValueChangedHandler(_ =>
            readout.Text = $"Value: {shortPicker.Value:yyyy-MM-dd HH:mm}");
        card.Controls.Add(CaptionRow("Short (typed or picked)", shortPicker));

        card.Controls.Add(CaptionRow("Long", new DateTimePicker(new DateTime(2026, 11, 4))
        {
            Format = DateTimePickerFormat.Long,
        }));

        card.Controls.Add(CaptionRow("Time", new DateTimePicker(new DateTime(2026, 11, 4, 9, 30, 0))
        {
            Format = DateTimePickerFormat.Time,
        }));

        card.Controls.Add(CaptionRow(
            "Custom (yyyy-MM-dd HH:mm)",
            new DateTimePicker(new DateTime(2026, 11, 4, 9, 30, 0))
            {
                Format = DateTimePickerFormat.CustomFormat,
                CustomFormat = "yyyy-MM-dd HH:mm",
            }));

        DateTime today = DateTime.Today;
        card.Controls.Add(CaptionRow("Bounded (today +/- 7 days)", new DateTimePicker(today)
        {
            MinDate = today.AddDays(-7),
            MaxDate = today.AddDays(7),
        }));

        card.Controls.Add(CaptionRow("Empty (placeholder)", new DateTimePicker
        {
            PlaceholderText = "Pick a date",
        }));

        card.Controls.Add(CaptionRow("Disabled", new DateTimePicker(new DateTime(2026, 11, 4))
        {
            Enabled = false,
        }));

        card.Controls.Add(new Label(
            "Click the calendar button, or press Alt+Down, to pick a date. Typing is parsed against the " +
            "current culture when the edit is committed - on Enter, when the focus leaves, or when the " +
            "calendar opens - and an entry that cannot be parsed reverts to the last good value. " +
            "Left/Right move between the segments, Up/Down step the segment under the caret, and " +
            "Page Up/Down step the month.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 78f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 6f, 0f, 0f),
        });

        return card;
    }

    /// <summary>
    /// <see cref="TextBox"/> autocomplete: the three modes over a custom source, the history
    /// source, and a disabled field. The readout follows whichever field was typed in last, so the
    /// completed text can be watched while the suffix is selected.
    /// </summary>
}
