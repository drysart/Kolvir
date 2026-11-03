using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A radio button: a round glyph followed by a label. Exactly one radio button in a group is
/// checked at a time, and checking one clears the rest of its group.
/// </summary>
/// <remarks>
/// <para>
/// Grouping follows WinForms: radio buttons that share a <see cref="XPlatControl.Parent"/> are one
/// group, so two sets of choices on the same panel need a container each. Set
/// <see cref="GroupName"/> to separate them without adding a panel - radios only answer to other
/// radios whose <see cref="GroupName"/> matches, and the empty name is the default group.
/// </para>
/// <para>
/// The arrow keys move the selection between the members of a group, and take the focus with it.
/// </para>
/// </remarks>
public class XPlatRadioButton : XPlatToggleControl
{
    private string _groupName = string.Empty;

    public XPlatRadioButton()
    {
    }

    public XPlatRadioButton(string text)
        : this()
    {
        Text = text;
    }

    /// <summary>
    /// The name of the group this radio button belongs to. Only radio buttons with the same
    /// <see cref="XPlatControl.Parent"/> and the same name affect each other. Defaults to empty.
    /// </summary>
    public string GroupName
    {
        get => _groupName;
        set => _groupName = value ?? string.Empty;
    }

    protected override void PaintGlyph(SKCanvas canvas, SKRect glyph)
    {
        float radius = glyph.Width / 2f;
        bool marked = CheckState == XPlatCheckState.Checked;

        if (marked)
        {
            XPlatPainter.Fill(canvas, glyph, GlyphFillColor, radius);
        }
        else if (IsMouseOver && Enabled)
        {
            XPlatPainter.Fill(canvas, glyph, XPlatTheme.Secondary, radius);
        }

        XPlatPainter.DrawBorder(canvas, glyph, GlyphBorderColor, 1.5f, radius);

        if (!marked)
        {
            return;
        }

        var dot = XPlatGeometry.Deflate(glyph, new XPlatThickness(glyph.Width * 0.32f));
        if (dot.Width > 0f && dot.Height > 0f)
        {
            XPlatPainter.Fill(canvas, dot, GlyphMarkColor, dot.Width / 2f);
        }
    }

    /// <summary>A radio button is only ever checked, never toggled back to unchecked by a click.</summary>
    protected override XPlatCheckState NextCheckState() => XPlatCheckState.Checked;

    protected override void OnCheckStateChanged()
    {
        if (CheckState == XPlatCheckState.Checked)
        {
            UncheckGroup();
        }

        base.OnCheckStateChanged();
    }

    protected override void OnKeyDown(XPlatKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !Enabled)
        {
            return;
        }

        int step = e.Key switch
        {
            XPlatKey.Left or XPlatKey.Up => -1,
            XPlatKey.Right or XPlatKey.Down => 1,
            _ => 0,
        };

        if (step != 0)
        {
            e.Handled = MoveSelection(step);
        }
    }

    private void UncheckGroup()
    {
        var parent = Parent;
        if (parent is null)
        {
            return;
        }

        foreach (var sibling in parent.Controls)
        {
            if (!ReferenceEquals(sibling, this)
                && sibling is XPlatRadioButton radio
                && radio.CheckState == XPlatCheckState.Checked
                && radio.IsInGroup(_groupName))
            {
                radio.CheckState = XPlatCheckState.Unchecked;
            }
        }
    }

    /// <summary>Moves the selection and the focus one step around the group, wrapping at the ends.</summary>
    private bool MoveSelection(int step)
    {
        var members = GroupInParentOrder();
        if (members.Count < 2)
        {
            return false;
        }

        int index = members.IndexOf(this);
        if (index < 0)
        {
            return false;
        }

        var target = members[((index + step) % members.Count + members.Count) % members.Count];
        target.Checked = true;
        target.Focus();
        return true;
    }

    private List<XPlatRadioButton> GroupInParentOrder()
    {
        var members = new List<XPlatRadioButton>();
        var parent = Parent;

        if (parent is not null)
        {
            foreach (var sibling in parent.Controls)
            {
                if (sibling is XPlatRadioButton radio && radio.IsInGroup(_groupName))
                {
                    members.Add(radio);
                }
            }
        }

        if (members.Count == 0)
        {
            members.Add(this);
        }

        return members;
    }

    private bool IsInGroup(string groupName) =>
        string.Equals(_groupName, groupName, StringComparison.Ordinal);
}
