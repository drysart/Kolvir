namespace Kolvir.Backend.Gtk4;

/// <summary>
/// The GTK4 dialog service: message boxes come from <see cref="Gtk.AlertDialog"/>, file pickers
/// from <see cref="Gtk.FileDialog"/> and fonts from <see cref="Gtk.FontChooserDialog"/>, so dialogs
/// look and behave like the rest of the desktop instead of being drawn by SkiaSharp.
/// </summary>
/// <remarks>
/// Both GTK4 dialog APIs are asynchronous, while <see cref="IDialogService"/> is deliberately
/// synchronous: callers expect a result when the method returns. The gap is bridged by running the
/// default main context until the dialog's task completes, which is the same technique the
/// clipboard uses. That keeps the rest of the window painting and the application's tick running
/// while the dialog is up.
/// </remarks>
internal sealed class Gtk4DialogService : IDialogService
{
    private const string FallbackTitle = "Kolvir";

    public DialogResult ShowMessage(MessageBoxOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (titles, results) = MapButtons(options.Buttons);

        // GObject creates instances through factory methods, and GirCore drops the C# constructors
        // on native classes in 1.0, so the dialog is built the way GObject itself builds one.
        var dialog = Gtk.AlertDialog.NewWithProperties([]);
        dialog.Message = string.IsNullOrWhiteSpace(options.Title) ? FallbackTitle : options.Title;
        dialog.Detail = options.Message;
        dialog.Modal = true;

        dialog.SetButtons(titles);

        var defaultButton = Array.IndexOf(results, options.DefaultResult);
        dialog.SetDefaultButton(defaultButton >= 0 ? defaultButton : 0);

        var cancelButton = Array.IndexOf(results, DialogResult.Cancel);
        if (cancelButton >= 0)
        {
            dialog.SetCancelButton(cancelButton);
        }

        try
        {
            var choose = dialog.ChooseAsync(ParentOf(owner));
            Pump(choose);

            var index = choose.GetAwaiter().GetResult();
            return index >= 0 && index < results.Length ? results[index] : DialogResult.Cancel;
        }
        catch (Exception)
        {
            // Dismissing the dialog without choosing (Escape, window close) surfaces as an error.
            return DialogResult.Cancel;
        }
    }

    public string? OpenFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dialog = Gtk.FileDialog.New();
        Configure(dialog, options);

