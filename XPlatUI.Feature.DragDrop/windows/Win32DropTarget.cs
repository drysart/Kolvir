using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using XPlatUI.Backend.Win32;

namespace XPlatUI.DragDrop;

/// <summary>
/// The OLE drop target of one Win32 window: the COM object the shell calls into while something is
/// dragged over the window, translated into the control tree's drag and drop events.
/// </summary>
/// <remarks>
/// <para>
/// The shell owns the drag, not this process, so there is no message loop involvement: everything
/// arrives as COM calls on the UI thread, in the window's single-threaded apartment. That is why
/// every entry point has to swallow its own exceptions rather than let them unwind into native
/// frames.
/// </para>
/// <para>
/// The COM object is laid out by hand instead of being handed to <c>ComWrappers</c>: a small block
/// of unmanaged memory holding the vtable pointer, the reference count and a handle back to this
/// instance, fronted by a vtable of <see cref="UnmanagedCallersOnlyAttribute"/> entry points. Those
/// entry points are real native code addresses, which is what Control Flow Guard demands of an
/// indirect call made by the shell, and building the vtable directly keeps the whole thing working
/// under NativeAOT.
/// </para>
/// </remarks>
internal sealed unsafe class Win32DropTarget : IDropTarget
{
    private const int VtableOffset = 0;
    private const int RefCountOffset = 1;
    private const int HandleOffset = 2;
    private const int ObjectSize = 3;
    private const int VtableSlots = 7;

    private static readonly object VtableGate = new();

    private static nint* s_vtable;

    private readonly Win32WindowBackend _window;
    private readonly XPlatDragDropRouter _router;

    private nint* _object;
    private XPlatDataObject? _data;

    // The borrowed pointer DragEnter was handed, kept only so DragOver - which receives no data
    // object at all - can sample the same object's reference count. Nothing is ever released
    // through it: the shell owns the reference for the whole drag.
    private IntPtr _dataObject;

