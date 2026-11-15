using System.Collections;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// One node of an <see cref="TreeView"/>: a label, the children under it and the state of its
/// own expander and check box.
/// </summary>
/// <remarks>
/// A node belongs to a tree only once it is put in a <see cref="TreeView.TreeNodeCollection"/>
/// that is itself reachable from <see cref="TreeView.Nodes"/>; a node that is in no tree can
/// still be built and populated, it just cannot repaint anything.
/// </remarks>
public class TreeNode
{
    private readonly List<TreeNode> _nodes = [];
    private readonly TreeView.TreeNodeCollection _collection;

    private string _text = string.Empty;
    private bool _expanded;
    private CheckState _checkState;

    /// <summary>Creates a node with an empty label and no children.</summary>
    public TreeNode()
    {
        _collection = new TreeView.TreeNodeCollection(this, _nodes);
    }

    /// <summary>Creates a node with a label and no children.</summary>
    /// <param name="text">The label; <see langword="null"/> is read as an empty string.</param>
    public TreeNode(string text)
        : this()
    {
        _text = text ?? string.Empty;
    }

    /// <summary>The node's label, which is also what type-ahead matches against.</summary>
    public string Text
    {
        get => _text;
        set
        {
            var text = value ?? string.Empty;
            if (_text == text)
            {
                return;
            }

            _text = text;
            Owner?.RefreshItems();
        }
    }

    /// <summary>Whatever the application wants to hang off the node.</summary>
    public object? Tag { get; set; }

    /// <summary>The nodes directly under this one, in the order they are painted.</summary>
    public TreeView.TreeNodeCollection Nodes => _collection;

    /// <summary>The node this one hangs off, or <see langword="null"/> when it is a root.</summary>
    public TreeNode? Parent { get; internal set; }

    /// <summary>The tree this node belongs to, or <see langword="null"/> when it is in none.</summary>
    public TreeView? Owner { get; internal set; }

    /// <summary>How deep the node is, which is how many ancestors it has.</summary>
    public int Level
    {
        get
        {
            int level = 0;
            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                level++;
            }

            return level;
        }
    }

    /// <summary>True when the node has at least one child, which is when it gets an expander glyph.</summary>
    public bool HasChildren => _nodes.Count > 0;

    /// <summary>The first child, or <see langword="null"/>.</summary>
    public TreeNode? FirstNode => _nodes.Count > 0 ? _nodes[0] : null;

    /// <summary>The last child, or <see langword="null"/>.</summary>
    public TreeNode? LastNode => _nodes.Count > 0 ? _nodes[^1] : null;

    /// <summary>Whether the node's children are shown. A node with no children can still be expanded.</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            SetExpandedCore(value);
            Owner?.OnStructureChanged();
        }
    }

    /// <summary>True when the node itself would be in the flattened list, which needs every ancestor to be expanded.</summary>
    public bool IsVisible
    {
        get
        {
            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                if (!parent._expanded)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Whether the node is part of the tree's selection.</summary>
    public bool Selected
    {
        get
        {
            if (Owner is not { } owner)
            {
                return false;
            }

            int index = owner.IndexOf(this);
            return index >= 0 && owner.GetSelected(index);
        }
        set
        {
            if (Owner is not { } owner)
            {
                return;
            }

            int index = owner.IndexOf(this);
            if (index >= 0)
            {
                owner.SetSelected(index, value);
            }
        }
    }

    /// <summary>
    /// Whether the node is checked, with the state of its children taken into account. Assigning a
    /// state pushes it down to every descendant; the ancestors above it are then re-derived, so a
    /// parent of a partly checked set reads <see cref="CheckState.Indeterminate"/>.
    /// </summary>
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (_checkState == value)
            {
                return;
            }

            SetCheckStateCore(value);

            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                parent.DeriveCheckState();
            }

            Owner?.RefreshItems();
        }
    }

    /// <summary>The check state as a plain flag: true only when the node is fully checked.</summary>
    public bool Checked
    {
        get => _checkState == CheckState.Checked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    /// <summary>Shows the node's children.</summary>
    public void Expand() => IsExpanded = true;

    /// <summary>Hides the node's children.</summary>
    public void Collapse() => IsExpanded = false;

    /// <summary>Flips <see cref="IsExpanded"/>.</summary>
    public void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>Expands the ancestors and scrolls the tree so that the node is in the viewport.</summary>
    public void EnsureVisible()
    {
        if (Owner is not { } owner)
        {
            return;
        }

        bool changed = false;
        for (var parent = Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent._expanded)
            {
                parent.SetExpandedCore(true);
                changed = true;
            }
        }

        if (changed)
        {
            owner.OnStructureChanged();
        }

        owner.EnsureVisible(owner.IndexOf(this));
    }

    /// <summary>Takes the node out of its parent's children, or out of the tree when it is a root.</summary>
    public void Remove()
    {
        if (Parent is { } parent)
        {
            parent.Nodes.Remove(this);
        }
        else
        {
            Owner?.Nodes.Remove(this);
        }
    }

    /// <summary>Changes the expansion without telling the tree, for callers that notify once themselves.</summary>
    internal void SetExpandedCore(bool expanded) => _expanded = expanded;

    /// <summary>Points the node and everything under it at a tree, or at nothing.</summary>
    internal void Attach(TreeView? owner)
    {
        Owner = owner;

        foreach (var child in _nodes)
        {
            child.Attach(owner);
        }
    }

    private void SetCheckStateCore(CheckState state)
    {
        _checkState = state;

        foreach (var child in _nodes)
        {
            child.SetCheckStateCore(state);
        }
    }

    private void DeriveCheckState()
    {
        if (_nodes.Count == 0)
        {
            return;
        }

        var state = _nodes[0]._checkState;
        foreach (var child in _nodes)
        {
            if (child._checkState != state)
            {
                state = CheckState.Indeterminate;
                break;
            }
        }

        _checkState = state;
    }
}

