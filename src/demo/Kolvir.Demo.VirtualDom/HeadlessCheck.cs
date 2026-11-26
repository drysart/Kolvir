using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;
using Kolvir.Feature.VirtualDom;
using Kolvir.Undo;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// Runs the sample's real component on the headless backend and drives the open, close and rename
/// flows through the real controls and real frames.
/// </summary>
/// <remarks>
/// This exists because the interesting flows are otherwise only visible to a person with a pointer.
/// It creates the same <see cref="DocumentApplication"/>, finds buttons by their control id, clicks
/// them through <see cref="UiHost"/>, closes a document through the backend's close path, and paints
/// the frames that make the renders happen. The output is the model's own readout beside the actual
/// window titles.
/// </remarks>
internal static class HeadlessCheck
{
    internal static int Run()
    {
        var options = new ApplicationOptions
        {
            ApplicationName = "Kolvir Virtual DOM check",
#if WINDOWS
            AttachConsole = true,
#endif
        };

        using var application = new Application(new HeadlessApplicationBackend(), options);
        using var mount = VirtualDomRuntime.Mount(application, new DocumentApplication());

        var list = Find(application, "Kolvir - Documents");

        Check(application.Windows.Count == 2, "the first render should open the list and Document 1");
        Check(Readout(list).Contains("1 document window(s)", StringComparison.Ordinal), "the readout should start with one document");
        Console.WriteLine($"initial: {Titles(application)}");

        Click(list, "open");
        Paint(list);

        Check(application.Windows.Count == 3, "opening a document should add its window");
        var second = Find(application, "Document 2 - document");
        Check(Readout(list).Contains("2 document window(s)", StringComparison.Ordinal), "the readout should follow the model");
        Console.WriteLine($"after open: {Titles(application)}");

        second.Backend.Close();

        Check(!second.IsClosed, "a native close should be refused until a render omits the window");
        Check(application.Windows.Count == 3, "and the window should still be open");

        Paint(list);

        Check(second.IsClosed, "the render that omitted Document 2 should close its window");
        Check(application.Windows.Count == 2, "and only Document 1 should remain");
        Check(Readout(list).Contains("1 document window(s)", StringComparison.Ordinal), "the readout should follow the removal");
        Console.WriteLine($"after native close: {Titles(application)}");

        var first = Find(application, "Document 1 - document");
        Click(first, "rename");
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");

        var title = first.TryGetControlById("title", out var control)
            ? control.Text
            : throw new InvalidOperationException("The document page should have a title label.");

        Check(title == "Document 1 renamed 1", $"the page should show the renamed title, was '{title}'");
        Check(Readout(list).Contains("Document 1 renamed 1", StringComparison.Ordinal), "the readout should show the renamed title");
        Console.WriteLine($"after rename: {Titles(application)}");

        // The table on the page is where the sample proves extension members end to end. Every position
        // below was set by an extension property on a node - the page never names
        // TableLayoutPanelExtensions - so the control's own record of its cell is the assertion.
        var table = FindControl(first, "cells") as TableLayoutPanel
            ?? throw new InvalidOperationException("The document page should have a table.");

        var start = Describe(Cell(table, "shift"));
        var popupStart = Describe(Cell(table, "popup"));

        Check(Cell(table, "cell-title") == (0, 0), "the title cell should start at column 0, row 0");
        Check(Span(table, "cell-title") == (2, 1), "the title should span both columns");
        Check(Cell(table, "shift") == (0, 1), "the move button should start in row 1");
        Check(Cell(table, "popup") == (1, 1), "the popup button should be at column 1, row 1");
        Check(Span(table, "popup") == (1, 2), "the popup button should span both rows");
        Console.WriteLine($"table: move button at {start}, popup at {popupStart}");

        // The re-render changes one row, so the control arrives at the patch with a different cell than
        // the one it has. That is the path an extension property could quietly get wrong: setting it on
        // a first apply is a setter call, but moving it is a detach from one cell and an attach to
        // another, and only the patch does that.
        var rowsBeforeShift = table.RowStyles[0];

        Click(first, "shift");
        Paint(first);

        first = Find(application, "Document 1 renamed 1 - document");

        var moved = Describe(Cell(table, "shift"));

        Check(Cell(table, "shift") == (0, 2), $"the patch should have moved the button to row 2, it is at {moved}");
        Check(Cell(table, "cell-title") == (0, 0), "an unchanged cell position should survive the patch");
        Check(Span(table, "popup") == (1, 2), "and so should an unchanged span");
        Console.WriteLine($"table: after the patch the move button is at {moved}");

        // That render also rebuilt the row sequence, and it said exactly what the table already had: the
        // same styles, by value, in the same order. A patch that replaced them anyway would be a table
        // rebuilt on every render, so the instance is the assertion - it is the same style object.
        Check(
            ReferenceEquals(rowsBeforeShift, table.RowStyles[0]),
            "a render that produced the same rows should have left the table's styles alone");

        // The styles themselves are settable on the node, so the page declares a whole sequence and the
        // control compares it by value. What the control holds is the assertion: counts, kinds, values,
        // and one layout pass each time the sequence really changed.
        Check(
            table.RowCount == 2 && table.ColumnCount == 2,
            $"the node's styles should have been applied, the table has {table.RowCount} row(s) and {table.ColumnCount} column(s)");
        Check(
            table.RowStyles[0].SizeType == SizeType.Absolute && table.RowStyles[0].Value == 24f,
            $"the first row should be 24 absolute pixels, it is {table.RowStyles[0].SizeType} {table.RowStyles[0].Value}");
        Check(
            table.ColumnStyles[0].SizeType == SizeType.Absolute && table.ColumnStyles[0].Value == 160f,
            $"the first column should be 160 absolute pixels, it is {table.ColumnStyles[0].SizeType} {table.ColumnStyles[0].Value}");
        Console.WriteLine($"styles: {table.RowCount}x{table.ColumnCount}, first row 24px, first column 160px");

        var shiftTop = FindControl(table.Window!, "shift").Bounds.Top;

        // A changed value.
        Click(first, "reshape");
        Paint(first);

        Check(table.RowCount == 2, $"the changed sequence should still have two rows, it has {table.RowCount}");
        Check(
            table.RowStyles[0].Value == 40f,
            $"the first row should have been replaced with 40 pixels, it is {table.RowStyles[0].Value}");
        Check(
            FindControl(table.Window!, "shift").Bounds.Top != shiftTop,
            "the changed row height should have been laid out, and the button above did not move");
        Console.WriteLine("styles: the changed row height was applied and laid out");

        // Reordered.
        Click(first, "reshape");
        Paint(first);

        Check(table.RowCount == 2, $"the reordered sequence should still have two rows, it has {table.RowCount}");
        Check(
            table.RowStyles[0].SizeType == SizeType.Percent,
            $"the reordered sequence should put the percent row first, it is {table.RowStyles[0].SizeType}");
        Console.WriteLine("styles: the reordered sequence was applied in order");

        // Shortened.
        Click(first, "reshape");
        Paint(first);

        Check(table.RowCount == 1, $"the shortened sequence should have one row, it has {table.RowCount}");
        Console.WriteLine("styles: the shortened sequence removed a row");

        // Empty.
        Click(first, "reshape");
        Paint(first);

        Check(table.RowCount == 0, $"the empty sequence should have no declared rows, it has {table.RowCount}");
        Console.WriteLine("styles: the empty sequence removed every row");

        // And back to the one the page declares, which is the sequence every render produces.
        Click(first, "reshape");
        Paint(first);

        Check(
            table.RowCount == 2 && table.RowStyles[0].Value == 24f,
            "the declared rows should be back after a full cycle");
        Console.WriteLine("styles: the cycle returned to the declared rows");

        // The other half: a handler pair the control does not declare, and one that lives inside an
        // extension(TextBox) block. Nothing in the page calls AddUndoStateChangedHandler - the node's
        // UndoStateChanged property did - so the readout moving is the proof that a node can attach
        // one exactly as it attaches a native event.
        Click(first, "notes");
        first.Host.ProcessTextInput("a");
        Paint(first);

        var activity = FindControl(first, "activity").Text;
        var notes = (TextBox)FindControl(first, "notes");

        Check(activity.StartsWith("undo: changed", StringComparison.Ordinal), $"the node's UndoStateChanged handler should have run, the readout says '{activity}'");
        Check(notes.UndoCount > 0, "the box should have recorded the keystroke, and its history is the extension property UndoEnabled");
        Console.WriteLine($"undo: {activity}, {notes.UndoCount} entry(ies) in the box's history");

        Console.WriteLine($"readout: {Readout(list)}");

        var homeLocation = first.Location;
        var homeSize = first.ClientSize;
        var userLocation = new SKPoint(900f, 900f);

        // A user move goes through the backend, so the window's own notification is what the model
        // sees - exactly the path a drag takes on a desktop.
        first.Backend.Location = userLocation;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Location == userLocation, "the render should leave a window where the user moved it");
        Check(Readout(list).Contains("900,900", StringComparison.Ordinal), "the model should see the user's move");
        Console.WriteLine("after move: the model tracks the position and the window stayed put");

