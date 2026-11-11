using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XPlatUI.Backend.Win32.Tests;

/// <summary>
/// The rules a window's message filters follow: the order they run in, what a filter that consumes a
/// message stops, and what happens to the set while a message is being delivered.
/// </summary>
/// <remarks>
/// These are the decisions a feature depends on and cannot see. A consuming filter that let the ones
/// after it run anyway would let two features both act on one message; a filter added mid-pass that
/// ran in that same pass would run against a window state it was not written for. Both are silent
/// until they are not, so they are pinned here rather than inferred from the backend.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32MessageFilterTests
{
    private static readonly IntPtr Window = new(0x1234);

    [TestMethod]
    public void FiltersRunInTheOrderTheyWereAdded()
    {
        var filters = new Win32MessageFilters();
        var seen = new List<string>();

        var first = filters.Add((hwnd, message, wParam, lParam) => { seen.Add("first"); return false; });
        var second = filters.Add((hwnd, message, wParam, lParam) => { seen.Add("second"); return false; });

        var handled = filters.Invoke(Window, 0x0400, IntPtr.Zero, IntPtr.Zero);

        Assert.IsFalse(handled, "no filter consumed the message");
        CollectionAssert.AreEqual(new[] { "first", "second" }, seen);

        first.Dispose();
        second.Dispose();
    }

    [TestMethod]
    public void AConsumingFilterStopsTheOnesAfterIt()
    {
        var filters = new Win32MessageFilters();
        var laterRan = false;

        var consumer = filters.Add((hwnd, message, wParam, lParam) => true);
        var later = filters.Add((hwnd, message, wParam, lParam) => { laterRan = true; return false; });

        Assert.IsTrue(filters.Invoke(Window, 0x0400, IntPtr.Zero, IntPtr.Zero), "the message was consumed");
        Assert.IsFalse(laterRan, "nothing after the consumer sees the message");

        consumer.Dispose();
        later.Dispose();
    }

    [TestMethod]
    public void TheMessageReachesTheFilterAsTheWindowReceivedIt()
    {
        var filters = new Win32MessageFilters();
        var seen = (IntPtr.Zero, 0u, IntPtr.Zero, IntPtr.Zero);

        var filter = filters.Add((hwnd, message, wParam, lParam) =>
        {
            seen = (hwnd, message, wParam, lParam);
            return false;
        });

        filters.Invoke(Window, 0x031F, new IntPtr(7), new IntPtr(9));

        Assert.AreEqual((Window, 0x031Fu, new IntPtr(7), new IntPtr(9)), seen);
        filter.Dispose();
    }

    [TestMethod]
    public void AFilterAddedWhileDeliveringStartsOnTheNextMessage()
    {
        var filters = new Win32MessageFilters();
        var nested = false;
        IDisposable? lease = null;

        var adder = filters.Add((hwnd, message, wParam, lParam) =>
        {
            // Adding from inside a pass is what a feature that reacts to a message would do; the
            // pass in progress must not pick it up.
            lease ??= filters.Add((_, _, _, _) => { nested = true; return false; });
            return false;
        });

        filters.Invoke(Window, 0x0400, IntPtr.Zero, IntPtr.Zero);
        Assert.IsFalse(nested, "a filter added during a pass does not run in that pass");

        filters.Invoke(Window, 0x0400, IntPtr.Zero, IntPtr.Zero);
        Assert.IsTrue(nested, "it runs from the next message on");

        adder.Dispose();
        lease?.Dispose();
    }

    [TestMethod]
    public void DisposingALeaseRemovesOnlyThatFilter()
    {
        var filters = new Win32MessageFilters();
        var survivingFilterRan = false;

        var lease = filters.Add((hwnd, message, wParam, lParam) => true);
        var surviving = filters.Add((hwnd, message, wParam, lParam) => { survivingFilterRan = true; return false; });

        lease.Dispose();

        Assert.IsFalse(filters.Invoke(Window, 0x0400, IntPtr.Zero, IntPtr.Zero), "the consumer is gone");
        Assert.IsTrue(survivingFilterRan, "the other filter is still attached");

        surviving.Dispose();
    }

    [TestMethod]
    public void DisposingALeaseTwiceRemovesNothingElse()
    {
        var filters = new Win32MessageFilters();
        var calls = 0;
        Win32MessageFilter filter = (hwnd, message, wParam, lParam) => { calls++; return false; };

        var lease = filters.Add(filter);
        lease.Dispose();
        lease.Dispose();

        filters.Invoke(Window, 0x0400, IntPtr.Zero, IntPtr.Zero);
        Assert.AreEqual(0, calls, "the filter was removed once and stays removed");
    }

    [TestMethod]
    public void RemovingAFilterThatIsNotAttachedDoesNothing()
    {
        var filters = new Win32MessageFilters();
        Assert.IsTrue(filters.IsEmpty, "a fresh window has nothing to run");

        filters.Remove((_, _, _, _) => false);

        Assert.IsTrue(filters.IsEmpty, "removing an unknown filter is not an error");
    }
}
