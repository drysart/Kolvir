using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace XPlatUI.Badges;

/// <summary>
/// Puts the badge on every window's taskbar button, and flashes the button when the badge is urgent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Windows badges a window, not an application.</b> The overlay icon belongs to a taskbar button,
/// and a process gets one button per window that is not grouped away, so "the application's badge" is
/// every top-level window this application has: each is set to the same icon and the same description,
/// which is what makes the several buttons of one application agree.
/// </para>
/// <para>
/// <b>The icon is per window because its size is.</b> The shell draws the overlay in the small-icon
/// slot for that window's DPI, so a 200% window needs a 32-pixel icon where a 100% window needs 16,
/// and a window that moves between monitors is redrawn when <c>WM_DPICHANGED</c> arrives.
/// </para>
/// <para>
/// <b>The shell throws the overlay away when it re-creates a taskbar button</b>, which it does whenever
/// Explorer restarts. The window hook listens for <c>TaskbarButtonCreated</c> through the backend's
/// message filter and re-applies, which is the only reason that seam exists.
/// </para>
/// <para>
/// <b>Threading.</b> The taskbar object and the icons live on the thread that owns the windows, so a
/// badge set from a worker is posted to each window and applied there. On the window's own thread the
/// change is applied straight away and the message is never posted.
/// </para>
/// </remarks>
internal sealed class Win32AppBadgePresenter : IXPlatAppBadgePresenter, IDisposable
{
    private readonly Dictionary<IntPtr, IntPtr> _icons = [];
    private readonly HashSet<IntPtr> _windows = [];

    private ITaskbarList3? _taskbar;
    private XPlatBadgeState _state = XPlatBadgeState.Empty;
    private bool _urgent;
    private bool _disposed;

    /// <inheritdoc />
    public XPlatBadgeCapabilities Capabilities { get; } = XPlatBadgeCapabilities.All;

    /// <inheritdoc />
    public void Apply(XPlatBadgeState state)
    {
        if (_disposed)
        {
            return;
        }

        _state = state;

        foreach (var hwnd in _windows)
        {
            if (IsCurrentThread(hwnd))
            {
                ApplyTo(hwnd);
            }
            else
            {
                Win32BadgeNativeMethods.PostMessage(hwnd, Win32BadgeNativeMethods.WmBadgeChanged, IntPtr.Zero, IntPtr.Zero);
            }
        }

        _urgent = state.Urgent;
    }

    /// <summary>Starts showing the current badge on <paramref name="hwnd"/>, which must be alive.</summary>
    internal void Attach(IntPtr hwnd)
    {
        if (_disposed || hwnd == IntPtr.Zero || !_windows.Add(hwnd))
        {
            return;
        }

        ApplyTo(hwnd);
    }

    /// <summary>Stops showing the badge on <paramref name="hwnd"/>, which must still be alive.</summary>
    internal void Detach(IntPtr hwnd)
    {
        if (_windows.Remove(hwnd) && _icons.Remove(hwnd, out var icon))
        {
            Win32BadgeNativeMethods.DestroyIcon(icon);
        }
    }

    /// <summary>
    /// Re-applies the current badge to <paramref name="hwnd"/>: its taskbar button was created again,
    /// or its DPI changed, and the overlay the shell holds is either gone or the wrong size.
    /// </summary>
    internal void Reapply(IntPtr hwnd)
    {
        if (_disposed || !_windows.Contains(hwnd))
        {
            return;
        }

        ApplyTo(hwnd);
    }

    /// <summary>The message a window posts to itself when a badge arrives from another thread.</summary>
    internal static uint BadgeChangedMessage => Win32BadgeNativeMethods.WmBadgeChanged;

    /// <summary>Releases the icons and the taskbar object. Called when the application shuts down.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var icon in _icons.Values)
        {
            Win32BadgeNativeMethods.DestroyIcon(icon);
        }

        _icons.Clear();
        _windows.Clear();