/// <summary>
/// A tree of nodes shown as indented rows, with an expander glyph per parent and an optional check
/// box per node. It is the closest thing this library has to <c>System.Windows.Forms.TreeView</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rows are virtualized the same way the other lists are: <see cref="VirtualList.ItemCount"/>
/// is the number of <em>visible</em> nodes - the flattened walk of the expanded ones - so only the
/// rows inside the viewport are measured, painted or invalidated no matter how large the tree is.
/// Collapsing a node therefore changes the row count, which is why the control remaps the selection
/// by node rather than by index whenever the shape of the tree changes.
/// </para>
/// <para>
/// Every row is <see cref="VirtualList.ItemHeight"/> tall, so unlike WinForms a node cannot be
/// taller than its siblings, and nodes carry no image - the expander is the only glyph the control
/// draws for a node besides its check box.
/// </para>
/// </remarks>
public class TreeView : VirtualList
{
    /// <summary>The size of the expander and check box squares.</summary>
    private const float GlyphSize = 13f;

    private const float GlyphGap = 5f;

    private readonly List<TreeNode> _roots = [];
    private readonly List<TreeNode> _visible = [];
    private readonly TreeNodeCollection _rootCollection;

    private bool _visibleDirty = true;
    private float _indent = 18f;
    private bool _checkBoxes;
    private Action<object>? _afterCheck;

    /// <summary>Creates a tree with no root nodes.</summary>
    public TreeView()
    {
        _rootCollection = new TreeNodeCollection(this, _roots);
    }

    /// <summary>The root nodes of the tree, in the order they are painted.</summary>
    public TreeNodeCollection Nodes => _rootCollection;

