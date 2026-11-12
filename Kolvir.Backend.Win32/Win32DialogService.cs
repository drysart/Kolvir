using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Kolvir.Backend.Win32;

/// <summary>
/// The Windows <see cref="IDialogService"/>: <c>MessageBoxW</c> for messages, the
/// <c>IFileDialog</c> family for open, save and folder picking, and <c>ChooseFontW</c> for fonts.
/// </summary>
/// <remarks>
/// <para>
/// Both are already modal loops owned by the OS: <c>MessageBoxW</c> and <c>IFileDialog::Show</c> run
/// their own message pump on the calling thread until the user answers. That satisfies the nested
/// loop requirement in <see cref="IDialogService"/> without this class pumping anything, because
/// those pumps dispatch every message for the thread - including the backend's wake-up message, so
/// <see cref="IApplicationBackend.Invoke"/> callbacks keep arriving while a dialog is open.
/// </para>
/// <para>
/// The file dialogs are the modern common item dialogs rather than the older
/// <c>GetOpenFileNameW</c>, because only those can pick a folder. They are driven through
/// source-generated COM so the whole thing survives NativeAOT, where built-in COM interop is
/// trimmed away.
/// </para>
/// <para>
/// The file dialogs are also the one part of this backend that needs the UI thread to be a
/// single-threaded COM apartment. <c>IFileDialog::Show</c> never returns - and never creates its
/// window - on a multi-threaded apartment, so rather than hang, <see cref="OpenFile"/> and the rest
/// throw when the thread is MTA. A message box is plain <c>user32</c> and needs neither COM nor an
/// apartment.
/// </para>
/// </remarks>
internal sealed class Win32DialogService : IDialogService
{
    /// <summary>The application name a message box falls back to when no title is given.</summary>
    private static readonly string FallbackTitle = "Kolvir";

    private readonly Win32ApplicationBackend _backend;

    /// <summary>Creates the service, which asks <paramref name="backend"/> about the COM apartment.</summary>
    /// <param name="backend">The backend whose UI thread will host the dialogs.</param>
    public Win32DialogService(Win32ApplicationBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <inheritdoc />
    public DialogResult ShowMessage(MessageBoxOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (buttonFlags, positions) = MapButtons(options.Buttons);
        var type = buttonFlags | MapIcon(options.Icon) | MapDefaultButton(options.DefaultResult, positions);

        var result = NativeMethods.MessageBoxW(
            HandleOf(owner),
            options.Message,
            options.Title ?? FallbackTitle,
            type);

        return result switch
        {
            NativeMethods.IdOk => DialogResult.Ok,
            NativeMethods.IdCancel => DialogResult.Cancel,
            NativeMethods.IdYes => DialogResult.Yes,
            NativeMethods.IdNo => DialogResult.No,
            NativeMethods.IdRetry => DialogResult.Retry,
            NativeMethods.IdAbort => DialogResult.Abort,
            NativeMethods.IdIgnore => DialogResult.Ignore,
            // Zero means the box could not be shown at all. Reporting Cancel is the answer a
            // dismissed box gives, and the alternatives are throwing from a diagnostic helper.
            _ => DialogResult.Cancel,
        };
    }

    /// <inheritdoc />
    public string? OpenFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireSingleThreadedApartment();

        return ShowFileDialog(
            Win32ComInterfaces.FileOpenDialogClass,
            FileOpenDialogOptions.FileMustExist | FileOpenDialogOptions.PathMustExist,
            options,
            owner);
    }

    /// <inheritdoc />
    public string? SaveFile(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireSingleThreadedApartment();

        return ShowFileDialog(
            Win32ComInterfaces.FileSaveDialogClass,
            options.OverwritePrompt ? FileOpenDialogOptions.OverwritePrompt : 0,
            options,
            owner);
    }

