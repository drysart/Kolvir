using AppKit;
using CoreGraphics;
using Foundation;
using UniformTypeIdentifiers;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// The macOS <see cref="IDialogService"/>: <c>NSAlert</c> for messages,
/// <c>NSOpenPanel</c>/<c>NSSavePanel</c> for open, save and folder picking, and <c>NSFontPanel</c>
/// for fonts.
/// </summary>
/// <remarks>
/// <para>
/// Both run their own modal loop on the calling thread, which is what the nested loop requirement
/// in <see cref="IDialogService"/> asks for. They are shown application-modal rather than as
/// window sheets: a sheet is asynchronous, so it would return to the caller before the user had
/// answered, and <see cref="IDialogService"/> is a blocking API by design.
/// </para>
/// <para>
/// The application backend's common-mode poll timer keeps firing during those loops, so
/// <see cref="IApplicationBackend.Invoke"/> callbacks and ticks still arrive while a dialog is
/// open.
/// </para>
/// </remarks>
internal sealed class AppKitDialogService : IDialogService
{
    /// <summary>The application name a message box falls back to when no title is given.</summary>
    private static readonly string FallbackTitle = "Kolvir";

    /// <summary>The response code AppKit returns for the first button of an alert.</summary>
    private static readonly nint FirstButtonResponse = 1000;

    /// <summary>The response code a panel returns when the user accepted.</summary>
    private static readonly nint PanelOkResponse = 1;

    /// <inheritdoc />
    public DialogResult ShowMessage(MessageBoxOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (titles, results) = MapButtons(options.Buttons);

        var alert = new NSAlert
        {
            // macOS puts the headline in bold and the detail under it, so the title is the headline
            // when there is one and the message stands on its own when there is not.
            MessageText = string.IsNullOrWhiteSpace(options.Title) ? FallbackTitle : options.Title,
            InformativeText = options.Message,
            AlertStyle = MapIcon(options.Icon),
        };

        foreach (var title in titles)
        {
            alert.AddButton(title);
        }

        ApplyDefaultButton(alert, results, options.DefaultResult);

        var response = alert.RunModal();
        var index = (int)(response - FirstButtonResponse);

        return index >= 0 && index < results.Length ? results[index] : DialogResult.Cancel;
    }

    /// <inheritdoc />
    public string? OpenFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        // AppKit wants panels created through its factory selectors: since 10.15 the whole panel
        // runs out of process, and the initializers are declared obsolete.
        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        panel.CanCreateDirectories = true;
        panel.ResolvesAliases = true;

        Configure(panel, options);

