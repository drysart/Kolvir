using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The busy and overlay page: the spinner, the shade and the work they cover.
/// </summary>
internal sealed class BusyOverlayPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public BusyOverlayPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="BusyIndicator"/> and <see cref="Overlay"/>: a spinner on its own, and the
    /// same spinner inside a layer that stops the panel underneath from being driven.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Busy indicator and overlay"));

        card.Controls.Add(new Label(
            "Two controls, because they answer two questions. The spinner says the work is running; " +
            "the overlay stops the user from driving the window while it does. Putting the spinner " +
            "inside the overlay is the usual combination.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 42f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        });

        var row = ButtonRow();
        row.Height = 40f;

        var spinner = new BusyIndicator { Dock = XDock.Left, Margin = new Thickness(0f, 4f, 12f, 0f) };
        row.Controls.Add(spinner);

        var toggle = new Button("Stop the spinner")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        toggle.AddClickHandler(_ =>
        {
            spinner.IsBusy = !spinner.IsBusy;
            toggle.Text = spinner.IsBusy ? "Stop the spinner" : "Start the spinner";
            SetStatus(spinner.IsBusy
                ? "The spinner is going round again - and it has stopped its timer while it was still."
                : "The spinner is still, and its timer is stopped: an idle spinner costs nothing.");
        });
        row.Controls.Add(toggle);

        var heavy = new Button("A heavier one")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Ghost,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        heavy.AddClickHandler(_ => SetStatus("A spinner's ring thickness is a property, not a theme value."));

        row.Controls.Add(new BusyIndicator
        {
            Dock = XDock.Left,
            Thickness = 5f,
            Margin = new Thickness(0f, 4f, 8f, 0f),
        });
        row.Controls.Add(heavy);

        // A spinner in a box that is deliberately nothing like square - wide and short, with the box
        // drawn round it so the control's shape is visible. The circle comes from the shorter side
        // and is centred, so it reads as a spinner rather than as a stretched oval.
        var wideBox = new SurfacePanel
        {
            Dock = XDock.Left,
            Width = 240f,
            Border = Framed(8f),
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 2f, 4f, 0f),
        };
        wideBox.Controls.Add(new BusyIndicator { Dock = XDock.Fill, Thickness = 5f });
        row.Controls.Add(wideBox);

        card.Controls.Add(row);

        // The shell that gets blocked: its content and the layer are both Fill children, so they
        // cover the same area and the layer is above it.
        var shell = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 170f,
            Border = Framed(8f),
            Padding = new Thickness(14f),
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        var content = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Padding = new Thickness(0f),
        };

        var readout = new Label("Nothing has been done yet.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        };

        var action = new Button("Do something in the blocked panel")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        int actions = 0;
        action.AddClickHandler(_ =>
        {
            actions++;
            readout.Text = $"The panel's button has been pressed {actions} time(s).";
            SetStatus("The panel's button works while the layer is hidden.");
        });

        var box = new CheckBox("A control under the layer") { Dock = XDock.Top, AutoSize = true };

        content.Controls.Add(action);
        content.Controls.Add(box);
        content.Controls.Add(readout);

        var busyCard = new SurfacePanel
        {
            AutoSize = false,
            Size = new SKSize(260f, 76f),
            Border = Framed(8f),
            Padding = new Thickness(12f),
        };
        busyCard.Controls.Add(new BusyIndicator
        {
            Dock = XDock.Left,
            Margin = new Thickness(0f, 0f, 14f, 0f),
        });
        busyCard.Controls.Add(new Label("Loading the project...")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ThemeColor.Foreground,
        });

        // The page opens with the layer up, so the blocked state is what a screenshot of the page
        // shows; "Unblock" releases it and "Block the panel" puts it back.
        var overlay = new Overlay();
        overlay.Controls.Add(busyCard);

        shell.Controls.Add(content);
        shell.Controls.Add(overlay);
        card.Controls.Add(shell);

        var layerRow = ButtonRow();
        layerRow.Height = 32f;

        var block = new Button("Block the panel")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Enabled = false,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        block.AddClickHandler(_ =>
        {
            overlay.Visible = true;
            block.Enabled = false;
            SetStatus("The panel is blocked: its button takes no press and the keyboard goes nowhere.");
        });

        var unblock = new Button("Unblock")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };
        unblock.AddClickHandler(_ =>
        {
            overlay.Visible = false;
            block.Enabled = true;
            SetStatus("The panel is free again, and the keyboard is back on whatever had the focus.");
        });

        layerRow.Controls.Add(block);
        layerRow.Controls.Add(unblock);
        card.Controls.Add(layerRow);

        card.Controls.Add(new Label(
            "The layer takes the presses by being the topmost control over the panel, and the keys " +
            "by joining the window as an input sink while it is on screen - the same seam an open " +
            "menu uses. Its dimming is the palette's background, so it follows a theme change. The " +
            "wide box in the row above is a spinner in a very un-square control, and it is still " +
            "a circle in the middle of it.")
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

    /// <summary>
    /// <see cref="WrapPanel"/>: a tag row that wraps at the page's width, a column flow of
    /// swatches, and a row whose items are of different heights so the stretch is visible.
    /// </summary>
}
