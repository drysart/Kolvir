using Kolvir.Animations;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The overlay fade page: the fade curve, driven a frame at a time.
/// </summary>
internal sealed class OverlayFadePage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public OverlayFadePage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The overlay's fade-in, from <c>Kolvir.Feature.Animations</c>: the layer dims in over the
    /// panel instead of appearing at full strength in one frame.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Overlay fade"));

        card.Controls.Add(new Label(
            "The same blocker as the busy-and-overlay page, with a duration: it dims in over the " +
            "panel it covers, and it is holding the keyboard from the first frame rather than from " +
            "the end of the fade. Block and unblock it to watch it come in again.")
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

        content.Controls.Add(action);
        content.Controls.Add(new CheckBox("A control under the layer") { Dock = XDock.Top, AutoSize = true });
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

        // The layer starts up, so the page opens with the blocked state on screen and the fade is
        // the first thing it does when the window appears.
        var overlay = new Overlay();
        overlay.FadeDuration = TimeSpan.FromMilliseconds(160);
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
            SetStatus("The panel is blocked again, and the layer dims in over it.");
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
            SetStatus("The panel is free again. There is no fade out: hiding the layer hides it.");
        });

        layerRow.Controls.Add(block);
        layerRow.Controls.Add(unblock);
        card.Controls.Add(layerRow);

        card.Controls.Add(new Label(
            "The fade is the layer's Opacity, so the dimming and the card inside it composite as one " +
            "picture rather than each blending with what is under it. Only coming in animates: a " +
            "fade out would mean the control holding on to a hidden layer, so assigning Visible " +
            "still takes it away at once - the application's word for what is on screen stays true.")
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
    /// A panel that draws the focus ring when it has the focus. A plain panel has no chrome of its
    /// own, and the readout alone is a weak way to show that a container - not a control inside it -
    /// is the thing Tab landed on.
    /// </summary>
}
