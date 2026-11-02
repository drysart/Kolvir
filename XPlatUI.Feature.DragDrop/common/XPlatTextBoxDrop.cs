using System.Text;

namespace XPlatUI.DragDrop;

/// <summary>
/// The handling the library itself gives an <see cref="XPlatTextBox"/>: text dropped on the field is
/// inserted where it was aimed, and a drag that carries no usable text is refused so it can land on
/// an ancestor that knows what to do with it.
/// </summary>
/// <remarks>
/// The base class carries no drag and drop members, so a field's own handling lives here rather than
/// in an override. It runs after the application's handlers, which is what leaves them the last word
/// on <see cref="XPlatDragEventArgs.Effect"/>.
/// </remarks>
internal static class XPlatTextBoxDrop
{
    /// <summary>Reports whether the field would take the drag, and what a drop would do.</summary>
    /// <param name="box">The field the drag is over.</param>
    /// <param name="e">The event data.</param>
    internal static void DragOver(XPlatTextBox box, XPlatDragEventArgs e) =>
        e.Effect = Accepts(box, e.Data) ? XPlatDragDropEffects.Copy : XPlatDragDropEffects.None;

    /// <summary>
    /// Inserts the dropped text where the pointer is, so a drop lands where it was aimed rather than
    /// at wherever the caret happened to be.
    /// </summary>
    /// <param name="box">The field the drop landed on.</param>
    /// <param name="e">The event data.</param>
    internal static void DragDrop(XPlatTextBox box, XPlatDragEventArgs e)
    {
        if (!Accepts(box, e.Data))
        {
            e.Effect = XPlatDragDropEffects.None;
            return;
        }

        var text = Sanitize(e.Data.GetText());
        if (text.Length == 0)
        {
            e.Effect = XPlatDragDropEffects.None;
            return;
        }

        box.Focus();
        box.Select((int)box.GetSelectionOffset(e.Location), 0);
        box.InsertText(text);
        e.Effect = XPlatDragDropEffects.Copy;
    }

    /// <summary>True when the drag carries text this field could take.</summary>
    private static bool Accepts(XPlatTextBox box, XPlatDataObject data) =>
        !box.ReadOnly && Sanitize(data.GetText()).Length > 0;

    /// <summary>
    /// Strips control characters (including line breaks) so the field stays single-line. It mirrors
    /// the rule the field applies to typed input.
    /// </summary>
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