    /// <summary>Creates the drop target of a window.</summary>
    /// <param name="window">The window the target belongs to.</param>
    /// <param name="router">The router that turns what the shell reports into control events.</param>
    public Win32DropTarget(Win32WindowBackend window, XPlatDragDropRouter router)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(router);
        _window = window;
        _router = router;
    }

    /// <summary>Registers the target with the window, so the shell starts sending it drags.</summary>
    /// <param name="hwnd">The native handle of the window.</param>
    /// <returns><see langword="true"/> when the window now accepts drops.</returns>
    public bool Register(IntPtr hwnd)
    {
        if (_object is not null)
        {
            return true;
        }

        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        // Registration fails with CO_E_NOTINITIALIZED unless OLE has put this thread into an
        // apartment. WinForms only does that for its own clipboard and drag support, so a window
        // created by this backend has to ask for it. The call is idempotent per thread and reports
        // S_FALSE once the apartment exists.
        Win32DropNativeMethods.OleInitialize(IntPtr.Zero);

        var instance = GCHandle.Alloc(this, GCHandleType.Normal);
        var comObject = (nint*)NativeMemory.Alloc(ObjectSize, (nuint)sizeof(nint));
        comObject[VtableOffset] = (nint)Vtable();
        comObject[RefCountOffset] = 1;
        comObject[HandleOffset] = GCHandle.ToIntPtr(instance);
        _object = comObject;

        var registerHr = Win32DropNativeMethods.RegisterDragDrop(hwnd, (IntPtr)comObject);
        if (registerHr == Win32DropNativeMethods.SOk)
        {
            return true;
        }

        // Registration fails when the thread is not in a single-threaded apartment, or when the
        // window already has a target. Either way the window simply never sees a drop, which is a
        // degraded but working application.
        ReleaseObject();
        return false;
    }

    /// <summary>Unregisters the target, which must happen before the window is destroyed.</summary>
    /// <param name="hwnd">The native handle of the window.</param>
    public void Revoke(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            Win32DropNativeMethods.RevokeDragDrop(hwnd);
        }

        ReleaseObject();
        _data = null;
    }

    /// <summary>Drops the reference this object holds on its own COM object.</summary>
    private void ReleaseObject()
    {
        if (_object is null)
        {
            return;
        }

        var comObject = _object;
        _object = null;
        ReleaseCore(comObject);
    }

    /// <inheritdoc />
    public int DragEnter(IntPtr dataObject, uint keyState, Win32DropNativeMethods.POINT point, ref uint effect)
    {
        try
        {
            _dataObject = dataObject;
            _data = Read(dataObject);

            // Sampled after the read, so the count is what the feature leaves behind for this event
            // rather than what it is holding while it works: in a correct implementation reading the
            // object is arithmetic-neutral and the drag's samples never move.
            Win32DropReferenceTrace.Record(Win32DropReferenceTrace.EnterEvent, dataObject);

            effect = _data is null
                ? Win32DropNativeMethods.DropEffectNone
                : (uint)_router.DragEnter(_window.ScreenToLogical(point.X, point.Y), _data, (XPlatDragDropEffects)effect, ModifiersOf(keyState));
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
            _data = null;
            effect = Win32DropNativeMethods.DropEffectNone;
        }

        return Win32DropNativeMethods.SOk;
    }

    /// <inheritdoc />
    public int DragOver(uint keyState, Win32DropNativeMethods.POINT point, ref uint effect)
    {
        try
        {
            Win32DropReferenceTrace.Record(Win32DropReferenceTrace.OverEvent, _dataObject);

            if (_data is null)
            {
                effect = Win32DropNativeMethods.DropEffectNone;
            }
            else
            {
                effect = (uint)_router.DragOver(_window.ScreenToLogical(point.X, point.Y), _data, (XPlatDragDropEffects)effect, ModifiersOf(keyState));
            }
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
            effect = Win32DropNativeMethods.DropEffectNone;
        }

        return Win32DropNativeMethods.SOk;
    }

    /// <inheritdoc />
    public int DragLeave()
    {
        try
        {
            _router.DragLeave();
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
        }

        _data = null;
        _dataObject = IntPtr.Zero;
        return Win32DropNativeMethods.SOk;
    }

    /// <inheritdoc />
    public int Drop(IntPtr dataObject, uint keyState, Win32DropNativeMethods.POINT point, ref uint effect)
    {
        try
        {
            // The data object handed to Drop is the authority: read it again rather than trusting
            // whatever was cached at DragEnter, which a source is allowed to have replaced.
            var data = Read(dataObject) ?? _data;

            // The second read of this pointer, and the sample that shows what it cost: the wrapper
            // cache releases the incoming reference here rather than at finalization.
            Win32DropReferenceTrace.Record(Win32DropReferenceTrace.DropEvent, dataObject);

            effect = data is null
                ? Win32DropNativeMethods.DropEffectNone
                : (uint)_router.DragDrop(_window.ScreenToLogical(point.X, point.Y), data, (XPlatDragDropEffects)effect, ModifiersOf(keyState));
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
            effect = Win32DropNativeMethods.DropEffectNone;
        }

        _data = null;
        _dataObject = IntPtr.Zero;
        return Win32DropNativeMethods.SOk;
    }

    /// <summary>Returns the vtable shared by every drop target in the process, building it once.</summary>
    /// <remarks>
    /// The slot order is <c>IUnknown</c> followed by the four <c>IDropTarget</c> methods, which is
    /// the order the shell dispatches by. The table lives in unmanaged memory for the lifetime of
    /// the process, so the entries never move.
    /// </remarks>
    private static nint* Vtable()
    {
        if (s_vtable is not null)
        {
            return s_vtable;
        }

        lock (VtableGate)
        {
            if (s_vtable is null)
            {
                var table = (nint*)NativeMemory.Alloc(VtableSlots, (nuint)sizeof(nint));
                table[0] = (nint)(delegate* unmanaged[MemberFunction]<void*, Guid*, void**, int>)&QueryInterface;
                table[1] = (nint)(delegate* unmanaged[MemberFunction]<void*, uint>)&AddRef;
                table[2] = (nint)(delegate* unmanaged[MemberFunction]<void*, uint>)&Release;
                table[3] = (nint)(delegate* unmanaged[MemberFunction]<void*, nint, uint, Win32DropNativeMethods.POINT, uint*, int>)&DragEnterCallback;
                table[4] = (nint)(delegate* unmanaged[MemberFunction]<void*, uint, Win32DropNativeMethods.POINT, uint*, int>)&DragOverCallback;
                table[5] = (nint)(delegate* unmanaged[MemberFunction]<void*, int>)&DragLeaveCallback;
                table[6] = (nint)(delegate* unmanaged[MemberFunction]<void*, nint, uint, Win32DropNativeMethods.POINT, uint*, int>)&DropCallback;
                s_vtable = table;
            }
        }

        return s_vtable;
    }

    /// <summary>Recovers the managed target a COM call was made on.</summary>
    private static Win32DropTarget? FromSelf(void* self) =>
        GCHandle.FromIntPtr(((nint*)self)[HandleOffset]).Target as Win32DropTarget;

    private static uint AddRefCore(void* self)
    {
        var comObject = (nint*)self;
        comObject[RefCountOffset]++;
        return (uint)comObject[RefCountOffset];
    }

    private static uint ReleaseCore(void* self)
    {
        var comObject = (nint*)self;
        comObject[RefCountOffset]--;
        var count = (uint)comObject[RefCountOffset];

        if (count == 0)
        {
            GCHandle.FromIntPtr(comObject[HandleOffset]).Free();
            NativeMemory.Free(comObject);
        }

        return count;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int QueryInterface(void* self, Guid* interfaceId, void** result)
    {
        if (result is null)
        {
            return Win32DropNativeMethods.EPointer;
        }

        *result = null;

        if (interfaceId is null)
        {
            return Win32DropNativeMethods.EInvalidArgument;
        }

        if (*interfaceId != Win32DropInterfaces.DropTargetId && *interfaceId != Win32DropInterfaces.UnknownId)
        {
            return Win32DropNativeMethods.ENoInterface;
        }

        *result = self;
        AddRefCore(self);
        return Win32DropNativeMethods.SOk;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static uint AddRef(void* self) => AddRefCore(self);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static uint Release(void* self) => ReleaseCore(self);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int DragEnterCallback(
        void* self,
        nint dataObject,
        uint keyState,
        Win32DropNativeMethods.POINT point,
        uint* effect)
    {
        var target = FromSelf(self);
        if (target is null)
        {
            return Win32DropNativeMethods.EPointer;
        }

        var requested = effect is null ? 0u : *effect;
        var result = target.DragEnter(dataObject, keyState, point, ref requested);

        if (effect is not null)
        {
            *effect = requested;
        }

        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int DragOverCallback(
        void* self,
        uint keyState,
        Win32DropNativeMethods.POINT point,
        uint* effect)
    {
        var target = FromSelf(self);
        if (target is null)
        {
            return Win32DropNativeMethods.EPointer;
        }

        var requested = effect is null ? 0u : *effect;
        var result = target.DragOver(keyState, point, ref requested);

        if (effect is not null)
        {
            *effect = requested;
        }

        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int DragLeaveCallback(void* self)
    {
        var target = FromSelf(self);
        return target is null ? Win32DropNativeMethods.EPointer : target.DragLeave();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvMemberFunction)])]
    private static int DropCallback(
        void* self,
        nint dataObject,
        uint keyState,
        Win32DropNativeMethods.POINT point,
        uint* effect)
    {
        var target = FromSelf(self);
        if (target is null)
        {
            return Win32DropNativeMethods.EPointer;
        }

        var requested = effect is null ? 0u : *effect;
        var result = target.Drop(dataObject, keyState, point, ref requested);

        if (effect is not null)
        {
            *effect = requested;
        }

        return result;
    }

    /// <summary>Maps the <c>MK_*</c> bits of a drag notification onto the library's modifiers.</summary>
    private static XPlatModifiers ModifiersOf(uint keyState)
    {
        var modifiers = XPlatModifiers.None;

        if ((keyState & Win32DropNativeMethods.MkShift) != 0)
        {
            modifiers |= XPlatModifiers.Shift;
        }

        if ((keyState & Win32DropNativeMethods.MkControl) != 0)
        {
            modifiers |= XPlatModifiers.Control;
        }

        if ((keyState & Win32DropNativeMethods.MkAlt) != 0)
        {
            modifiers |= XPlatModifiers.Alt;
        }

        return modifiers;
    }

    /// <summary>Reads everything the library understands out of a dragged data object.</summary>
    /// <remarks>
    /// The pointer OLE passes is borrowed: the shell owns that reference and keeps using the object
    /// for the rest of the drag, so this method must leave the count exactly as it found it.
    /// </remarks>
    private static unsafe XPlatDataObject? Read(IntPtr dataObject)
    {
        if (dataObject == IntPtr.Zero)
        {
            return null;
        }

        var com = ComInterfaceMarshaller<IComDataObject>.ConvertToManaged((void*)dataObject);
        if (com is null)
        {
            return null;
        }

        XPlatDataObject data = new();

        try
        {
            var files = ReadFiles(com);
            if (files.Length > 0)
            {
                data.SetFiles(files);
            }

            if (ReadText(com) is { Length: > 0 } text)
            {
                data.SetText(text);
            }
        }
        finally
        {
            // The pointer this wrapper came from is borrowed: IDropTarget is handed it by OLE, which
            // owns that reference and keeps using the object after this callback returns. The
            // generated wrapper is a ComObject, which is not IDisposable, so this call is a no-op -
            // and it has to stay one. Releasing here (with FinalRelease, say) would drop OLE's
            // reference synchronously, in the middle of a drag that still has DragOver calls and a
            // Drop to come, which over-releases a live object.
            //
            // That is the real defect: ConvertToManaged takes ownership of whatever reference it is
            // given and does not AddRef, so the wrapper believes it owns OLE's reference and
            // eventually releases one it never took, at finalization. The fix is to give the
            // wrapper a reference of its own first - an AddRef on the borrowed pointer before
            // ConvertToManaged, once that is done - and it needs a real OLE drop to verify, so it is
            // not this change. Do not simply swap this line for FinalRelease.
            (com as IDisposable)?.Dispose();
        }

        // An empty object is still a drag: the source may be offering a format the library has no
        // model for, and the handlers are the ones that decide whether that is useful.
        return data;
    }

    /// <summary>Reads the dropped files, if the source offers any.</summary>
    private static string[] ReadFiles(IComDataObject com)
    {
        var format = FormatOf(Win32DropNativeMethods.CfHDrop);

        if (com.GetData(format, out var medium) != Win32DropNativeMethods.SOk)
        {
            return [];
        }

        try
        {
            if (medium.Tymed != Win32DropInterfaces.TymedHGlobal || medium.Handle == IntPtr.Zero)
            {
                return [];
            }

            var count = Win32DropNativeMethods.DragQueryFileW(medium.Handle, Win32DropInterfaces.DragQueryFileCount, null, 0);
            if (count == 0)
            {
                return [];
            }

            var files = new string[count];
            var found = 0;

            for (uint index = 0; index < count; index++)
            {
                var length = Win32DropNativeMethods.DragQueryFileW(medium.Handle, index, null, 0);
                if (length == 0)
                {
                    continue;
                }

                var buffer = new char[length + 1];
                if (Win32DropNativeMethods.DragQueryFileW(medium.Handle, index, buffer, (uint)buffer.Length) == 0)
                {
                    continue;
                }

                files[found++] = new string(buffer, 0, (int)length);
            }

            return found == count ? files : files[..found];
        }
        finally
        {
            Win32DropNativeMethods.ReleaseStgMedium(ref medium);
        }
    }

    /// <summary>Reads the dragged text, if the source offers any.</summary>
    private static string? ReadText(IComDataObject com)
    {
        var format = FormatOf(Win32DropNativeMethods.CfUnicodeText);

        if (com.GetData(format, out var medium) != Win32DropNativeMethods.SOk)
        {
            return null;
        }

        try
        {
            if (medium.Tymed != Win32DropInterfaces.TymedHGlobal || medium.Handle == IntPtr.Zero)
            {
                return null;
            }

            var pointer = Win32DropNativeMethods.GlobalLock(medium.Handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                Win32DropNativeMethods.GlobalUnlock(medium.Handle);
            }
        }
        finally
        {
            Win32DropNativeMethods.ReleaseStgMedium(ref medium);
        }
    }

    /// <summary>Builds the request for one clipboard format, rendered as global memory.</summary>
    private static FORMATETC FormatOf(uint clipboardFormat) =>
        new()
        {
            cfFormat = (ushort)clipboardFormat,
            dwAspect = Win32DropInterfaces.DvAspectContent,
            lindex = -1,
            tymed = Win32DropInterfaces.TymedHGlobal,
        };
}
