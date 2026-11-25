using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The popups page: the public popup API end to end - placement, flipping and clamping, content that
/// takes the keyboard, a popup opened from inside a popup, and every result a dismissal can report.
/// </summary>
/// <remarks>
/// <para>
/// Every button here calls <c>ShowPopupAsync</c> for real, and the readout shows the
/// <see cref="PopupResult"/> that call completed with. A reason on this page is the library's answer
/// rather than a description of one.
/// </para>
/// <para>
/// Content is built for each show, because a control has one home at a time. The exception is the
/// control the closed handler is attached to: that one is shown again and again, which is the point
/// of <c>AddPopupClosedHandler</c> - it follows the control, not any one popup.
/// </para>
/// <para>
/// The handler that opens the nested popup and the one that opens the replacement pair are
/// <c>async</c> and write to a label after their <c>await</c>. Those continuations run on the UI
/// thread only because the application installs a
/// <see cref="System.Threading.SynchronizationContext"/>, so this page exercises that path as much as
/// it does the popups.
/// </para>
/// </remarks>
internal sealed class PopupPage : DemoPage
{
    private Label _readout = null!;
    private Label _closedCount = null!;
    private SurfacePanel _countedContent = null!;
    private readonly Action<object, PopupResult> _countedClosedHandler;
    private int _closedCalls;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public PopupPage(DemoUi shell) : base(shell)
    {
        _countedClosedHandler = OnCountedPopupClosed;
        Initialize(Build());
    }

    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Popups"));

        card.Controls.Add(new Label(
            "Each button opens a real ShowPopupAsync. The result it completes with lands in the " +
            "readout below and in the status line: the reason, and the value when there is one. " +
            "Place one, push one at the edge, type in one, open one from inside another.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 2f),
        });