    /// <inheritdoc />
    public string? PickFolder(FileDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireSingleThreadedApartment();

        // FOS_PICKFOLDERS turns the same dialog into a folder picker; the file-only flags would
        // then reject every folder the user picks, so they are left off.
        return ShowFileDialog(
            Win32ComInterfaces.FileOpenDialogClass,
            FileOpenDialogOptions.PickFolders | FileOpenDialogOptions.PathMustExist,
            options,
            owner);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>ChooseFontW</c> runs its own modal message loop on the calling thread, so it satisfies the
    /// nested-loop requirement without this class pumping anything. It is plain <c>comdlg32</c> and
    /// needs neither COM nor an apartment, so unlike the shell file dialogs it works on an MTA
    /// thread too.
    /// </remarks>
    public unsafe FontSelection? PickFont(FontDialogOptions options, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ownerHandle = HandleOf(owner);
        var dpi = ownerHandle != IntPtr.Zero ? NativeMethods.GetDpiForWindow(ownerHandle) : NativeMethods.GetDpiForSystem();
        if (dpi == 0)
        {
            dpi = 96;
        }

        var initialSize = options.ClampSize(options.InitialSize);
        var initialPoints = FontMapping.PixelsToPoints(initialSize);

        // ChooseFont's size field is in whole points, so it displays and returns the nearest one.
        // Recording that rounded value is what lets an untouched dialog hand the caller's pixel
        // size back exactly instead of turning 13 px into 13.33 px on every open.
        var initialTenths = (int)MathF.Round(initialPoints) * 10;

        var logFont = new NativeMethods.LOGFONTW
        {
            // A logical pixel is 1/96 inch and a point is 1/72, so the device height the dialog
            // starts from is the logical size scaled by the display's DPI.
            lfHeight = -(int)MathF.Round(initialSize * dpi / 96f),
            lfWeight = FontMapping.ClampWeight(options.InitialWeight),
            lfItalic = (byte)(options.InitialItalic ? 1 : 0),
            lfCharSet = 1, // DEFAULT_CHARSET
        };

        // The struct is a local, so its address is stable for the duration of the call and the
        // fixed buffer never needs a pin. Passing the pointer keeps the struct blittable, which
        // matters under NativeAOT.
        var font = &logFont;
        SetFaceName(font, options.InitialFamilyName);

        var minimumPoints = Math.Max(1, (int)MathF.Round(FontMapping.PixelsToPoints(options.ClampSize(options.MinimumSize))));
        var maximumPoints = Math.Max(minimumPoints, (int)MathF.Round(FontMapping.PixelsToPoints(options.ClampSize(options.MaximumSize))));

        var choose = new NativeMethods.CHOOSEFONT
        {
            lStructSize = Marshal.SizeOf<NativeMethods.CHOOSEFONT>(),
            hwndOwner = ownerHandle,
            lpLogFont = (IntPtr)font,
            iPointSize = initialTenths,
            Flags = NativeMethods.CfScreenFonts
                | NativeMethods.CfInittLogFontStruct
                | NativeMethods.CfLimitSize
                | NativeMethods.CfForceFontExist
                | NativeMethods.CfScalableOnly
                | NativeMethods.CfNoVertFonts,
            nSizeMin = minimumPoints,
            nSizeMax = maximumPoints,
        };

        if (!NativeMethods.ChooseFontW(ref choose))
        {
            // False covers Cancel and a dialog that could not be created; both are "no choice".
            return null;
        }

        var family = FaceNameOf(font);
        if (string.IsNullOrWhiteSpace(family))
        {
            family = string.IsNullOrWhiteSpace(options.InitialFamilyName) ? "sans-serif" : options.InitialFamilyName;
        }

        // The dialog reports tenths of a point. When it comes back with the value it was seeded
        // with, the caller's own size is kept exactly rather than round-tripped through a unit that
        // cannot represent every pixel size.
        var size = choose.iPointSize == initialTenths || choose.iPointSize <= 0
            ? initialSize
            : options.ClampSize(FontMapping.PointsToPixels(choose.iPointSize / 10f));

        // LOGFONT uses 0 as "the face's own default", which is regular for every family that has
        // one; passing that through would produce a weight of 1.
        var weight = logFont.lfWeight == 0 ? FontMapping.NormalWeight : logFont.lfWeight;

        return new FontSelection(family, size, weight, logFont.lfItalic != 0);
    }

    /// <summary>Copies a family name into the fixed face-name buffer, truncating at the native limit.</summary>
    private static unsafe void SetFaceName(NativeMethods.LOGFONTW* logFont, string? family)
    {
        if (string.IsNullOrEmpty(family))
        {
            return;
        }

        var length = Math.Min(family.Length, 31);
        for (var index = 0; index < length; index++)
        {
            logFont->lfFaceName[index] = family[index];
        }
    }

    /// <summary>Reads the NUL-terminated face name out of the fixed buffer.</summary>
    private static unsafe string FaceNameOf(NativeMethods.LOGFONTW* logFont)
    {
        var length = 0;
        while (length < 32 && logFont->lfFaceName[length] != '\0')
        {
            length++;
        }

        return length == 0 ? string.Empty : new string(logFont->lfFaceName, 0, length);
    }

    /// <summary>
    /// Fails loudly when the UI thread is not a single-threaded apartment, because
    /// <c>IFileDialog::Show</c> blocks forever there instead of returning an error.
    /// </summary>
    /// <exception cref="InvalidOperationException">The UI thread is a multi-threaded apartment.</exception>
    private void RequireSingleThreadedApartment()
    {
        if (_backend.IsSingleThreadedApartment)
        {
            return;
        }

        throw new InvalidOperationException(
            "The shell's file dialogs need a single-threaded COM apartment, and the UI thread is " +
            "multi-threaded, so IFileDialog::Show would never return. Add [STAThread] to the entry " +
            "point: the runtime reads it before Main runs and initializes the thread as STA, which " +
            "no COM call made later can do.");
    }

    private static unsafe string? ShowFileDialog(
        Guid classId,
        FileOpenDialogOptions modeOptions,
        FileDialogOptions options,
        Window? owner)
    {
        var interfaceId = typeof(IFileDialog).GUID;
        var hr = NativeMethods.CoCreateInstance(classId, IntPtr.Zero, NativeMethods.ClsCtxInprocServer, interfaceId, out var unknown);
        Marshal.ThrowExceptionForHR(hr);

        var dialog = Wrap<IFileDialog>(unknown);
        try
        {
            dialog.GetOptions(out var current);
            dialog.SetOptions(current | modeOptions | FileOpenDialogOptions.ForceFileSystem | FileOpenDialogOptions.NoChangeDir);

            if (!string.IsNullOrWhiteSpace(options.Title))
            {
                dialog.SetTitle(options.Title);
            }

            if (!string.IsNullOrWhiteSpace(options.FileName))
            {
                dialog.SetFileName(options.FileName);
            }

            ApplyFilters(dialog, options);

            if (!string.IsNullOrWhiteSpace(options.InitialDirectory))
            {
                ApplyInitialDirectory(dialog, options.InitialDirectory);
            }

            var show = dialog.Show(HandleOf(owner));
            if (show == NativeMethods.ErrorCancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(show);

            dialog.GetResult(out var itemPointer);
            var item = Wrap<IShellItem>(itemPointer);
            try
            {
                item.GetDisplayName(ShellItemDisplayName.FileSysPath, out var name);
                try
                {
                    return Marshal.PtrToStringUni(name);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }
            }
            finally
            {
                Release(item);
            }
        }
        finally
        {
            Release(dialog);
        }
    }

    /// <summary>
    /// Hands the filter drop-down to the dialog. The strings live in unmanaged memory only for the
    /// duration of the call, because the dialog copies them.
    /// </summary>
    private static void ApplyFilters(IFileDialog dialog, FileDialogOptions options)
    {
        if (options.Filters.Count == 0)
        {
            return;
        }

        var specs = new ComDlgFilterSpec[options.Filters.Count];
        var pinned = new List<IntPtr>(options.Filters.Count * 2);
        try
        {
            for (var i = 0; i < options.Filters.Count; i++)
            {
                var filter = options.Filters[i];
                specs[i] = new ComDlgFilterSpec
                {
                    Name = Marshal.StringToHGlobalUni(filter.Name),
                    // One entry can name several patterns; the common dialog wants them joined by
                    // semicolons in a single string.
                    Spec = Marshal.StringToHGlobalUni(string.Join(';', filter.Patterns)),
                };
                pinned.Add(specs[i].Name);
                pinned.Add(specs[i].Spec);
            }

            dialog.SetFileTypes((uint)specs.Length, specs);
            dialog.SetFileTypeIndex(1);
        }
        finally
        {
            foreach (var pointer in pinned)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    private static unsafe void ApplyInitialDirectory(IFileDialog dialog, string directory)
    {
        var interfaceId = Win32ComInterfaces.ShellItemInterface;
        if (NativeMethods.SHCreateItemFromParsingName(directory, IntPtr.Zero, interfaceId, out var unknown) != 0)
        {
            // A directory that no longer exists is not worth failing the whole dialog over; the
            // dialog simply opens wherever it last remembered.
            return;
        }

        var folder = Wrap<IShellItem>(unknown);
        try
        {
            dialog.SetFolder(folder);
        }
        finally
        {
            Release(folder);
        }
    }

    /// <summary>
    /// Wraps a COM reference this class owns. <c>ComInterfaceMarshaller&lt;T&gt;.ConvertToManaged</c>
    /// would hand back a cached wrapper, and <see cref="ComObject.FinalRelease"/> deliberately does
    /// nothing for a cached wrapper; <see cref="UniqueComInterfaceMarshaller{T}"/> makes one wrapper
    /// per reference, which is what lets <see cref="Release"/> actually release it.
    /// </summary>
    /// <param name="unknown">The owned interface pointer. The wrapper takes the reference over.</param>
    /// <returns>The wrapper, which implements <typeparamref name="T"/>.</returns>
    internal static unsafe T Wrap<T>(IntPtr unknown)
        where T : class =>
        UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)unknown)!;

    /// <summary>
    /// Releases this class's reference to a wrapper made by <see cref="Wrap{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wrapper is a <c>ComWrappers</c> <see cref="ComObject"/> rather than a classic RCW, and
    /// because <see cref="UniqueComInterfaceMarshaller{T}"/> creates a unique instance,
    /// <see cref="ComObject.FinalRelease"/> really does release the reference here - on CoreCLR and
    /// under NativeAOT alike. That pairing is the contract: a wrapper from the cached
    /// <c>ComInterfaceMarshaller&lt;T&gt;</c> would make this a silent no-op.
    /// </para>
    /// <para>
    /// <c>Marshal.FinalReleaseComObject</c> must not be used for these at all. It throws
    /// <c>ArgumentException</c> on CoreCLR and <c>NotSupportedException</c> under NativeAOT, and
    /// <c>PlatformNotSupportedException</c> derives from the second rather than the first, so a
    /// filter naming the CoreCLR exceptions lets the AOT one escape the <c>finally</c> that calls
    /// this and takes the process with it.
    /// </para>
    /// </remarks>
    internal static void Release(object wrapper)
    {
        if (wrapper is ComObject comObject)
        {
            comObject.FinalRelease();
            return;
        }

        // Nothing on this path is another shape today; a wrapper that is one releases if it can.
        (wrapper as IDisposable)?.Dispose();
    }

    private static IntPtr HandleOf(Window? owner) =>
        owner?.Backend is Win32WindowBackend window ? window.Handle : IntPtr.Zero;

    private static (uint Flags, DialogResult[] Positions) MapButtons(MessageBoxButtons buttons) =>
        buttons switch
        {
            MessageBoxButtons.Ok => (NativeMethods.MbOk, [DialogResult.Ok]),
            MessageBoxButtons.OkCancel => (NativeMethods.MbOkCancel, [DialogResult.Ok, DialogResult.Cancel]),
            MessageBoxButtons.YesNo => (NativeMethods.MbYesNo, [DialogResult.Yes, DialogResult.No]),
            MessageBoxButtons.YesNoCancel => (NativeMethods.MbYesNoCancel, [DialogResult.Yes, DialogResult.No, DialogResult.Cancel]),
            MessageBoxButtons.RetryCancel => (NativeMethods.MbRetryCancel, [DialogResult.Retry, DialogResult.Cancel]),
            MessageBoxButtons.AbortRetryIgnore => (NativeMethods.MbAbortRetryIgnore, [DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore]),
            _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, "Unknown button set."),
        };

    private static uint MapIcon(MessageBoxIcon icon) =>
        icon switch
        {
            MessageBoxIcon.None => 0,
            MessageBoxIcon.Information => NativeMethods.MbIconInformation,
            MessageBoxIcon.Warning => NativeMethods.MbIconWarning,
            MessageBoxIcon.Error => NativeMethods.MbIconError,
            MessageBoxIcon.Question => NativeMethods.MbIconQuestion,
            _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, "Unknown icon."),
        };

    /// <summary>
    /// Picks the <c>MB_DEFBUTTON</c> flag for the requested default. A default the button set does
    /// not contain falls back to the first button, which is what WinForms does.
    /// </summary>
    private static uint MapDefaultButton(DialogResult preferred, DialogResult[] positions)
    {
        var index = Array.IndexOf(positions, preferred);
        return index switch
        {
            1 => NativeMethods.MbDefButton2,
            2 => NativeMethods.MbDefButton3,
            _ => NativeMethods.MbDefButton1,
        };
    }
}
