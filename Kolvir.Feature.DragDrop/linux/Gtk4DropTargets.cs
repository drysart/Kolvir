using SkiaSharp;
using Kolvir.Backend.Gtk4;

namespace Kolvir.DragDrop;

/// <summary>
/// The GTK4 half of drag and drop: the drop target controllers a window's drawing area carries, and
/// the signal handlers that route what they report into the control tree.
/// </summary>
/// <remarks>
/// <para>
/// One target per kind of drag, because the binding offers no way to add a second type to a target.
/// Both are declared as text: a file list offers itself as the paths of the files it carries, and
/// asking GTK for a <c>GdkFileList</c> instead fails, because a file drag also offers the portal
/// transfer token that GDK then prefers, and that token can only be turned into files by a portal
/// round trip that the synchronous read cannot wait for. The text form reads back immediately, one
/// path per line.
/// </para>
/// <para>
/// Both targets match a file drag, so each accepts only the drags it is meant for: the file target
/// takes the drags that carry files and the text target turns those away rather than read a file
/// drop as the paths of the files it carries.
/// </para>
/// <para>
/// Preloading makes GTK fetch the data as soon as the drag is accepted, so the payload is in hand
/// before the drag ends. The fetch is asynchronous: <c>accept</c> and <c>enter</c> run before it
/// finishes and see no value at all, and the first <c>motion</c> is the earliest signal that can
/// read the payload.
/// </para>
/// </remarks>
internal sealed class Gtk4DropTargets(Gtk.DrawingArea area, DragDropRouter router) : IDisposable
{
    /// <summary>The effects a drop target offers the drag source.</summary>
    private const DragDropEffects DragEffects =
        DragDropEffects.Copy | DragDropEffects.Move;

    private readonly Gtk.DrawingArea _area = area;
    private readonly DragDropRouter _router = router;

    private Gtk.DropTarget? _fileDropTarget;
    private Gtk.DropTarget? _textDropTarget;
    private DataObject? _dragData;

    /// <summary>Starts accepting drags, and does nothing if that has already happened.</summary>
    internal void Attach()
    {
        if (_fileDropTarget is not null)
        {
            return;
        }

        _fileDropTarget = AddDropTarget();
        _textDropTarget = AddDropTarget();
    }

    /// <summary>Stops accepting drags, and forgets any payload the last one left behind.</summary>
    public void Dispose()
    {
        if (_fileDropTarget is not null)
        {
            _area.RemoveController(_fileDropTarget);
            _fileDropTarget = null;
        }

        if (_textDropTarget is not null)
        {
            _area.RemoveController(_textDropTarget);
            _textDropTarget = null;
        }

        _dragData = null;
    }

    /// <summary>Adds a drop target for one data type, and hooks the signals a drag raises on it.</summary>
    private Gtk.DropTarget AddDropTarget()
    {
        var target = Gtk.DropTarget.New(GObject.Type.String, Gdk.DragAction.Copy | Gdk.DragAction.Move);
        target.SetPreload(true);
        target.OnAccept += OnDragAccept;
        target.OnEnter += OnDragEnter;
        target.OnMotion += OnDragMotion;
        target.OnLeave += OnDragLeaveSignal;
        target.OnDrop += OnDragDrop;
        _area.AddController(target);
        return target;
    }

    /// <summary>Decides whether a drag may be dropped.</summary>
    /// <remarks>
    /// The payload is not readable yet - the preload fetch is still running - so the formats on
    /// offer decide which target the drag belongs to. A drag that carries files goes to the file
    /// target and one that does not goes to the text target, which keeps a file drop from being
    /// read as the paths of the files it carries.
    /// </remarks>
    private bool OnDragAccept(Gtk.DropTarget sender, Gtk.DropTarget.AcceptSignalArgs args)
    {
        var formats = args.Drop?.GetFormats();
        if (formats is null)
        {
            return true;
        }

        var files = formats.ContainGtype(Gdk.FileList.GetGType()) ||
            formats.ContainMimeType("text/uri-list") ||
            formats.ContainMimeType("application/vnd.portal.filetransfer");

        return ReferenceEquals(sender, _fileDropTarget) ? files : !files;
    }

