using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Kolvir.Badges;

/// <summary>
/// The taskbar's overlay icon API and the GDI calls that turn drawn pixels into the <c>HICON</c> it
/// takes, plus the message and DPI queries the feature needs to keep the icon current.
/// </summary>
/// <remarks>
/// <para>
/// <b>The interface identifier ends <c>…9E9F8A5EEFAF</c>.</b> The <c>…EECAF</c> spelling is widely
/// copied and is not ITaskbarList3: querying for it fails with <c>E_NOINTERFACE</c> while ITaskbarList4
/// still succeeds, which reads like a broken Windows install rather than a typo. The value below comes
/// from <c>shobjidl_core.h</c>.
/// </para>
/// <para>
/// The COM interface is declared with <see cref="GeneratedComInterfaceAttribute"/> rather than
/// <see cref="ComImportAttribute"/> for the same reason the backend's file dialogs are: the library
/// publishes with NativeAOT, where built-in COM interop is trimmed away, and the generated marshaller
/// is a source-generated vtable with no runtime reflection. Method order is the vtable order from the
/// SDK headers and must not be reordered.
/// </para>
/// <para>
/// <b>Strings cross this boundary as pointers.</b> The shell wants a localized description of the
/// overlay for accessibility, and handing it an <see cref="IntPtr"/> to a null-terminated UTF-16 string
/// keeps the call out of the string marshaller entirely, which is the shape that publishes cleanly.
/// </para>
/// </remarks>
internal static class Win32BadgeNativeMethods
{
    /// <summary><c>CLSID_TaskbarList</c>.</summary>
    internal static readonly Guid TaskbarListClass = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    /// <summary><c>IID_ITaskbarList3</c>, spelled the way <c>shobjidl_core.h</c> spells it.</summary>
    internal static readonly Guid TaskbarList3Interface = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");

    internal const uint ClsCtxInprocServer = 0x1;
    internal const uint DibRgbColors = 0;

    /// <summary><c>SM_CXSMICON</c>: the square the shell draws an overlay icon in.</summary>
    internal const int SmCxSmIcon = 49;

    /// <summary><c>WM_DPICHANGED</c>, after which the icon has to be redrawn at the new size.</summary>
    internal const uint WmDpiChanged = 0x02E0;

    /// <summary>
    /// A message this feature posts to its own windows, so a badge set on a worker thread is applied
    /// on the thread that owns the taskbar button.
    /// </summary>
    internal const uint WmBadgeChanged = 0x8000 + 0x0B1;

    internal const uint FlashwAll = 0x3;
    internal const uint FlashwTimerNoForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    internal struct FlashInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    internal static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("user32.dll")]
    internal static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr obj);
}

/// <summary>The part of the taskbar's overlay icon API the feature uses.</summary>
/// <remarks>
/// Only the tail of the interface is declared, because only the tail is called: the vtable order up to
/// <c>SetOverlayIcon</c> is the SDK's, and a method that is never invoked does not have to be spelled
/// out in a callable form. The slots before it are what fix its position.
/// </remarks>
[GeneratedComInterface]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
internal partial interface ITaskbarList3
{
    [PreserveSig]
    int HrInit();

    [PreserveSig]
    int AddTab(IntPtr hwnd);

    [PreserveSig]
    int DeleteTab(IntPtr hwnd);

    [PreserveSig]
    int ActivateTab(IntPtr hwnd);

    [PreserveSig]
    int SetActiveAlt(IntPtr hwnd);

    [PreserveSig]
    int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

    [PreserveSig]
    int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);

    [PreserveSig]
    int SetProgressState(IntPtr hwnd, int state);

    [PreserveSig]
    int RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);

    [PreserveSig]
    int UnregisterTab(IntPtr hwndTab);

    [PreserveSig]
    int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);

    [PreserveSig]
    int SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);

    [PreserveSig]
    int ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);

    [PreserveSig]
    int ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);

    [PreserveSig]
    int ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);

    [PreserveSig]
    int SetOverlayIcon(IntPtr hwnd, IntPtr icon, IntPtr description);

    [PreserveSig]
    int SetThumbnailTooltip(IntPtr hwnd, IntPtr tooltip);

    [PreserveSig]
    int SetThumbnailClip(IntPtr hwnd, IntPtr clip);
}