        var userSize = new SKSize(620f, 420f);
        first.Backend.ClientSize = userSize;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.ClientSize == userSize, "the render should leave a window at the size the user gave it");
        Check(first.Backend.ClientSize == userSize, "the resize should reach the backend");
        Check(Readout(list).Contains("620x420", StringComparison.Ordinal), "the model should see the user's resize");
        Check(first.Location == userLocation, "resizing should not move the window");
        Console.WriteLine("after resize: the model tracks the size and the window stayed put");

        first.Backend.State = WindowState.Minimized;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Backend.State == WindowState.Minimized, "the user's minimize should survive the render");
        Check(Readout(list).Contains("Minimized", StringComparison.Ordinal), "the model should see the state change");
        Check(first.Location == userLocation && first.ClientSize == userSize, "minimizing should not change the geometry");

        first.Backend.State = WindowState.Normal;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Backend.State == WindowState.Normal, "the user's restore should survive the render");
        Check(Readout(list).Contains("Normal", StringComparison.Ordinal), "the model should see the restore");
        Console.WriteLine("after minimize and restore: the model tracked both states");

        Click(list, "tidy");
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Location == homeLocation, "Tidy should put the document back where the model assigned it");
        Check(first.ClientSize == homeSize, "Tidy should put the document back at the size the model assigned");
        Check(first.Backend.State == WindowState.Normal, "Tidy should restore the model's window state");
        Check(Readout(list).Contains("160,60 480x300 Normal", StringComparison.Ordinal), "the readout should follow the tidy");
        Console.WriteLine("after tidy: the moved document returned to its assigned position and size");

        Click(list, "quit");

        Check(application.Windows.Count == 0, "Quit should close every window");
        Console.WriteLine("after quit: no open windows");
        Console.WriteLine("check: OK");

        return 0;
    }

    private static Window Find(Application application, string title) =>
        application.Windows.FirstOrDefault(window =>
            !window.IsClosed && window.Title.Contains(title, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No open window contains '{title}'.");

    private static string Readout(Window window) =>
        window.TryGetControlById("readout", out var control)
            ? control.Text
            : throw new InvalidOperationException("The list window should have a readout label.");

    private static Control FindControl(Window window, string id) =>
        window.TryGetControlById(id, out var control)
            ? control
            : throw new InvalidOperationException($"The window does not contain a control with id '{id}'.");

    /// <summary>The cell a control reports for itself - the table's view of what the node attached.</summary>
    private static (int Column, int Row) Cell(TableLayoutPanel table, string id) =>
        table.GetCellPosition(FindControl(table.Window!, id));

    private static (int ColumnSpan, int RowSpan) Span(TableLayoutPanel table, string id) =>
        table.GetCellSpan(FindControl(table.Window!, id));

    private static string Describe((int Column, int Row) cell) => $"column {cell.Column}, row {cell.Row}";

    private static string Titles(Application application) =>
        string.Join(", ", application.Windows.Where(window => !window.IsClosed).Select(window => window.Title));

    private static void Click(Window window, string id)
    {
        window.Host.PerformLayout();

        if (!window.TryGetControlById(id, out var control))
        {
            throw new InvalidOperationException($"The window does not contain a control with id '{id}'.");
        }

        var origin = control.GetPositionInHost();
        var point = new SKPoint(origin.X + (control.Width / 2f), origin.Y + (control.Height / 2f));

        window.Host.ProcessMouseMove(point);
        window.Host.ProcessMouseDown(point, MouseButtons.Left, Modifiers.None);
        window.Host.ProcessMouseUp(point, MouseButtons.Left, Modifiers.None);
    }

    private static void Paint(Window window)
    {
        window.Host.PerformLayout();

        using var surface = SKSurface.Create(new SKImageInfo(
            Math.Max(1, (int)MathF.Round(window.ClientSize.Width)),
            Math.Max(1, (int)MathF.Round(window.ClientSize.Height))));

        window.Host.Paint(surface.Canvas);

        // A close the frame asked for is deferred until no paint is in progress. The headless
        // backend has no message loop to turn, so the harness turns it explicitly: this is the
        // turn that would follow the frame on a desktop.
        if (window.Application.Backend is HeadlessApplicationBackend backend)
        {
            backend.DrainPending();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"check failed: {message}");
        }
    }
}
