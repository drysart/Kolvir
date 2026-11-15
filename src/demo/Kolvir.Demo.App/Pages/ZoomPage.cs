using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The zoom page: a panel that scales its children while its own chrome stays put.
/// </summary>
internal sealed class ZoomPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ZoomPage(DemoUi shell) : base(shell) => Initialize(Build());

    // The panel the slider zooms, and the controls the popup screenshots anchor to: the combo box
    // the dropdown opens from, the button row the tooltip docks beside, and the slider that sets the scale.
    private TrackBar? _zoomSlider;
    private Panel? _zoomPanel;
    private ComboBox? _zoomCombo;
    private Control? _zoomMenuTarget;
    private Control? _zoomButtonRow;
    private Button? _zoomTipTarget;
    private ToolTip? _zoomToolTip;

    /// <summary>
    /// Page 55: a panel whose <see cref="Panel.ScaleFactor"/> zooms its children while its own
    /// chrome stays put, with a nested panel at the reciprocal multiplier proving that they compose.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Panel zoom"));

        var readout = ScrollCaption("Panel 1.00x - nested 1.00x - effective 1.00x");
        readout.Height = 18f;
        card.Controls.Add(readout);

        var zoomed = new SurfacePanel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
            HorizontalScroll = ScrollStyle.Automatic,
            VerticalScroll = ScrollStyle.Automatic,
            Margin = new Thickness(0f, 8f, 0f, 0f),
        };
        _zoomPanel = zoomed;

        // The nested panel carries the reciprocal multiplier, so its children see the same logical
        // space at every position of the slider: the two multipliers cancel.
        var nested = new SurfacePanel
        {
            Bounds = new SKRect(8f, 8f, 348f, 96f),
        };

        // The nested panel's box is in its parent's content units, so it draws bigger as the
        // slider goes up; its one child is a fixed-height row, so the button inside it is exactly
        // the same size at every multiplier. That is what "nested 0.50x" cancelling "panel 2.00x"
        // means, and it is why the row is here instead of the button being docked straight in: a
        // docked button would stretch along the axis the panel hands it more space on.
        var nestedRow = Row(34f);
        nestedRow.Margin = new Thickness(0f);
        var nestedButton = new Button("Nested button")
        {
            Dock = XDock.Left,
            AutoSize = true,
        };
        nestedRow.Controls.Add(nestedButton);
        nested.Controls.Add(nestedRow);

        var combo = new ComboBox
        {
            Bounds = new SKRect(8f, 104f, 208f, 134f),
        };
        combo.Items.Add("First row");
        combo.Items.Add("Second row");
        combo.Items.Add("A longer third row");
        combo.Items.Add("Fourth row");
        combo.SelectedIndex = 0;
        _zoomCombo = combo;

        var caption = new Label(
            "A 14 px label, a 20 px button and a 30 px text box all follow the subtree multiplier. " +
            "The panel's border, padding, corner radius and scrollbar do not zoom. The boxed panel " +
            "above carries the reciprocal multiplier, so its button never changes size.")
        {
            Bounds = new SKRect(8f, 142f, 348f, 208f),
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
        };

        var buttonRow = Row(34f);
        buttonRow.Dock = XDock.None;
        buttonRow.Margin = new Thickness(0f);
        buttonRow.Bounds = new SKRect(8f, 214f, 348f, 248f);
        _zoomButtonRow = buttonRow;
        var buttonA = new Button("A button")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        buttonRow.Controls.Add(buttonA);
        buttonRow.Controls.Add(new Button("Another")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        });

        var edit = new TextBox
        {
            Bounds = new SKRect(8f, 256f, 308f, 286f),
            PlaceholderText = "Type at any zoom",
        };

        var image = new Image
        {
            Bounds = new SKRect(320f, 256f, 480f, 356f),
            Dock = XDock.None,
            AutoSize = false,
            SizeMode = SizeMode.StretchImage,
            // The source has the same 8:5 shape as the box and a caption drawn well inside its
            // edges, so the picture is whole and legible at every multiplier.
            Source = Images.LoadFromStream(CreateGradientPng(320, 200)),
        };

        // Controls that leave the panel's canvas for a top-level window of their own. The combo
        // carries a context menu and the first button a tooltip, so both surfaces can be opened
        // from inside the zoomed panel and shown to inherit its multiplier.
        combo.ContextMenu = new ContextMenu().Add("Zoomed row").Add("Another row");
        _zoomMenuTarget = combo;

        zoomed.Controls.Add(nested);
        zoomed.Controls.Add(combo);
        zoomed.Controls.Add(caption);
        zoomed.Controls.Add(buttonRow);
        zoomed.Controls.Add(edit);
        zoomed.Controls.Add(image);

        var toolTip = new ToolTip { InitialDelay = 0, AutoPopDelay = 0 };
        toolTip.SetToolTip(buttonA, "This tip is painted at the panel's multiplier.");
        _zoomToolTip = toolTip;
        _zoomTipTarget = buttonA;

        var sliderRow = Row(42f);
        var slider = new TrackBar
        {
            Dock = XDock.Fill,
            Minimum = 0.5f,
            Maximum = 3f,
            Value = 1f,
            SmallChange = 0.05f,
            LargeChange = 0.25f,
            TickFrequency = 0.5f,
        };
        sliderRow.Controls.Add(slider);
        _zoomSlider = slider;

        card.Controls.Add(sliderRow);
        card.Controls.Add(zoomed);

        slider.AddValueChangedHandler(_ =>
        {
            float multiplier = slider.Value;
            float reciprocal = 1f / multiplier;

            zoomed.ScaleFactor = multiplier;
            nested.ScaleFactor = reciprocal;

            readout.Text =
                $"Panel {multiplier:0.00}x - nested {reciprocal:0.00}x - effective {multiplier * reciprocal:0.00}x";

            SetStatus(
                "The panel's children zoom with the slider; its chrome and scrollbar do not. " +
                "The nested panel cancels the multiplier, so its own content is unchanged.");
        });

        return card;
    }

    /// <summary>
    /// Drives the Zoom page's slider, so a headless render can show a fixed multiplier without a
    /// click. The value is clamped by the track bar to its own range.
    /// </summary>
    public void SetPanelZoom(float multiplier) => _zoomSlider?.Value = multiplier;

    private static string Describe(CheckState state) => state switch
    {
        CheckState.Checked => "checked",
        CheckState.Indeterminate => "indeterminate",
        _ => "unchecked",
    };

    /// <summary>
    /// Opens one of the page's popups at a zoom multiplier, so a screenshot can show a dropdown, a
    /// menu or a tooltip at the scale the zoomed images are taken at.
    /// </summary>
    /// <param name="window">The window the popup is drawn in.</param>
    /// <param name="kind">Which popup: dropdown, menu or tooltip.</param>
    /// <param name="multiplier">The zoom multiplier to set first.</param>
    internal void ShowZoomPopup(Window window, string kind, float multiplier = 2f)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(kind);

        SetPanelZoom(multiplier);

        switch (kind)
        {
            case "dropdown":
                _zoomPanel?.ScrollTo(0f, 0f);
                _zoomCombo?.OpenDropDown();
                break;

            case "menu":
                if (_zoomMenuTarget is not null)
                {
                    _zoomPanel?.ScrollControlIntoView(_zoomMenuTarget);
                    window.Host.ProcessContextMenu(_zoomMenuTarget.ClientToHost(new SKPoint(24f, 15f)));
                }

                break;

            case "tooltip":
                if (_zoomTipTarget is not null && _zoomToolTip is not null)
                {
                    _zoomPanel?.ScrollControlIntoView(_zoomButtonRow ?? _zoomTipTarget);
                    var tipPoint = _zoomTipTarget.ClientToHost(new SKPoint(20f, 12f));
                    window.Host.ProcessMouseMove(tipPoint);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Expected dropdown, menu or tooltip.");
        }
    }
}