    /// <summary>How far each level is indented. It is also the width of the expander and check box columns.</summary>
    public float Indent
    {
        get => _indent;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (_indent == value)
            {
                return;
            }

            _indent = value;
            RefreshItems();
        }
    }

    /// <summary>Whether each row shows a check box. Turning them on does not change any node's state.</summary>
    public bool CheckBoxes
    {
        get => _checkBoxes;
        set
        {
            if (_checkBoxes == value)
            {
                return;
            }

            _checkBoxes = value;
            RefreshItems();
        }
    }

    /// <summary>The node at <see cref="VirtualList.SelectedIndex"/>, or <see langword="null"/>.</summary>
    public TreeNode? SelectedNode
    {
        get
        {
            int index = SelectedIndex;
            return index >= 0 && index < ItemCount ? VisibleNodes[index] : null;
        }
        set
        {
            int index = value is null ? -1 : IndexOf(value);
            SelectedIndex = index >= 0 ? index : -1;
        }
    }

    /// <summary>Adds a handler called after a click changes a node's check state. The node is the argument.</summary>
    public IDisposable AddAfterCheckHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _afterCheck += handler;
        return new HandlerToken<Action<object>>(RemoveAfterCheckHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddAfterCheckHandler"/> added.</summary>
    public void RemoveAfterCheckHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _afterCheck -= handler;
    }

    /// <summary>Expands every node in the tree.</summary>
    public void ExpandAll() => SetExpandedRecursive(Nodes, true);

    /// <summary>Collapses every node in the tree.</summary>
    public void CollapseAll() => SetExpandedRecursive(Nodes, false);

    /// <summary>The node's position in the flattened list of visible nodes, or -1 when it is not showing.</summary>
    public int IndexOf(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!ReferenceEquals(node.Owner, this))
        {
            return -1;
        }

        EnsureVisibleList();
        return _visible.IndexOf(node);
    }

    /// <summary>The number of visible nodes, which is what the rows are indexed by.</summary>
    public override int ItemCount
    {
        get
        {
            EnsureVisibleList();
            return _visible.Count;
        }
    }

    /// <summary>The label of a visible node.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public override string GetItemText(int index)
    {
        EnsureVisibleList();
        return _visible[index].Text;
    }

    /// <summary>The visible node at an index.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    public override object? GetItem(int index)
    {
        EnsureVisibleList();
        return _visible[index];
    }

    /// <summary>
    /// Rebinds the rows after the shape of the tree changed, keeping the selection and the focus
    /// rectangle on the nodes they were on rather than on the row numbers they used to have.
    /// </summary>
    internal void OnStructureChanged()
    {
        var lead = LeadIndex >= 0 && LeadIndex < ItemCount ? _visible[LeadIndex] : null;
        var selected = new List<TreeNode>(SelectedIndices.Count);
        foreach (int index in SelectedIndices)
        {
            if (index >= 0 && index < ItemCount)
            {
                selected.Add(_visible[index]);
            }
        }

        int leadIndex = -1;

        BeginUpdate();
        try
        {
            _visibleDirty = true;
            RebuildVisible();

            ClearSelected();
            foreach (var node in selected)
            {
                int index = _visible.IndexOf(node);
                if (index >= 0)
                {
                    SetSelected(index, true);
                }
            }

            leadIndex = lead is null ? -1 : _visible.IndexOf(lead);
            SetLead(leadIndex);
        }
        finally
        {
            EndUpdate();
        }

        // The extent only reflects the new row count once the batch above has laid out.
        if (leadIndex >= 0)
        {
            EnsureVisible(leadIndex);
        }
    }

    /// <inheritdoc />
    protected override float MeasureItemWidth()
    {
        EnsureVisibleList();

        float overhead = (TextInset * 2f) + GlyphSize + GlyphGap + (_checkBoxes ? GlyphSize + GlyphGap : 0f);
        float width = 0f;

        foreach (var node in _visible)
        {
            float left = overhead + (node.Level * _indent);
            width = MathF.Max(width, left + Font.MeasureWidth(node.Text));
        }

        return width;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The row is indented by the node's level; the check box and then the expander are painted
    /// before the label.
    /// </remarks>
    protected override void OnDrawItem(SKCanvas canvas, int index, SKRect bounds, DrawItemState state, SKColor textColor)
    {
        EnsureVisibleList();

        var node = _visible[index];
        bool focused = (state & DrawItemState.Focused) != 0;

        float left = bounds.Left + TextInset + (node.Level * _indent);

        if (_checkBoxes)
        {
            var box = GlyphRect(bounds, left);
            PaintCheckBox(canvas, box, node.CheckState, state);
            left = box.Right + GlyphGap;
        }

        if (node.HasChildren)
        {
            var expander = GlyphRect(bounds, left);
            PaintExpander(canvas, expander, node.IsExpanded, textColor);
        }

        left += GlyphSize + GlyphGap;

        var textBounds = new SKRect(left, bounds.Top, bounds.Right - TextInset, bounds.Bottom);
        TextRenderer.Draw(canvas, node.Text, Font, textColor, textBounds, ContentAlignment.MiddleLeft);

        if (focused)
        {
            Painter.DrawBorder(
                canvas,
                Geometry.Deflate(textBounds, 1f),
                textColor.WithAlpha(120),
                1f,
                2f);
        }
    }

    /// <inheritdoc />
    /// <remarks>A node that has children is expanded or collapsed before the handlers run.</remarks>
    protected override void OnItemActivated()
    {
        // A double click on a parent opens or closes it, the way the same click does in every other
        // tree; the activation handler still runs afterwards so an application can listen for it.
        if (SelectedNode is { HasChildren: true } node)
        {
            node.Toggle();
        }

        base.OnItemActivated();
    }

    /// <summary>
    /// Opens or closes a branch, or toggles a node's check box, when the click lands on one of the
    /// row's glyphs; the row is selected first, as any other click on it would select it.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.ClickCount == 1 && ItemCount > 0)
        {
            int index = IndexFromPoint(e.Location);
            if (index >= 0)
            {
                var node = _visible[index];
                var hit = GlyphAt(node, index, e.Location);
                if (hit != GlyphHit.None)
                {
                    // The row is taken the way any other click on it would take it before the glyph
                    // acts, so that opening a branch does not also move the selection somewhere else.
                    Focus();
                    SelectOnly(index);
                    ActivateGlyph(node, hit);
                    e.Handled = true;
                    return;
                }
            }
        }

        base.OnMouseDown(e);
    }

    /// <summary>
    /// Checks the node the keyboard is on when the tree shows check boxes, and otherwise reads Left
    /// and Right as collapse and expand - or as a move to the parent and to the first child.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Space checks a node rather than toggling its selection when the tree shows check boxes,
        // which is the one place where the two meanings of the key would otherwise collide.
        if (!e.Handled && _checkBoxes && e.Key == Key.Space && ItemCount > 0)
        {
            var target = LeadIndex >= 0 && LeadIndex < ItemCount ? _visible[LeadIndex] : null;
            if (target is not null)
            {
                ToggleCheck(target);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);

        if (e.Handled || ItemCount == 0)
        {
            return;
        }

        var node = LeadIndex >= 0 && LeadIndex < ItemCount ? _visible[LeadIndex] : null;
        if (node is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                if (node.IsExpanded && node.HasChildren)
                {
                    node.Collapse();
                }
                else if (node.Parent is { } parent)
                {
                    MoveTo(parent);
                }
                else
                {
                    return;
                }

                break;

            case Key.Right:
                if (!node.IsExpanded && node.HasChildren)
                {
                    node.Expand();
                }
                else if (node.FirstNode is { } child)
                {
                    MoveTo(child);
                }
                else
                {
                    return;
                }

                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Reads <c>+</c>, <c>-</c> and <c>*</c> as expand, collapse and expand-all; anything else goes
    /// on to type-ahead.
    /// </summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        // +, - and * are the WinForms shortcuts for expand, collapse and expand-all. They arrive as
        // text rather than as keys, and a node that starts with one of them is still reachable by
        // typing the rest of its name.
        if (!e.Handled && ItemCount > 0 && e.Text is { Length: 1 } text)
        {
            var node = LeadIndex >= 0 && LeadIndex < ItemCount ? _visible[LeadIndex] : null;

            switch (text[0])
            {
                case '+':
                    node?.Expand();
                    e.Handled = true;
                    return;

                case '-':
                    node?.Collapse();
                    e.Handled = true;
                    return;

                case '*':
                    ExpandAll();
                    e.Handled = true;
                    return;
            }
        }

        base.OnTextInput(e);
    }

    /// <summary>The flattened list of nodes whose ancestors are all expanded.</summary>
    private List<TreeNode> VisibleNodes
    {
        get
        {
            EnsureVisibleList();
            return _visible;
        }
    }

    /// <summary>The glyph a click landed on, if any.</summary>
    private enum GlyphHit
    {
        None,
        CheckBox,
        Expander,
    }

    private void MoveTo(TreeNode node)
    {
        int index = IndexOf(node);
        if (index >= 0)
        {
            SelectOnly(index);
        }
    }

    private void SetExpandedRecursive(TreeNodeCollection nodes, bool expanded)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        foreach (var node in nodes)
        {
            node.SetExpandedCore(expanded);
            SetExpandedRecursive(node.Nodes, expanded);
        }

        OnStructureChanged();
    }

    private void EnsureVisibleList()
    {
        if (_visibleDirty)
        {
            RebuildVisible();
        }
    }

    private void RebuildVisible()
    {
        _visible.Clear();

        foreach (var node in _roots)
        {
            AddVisible(node);
        }

        _visibleDirty = false;
    }

    private void AddVisible(TreeNode node)
    {
        _visible.Add(node);

        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Nodes)
        {
            AddVisible(child);
        }
    }

    private static SKRect GlyphRect(SKRect bounds, float left) =>
        new(left, bounds.MidY - (GlyphSize / 2f), left + GlyphSize, bounds.MidY + (GlyphSize / 2f));

    /// <summary>Which of the row's glyphs the point landed on, or <see cref="GlyphHit.None"/>.</summary>
    private GlyphHit GlyphAt(TreeNode node, int index, SKPoint point)
    {
        var bounds = GetItemRect(index);
        float left = bounds.Left + TextInset + (node.Level * _indent);

        if (_checkBoxes)
        {
            var box = GlyphRect(bounds, left);
            if (Geometry.Contains(box, point))
            {
                return GlyphHit.CheckBox;
            }

            left = box.Right + GlyphGap;
        }

        if (node.HasChildren && Geometry.Contains(GlyphRect(bounds, left), point))
        {
            return GlyphHit.Expander;
        }

        return GlyphHit.None;
    }

    private void ActivateGlyph(TreeNode node, GlyphHit hit)
    {
        switch (hit)
        {
            case GlyphHit.CheckBox:
                ToggleCheck(node);
                break;

            case GlyphHit.Expander:
                node.Toggle();
                break;
        }
    }

    private void ToggleCheck(TreeNode node)
    {
        node.CheckState = node.CheckState == CheckState.Checked
            ? CheckState.Unchecked
            : CheckState.Checked;

        _afterCheck?.Invoke(node);
    }

    private void PaintExpander(SKCanvas canvas, SKRect glyph, bool expanded, SKColor color)
    {
        float inset = 2f;

        using var builder = new SKPathBuilder();
        if (expanded)
        {
            builder.MoveTo(glyph.Left + inset, glyph.Top + inset + 1f);
            builder.LineTo(glyph.Right - inset, glyph.Top + inset + 1f);
            builder.LineTo(glyph.MidX, glyph.Bottom - inset - 1f);
        }
        else
        {
            builder.MoveTo(glyph.Left + inset + 1f, glyph.Top + inset);
            builder.LineTo(glyph.Right - inset - 1f, glyph.MidY);
            builder.LineTo(glyph.Left + inset + 1f, glyph.Bottom - inset);
        }

        builder.Close();

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = color.WithAlpha((byte)(color.Alpha / 2)),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawPath(path, paint);
    }

    private static void PaintCheckBox(SKCanvas canvas, SKRect glyph, CheckState state, DrawItemState itemState)
    {
        const float Radius = 3f;

        bool disabled = (itemState & DrawItemState.Disabled) != 0;
        bool marked = state != CheckState.Unchecked;

        var fill = disabled ? Theme.DisabledBackground : Theme.Accent;
        var border = disabled ? Theme.DisabledForeground
            : marked ? Theme.Accent
            : Theme.Border;
        var mark = disabled ? Theme.DisabledForeground : Theme.AccentForeground;

        if (marked)
        {
            Painter.Fill(canvas, glyph, fill, Radius);
        }

        Painter.DrawBorder(canvas, glyph, border, 1.5f, Radius);

        if (!marked)
        {
            return;
        }

        if (state == CheckState.Indeterminate)
        {
            var bar = new SKRect(glyph.Left + 3.5f, glyph.MidY - 1f, glyph.Right - 3.5f, glyph.MidY + 1f);
            Painter.Fill(canvas, bar, mark, 1f);
            return;
        }

        using var builder = new SKPathBuilder();
        builder.MoveTo(glyph.Left + 3.2f, glyph.MidY);
        builder.LineTo(glyph.Left + 5.6f, glyph.Bottom - 4.2f);
        builder.LineTo(glyph.Right - 3f, glyph.Top + 4.2f);

        using var path = builder.Snapshot();
        using var paint = new SKPaint
        {
            Color = mark,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.8f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        canvas.DrawPath(path, paint);
    }

    /// <summary>The nodes under an <see cref="TreeNode"/>, or the roots of an <see cref="TreeView"/>.</summary>
    public sealed class TreeNodeCollection : IList<TreeNode>
    {
        private readonly TreeNode? _parent;
        private readonly TreeView? _tree;
        private readonly List<TreeNode> _nodes;

        internal TreeNodeCollection(TreeNode parent, List<TreeNode> nodes)
        {
            _parent = parent;
            _nodes = nodes;
        }

        internal TreeNodeCollection(TreeView tree, List<TreeNode> nodes)
        {
            _tree = tree;
            _nodes = nodes;
        }

        /// <summary>
        /// Gets or sets the node at a position. Setting one detaches the node it replaces, with its
        /// whole subtree, and attaches the new one to the same tree.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
        public TreeNode this[int index]
        {
            get => _nodes[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                var previous = _nodes[index];
                if (ReferenceEquals(previous, value))
                {
                    return;
                }

                previous.Parent = null;
                previous.Attach(null);

                value.Parent = _parent;
                value.Attach(Tree);
                _nodes[index] = value;

                Notify();
            }
        }

        /// <summary>The number of nodes.</summary>
        public int Count => _nodes.Count;

        /// <summary>Always <see langword="false"/>; the collection can be changed.</summary>
        public bool IsReadOnly => false;

        private TreeView? Tree => _parent is null ? _tree : _parent.Owner;

        /// <summary>
        /// Adds a node at the end and attaches it, with its subtree, to the tree the collection
        /// belongs to.
        /// </summary>
        public void Add(TreeNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            AddCore(node);
            Notify();
        }

        /// <summary>Adds several nodes with a single rebuild of the rows.</summary>
        public void AddRange(IEnumerable<TreeNode> nodes)
        {
            ArgumentNullException.ThrowIfNull(nodes);

            var added = new List<TreeNode>(nodes);
            if (added.Count == 0)
            {
                return;
            }

            if (Tree is not { } tree)
            {
                foreach (var node in added)
                {
                    ArgumentNullException.ThrowIfNull(node);
                    AddCore(node);
                }

                return;
            }

            tree.BeginUpdate();
            try
            {
                foreach (var node in added)
                {
                    ArgumentNullException.ThrowIfNull(node);
                    AddCore(node);
                }

                Notify();
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        /// <summary>Inserts a node at a position, attaching it and its subtree to the tree.</summary>
        public void Insert(int index, TreeNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            node.Parent = _parent;
            node.Attach(Tree);
            _nodes.Insert(index, node);
            Notify();
        }

        /// <summary>Removes a node, detaching it from the tree; false when it is not there.</summary>
        public bool Remove(TreeNode node)
        {
            int index = _nodes.IndexOf(node);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        /// <summary>Removes the node at a position, detaching it with its subtree.</summary>
        public void RemoveAt(int index)
        {
            var node = _nodes[index];
            _nodes.RemoveAt(index);
            node.Parent = null;
            node.Attach(null);
            Notify();
        }

        /// <summary>Removes every node, detaching them and their subtrees from the tree.</summary>
        public void Clear()
        {
            if (_nodes.Count == 0)
            {
                return;
            }

            foreach (var node in _nodes)
            {
                node.Parent = null;
                node.Attach(null);
            }

            _nodes.Clear();
            Notify();
        }

        /// <summary>True when the node is in the collection.</summary>
        public bool Contains(TreeNode node) => _nodes.Contains(node);

        /// <summary>Copies the nodes into an array, starting at an index.</summary>
        public void CopyTo(TreeNode[] array, int arrayIndex) => _nodes.CopyTo(array, arrayIndex);

        /// <summary>The position of a node, or -1 when it is not in the collection.</summary>
        public int IndexOf(TreeNode node) => _nodes.IndexOf(node);

        /// <summary>Enumerates the nodes in the order they are painted.</summary>
        public IEnumerator<TreeNode> GetEnumerator() => _nodes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void AddCore(TreeNode node)
        {
            node.Parent = _parent;
            node.Attach(Tree);
            _nodes.Add(node);
        }

        private void Notify() => Tree?.OnStructureChanged();
    }
}
