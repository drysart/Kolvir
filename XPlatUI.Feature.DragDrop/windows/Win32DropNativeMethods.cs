using System.Runtime.InteropServices;

namespace XPlatUI.DragDrop;

/// <summary>
/// The native side of the Windows half of this feature: the OLE entry points that hand the shell a
/// drop target, and the clipboard formats a drag carries.
/// </summary>
/// <remarks>
/// These live here rather than in the backend's own <c>NativeMethods</c> so that a backend an
/// application never opts in to contains no trace of drag and drop: no import, no constant, no
/// struct. Nothing in this class is reachable unless something retained names it, and the only
/// thing that does is <see cref="Win32DropTarget"/>.
/// </remarks>
internal static class Win32DropNativeMethods
{
    /// <summary><c>CF_HDROP</c>: the format a drag of files is offered as.</summary>
    internal const uint CfHDrop = 15;

    /// <summary><c>DROPEFFECT_NONE</c>: the target will not take the drop.</summary>
    internal const uint DropEffectNone = 0x00000000;

    // The MK_* bits IDropTarget receives in its key state argument.
    internal const uint MkShift = 0x0004;
    internal const uint MkControl = 0x0008;
    internal const uint MkAlt = 0x0020;

    /// <summary>
    /// <c>STGMEDIUM</c>: the transfer medium <c>IDataObject.GetData</c> returns. The middle field
    /// is a union; for the global-memory medium this feature asks for it holds an <c>HGLOBAL</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct STGMEDIUM
    {
        public uint Tymed;
        public IntPtr Handle;
        public IntPtr UnknownForRelease;
    }

    // RegisterDragDrop hands the shell a COM object it calls into for the rest of the drag, and
    // needs the thread to be in a single-threaded apartment that OLE has set up, which no other
    // part of the library asks for.
    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int RegisterDragDrop(IntPtr hwnd, IntPtr dropTarget);

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint DragQueryFileW(IntPtr hDrop, uint index, [Out] char[]? fileName, uint cch);
}