        return Run(panel);
    }

    /// <inheritdoc />
    public string? SaveFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        // NSSavePanel always confirms an overwrite; FileDialogOptions.OverwritePrompt has no
        // switch to turn that off, so it is honoured in the only direction macOS allows.
        var panel = NSSavePanel.SavePanel;
        panel.CanCreateDirectories = true;
        panel.ExtensionHidden = false;

        Configure(panel, options);

        return Run(panel);
    }

    /// <inheritdoc />
    public string? PickFolder(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseFiles = false;
        panel.CanChooseDirectories = true;
        panel.AllowsMultipleSelection = false;
        panel.CanCreateDirectories = true;
        panel.ResolvesAliases = true;

        Configure(panel, options);

        return Run(panel);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <c>NSFontPanel</c> is not a modal dialog: it is a floating panel that stays open while the
    /// application keeps running, so it has no loop of its own and no OK or Cancel of its own. This
    /// implementation gives it both. A small accessory view at the bottom of the panel carries
    /// Select and Cancel, and the panel is put into an application-modal session with
    /// <c>RunModalForWindow</c> until one of them is pressed. That is a genuine nested run loop on
    /// the calling thread, which is what <see cref="IDialogService"/> requires; the backend's
    /// common-mode poll timer keeps <see cref="IApplicationBackend.Invoke"/> work flowing
    /// through it. Escape cancels and Return selects through the buttons' key equivalents.
    /// </para>
    /// <para>
    /// The panel's close and minimize buttons are hidden for the duration. Closing a window does not
    /// end a modal session by itself, so leaving those buttons available would give the user a way
    /// to dismiss the panel while this call stayed blocked forever. Select and Cancel are the only
    /// exits, and <c>finally</c> aborts the session if anything else goes wrong.
    /// </para>
    /// <para>
    /// <c>NSFontPanel</c> is shared and long-lived, and AppKit restores its last frame from the
    /// application's <c>NSWindow Frame NSFontPanel</c> default. Left alone that can present a small
    /// panel at an old position, possibly partly off-screen or far from the window it belongs to.
    /// Every modal presentation therefore computes its own frame: at least 600 by 400, centred on
    /// the owner's window (or on the visible screen when there is no owner) and clamped inside the
    /// visible frame of the screen the owner is on. The arithmetic is in AppKit points; scaling by
    /// the backing factor would double the frame on a Retina display.
    /// </para>
    /// </remarks>
    public FontSelection? PickFont(FontDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var size = options.ClampSize(options.InitialSize);
        var weight = FontMapping.ClampWeight(options.InitialWeight);
        var appKitWeight = (IntPtr)FontMapping.ToAppKitWeight(weight);

        // AppKit expresses weight through the face selected for a family plus the Bold/Unbold
        // trait; the manager's weight index chooses among the faces the family actually has.
        var traits = NSFontTraitMask.Unbold | NSFontTraitMask.Unitalic;
        if (weight >= FontMapping.BoldWeight)
        {
            traits = (traits & ~NSFontTraitMask.Unbold) | NSFontTraitMask.Bold;
        }

        if (options.InitialItalic)
        {
            traits = (traits & ~NSFontTraitMask.Unitalic) | NSFontTraitMask.Italic;
        }

        var manager = NSFontManager.SharedFontManager;
        var initial = string.IsNullOrWhiteSpace(options.InitialFamilyName)
            ? null
            : manager.FontWithFamily(options.InitialFamilyName, traits, appKitWeight, size);
        initial ??= NSFont.SystemFontOfSize(size)!;

        var panel = NSFontPanel.SharedFontPanel;

        // SetSelectedFont seeds the panel's visible selection, not just the manager's notion of it:
        // SetPanelFont alone leaves the face list on the family's regular face even when the font
        // handed to it is bold, italic or both.
        panel.SetPanelFont(initial, false);
        manager.SetSelectedFont(initial, false);

        var accepted = false;
        var selectButton = NSButton.CreateButton("Select", () =>
        {
            accepted = true;
            NSApplication.SharedApplication.StopModal();
        });
        var cancelButton = NSButton.CreateButton("Cancel", () =>
        {
            accepted = false;
            NSApplication.SharedApplication.StopModal();
        });
        selectButton.Frame = new CGRect(166, 6, 88, 26);
        cancelButton.Frame = new CGRect(72, 6, 88, 26);
        selectButton.KeyEquivalent = "\r";
        cancelButton.KeyEquivalent = "\u001b";

        var accessory = new NSView(new CGRect(0, 0, 260, 38));
        accessory.AddSubview(selectButton);
        accessory.AddSubview(cancelButton);
        panel.AccessoryView = accessory;

        // Setting the accessory can change the panel's natural size, so the frame is computed after
        // it. Ordering the shared panel front can then restore AppKit's remembered frame, which is
        // why it is applied again once the panel is on screen.
        var panelFrame = ModalFrameFor(panel, owner);
        panel.SetFrame(panelFrame, false);

        var closeButton = panel.StandardWindowButton(NSWindowButton.CloseButton);
        var minimizeButton = panel.StandardWindowButton(NSWindowButton.MiniaturizeButton);
        var closeWasHidden = closeButton?.Hidden ?? false;
        var minimizeWasHidden = minimizeButton?.Hidden ?? false;

        if (closeButton is not null)
        {
            closeButton.Hidden = true;
        }

        if (minimizeButton is not null)
        {
            minimizeButton.Hidden = true;
        }

        NSFont? chosen = null;

        try
        {
            panel.MakeKeyAndOrderFront(null!);
            panel.SetFrame(panelFrame, true);

            // The panel does not run this itself; this is the modal session the interface promises.
            NSApplication.SharedApplication.RunModalForWindow(panel);

            if (!accepted)
            {
                return null;
            }

            chosen = manager.SelectedFont;
            chosen ??= manager.ConvertFont(initial) ?? initial;
        }
        finally
        {
            // A throw inside the session must not leave the application modal to the panel.
            if (ReferenceEquals(NSApplication.SharedApplication.ModalWindow, panel))
            {
                NSApplication.SharedApplication.AbortModal();
            }

            panel.OrderOut(null!);
            panel.AccessoryView = null;

            selectButton.RemoveFromSuperview();
            cancelButton.RemoveFromSuperview();
            selectButton.Dispose();
            cancelButton.Dispose();
            accessory.Dispose();

            if (closeButton is not null)
            {
                closeButton.Hidden = closeWasHidden;
            }

            if (minimizeButton is not null)
            {
                minimizeButton.Hidden = minimizeWasHidden;
            }
        }

        var resultFont = chosen ?? initial;
        var family = resultFont.FamilyName;
        if (string.IsNullOrWhiteSpace(family))
        {
            family = string.IsNullOrWhiteSpace(options.InitialFamilyName) ? "sans-serif" : options.InitialFamilyName;
        }

        var pointSize = (float)resultFont.PointSize;
        var chosenSize = pointSize > 0f ? options.ClampSize(pointSize) : size;
        var chosenWeight = FontMapping.FromAppKitWeight((int)(long)manager.WeightOfFont(resultFont));
        var chosenItalic = (manager.TraitsOfFont(resultFont) & NSFontTraitMask.Italic) != 0;

        return new FontSelection(family, chosenSize, chosenWeight, chosenItalic);
    }

    /// <summary>The smallest frame a modal font choice is presented in, in AppKit points.</summary>
    private const double MinimumFontPanelWidth = 600;

    /// <summary>The smallest frame a modal font choice is presented in, in AppKit points.</summary>
    private const double MinimumFontPanelHeight = 400;

    /// <summary>Space kept between the modal font panel and the visible screen edge, in points.</summary>
    private const double FontPanelScreenMargin = 24;

    /// <summary>
    /// The frame a modal font choice is presented in: centred on the owner window when there is
    /// one, otherwise on the visible screen, clamped to stay inside that screen.
    /// </summary>
    private static CGRect ModalFrameFor(NSFontPanel panel, Window? owner)
    {
        var window = (owner?.Backend as AppKitWindowBackend)?.Window;

        // AppKit frames are points, so the backing scale factor is deliberately not applied: on a
        // Retina display the points are already the right size for SetFrame.
        var screen = window?.Screen ?? NSScreen.MainScreen;
        if (screen is null)
        {
            return panel.Frame;
        }

        var visible = screen.VisibleFrame;
        var maxWidth = Math.Max(1, visible.Width - (2 * FontPanelScreenMargin));
        var maxHeight = Math.Max(1, visible.Height - (2 * FontPanelScreenMargin));

        // A stored frame may be smaller than the content needs or larger than the screen allows.
        var width = Math.Min(Math.Max(panel.Frame.Width, MinimumFontPanelWidth), maxWidth);
        var height = Math.Min(Math.Max(panel.Frame.Height, MinimumFontPanelHeight), maxHeight);

        var anchor = window?.Frame ?? visible;
        var x = anchor.GetMidX() - (width / 2);
        var y = anchor.GetMidY() - (height / 2);

        var minimumX = visible.X + FontPanelScreenMargin;
        var minimumY = visible.Y + FontPanelScreenMargin;
        var maximumX = Math.Max(minimumX, visible.GetMaxX() - FontPanelScreenMargin - width);
        var maximumY = Math.Max(minimumY, visible.GetMaxY() - FontPanelScreenMargin - height);

        return new CGRect(
            Math.Clamp(x, minimumX, maximumX),
            Math.Clamp(y, minimumY, maximumY),
            width,
            height);
    }

    private static void Configure(NSSavePanel panel, FileDialogOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            panel.Title = options.Title;
        }

        if (!string.IsNullOrWhiteSpace(options.FileName))
        {
            panel.NameFieldStringValue = options.FileName;
        }

        if (!string.IsNullOrWhiteSpace(options.InitialDirectory))
        {
            panel.DirectoryUrl = NSUrl.FromFilename(options.InitialDirectory);
        }

        var types = TypesFrom(options);
        if (types.Length > 0)
        {
            panel.AllowedContentTypes = types;
        }
    }

    private static string? Run(NSSavePanel panel)
    {
        var response = panel.RunModal();

        return response == PanelOkResponse ? panel.Url?.Path : null;
    }

    /// <summary>
    /// Turns the filter patterns into the uniform types macOS filters by. A pattern whose extension
    /// has no registered type is dropped rather than widening the dialog to everything.
    /// </summary>
    private static UTType[] TypesFrom(FileDialogOptions options)
    {
        if (options.Filters.Count == 0)
        {
            return [];
        }

        var types = new List<UTType>();

        foreach (var filter in options.Filters)
        {
            foreach (var pattern in filter.Patterns)
            {
                if (ExtensionOf(pattern) is not { } extension)
                {
                    continue;
                }

                if (UTType.CreateFromExtension(extension) is { } type && !types.Contains(type))
                {
                    types.Add(type);
                }
            }
        }

        return [.. types];
    }

    /// <summary>Extracts the extension a glob pattern matches, or null when it matches everything.</summary>
    private static string? ExtensionOf(string pattern)
    {
        var extension = pattern.Trim();

        var star = extension.LastIndexOf('*');
        if (star >= 0)
        {
            extension = extension[(star + 1)..];
        }

        extension = extension.TrimStart('.');

        return extension.Length == 0 || extension.Contains('*') ? null : extension;
    }

    /// <summary>
    /// Moves the Enter key to the requested button and the Escape key to Cancel, which is the only
    /// way AppKit lets a caller choose the default without reordering the buttons on screen.
    /// </summary>
    private static void ApplyDefaultButton(NSAlert alert, DialogResult[] results, DialogResult preferred)
    {
        var buttons = alert.Buttons;
        if (buttons.Length == 0)
        {
            return;
        }

        foreach (var button in buttons)
        {
            button.KeyEquivalent = string.Empty;
        }

        var index = Array.IndexOf(results, preferred);
        buttons[index >= 0 ? index : 0].KeyEquivalent = "\r";

        var cancel = Array.IndexOf(results, DialogResult.Cancel);
        if (cancel >= 0)
        {
            buttons[cancel].KeyEquivalent = "\u001b";
        }
    }

    private static NSAlertStyle MapIcon(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Error => NSAlertStyle.Critical,
        MessageBoxIcon.Warning => NSAlertStyle.Warning,
        _ => NSAlertStyle.Informational,
    };

    /// <summary>
    /// The buttons and the result each one reports, in the order they appear. macOS puts the
    /// affirmative button leftmost, which is the same order Windows uses.
    /// </summary>
    private static (string[] Titles, DialogResult[] Results) MapButtons(MessageBoxButtons buttons) =>
        buttons switch
        {
            MessageBoxButtons.OkCancel => (["OK", "Cancel"], [DialogResult.Ok, DialogResult.Cancel]),
            MessageBoxButtons.YesNo => (["Yes", "No"], [DialogResult.Yes, DialogResult.No]),
            MessageBoxButtons.YesNoCancel => (["Yes", "No", "Cancel"], [DialogResult.Yes, DialogResult.No, DialogResult.Cancel]),
            MessageBoxButtons.RetryCancel => (["Retry", "Cancel"], [DialogResult.Retry, DialogResult.Cancel]),
            MessageBoxButtons.AbortRetryIgnore => (["Abort", "Retry", "Ignore"], [DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore]),
            _ => (["OK"], [DialogResult.Ok]),
        };
}
