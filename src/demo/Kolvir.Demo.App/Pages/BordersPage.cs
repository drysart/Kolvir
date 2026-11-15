using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The borders page: a frame per edge, a radius per corner, and a caption in the top edge.
/// </summary>
internal sealed class BordersPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public BordersPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The page for the border object: a frame whose edges and corners can each be set, and the room
    /// each of them takes out of the content.
    /// </summary>
    private Panel Build()
    {
        var body = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Padding = Thickness.Empty,
        };

        body.Controls.Add(BorderRow(
            "An accent edge",
            "Four pixels on the left, one on the others: the content starts after the thicker edge.",
            new SimpleBorder(new Thickness(4f, 1f, 1f, 1f), 8f)
                .WithThemeColor(BorderSide.Left, ThemeColor.Accent)));

        body.Controls.Add(BorderRow(
            "A radius per corner",
            "Rounded at the top and square at the feet, and the fill and the clip follow the same radii.",
            SimpleBorder.Thin.WithCorners(new CornerRadii(14f, 14f, 0f, 0f))));

        body.Controls.Add(BorderRow(
            "A colour per edge",
            "Accent on top, pressed secondary below, the border role elsewhere, mitred where they meet.",
            SimpleBorder.Thin
                .WithThemeColor(BorderSide.Top, ThemeColor.Accent)
                .WithThemeColor(BorderSide.Bottom, ThemeColor.SecondaryPressed)));

        body.Controls.Add(BorderRow(
            "A band along the top",
            "Eight pixels across the top and one everywhere else, with the content clear of the band.",
            new SimpleBorder(new Thickness(1f, 8f, 1f, 1f), 0f)));

        body.Controls.Add(BorderRow(
            "A state colour",
            "Point at this row: the frame resolves its own colour from the control's state.",
            SimpleBorder.Thin
                .WithHoverColor(ThemeColor.Accent)
                .WithFocusColor(ThemeColor.Accent)
                .WithCornerRadius(8f),
            hoverIncludesChildren: true));

        var groupRow = BorderRow(
            "A frame that shows the control's text",
            "The top edge drops by half a band and is cut away behind the text. GroupBox is a panel "
            + "with this border.",
            SimpleBorder.Thin.WithCornerRadius(8f).WithControlText(),
            height: 88f);
        groupRow.Text = "Options";
        groupRow.ForeColorRole = ThemeColor.MutedForeground;
        body.Controls.Add(groupRow);

        return body;
    }

    /// <summary>One border sample: a caption block inside an example frame.</summary>
    private static Panel BorderRow(
            string title,
            string caption,
            SimpleBorder border,
            float height = 74f,
            bool hoverIncludesChildren = false)
    {
        var row = new Panel
        {
            Dock = XDock.Top,
            Height = height,
            Margin = new Thickness(0f, 0f, 0f, 10f),
            Border = border,
            Padding = new Thickness(16f, 12f, 16f, 12f),
            HoverIncludesChildren = hoverIncludesChildren,
        };

        row.Controls.Add(new Label(title)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = Theme.EmphasizedFont,
            TextAlign = ContentAlignment.BottomLeft,
        });

        row.Controls.Add(new Label(caption)
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return row;
    }
}
