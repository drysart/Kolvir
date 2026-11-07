using AppKit;
using CoreGraphics;
using Foundation;
using UniformTypeIdentifiers;

namespace XPlatUI.Backend.AppKit;

/// <summary>
/// The macOS <see cref="IXPlatDialogService"/>: <c>NSAlert</c> for messages,
/// <c>NSOpenPanel</c>/<c>NSSavePanel</c> for open, save and folder picking, and <c>NSFontPanel</c>
/// for fonts.
/// </summary>
/// <remarks>
/// <para>
/// Both run their own modal loop on the calling thread, which is what the nested loop requirement
/// in <see cref="IXPlatDialogService"/> asks for. They are shown application-modal rather than as
/// window sheets: a sheet is asynchronous, so it would return to the caller before the user had
/// answered, and <see cref="IXPlatDialogService"/> is a blocking API by design.
/// </para>
/// <para>
/// The application backend's common-mode poll timer keeps firing during those loops, so
/// <see cref="IXPlatApplicationBackend.Invoke"/> callbacks and ticks still arrive while a dialog is
/// open.
/// </para>
/// </remarks>
internal sealed class AppKitDialogService : IXPlatDialogService
{
    /// <summary>The application name a message box falls back to when no title is given.</summary>
    private static readonly string FallbackTitle = "XPlat UI";

    /// <summary>The response code AppKit returns for the first button of an alert.</summary>
    private static readonly nint FirstButtonResponse = 1000;

    /// <summary>The response code a panel returns when the user accepted.</summary>
    private static readonly nint PanelOkResponse = 1;

    /// <inheritdoc />
    public XPlatDialogResult ShowMessage(XPlatMessageBoxOptions options, XPlatWindow? owner)
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

        return index >= 0 && index < results.Length ? results[index] : XPlatDialogResult.Cancel;
    }

    /// <inheritdoc />
    public string? OpenFile(XPlatFileDialogOptions options, XPlatWindow? owner)
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
    public string? SaveFile(XPlatFileDialogOptions options, XPlatWindow? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        // NSSavePanel always confirms an overwrite; XPlatFileDialogOptions.OverwritePrompt has no
        // switch to turn that off, so it is honoured in the only direction macOS allows.
        var panel = NSSavePanel.SavePanel;
        panel.CanCreateDirectories = true;
        panel.ExtensionHidden = false;

        Configure(panel, options);

        return Run(panel);
    }

    /// <inheritdoc />
    public string? PickFolder(XPlatFileDialogOptions options, XPlatWindow? owner)
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
    /// the calling thread, which is what <see cref="IXPlatDialogService"/> requires; the backend's
    /// common-mode poll timer keeps <see cref="IXPlatApplicationBackend.Invoke"/> work flowing
    /// through it. Escape cancels and Return selects through the buttons' key equivalents.
    /// </para>
    /// <para>
    /// The panel's close and minimize buttons are hidden for the duration. Closing a window does not
    /// end a modal session by itself, so leaving those buttons available would give the user a way
    /// to dismiss the panel while this call stayed blocked forever. Select and Cancel are the only
    /// exits, and <c>finally</c> aborts the session if anything else goes wrong.
    /// </para>
    /// </remarks>
    public XPlatFontSelection? PickFont(XPlatFontDialogOptions options, XPlatWindow? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var size = options.ClampSize(options.InitialSize);
        var weight = XPlatFontMapping.ClampWeight(options.InitialWeight);
        var appKitWeight = (IntPtr)XPlatFontMapping.ToAppKitWeight(weight);

        // AppKit expresses weight through the face selected for a family plus the Bold/Unbold
        // trait; the manager's weight index chooses among the faces the family actually has.
        var traits = NSFontTraitMask.Unbold | NSFontTraitMask.Unitalic;
        if (weight >= XPlatFontMapping.BoldWeight)
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
        panel.SetPanelFont(initial, false);

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
        var chosenWeight = XPlatFontMapping.FromAppKitWeight((int)(long)manager.WeightOfFont(resultFont));
        var chosenItalic = (manager.TraitsOfFont(resultFont) & NSFontTraitMask.Italic) != 0;

        return new XPlatFontSelection(family, chosenSize, chosenWeight, chosenItalic);
    }

    private static void Configure(NSSavePanel panel, XPlatFileDialogOptions options)
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
    private static UTType[] TypesFrom(XPlatFileDialogOptions options)
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
    private static void ApplyDefaultButton(NSAlert alert, XPlatDialogResult[] results, XPlatDialogResult preferred)
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

        var cancel = Array.IndexOf(results, XPlatDialogResult.Cancel);
        if (cancel >= 0)
        {
            buttons[cancel].KeyEquivalent = "\u001b";
        }
    }

    private static NSAlertStyle MapIcon(XPlatMessageBoxIcon icon) => icon switch
    {
        XPlatMessageBoxIcon.Error => NSAlertStyle.Critical,
        XPlatMessageBoxIcon.Warning => NSAlertStyle.Warning,
        _ => NSAlertStyle.Informational,
    };

    /// <summary>
    /// The buttons and the result each one reports, in the order they appear. macOS puts the
    /// affirmative button leftmost, which is the same order Windows uses.
    /// </summary>
    private static (string[] Titles, XPlatDialogResult[] Results) MapButtons(XPlatMessageBoxButtons buttons) =>
        buttons switch
        {
            XPlatMessageBoxButtons.OkCancel => (["OK", "Cancel"], [XPlatDialogResult.Ok, XPlatDialogResult.Cancel]),
            XPlatMessageBoxButtons.YesNo => (["Yes", "No"], [XPlatDialogResult.Yes, XPlatDialogResult.No]),
            XPlatMessageBoxButtons.YesNoCancel => (["Yes", "No", "Cancel"], [XPlatDialogResult.Yes, XPlatDialogResult.No, XPlatDialogResult.Cancel]),
            XPlatMessageBoxButtons.RetryCancel => (["Retry", "Cancel"], [XPlatDialogResult.Retry, XPlatDialogResult.Cancel]),
            XPlatMessageBoxButtons.AbortRetryIgnore => (["Abort", "Retry", "Ignore"], [XPlatDialogResult.Abort, XPlatDialogResult.Retry, XPlatDialogResult.Ignore]),
            _ => (["OK"], [XPlatDialogResult.Ok]),
        };
}
