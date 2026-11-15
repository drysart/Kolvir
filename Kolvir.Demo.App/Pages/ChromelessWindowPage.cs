using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The chromeless page: the window without a frame, and what the backend reports about it.
/// </summary>
internal sealed class ChromelessWindowPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ChromelessWindowPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Label? _chromeReadout;

    private Control? _chromeDragHandle;

    private static string Yes(bool value) => value ? "yes" : "no";

    /// <summary>
    /// Subscribes to the attached window once it exists: the readout reports what the backend
    /// answered rather than what the page asked for, so it has to follow the window's own changes.
    /// </summary>
    /// <param name="window">The window the sample is displayed in.</param>
    /// <remarks>
    /// The snapshot moves when the window changes: after a resize, a maximise, a change of explicit
    /// chrome or of the buttons requested. Showing counts as a change even though no backend reports
    /// it as one: a borderless window's measured frame, and therefore its insets and button rects, is
    /// only known once the native window exists, and the page is attached before that.
    /// </remarks>
    internal override void OnWindowAttached(Window window)
    {
        window.AddChromeMetricsChangedHandler(_ => RefreshChromeReadout());
        window.AddStateChangedHandler(_ => RefreshChromeReadout());
        window.AddClientSizeChangedHandler(_ => RefreshChromeReadout());
        window.AddShownHandler(_ => RefreshChromeReadout());

        RefreshChromeReadout();
    }

    /// <summary>
    /// Page 58: the window's own chrome. The buttons switch this window between the platform's
    /// decoration and a borderless one and ask for the platform's caption buttons, the strip starts a
    /// drag from the application's own press, and the readout reports what the backend answered
    /// rather than what was asked for.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Chromeless window"));

        var buttons = Row(34f);
        buttons.Controls.Add(ChromeButton("Borderless", MakeBorderless));
        buttons.Controls.Add(ChromeButton("System chrome", MakeDecorated));
        buttons.Controls.Add(ChromeButton("Platform buttons", AskForPlatformButtons));
        buttons.Controls.Add(ChromeButton("Our own buttons", AskForNoButtons));
        card.Controls.Add(buttons);

        // The application's own title bar: a press here asks the platform to move the window, which is
        // the one thing a borderless window cannot do for itself once the platform's caption is gone.
        var handle = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 36f,
            Border = Framed(6f),
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        handle.Controls.Add(new Label("Press and drag here to move the window")
        {
            Dock = XDock.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
        });
        AttachWindowDrag(handle);
        _chromeDragHandle = handle;
        card.Controls.Add(handle);

        var readout = new Label("No window attached, so there is nothing to report.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 150f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        _chromeReadout = readout;
        card.Controls.Add(readout);

        card.Controls.Add(new Label(
            "A borderless window is still a window: the platform keeps its shadow, its rounded " +
            "corners, its resize band and its behaviours. Ask ChromeCapabilities first - the setters " +
            "throw when the answer is no - and read ChromeMetrics back for what was actually " +
            "delivered. On Windows the caption buttons are the backend's own emulation and are " +
            "invisible to assistive technology, so an application that needs them reachable should " +
            "draw its own and keep Alt+F4 and Alt+Space working.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
        });

        return card;
    }

    /// <summary>A button that sits beside the one before it rather than under it.</summary>
    private static Button ChromeButton(string text, Action onClick)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => onClick());
        return button;
    }

    /// <summary>Switches the sample's window to the application-owned chrome, if the backend has it.</summary>
    private void MakeBorderless() => ChangeChrome(window =>
        {
            if (!window.ChromeCapabilities.Borderless)
            {
                return "This backend cannot make a window borderless.";
            }

            window.Chrome = WindowChrome.Borderless;
            return "Borderless: the application owns every visible pixel.";
        });

    /// <summary>Hands the window back to the platform's own decoration.</summary>
    private void MakeDecorated() => ChangeChrome(window =>
        {
            window.Chrome = WindowChrome.System;
            return "System chrome: the platform draws and drives the window again.";
        });

    /// <summary>Asks for the platform's own caption buttons where the platform has any.</summary>
    private void AskForPlatformButtons() => ChangeChrome(window =>
        {
            if (!window.ChromeCapabilities.CaptionButtons)
            {
                return "This backend cannot draw caption buttons of its own.";
            }

            window.CaptionButtons = CaptionButtons.Platform;
            return window.ChromeMetrics.CaptionButtons == CaptionButtons.Platform
                ? "Platform buttons: the platform draws them and claims their clicks."
                : "Platform buttons were requested, but none are on screen.";
        });

    /// <summary>Goes back to a window with no buttons of the platform's, so the application can draw its own.</summary>
    private void AskForNoButtons() => ChangeChrome(window =>
        {
            window.CaptionButtons = CaptionButtons.None;
            return "No platform buttons: the application draws and handles its own.";
        });

    /// <summary>
    /// Runs a chrome change against the attached window and reports what came of it, including the
    /// refusal a backend without the feature answers with.
    /// </summary>
    private void ChangeChrome(Func<Window, string> change)
    {
        if (OwnerWindow is not { } window)
        {
            SetStatus("No window attached to change.");
            return;
        }

        try
        {
            SetStatus(change(window));
        }
        catch (NotSupportedException ex)
        {
            // The seam throws where an application would have to guess, and the capabilities are how
            // it is meant to find out first; showing the message is the honest version of that.
            SetStatus(ex.Message);
        }

        RefreshChromeReadout();
    }

    /// <summary>
    /// Asks the platform to move the window from the press being handled, which is only effective
    /// while the press is still in flight - which is why this runs from the mouse-down itself.
    /// </summary>
    private void DragWindowFromThisPress()
    {
        if (OwnerWindow is not { } window)
        {
            return;
        }

        bool started = window.TryBeginDrag();
        SetStatus(started
            ? "The platform took the press and is moving the window."
            : "No window drag started: the backend refused, or there is no press to drag with.");
    }

    /// <summary>Re-reads the window's chrome state into the page's readout.</summary>
    private void RefreshChromeReadout()
    {
        if (_chromeReadout is not { } readout)
        {
            return;
        }

        if (OwnerWindow is not { } window)
        {
            readout.Text = "No window attached, so there is nothing to report.";
            return;
        }

        var caps = window.ChromeCapabilities;
        var metrics = window.ChromeMetrics;

        var buttons = metrics.CaptionButtonBounds.Count == 0
            ? "none"
            : string.Join(", ", metrics.CaptionButtonBounds.Select(b => $"{b.Kind} {Describe(b.Bounds)}"));

        readout.Text =
            $"Window: {window.Title}, client {window.ClientSize.Width:0}x{window.ClientSize.Height:0}, " +
            $"state {window.State}\n" +
            $"Chrome: {window.Chrome}; buttons requested: {window.CaptionButtons}\n" +
            $"Supported: borderless {Yes(caps.Borderless)}, caption buttons {Yes(caps.CaptionButtons)}, " +
            $"window colours {Yes(caps.WindowColors)}\n" +
            $"Surface inset {Describe(metrics.SurfaceInset)}, resize inset {Describe(metrics.ResizeInset)}\n" +
            $"Corner radius {metrics.CornerRadius:0.#}, corner resize {metrics.CornerResizeExtent:0.#}\n" +
            $"Buttons: {metrics.CaptionButtons} on the {metrics.CaptionButtonSide} - {buttons}\n" +
            $"Strip: {Describe(metrics.CaptionButtonStrip)}";
    }
}