        _readout = new Label("No popup shown yet - the last result appears here.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = Theme.DefaultFont,
            ForeColor = ThemeColor.Foreground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 4f),
        };
        card.Controls.Add(_readout);

        card.Controls.Add(ScrollCaption("Placement - Auto both ways, ClientPoint, and the nine aligns"));

        var auto = ControlsRow();
        auto.Controls.Add(PageButton("Auto (pointer)", ShowAutoPointer));
        auto.Controls.Add(PageButton("Auto (anchored)", ShowAutoAnchored));
        auto.Controls.Add(PageButton("ClientPoint", ShowClientPoint));
        auto.Controls.Add(PageButton("Tall from the top edge", ShowTallFromTop));
        card.Controls.Add(auto);

        card.Controls.Add(AlignRow(PopupAlign.BelowStart, PopupAlign.BelowCenter, PopupAlign.BelowEnd));
        card.Controls.Add(AlignRow(PopupAlign.AboveStart, PopupAlign.AboveCenter, PopupAlign.AboveEnd));
        card.Controls.Add(AlignRow(PopupAlign.RightOf, PopupAlign.LeftOf, PopupAlign.AtPointer));

        card.Controls.Add(ScrollCaption("Flip, clamp, offset and size - the display has the last word"));
        card.Controls.Add(BuildWideRow());
        card.Controls.Add(BuildOptionsRow());

        card.Controls.Add(ScrollCaption(
            "Content and keyboard - a tooltip, a menu with a text box, and a popup inside a popup"));
        var content = ControlsRow();
        content.Controls.Add(PageButton("Rich tooltip", ShowTooltip));
        content.Controls.Add(PageButton("Context menu with a text box", ShowContextMenu));
        content.Controls.Add(BuildNestedButton());
        card.Controls.Add(content);

        card.Controls.Add(ScrollCaption("Results - every reason, with and without a value"));
        var results = ControlsRow();
        results.Controls.Add(PageButton("OK / Cancel", ShowOkCancel));
        results.Controls.Add(PageButton("Esc closes", anchor => ShowEscape(anchor, escapeCloses: true)));
        results.Controls.Add(PageButton("Esc is swallowed", anchor => ShowEscape(anchor, escapeCloses: false)));
        results.Controls.Add(PageButton("Second popup (Cancelled)", anchor => _ = ShowReplacementAsync(anchor)));
        card.Controls.Add(results);

        card.Controls.Add(ScrollCaption("Closed handlers - one control shown again and again"));
        _countedContent = BuildCountedContent();
        _countedContent.AddPopupClosedHandler(_countedClosedHandler);
        card.Controls.Add(BuildHandlerRow());

        // Declared last on purpose: a docked child takes its edge in declaration order, so by now the
        // only edge left is the bottom of the card, where the flip demo needs its button.
        var bottom = ControlsRow(32f);
        bottom.Margin = new Thickness(0f, 4f, 0f, 0f);
        bottom.Controls.Add(PageButton("Tall from the bottom edge", ShowTallFromBottom));
        card.Controls.Add(bottom);

        return card;
    }

    /// <summary>A row of buttons, laid out left to right, that reports every popup it opens.</summary>
    private static Panel ControlsRow(float height = 32f) => new()
    {
        Dock = XDock.Top,
        Height = height,
        Padding = new Thickness(0f),
        Margin = new Thickness(0f, 0f, 0f, 4f),
    };

    /// <summary>One of the page's buttons; the handler receives the button, which is the popup's anchor.</summary>
    private static Button PageButton(string text, Action<Button> onClick)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };

        button.AddClickHandler(_ => onClick(button));
        return button;
    }

    /// <summary>
    /// A row of three align buttons of one width, so the nine aligns read as a grid rather than as a
    /// list of names.
    /// </summary>
    private Panel AlignRow(params PopupAlign[] aligns)
    {
        var row = ControlsRow(34f);

        foreach (var align in aligns)
        {
            var button = PageButton(align.ToString(), anchor => ShowAlignment(anchor, align));
            button.AutoSize = false;
            button.Width = 148f;
            row.Controls.Add(button);
        }

        return row;
    }

    private Panel BuildWideRow()
    {
        var row = ControlsRow(34f);

        var wide = PageButton("Wide from the right edge", ShowWide);
        wide.Dock = XDock.Right;
        wide.Margin = new Thickness(8f, 0f, 0f, 0f);
        row.Controls.Add(wide);

        row.Controls.Add(new Label(
            "RightOf with more width than there is to the right: it flips, and the clamp has the last word.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f),
        });

        return row;
    }

    private Panel BuildOptionsRow()
    {
        var row = ControlsRow();

        var offset = new CheckBox("Offset 24,24")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new Thickness(0f, 0f, 12f, 0f),
        };

        var size = new CheckBox("Size 320x200")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new Thickness(0f, 0f, 12f, 0f),
        };

        var maximum = new CheckBox("MaximumSize 320x200")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new Thickness(0f, 0f, 12f, 0f),
        };

        // Size fixes the popup and MaximumSize caps its content, so the two are different shows;
        // the page keeps one of them on rather than reading two numbers into one option.
        size.AddCheckedChangedHandler(_ =>
        {
            if (size.Checked)
            {
                maximum.Checked = false;
            }
        });

        maximum.AddCheckedChangedHandler(_ =>
        {
            if (maximum.Checked)
            {
                size.Checked = false;
            }
        });

        row.Controls.Add(offset);
        row.Controls.Add(size);
        row.Controls.Add(maximum);
        row.Controls.Add(PageButton("Show with these options", anchor => ShowWithOptions(anchor, offset, size, maximum)));
        return row;
    }

    private Button BuildNestedButton()
    {
        var button = new Button("Nested popup")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };

        // The page's one handler written as async directly. Everything after the await is a control,
        // which is the demo of the UI-thread context as well as of the popup API.
        button.AddClickHandler(async _ =>
        {
            if (OwnerWindow is null)
            {
                NoWindow();
                return;
            }

            try
            {
                var result = await button.ShowPopupAsync(BuildNestedContent(), Sized(320f, 200f));
                Report($"Nested popup: {DescribeResult(result)}.");
            }
            catch (Exception error)
            {
                Report($"Nested popup failed: {error.Message}");
            }
        });

        return button;
    }

    private Panel BuildHandlerRow()
    {
        var row = ControlsRow();

        var toggle = new CheckBox("Count closes")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Checked = true,
            Margin = new Thickness(0f, 0f, 12f, 0f),
        };

        toggle.AddCheckedChangedHandler(_ =>
        {
            if (toggle.Checked)
            {
                _countedContent.AddPopupClosedHandler(_countedClosedHandler);
            }
            else
            {
                _countedContent.RemovePopupClosedHandler(_countedClosedHandler);
            }

            Report(toggle.Checked
                ? "Closed handler attached - the count follows every popup this control is shown in."
                : "Closed handler removed - the count stays where it is.");
        });

        row.Controls.Add(toggle);
        row.Controls.Add(PageButton("Show the counted popup", ShowCounted));

        _closedCount = new Label("Closed handler calls: 0")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(12f, 0f, 0f, 0f),
        };
        row.Controls.Add(_closedCount);

        return row;
    }

    private void ShowAutoPointer(Button anchor)
    {
        if (OwnerWindow is not { } window)
        {
            NoWindow();
            return;
        }

        var content = PlacementCard(
            "Auto (no anchor)",
            "Auto resolved to Pointer because the call supplied no anchor, so this opened where the pointer was.");

        _ = ShowAndReportAsync(
            "Auto at the pointer",
            () => window.ShowPopupAsync(content, Sized(240f, 110f)));
    }

    private void ShowAutoAnchored(Button anchor)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        var content = PlacementCard(
            "Auto (anchored)",
            "Auto resolved to Anchor, and the default align put this below the button it was opened from.");

        _ = ShowAndReportAsync(
            "Auto on the anchor",
            () => anchor.ShowPopupAsync(content, Sized(240f, 110f)));
    }

    private void ShowClientPoint(Button anchor)
    {
        if (OwnerWindow is not { } window)
        {
            NoWindow();
            return;
        }

        var content = PlacementCard(
            "ClientPoint",
            "Placement is ClientPoint and the offset is the point itself: 420,160 in the window's client space.");

        _ = ShowAndReportAsync(
            "ClientPoint",
            () => window.ShowPopupAsync(content, new PopupOptions
            {
                Placement = PopupPlacement.ClientPoint,
                Offset = new SKPoint(420f, 160f),
                Size = new SKSize(240f, 110f),
            }));
    }

    private void ShowAlignment(Button anchor, PopupAlign align)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        var content = PlacementCard(
            align.ToString(),
            "Anchored to this button. Click outside or press Esc to dismiss it.");

        _ = ShowAndReportAsync(
            $"Align {align}",
            () => anchor.ShowPopupAsync(content, Sized(190f, 100f)));
    }

    private void ShowTallFromTop(Button anchor) => ShowTall(anchor, PopupAlign.AboveStart);

    private void ShowTallFromBottom(Button anchor) => ShowTall(anchor, PopupAlign.BelowStart);

    private void ShowTall(Button anchor, PopupAlign align)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        var content = FlipCard(
            $"Requested {align}",
            "FlipIntoView asks for the other side of the anchor when this does not fit. If neither side " +
            "has room, the popup is clamped inside the display instead.");

        _ = ShowAndReportAsync(
            $"Tall popup ({align})",
            () => anchor.ShowPopupAsync(content, new PopupOptions
            {
                Align = align,
                Size = new SKSize(240f, 600f),
            }));
    }

    private void ShowWide(Button anchor)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        var content = FlipCard(
            "Requested RightOf",
            "There is not this much room to the right, so it flips to LeftOf; when the left has no room " +
            "either, the clamp puts it at the display's edge.");

        _ = ShowAndReportAsync(
            "Wide popup (RightOf)",
            () => anchor.ShowPopupAsync(content, new PopupOptions
            {
                Align = PopupAlign.RightOf,
                Size = new SKSize(900f, 160f),
            }));
    }

    private void ShowWithOptions(Button anchor, CheckBox offset, CheckBox size, CheckBox maximum)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        var options = new PopupOptions();
        var parts = new List<string>();

        if (offset.Checked)
        {
            options.Offset = new SKPoint(24f, 24f);
            parts.Add("offset 24,24");
        }

        if (size.Checked)
        {
            options.Size = new SKSize(320f, 200f);
            parts.Add("size 320x200");
        }
        else if (maximum.Checked)
        {
            options.MaximumSize = new SKSize(320f, 200f);
            parts.Add("maximum 320x200");
        }

        var description = parts.Count == 0
            ? "Options (defaults)"
            : $"Options ({string.Join(", ", parts)})";

        var content = OptionsCard();
        _ = ShowAndReportAsync(description, () => anchor.ShowPopupAsync(content, options));
    }

    private void ShowTooltip(Button anchor)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        _ = ShowAndReportAsync(
            "Rich tooltip",
            () => anchor.ShowPopupAsync(BuildTooltipContent(), Sized(320f, 110f)));
    }

    private void ShowContextMenu(Button anchor)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        _ = ShowAndReportAsync(
            "Context menu",
            () => anchor.ShowPopupAsync(BuildContextMenuContent(), Sized(320f, 220f)));
    }

    private void ShowOkCancel(Button anchor)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        var content = PopupCard();
        content.Controls.Add(PopupTitle("A dialog-shaped popup"));
        content.Controls.Add(PopupCaption("OK closes with the value \"OK\"; Cancel closes with none."));

        var row = ControlsRow(34f);
        var ok = new Button("OK")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        ok.AddClickHandler(_ => content.ClosePopup(PopupResult.Closed("OK")));

        var cancel = new Button("Cancel")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        cancel.AddClickHandler(_ => content.ClosePopup());

        row.Controls.Add(ok);
        row.Controls.Add(cancel);
        content.Controls.Add(row);

        _ = ShowAndReportAsync(
            "OK / Cancel",
            () => anchor.ShowPopupAsync(content, Sized(290f, 150f)));
    }

    private void ShowEscape(Button anchor, bool escapeCloses)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        var content = PopupCard();
        content.Controls.Add(PopupTitle(escapeCloses ? "EscapeCloses is true" : "EscapeCloses is false"));
        content.Controls.Add(PopupCaption(escapeCloses
            ? "Press Esc: the popup closes and the result reports Escape."
            : "Press Esc: the key is consumed and this popup stays open. Use the button to close it."));

        if (!escapeCloses)
        {
            var close = new Button("Close the popup")
            {
                Dock = XDock.Top,
                AutoSize = true,
                Style = ButtonStyle.Primary,
                Margin = new Thickness(0f, 2f, 0f, 0f),
            };
            close.AddClickHandler(_ => content.ClosePopup());
            content.Controls.Add(close);
        }

        var options = Sized(310f, 150f);
        options.EscapeCloses = escapeCloses;

        _ = ShowAndReportAsync(
            escapeCloses ? "Escape closes" : "Escape is swallowed",
            () => anchor.ShowPopupAsync(content, options));
    }

    /// <summary>
    /// Shows two popups on one surface back to back, which is what makes the first one report
    /// <see cref="PopupCloseReason.Cancelled"/>: a surface holds one popup at a time.
    /// </summary>
    private async Task ShowReplacementAsync(Button anchor)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        try
        {
            var first = anchor.ShowPopupAsync(
                PlacementCard("First popup", "Replaced as soon as the second is shown, so it is never seen."),
                Sized(230f, 110f));

            var second = anchor.ShowPopupAsync(
                PlacementCard("Replacement", "The first popup reported Cancelled. Close this one as usual."),
                Sized(230f, 110f));

            // Both results are read after the replacement closes, and writing the readout is
            // application code running on the UI thread after an await.
            var firstResult = await first;
            var secondResult = await second;
            Report(
                $"Two on one surface: the first {DescribeResult(firstResult)}; " +
                $"the second {DescribeResult(secondResult)}.");
        }
        catch (Exception error)
        {
            Report($"Two on one surface failed: {error.Message}");
        }
    }

    private void ShowCounted(Button anchor)
    {
        if (OwnerWindow is null)
        {
            NoWindow();
            return;
        }

        _ = ShowAndReportAsync(
            "Counted popup",
            () => anchor.ShowPopupAsync(_countedContent, Sized(310f, 150f)));
    }

    private void OnCountedPopupClosed(object sender, PopupResult result)
    {
        _closedCalls++;
        _closedCount.Text = $"Closed handler calls: {_closedCalls} (last: {DescribeResult(result)})";
    }

    private static SurfacePanel PopupCard() => new() { Padding = new Thickness(12f) };

    private static Label PopupTitle(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = true,
        Font = Theme.EmphasizedFont,
        ForeColor = ThemeColor.Foreground,
        Margin = new Thickness(0f, 0f, 0f, 4f),
    };

    private static Label PopupCaption(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = true,
        WrapText = true,
        Font = Theme.CaptionFont,
        ForeColor = ThemeColor.MutedForeground,
        Margin = new Thickness(0f, 0f, 0f, 6f),
    };

    private static SurfacePanel PlacementCard(string title, string detail)
    {
        var card = PopupCard();
        card.Controls.Add(PopupTitle(title));
        card.Controls.Add(PopupCaption(detail));
        return card;
    }

    private static SurfacePanel FlipCard(string request, string note)
    {
        var card = PopupCard();
        card.Controls.Add(PopupTitle(request));
        card.Controls.Add(PopupCaption(note));

        var foot = PopupCaption("An outside click or Esc dismisses it, and the reason comes back to the page.");
        foot.Dock = XDock.Bottom;
        card.Controls.Add(foot);
        return card;
    }

    private static SurfacePanel OptionsCard()
    {
        var card = PopupCard();
        card.Controls.Add(PopupTitle("Offset, Size and MaximumSize"));
        card.Controls.Add(PopupCaption(
            "This paragraph measures one line without a cap. MaximumSize wraps it at 320 and lets " +
            "the height grow; Size forces the box whether the text needs it or not."));
        return card;
    }

    private static SurfacePanel BuildTooltipContent()
    {
        var card = PopupCard();

        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 28f,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        var icon = new Image
        {
            Dock = XDock.Left,
            AutoSize = false,
            Width = 20f,
            SizeMode = SizeMode.Zoom,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoIcons.Tick)))
        {
            icon.Source = Images.LoadFromStream(stream);
        }

        row.Controls.Add(icon);
        row.Controls.Add(new Label("Saved to disk")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.EmphasizedFont,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        card.Controls.Add(row);
        card.Controls.Add(PopupCaption("An icon and a label: nothing in here is a tab stop. An outside click dismisses it."));
        return card;
    }

    private static SurfacePanel BuildContextMenuContent()
    {
        var card = PopupCard();
        card.Controls.Add(PopupCaption(
            "Type in the box. Tab moves between this popup's controls and the focus stays inside it."));

        var box = new TextBox
        {
            Dock = XDock.Top,
            Height = 30f,
            Text = "popup text",
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(box);

        var inserted = PopupCaption("Insert puts what the box holds here.");
        inserted.Margin = new Thickness(0f, 6f, 0f, 0f);

        var row = ControlsRow(34f);
        var insert = new Button("Insert")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        insert.AddClickHandler(_ => inserted.Text = $"The box holds \"{box.Text}\".");

        var close = new Button("Close")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        close.AddClickHandler(_ => card.ClosePopup(PopupResult.Closed(box.Text)));

        row.Controls.Add(insert);
        row.Controls.Add(close);
        card.Controls.Add(row);
        card.Controls.Add(inserted);
        return card;
    }

    private SurfacePanel BuildNestedContent()
    {
        var card = PopupCard();
        card.Controls.Add(PopupTitle("Outer popup"));

        var status = PopupCaption("The inner popup's result appears here.");
        card.Controls.Add(status);

        var open = new Button("Open a second popup")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        // Another async handler, this time on a control inside the popup: the label it writes after
        // the await belongs to the outer popup, which is still on screen while the inner one closes.
        open.AddClickHandler(async _ =>
        {
            try
            {
                var result = await open.ShowPopupAsync(BuildInnerContent(), new PopupOptions
                {
                    Align = PopupAlign.BelowCenter,
                    Size = new SKSize(280f, 130f),
                });

                status.Text = $"Inner popup: {DescribeResult(result)}.";
                Report($"Nested inner popup: {DescribeResult(result)}.");
            }
            catch (Exception error)
            {
                status.Text = $"Inner popup failed: {error.Message}";
            }
        });

        card.Controls.Add(open);

        var close = new Button("Close the outer popup")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f),
        };
        close.AddClickHandler(_ => card.ClosePopup());
        card.Controls.Add(close);

        return card;
    }

    private static SurfacePanel BuildInnerContent()
    {
        var card = PopupCard();
        card.Controls.Add(PopupTitle("Inner popup"));
        card.Controls.Add(PopupCaption("Opened from a button inside the outer one, so it belongs to that popup's surface."));

        var close = new Button("Close the inner popup")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f, 2f, 0f, 0f),
        };
        close.AddClickHandler(_ => card.ClosePopup(PopupResult.Closed("inner")));
        card.Controls.Add(close);
        return card;
    }

    private static SurfacePanel BuildCountedContent()
    {
        var card = PopupCard();
        card.Controls.Add(PopupTitle("A counted popup"));
        card.Controls.Add(PopupCaption(
            "This control carries the closed handler. Show it, close it, show it again: the count " +
            "follows the control rather than one popup."));

        var close = new Button("Close this popup")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f, 2f, 0f, 0f),
        };
        close.AddClickHandler(_ => card.ClosePopup());
        card.Controls.Add(close);
        return card;
    }

    private static PopupOptions Sized(float width, float height) =>
        new() { Size = new SKSize(width, height) };

    private async Task ShowAndReportAsync(string what, Func<Task<PopupResult>> show)
    {
        try
        {
            var result = await show();
            Report($"{what}: {DescribeResult(result)}.");
        }
        catch (Exception error)
        {
            Report($"{what} failed: {error.Message}");
        }
    }

    private void Report(string message)
    {
        _readout.Text = message;
        SetStatus(message);
    }

    private void NoWindow() => Report("No window attached, so there is no surface to show a popup on.");

    private static string DescribeResult(PopupResult result) =>
        result.Value is null
            ? $"{result.Reason}, no value"
            : $"{result.Reason}, value \"{result.Value}\"";
}
