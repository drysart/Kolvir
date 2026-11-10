using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The release contract for the COM wrappers the file dialogs hold. The dialogs themselves need a
/// user; what has to hold on every runtime - that the reference is really released, and that an
/// object which is not a wrapper does not throw - does not.
/// </summary>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32DialogServiceTests
{
    /// <summary>A wrapper of the shape the helper falls back to when it is not a <c>ComObject</c>.</summary>
    private sealed class FakeComWrapper : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>
    /// A wrapper made by <see cref="Win32DialogService.Wrap{T}"/> is a unique instance, so
    /// <c>ComObject.FinalRelease</c> really releases it - the wrapper refuses to hand out its vtable
    /// afterwards. A cached wrapper from <c>ComInterfaceMarshaller&lt;T&gt;</c> would fail this,
    /// because <c>FinalRelease</c> is defined to do nothing unless the instance is unique.
    /// </summary>
    [TestMethod]
    public void Release_ReleasesAUniqueWrapper()
    {
        object? wrapper = null;
        Exception? failure = null;

        // Created on an STA thread, as the backend's UI thread is. Nothing shows the dialog, so no
        // desktop and no user are needed.
        var apartment = new Thread(() =>
        {
            try
            {
                var hr = NativeMethods.CoCreateInstance(
                    Win32ComInterfaces.FileOpenDialogClass,
                    IntPtr.Zero,
                    NativeMethods.ClsCtxInprocServer,
                    typeof(IFileDialog).GUID,
                    out var unknown);

                if (hr == 0)
                {
                    wrapper = Win32DialogService.Wrap<IFileDialog>(unknown);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        apartment.SetApartmentState(ApartmentState.STA);
        apartment.Start();
        apartment.Join();

        Assert.IsNull(failure, failure?.ToString());

        if (wrapper is not { } created)
        {
            Assert.Inconclusive("The shell's file dialog object could not be created in this environment.");
            return;
        }

        var dialog = (IFileDialog)created;

        // The wrapper is usable while the reference it owns is held...
        dialog.GetOptions(out _);

        Win32DialogService.Release(created);

        // ...and a released unique wrapper will not produce a vtable again, which is what proves the
        // reference was really dropped rather than left to finalization.
        Assert.ThrowsExactly<ObjectDisposedException>(() => dialog.GetOptions(out _));
    }

    /// <summary>
    /// A wrapper of another shape is released if it can be. Nothing on the dialog path is one today;
    /// this pins the fallback arm so a future wrapper cannot quietly become unreleasable.
    /// </summary>
    [TestMethod]
    public void Release_DisposesAWrapperThatImplementsIDisposable()
    {
        var wrapper = new FakeComWrapper();

        Win32DialogService.Release(wrapper);

        Assert.AreEqual(1, wrapper.DisposeCount, "The wrapper must be released, not left to finalization.");
    }

    /// <summary>
    /// An object that is not a wrapper must not make the service throw. NativeAOT's
    /// <c>NotSupportedException</c> from <c>Marshal.FinalReleaseComObject</c> is what turned this into
    /// a process exit, so "returns quietly" is the contract rather than "throws something catchable".
    /// </summary>
    [TestMethod]
    public void Release_DoesNothingForAnObjectThatIsNotWrappable()
    {
        Win32DialogService.Release(new object());
    }
}