        if (_taskbar is not null)
        {
            Release(_taskbar);
            _taskbar = null;
        }
    }

    private void ApplyTo(IntPtr hwnd)
    {
        var taskbar = Taskbar();

        if (taskbar is null)
        {
            return;
        }

        var size = IconSize(hwnd);

        if (size <= 0)
        {
            return;
        }

        var replacement = Win32BadgeIcon.Create(_state, size);
        var description = Describe(_state);

        // The shell has to be told even when there is nothing to show: that is how a badge is cleared.
        var pointer = description is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(description);

        try
        {
            taskbar.SetOverlayIcon(hwnd, replacement, pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        if (_icons.Remove(hwnd, out var previous))
        {
            // The shell has already taken a copy of the pixels, so the old handle can go now.
            Win32BadgeNativeMethods.DestroyIcon(previous);
        }

        if (replacement != IntPtr.Zero)
        {
            _icons[hwnd] = replacement;
        }

        if (_state.Urgent && !_urgent)
        {
            Flash(hwnd);
        }
    }

    /// <summary>Flashes the taskbar button once per urgent transition, the way an attention request does.</summary>
    private static void Flash(IntPtr hwnd)
    {
        var flash = new Win32BadgeNativeMethods.FlashInfo
        {
            cbSize = (uint)Marshal.SizeOf<Win32BadgeNativeMethods.FlashInfo>(),
            hwnd = hwnd,
            dwFlags = Win32BadgeNativeMethods.FlashwAll | Win32BadgeNativeMethods.FlashwTimerNoForeground,
            uCount = 3,
        };

        Win32BadgeNativeMethods.FlashWindowEx(ref flash);
    }

    /// <summary>The small-icon slot for <paramref name="hwnd"/>'s current DPI, in device pixels.</summary>
    private static int IconSize(IntPtr hwnd)
    {
        var dpi = Win32BadgeNativeMethods.GetDpiForWindow(hwnd);

        if (dpi == 0)
        {
            dpi = 96;
        }

        return Win32BadgeNativeMethods.GetSystemMetricsForDpi(Win32BadgeNativeMethods.SmCxSmIcon, dpi);
    }

    /// <summary>
    /// The text the shell gives a screen reader for the overlay, or null when nothing is shown.
    /// </summary>
    private static string? Describe(XPlatBadgeState state) => state.Kind switch
    {
        XPlatBadgeKind.Count => $"{state.Count} unread",
        XPlatBadgeKind.Text => state.Text,
        XPlatBadgeKind.Dot => "New activity",
        _ => null,
    };

    private bool IsCurrentThread(IntPtr hwnd) =>
        Win32BadgeNativeMethods.GetWindowThreadProcessId(hwnd, IntPtr.Zero) == Win32BadgeNativeMethods.GetCurrentThreadId();

    /// <summary>
    /// The taskbar object, created on first use. A failure is not fatal: the window simply never shows
    /// an overlay, which is what happens on a system whose shell does not implement the interface.
    /// </summary>
    private unsafe ITaskbarList3? Taskbar()
    {
        if (_taskbar is not null)
        {
            return _taskbar;
        }

        var hr = Win32BadgeNativeMethods.CoCreateInstance(
            in Win32BadgeNativeMethods.TaskbarListClass,
            IntPtr.Zero,
            Win32BadgeNativeMethods.ClsCtxInprocServer,
            in Win32BadgeNativeMethods.TaskbarList3Interface,
            out var unknown);

        if (hr < 0 || unknown == IntPtr.Zero)
        {
            return null;
        }

        // The unique marshaller takes ownership of the reference the call handed back, which is why
        // nothing releases `unknown` here: the wrapper is what gives it back.
        var taskbar = UniqueComInterfaceMarshaller<ITaskbarList3>.ConvertToManaged((void*)unknown);

        if (taskbar is null || taskbar.HrInit() < 0)
        {
            if (taskbar is not null)
            {
                Release(taskbar);
            }

            return null;
        }

        _taskbar = taskbar;
        return _taskbar;
    }

    /// <summary>
    /// Releases a wrapper made by <see cref="UniqueComInterfaceMarshaller{T}"/>.
    /// </summary>
    /// <remarks>
    /// The wrapper is a <see cref="ComObject"/> and, because the unique marshaller creates one instance
    /// per conversion, <see cref="ComObject.FinalRelease"/> really does drop the reference - on CoreCLR
    /// and under NativeAOT alike. <c>Marshal.FinalReleaseComObject</c> must not be used: it throws on
    /// both, and the AOT exception type derives from the CoreCLR one, so a filter naming the CoreCLR
    /// case lets the other escape.
    /// </remarks>
    private static void Release(object wrapper)
    {
        if (wrapper is ComObject comObject)
        {
            comObject.FinalRelease();
            return;
        }

        (wrapper as IDisposable)?.Dispose();
    }
}