    /// <summary>Reports the drag crossing into the view, and what a drop there would do.</summary>
    /// <remarks>
    /// The payload usually arrives too late for this signal, so an unread drag answers with an
    /// optimistic copy. Answering with <see cref="Gdk.DragAction.None"/> would cancel the drag
    /// outright, and the first motion corrects the answer once the data is in hand.
    /// </remarks>
    private Gdk.DragAction OnDragEnter(Gtk.DropTarget sender, Gtk.DropTarget.EnterSignalArgs args)
    {
        try
        {
            _dragData ??= ReadDropValue(sender, sender.GetValue());
            if (_dragData is null)
            {
                return Gdk.DragAction.Copy;
            }

            var point = new SKPoint((float)args.X, (float)args.Y);
            return ToAction(_router.DragEnter(point, _dragData, DragEffects, CurrentModifiers(sender)));
        }
        catch (Exception ex)
        {
            Application.HandleNativeBoundary(ex);
            return Gdk.DragAction.None;
        }
    }

    /// <summary>Reports the drag moving over the view, and what a drop there would do.</summary>
    private Gdk.DragAction OnDragMotion(Gtk.DropTarget sender, Gtk.DropTarget.MotionSignalArgs args)
    {
        try
        {
            _dragData ??= ReadDropValue(sender, sender.GetValue());
            if (_dragData is null)
            {
                return Gdk.DragAction.Copy;
            }

            var point = new SKPoint((float)args.X, (float)args.Y);
            return ToAction(_router.DragOver(point, _dragData, DragEffects, CurrentModifiers(sender)));
        }
        catch (Exception ex)
        {
            Application.HandleNativeBoundary(ex);
            return Gdk.DragAction.None;
        }
    }

    /// <summary>Reports the drag leaving the view without being dropped.</summary>
    private void OnDragLeaveSignal(Gtk.DropTarget sender, EventArgs args)
    {
        if (_dragData is null)
        {
            return;
        }

        _dragData = null;
        _router.DragLeave();
    }

    /// <summary>Routes the drop into the control tree.</summary>
    private bool OnDragDrop(Gtk.DropTarget sender, Gtk.DropTarget.DropSignalArgs args)
    {
        try
        {
            var data = ReadDropValue(sender, args.Value) ?? _dragData;
            _dragData = null;

            if (data is null)
            {
                return false;
            }

            var point = new SKPoint((float)args.X, (float)args.Y);
            return _router.DragDrop(point, data, DragEffects, CurrentModifiers(sender)) !=
                DragDropEffects.None;
        }
        catch (Exception ex)
        {
            Application.HandleNativeBoundary(ex);
            _dragData = null;
            return false;
        }
    }

    /// <summary>Turns the value GTK negotiated for a drag into the library's data object.</summary>
    /// <remarks>
    /// A file drag arrives as text, one path per line. A drag that offers files but names none that
    /// are local - a web address, say - reads back as its text.
    /// </remarks>
    private DataObject? ReadDropValue(Gtk.DropTarget sender, GObject.Value? value)
    {
        if (value is null || value.GetString() is not { Length: > 0 } text)
        {
            return null;
        }

        if (ReferenceEquals(sender, _fileDropTarget) && ParsePaths(text) is { Length: > 0 } paths)
        {
            DataObject fromFiles = new();
            fromFiles.SetFiles(paths);
            return fromFiles;
        }

        return DataObject.FromText(text);
    }

    /// <summary>Reads the local paths out of the text form of a file drag, one per line.</summary>
    /// <remarks>
    /// The text form holds a plain path per line, but a source that only offers
    /// <c>text/uri-list</c> hands over <c>file:</c> URIs instead, so both are accepted.
    /// </remarks>
    private static string[] ParsePaths(string text)
    {
        List<string> paths = [];
        foreach (var line in text.Split('\n'))
        {
            var entry = line.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri))
            {
                paths.Add(entry);
            }
            else if (uri.IsFile)
            {
                paths.Add(uri.LocalPath);
            }
        }

        return [.. paths];
    }

    /// <summary>Reads the modifier keys that are down right now, as the drag controller sees them.</summary>
    private static Modifiers CurrentModifiers(Gtk.DropTarget sender)
    {
        var state = sender.GetCurrentEventState();
        var modifiers = Modifiers.None;

        if ((state & Gdk.ModifierType.ShiftMask) != 0)
        {
            modifiers |= Modifiers.Shift;
        }

        if ((state & Gdk.ModifierType.ControlMask) != 0)
        {
            modifiers |= Modifiers.Control;
        }

        if ((state & Gdk.ModifierType.AltMask) != 0)
        {
            modifiers |= Modifiers.Alt;
        }

        return modifiers;
    }

    /// <summary>Translates the effect the UI accepted into the action GTK shows for it.</summary>
    private static Gdk.DragAction ToAction(DragDropEffects effect)
    {
        if (effect.HasFlag(DragDropEffects.Move))
        {
            return Gdk.DragAction.Move;
        }

        return effect.HasFlag(DragDropEffects.Copy) ? Gdk.DragAction.Copy : Gdk.DragAction.None;
    }
}