        return Complete(dialog.OpenAsync(ParentOf(owner)));
    }

    public string? SaveFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dialog = Gtk.FileDialog.New();
        Configure(dialog, options);

        return Complete(dialog.SaveAsync(ParentOf(owner)));
    }

    public string? PickFolder(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dialog = Gtk.FileDialog.New();
        Configure(dialog, options);

        return Complete(dialog.SelectFolderAsync(ParentOf(owner)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>Gtk.FontChooserDialog</c> is asynchronous like the other GTK4 dialogs, so the synchronous
    /// contract is met by running the default main context until its response signal arrives. The
    /// dialog is modal to its owner and GTK keeps painting the rest of the application while that
    /// loop runs.
    /// <para>
    /// A font description assigned before the chooser is mapped is discarded when its family list
    /// finishes populating. The dialog is therefore mapped transparent, the requested description
    /// is retried until the chooser reports it, and only then is the dialog revealed - so it opens
    /// on the caller's font rather than on whatever family the list happened to finish with.
    /// </para>
    /// </remarks>
    public FontSelection? PickFont(FontDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var initialSize = options.ClampSize(options.InitialSize);
        var initialUnits = ToPangoUnits(initialSize);

        // A font description is a copy, so it is disposed after the dialog has read it.
        using var initial = Pango.FontDescription.New();
        if (!string.IsNullOrWhiteSpace(options.InitialFamilyName))
        {
            initial.SetFamily(options.InitialFamilyName);
        }

        initial.SetSize(initialUnits);
        initial.SetWeight((Pango.Weight)FontMapping.ClampWeight(options.InitialWeight));
        initial.SetStyle(options.InitialItalic ? Pango.Style.Italic : Pango.Style.Normal);

        var dialog = Gtk.FontChooserDialog.New(options.Title ?? FallbackTitle, ParentOf(owner));
        dialog.Modal = true;

        var response = int.MinValue;
        dialog.OnResponse += (_, value) => response = value.ResponseId;

        // A window-manager close is a cancel, and stopping the pump here means a dialog that never
        // emits a response cannot leave the caller blocked.
        dialog.OnCloseRequest += (_, _) =>
        {
            if (response == int.MinValue)
            {
                response = (int)Gtk.ResponseType.Cancel;
            }

            return true;
        };

        uint initialSource = 0;
        var attempts = 0;

        try
        {
            // The chooser populates its family list asynchronously and only then keeps a
            // description assigned to it; anything set before that is overwritten by the default
            // family. The dialog is mapped transparently, the requested font is retried until the
            // chooser reports it, and only then does the dialog become visible - so a caller sees
            // its own font when the window appears rather than the list's first family.
            dialog.SetOpacity(0.0);
            dialog.Present();

            var context = GLib.MainContext.Default();
            initialSource = GLib.Functions.TimeoutAdd(0, 50, () =>
            {
                if (response != int.MinValue)
                {
                    dialog.SetOpacity(1.0);
                    return false;
                }

                dialog.FontDesc = initial;

                using var check = dialog.GetFontDesc();
                var applied = check is not null
                    && string.Equals(check.GetFamily(), initial.GetFamily(), StringComparison.Ordinal)
                    && check.GetSize() == initial.GetSize();

                if (applied || ++attempts >= 40)
                {
                    dialog.SetOpacity(1.0);
                    return false;
                }

                return true;
            });

            while (response == int.MinValue)
            {
                context.Iteration(true);
            }

            // Anything other than Select is the user closing the dialog: Cancel, Escape or the
            // window manager's close request all arrive here under a different response.
            if (response != (int)Gtk.ResponseType.Ok)
            {
                return null;
            }

            using var chosen = dialog.GetFontDesc();
            if (chosen is null)
            {
                var fallbackFamily = string.IsNullOrWhiteSpace(options.InitialFamilyName) ? "sans-serif" : options.InitialFamilyName;
                return new FontSelection(fallbackFamily, initialSize, FontMapping.ClampWeight(options.InitialWeight), options.InitialItalic);
            }

            var family = chosen.GetFamily();
            if (string.IsNullOrWhiteSpace(family))
            {
                family = string.IsNullOrWhiteSpace(options.InitialFamilyName) ? "sans-serif" : options.InitialFamilyName;
            }

            // Pango sizes are in points. A size unchanged from the one the dialog was seeded with
            // keeps the caller's exact pixel value; a changed one is converted back through 96 DPI.
            var chosenUnits = chosen.GetSize();
            var size = chosenUnits == initialUnits
                ? initialSize
                : options.ClampSize(FromPangoUnits(chosenUnits));

            var style = chosen.GetStyle();
            var italic = style is Pango.Style.Italic or Pango.Style.Oblique;

            return new FontSelection(family, size, (int)chosen.GetWeight(), italic);
        }
        finally
        {
            if (initialSource != 0)
            {
                GLib.Functions.SourceRemove(initialSource);
            }

            // Destroy drops the toplevel's native reference; the managed wrapper is collected the
            // same way the other GTK dialogs here are left to be. Flushing the pending unmap and
            // destroy events matters when the caller opens another dialog immediately: GTK maps the
            // next one only after the previous toplevel has really gone.
            dialog.Destroy();

            var context = GLib.MainContext.Default();
            for (var index = 0; index < 16 && context.Iteration(false); index++)
            {
            }
        }
    }

    /// <summary>Pango stores font sizes as points multiplied by this factor.</summary>
    private const int PangoUnitsPerPoint = 1024;

    private static int ToPangoUnits(float pixels) =>
        (int)MathF.Round(FontMapping.PixelsToPoints(pixels) * PangoUnitsPerPoint);

    private static float FromPangoUnits(int units) =>
        FontMapping.PointsToPixels(units / (float)PangoUnitsPerPoint);

    private static void Configure(Gtk.FileDialog dialog, FileDialogOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Title))
        {
            dialog.Title = options.Title;
        }

        if (!string.IsNullOrWhiteSpace(options.FileName))
        {
            dialog.InitialName = options.FileName;
        }

        if (!string.IsNullOrWhiteSpace(options.InitialDirectory))
        {
            dialog.InitialFolder = Gio.Functions.FileNewForPath(options.InitialDirectory);
        }

        if (CreateFilters(options) is { } filters)
        {
            dialog.Filters = filters;
        }
    }

    private static Gio.ListStore? CreateFilters(FileDialogOptions options)
    {
        if (options.Filters.Count == 0)
        {
            return null;
        }

        var store = Gio.ListStore.New<Gtk.FileFilter>();

        foreach (var filter in options.Filters)
        {
            var native = Gtk.FileFilter.New();
            native.SetName(filter.Name);

            var patterns = 0;
            foreach (var pattern in filter.Patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                // GTK file filters are glob patterns, the same syntax the library already uses.
                native.AddPattern(pattern);
                patterns++;
            }

            if (patterns > 0)
            {
                store.Append(native);
            }
        }

        return store.GetNItems() > 0 ? store : null;
    }

    private static string? Complete(Task<Gio.File?> task)
    {
        try
        {
            Pump(task);
            return task.GetAwaiter().GetResult()?.GetPath();
        }
        catch (Exception)
        {
            // Cancelling a GTK4 file dialog is reported as an exception.
            return null;
        }
    }

    /// <summary>
    /// Runs the default main context until <paramref name="task"/> finishes. Iteration is blocking,
    /// so this does not spin, and the application backend's poll timer guarantees it wakes up
    /// regularly even when the dialog never answers.
    /// </summary>
    private static void Pump(Task task)
    {
        var context = GLib.MainContext.Default();

        while (!task.IsCompleted)
        {
            context.Iteration(true);
        }
    }

    private static Gtk.Window? ParentOf(Window? owner) =>
        (owner?.Backend as Gtk4WindowBackend)?.Window;

    /// <summary>
    /// Maps the model's button set onto GTK's "an array of labels, answered by index" model.
    /// </summary>
    private static (string[] Titles, DialogResult[] Results) MapButtons(MessageBoxButtons buttons) =>
        buttons switch
        {
            MessageBoxButtons.OkCancel => (
                ["Cancel", "OK"],
                [DialogResult.Cancel, DialogResult.Ok]),

            MessageBoxButtons.YesNo => (
                ["No", "Yes"],
                [DialogResult.No, DialogResult.Yes]),

            MessageBoxButtons.YesNoCancel => (
                ["Cancel", "No", "Yes"],
                [DialogResult.Cancel, DialogResult.No, DialogResult.Yes]),

            MessageBoxButtons.RetryCancel => (
                ["Cancel", "Retry"],
                [DialogResult.Cancel, DialogResult.Retry]),

            MessageBoxButtons.AbortRetryIgnore => (
                ["Ignore", "Retry", "Abort"],
                [DialogResult.Ignore, DialogResult.Retry, DialogResult.Abort]),

            _ => (
                ["OK"],
                [DialogResult.Ok]),
        };
}
