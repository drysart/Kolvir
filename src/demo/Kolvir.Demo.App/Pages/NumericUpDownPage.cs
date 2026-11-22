using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The numeric page: the ranges, the step and the formats.
/// </summary>
internal sealed class NumericUpDownPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public NumericUpDownPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="NumericUpDown"/>: the spinner is the whole point, so the fields on this page
    /// cover a plain integer, a money amount with two decimals and a coarse step, a percentage
    /// whose increment is larger than one, and a disabled field for comparison.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Numeric up-down"));

        var readout = ScrollCaption("Quantity 3 x 12.50 = 37.50");
        card.Controls.Add(readout);

        var quantity = new NumericUpDown
        {
            Minimum = 1m,
            Maximum = 99m,
            Value = 3m,
        };

        var price = new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 9999.99m,
            DecimalPlaces = 2,
            Increment = 0.05m,
            Value = 12.5m,
        };

        quantity.AddValueChangedHandler(_ => UpdateTotal());
        price.AddValueChangedHandler(_ => UpdateTotal());

        void UpdateTotal() => readout.Text =
            $"Quantity {quantity.Value:0} x {price.Value:0.00} = {quantity.Value * price.Value:0.00}";

        card.Controls.Add(NumericRow("Quantity (1-99, step 1)", quantity));
        card.Controls.Add(NumericRow("Unit price (0-9999.99, step 0.05)", price));

        card.Controls.Add(NumericRow("Percentage (step 5, typed value snaps on Enter)", new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 100m,
            Increment = 5m,
            Value = 40m,
        }));

        card.Controls.Add(NumericRow("Large value (two decimals, step 0.01)", new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 999999.99m,
            DecimalPlaces = 2,
            Increment = 0.01m,
            Value = 987654.32m,
        }));

        card.Controls.Add(NumericRow("Disabled", new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 10m,
            Value = 5m,
            Enabled = false,
        }));

        card.Controls.Add(new Label(
            "Arrow keys step by Increment, Page Up/Down by ten times that, the wheel steps one detent per notch, " +
            "and a held spinner button repeats after a short delay. Typing is free-form: the value follows the text, " +
            "and the text is rewritten from the value on Enter or when the focus leaves.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 6f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A caption with a fixed-width field on the right, so the fields line up.</summary>
    private static Panel NumericRow(string caption, Control field)
    {
        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 34f,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };

        row.Controls.Add(new Label(caption)
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.DefaultFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 12f, 0f),
        });

        field.Dock = XDock.Right;
        field.Width = 170f;
        row.Controls.Add(field);

        return row;
    }

    /// <summary>
    /// <see cref="TrackBar"/>: a horizontal bar whose value drives a vertical one, so the two
    /// orientations can be compared against the same number, plus a bar with no ticks and a
    /// disabled one.
    /// </summary>
}
