using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// The row lists and the tree: <see cref="XPlatListBox"/>, <see cref="XPlatListView"/>,
/// <see cref="XPlatTreeView"/> and the <see cref="XPlatVirtualList"/> behaviour they share.
/// </summary>
/// <remarks>
/// The shared base is where selection, geometry, type-ahead and scrolling live, so a test that
/// belongs to the base runs once against <see cref="XPlatListBox"/>; the kinds then each get the
/// behaviour that is theirs - columns and sorting, and the tree's expanders and check boxes.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ListControlTests : HeadlessTestBase
{
    [TestMethod]
    public void AListBoxReportsItsRowsAndTheirText()
    {
        var list = NewList("alpha", "beta", "gamma");
        using var scene = HeadlessScene.Create(list, 200f, 300f);

        Assert.AreEqual(3, list.ItemCount);
        Assert.AreEqual("alpha", list.GetItemText(0));
        Assert.AreEqual("gamma", list.GetItem(2));
        Assert.AreEqual("gamma", list.Items[2]);
    }

    [TestMethod]
    public void SelectingARowRaisesOneSelectedIndexChanged()
    {
        var list = NewList("alpha", "beta", "gamma");
        using var scene = HeadlessScene.Create(list, 200f, 300f);
        int changes = 0;
        list.AddSelectedIndexChangedHandler(_ => changes++);

        list.SelectedIndex = 1;

        Assert.AreEqual(1, list.SelectedIndex);
        Assert.AreEqual("beta", list.SelectedItem);
        Assert.AreEqual(1, changes);

        list.SelectedIndex = 1;
        Assert.AreEqual(1, changes, "selecting the row that is already selected changes nothing");
    }

    [TestMethod]
    public void ClickingARowSelectsIt()
    {
        var list = NewList("one", "two", "three");
        list.ItemHeight = 24f;
        using var scene = HeadlessScene.Create(list, 200f, 120f);

        var second = list.GetItemRect(1);
        scene.ClickAt(new SKPoint(second.MidX, second.MidY));

        Assert.AreEqual(1, list.SelectedIndex, "a click on a row should select it");
        Assert.IsTrue(list.GetSelected(1));
    }

    [TestMethod]
    public void MultiSimpleSelectsEachClickedRowAndToggles()
    {
        var list = NewList("one", "two", "three");
        list.ItemHeight = 24f;
        list.SelectionMode = XPlatSelectionMode.MultiSimple;
        using var scene = HeadlessScene.Create(list, 200f, 120f);

        scene.ClickAt(RowCentre(list, 0));
        scene.ClickAt(RowCentre(list, 2), XPlatModifiers.Control);

        CollectionAssert.AreEquivalent(new[] { 0, 2 }, list.SelectedIndices.ToArray(), "Ctrl adds a row to a simple multi selection");

        scene.ClickAt(RowCentre(list, 0), XPlatModifiers.Control);
        CollectionAssert.AreEquivalent(new[] { 2 }, list.SelectedIndices.ToArray(), "and a second Ctrl click deselects");
    }

    [TestMethod]
    public void MultiExtendedUsesTheModifiersForRangeAndToggle()
    {
        var list = NewList("0", "1", "2", "3", "4");
        list.ItemHeight = 24f;
        list.SelectionMode = XPlatSelectionMode.MultiExtended;
        using var scene = HeadlessScene.Create(list, 200f, 200f);

        scene.ClickAt(RowCentre(list, 1));
        scene.ClickAt(RowCentre(list, 3), XPlatModifiers.Shift);

        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, list.SelectedIndices.ToArray(), "Shift should extend the selection");

        scene.ClickAt(RowCentre(list, 0), XPlatModifiers.Control);
        CollectionAssert.IsSubsetOf(new[] { 0 }, list.SelectedIndices.ToArray(), "Ctrl should add without clearing");
    }

    [TestMethod]
    public void SelectionModeNoneTakesNoSelection()
    {
        var list = NewList("one", "two");
        list.ItemHeight = 24f;
        list.SelectionMode = XPlatSelectionMode.None;
        using var scene = HeadlessScene.Create(list, 200f, 80f);

        scene.ClickAt(RowCentre(list, 0));

        Assert.AreEqual(-1, list.SelectedIndex);
        Assert.IsFalse(list.GetSelected(0));
    }

    [TestMethod]
    public void TheArrowKeysWalkTheSelection()
    {
        var list = NewList("one", "two", "three");
        list.ItemHeight = 24f;
        using var scene = HeadlessScene.Create(list, 200f, 120f);

        list.Focus();
        list.SelectedIndex = 0;

        scene.Key(XPlatKey.Down);
        Assert.AreEqual(1, list.SelectedIndex);

        scene.Key(XPlatKey.Up);
        Assert.AreEqual(0, list.SelectedIndex);

        scene.Key(XPlatKey.End);
        Assert.AreEqual(2, list.SelectedIndex, "End should select the last row");

        scene.Key(XPlatKey.Home);
        Assert.AreEqual(0, list.SelectedIndex, "Home should select the first row");
    }

    [TestMethod]
    public void TypingAheadSelectsTheFirstMatchingRow()
    {
        var list = NewList("alpha", "beta", "bravo", "charlie");
        list.ItemHeight = 24f;
        using var scene = HeadlessScene.Create(list, 200f, 160f);

        list.Focus();
        list.SelectedIndex = 0;

        scene.Type("b");
        Assert.AreEqual(1, list.SelectedIndex, "'b' should jump to the first row starting with b");

        scene.Type("r");
        Assert.AreEqual(2, list.SelectedIndex, "'br' should narrow it to bravo");
    }

    [TestMethod]
    public void DoubleActivationRaisesItemActivated()
    {
        var list = NewList("one", "two");
        list.ItemHeight = 24f;
        using var scene = HeadlessScene.Create(list, 200f, 80f);
        int activated = 0;
        list.AddItemActivatedHandler(_ => activated++);

        var row = RowCentre(list, 0);
        Assert.AreEqual(0, list.IndexFromPoint(row), "the row centre should map to row 0");

        // A real double click is two presses: the first selects the row, the second carries the
        // count of 2 and is what activates it.
        scene.ClickAt(row);
        scene.Host.ProcessMouseDown(row, XPlatMouseButtons.Left, XPlatModifiers.None, clickCount: 2);
        scene.Host.ProcessMouseUp(row);

        Assert.AreEqual(1, activated, "a double click should activate the row");
    }

    [TestMethod]
    public void ItemRectsAreRowHeightStepsAndIndexOfPointAgrees()
    {
        var list = NewList("one", "two", "three");
        list.ItemHeight = 20f;
        using var scene = HeadlessScene.Create(list, 200f, 120f);

        Assert.AreEqual(20f, list.GetItemRect(1).Height);

        var point = list.GetItemRect(2);
        Assert.AreEqual(2, list.IndexFromPoint(new SKPoint(point.MidX, point.MidY)));

        Assert.AreEqual(-1, list.IndexFromPoint(new SKPoint(5f, -5f)), "a point above the first row is no row");
    }

    [TestMethod]
    public void ALongListScrollsAndEnsureVisibleBringsARowIntoView()
    {
        var list = new XPlatListBox { Dock = DockStyle.Fill, ItemHeight = 20f };
        for (int i = 0; i < 100; i++)
        {
            list.Items.Add($"row {i}");
        }

        using var scene = HeadlessScene.Create(list, 200f, 100f);

        Assert.IsTrue(list.ItemCount == 100);
        Assert.AreEqual(0f, list.VerticalOffset);

        list.EnsureVisible(90);

        Assert.IsTrue(list.VerticalOffset > 0f, "the list should scroll to reach row 90");
        Assert.IsTrue(list.VerticalScrollBar is not null, "and the bar should exist");
    }

    [TestMethod]
    public void AddingAnItemKeepsTheSelectionStable()
    {
        var list = NewList("one", "two");
        using var scene = HeadlessScene.Create(list, 200f, 120f);

        list.SelectedIndex = 1;
        list.Items.Add("three");

        Assert.AreEqual(1, list.SelectedIndex);
        Assert.AreEqual("two", list.SelectedItem);
    }

    [TestMethod]
    public void AListViewInListModeHasOneColumnOfRows()
    {
        var view = NewListView();
        view.Items.Add(new XPlatListViewItem("first"));
        view.Items.Add(new XPlatListViewItem("second"));

        using var scene = HeadlessScene.Create(view, 240f, 160f);

        Assert.AreEqual(XPlatView.Details, view.View, "a list view starts in Details");
        Assert.AreEqual(2, view.ItemCount);
        Assert.AreEqual("second", view.GetItemText(1));

        view.View = XPlatView.List;
        Assert.AreEqual(XPlatView.List, view.View, "and switches to List on request");
        Assert.AreEqual(2, view.ItemCount, "the rows are the same in either view");
    }

    [TestMethod]
    public void AListViewInDetailsModeReportsCells()
    {
        var view = NewListView();
        view.View = XPlatView.Details;
        view.Columns.Add(new XPlatListViewColumn("Name", 100f));
        view.Columns.Add(new XPlatListViewColumn("Size", 60f));

        var item = new XPlatListViewItem("file.txt", "12 KB");
        view.Items.Add(item);

        using var scene = HeadlessScene.Create(view, 240f, 160f);

        Assert.AreEqual(2, view.Columns.Count);
        Assert.AreEqual("12 KB", item.GetCell(1));
        Assert.AreEqual(0, view.ColumnFromPoint(new SKPoint(10f, 5f)), "a point over the first header belongs to column 0");
        Assert.AreEqual(1, view.ColumnFromPoint(new SKPoint(150f, 5f)), "and one further right to column 1");
    }

    [TestMethod]
    public void SortingAColumnReordersTheRows()
    {
        var view = NewListView();
        view.View = XPlatView.Details;
        view.Columns.Add(new XPlatListViewColumn("Name", 120f));
        view.Items.Add(new XPlatListViewItem("c"));
        view.Items.Add(new XPlatListViewItem("a"));
        view.Items.Add(new XPlatListViewItem("b"));

        using var scene = HeadlessScene.Create(view, 240f, 200f);

        view.SortColumn = 0;
        view.SortOrder = XPlatSortOrder.Ascending;
        view.Sort();

        Assert.AreEqual("a", view.GetItemText(0));
        Assert.AreEqual("b", view.GetItemText(1));
        Assert.AreEqual("c", view.GetItemText(2));

        view.SortOrder = XPlatSortOrder.Descending;
        view.Sort();
        Assert.AreEqual("c", view.GetItemText(0), "the other order should reverse the rows");
    }

    [TestMethod]
    public void ACustomColumnComparerWins()
    {
        var view = NewListView();
        view.View = XPlatView.Details;
        var column = new XPlatListViewColumn("Name", 120f);
        column.Comparer = (left, right) => string.CompareOrdinal(right.Text, left.Text);
        view.Columns.Add(column);
        view.Items.Add(new XPlatListViewItem("a"));
        view.Items.Add(new XPlatListViewItem("b"));

        using var scene = HeadlessScene.Create(view, 240f, 160f);

        view.SortColumn = 0;
        view.SortOrder = XPlatSortOrder.Ascending;
        view.Sort();

        Assert.AreEqual("b", view.GetItemText(0), "the comparer should decide the order");
    }

    [TestMethod]
    public void ATreeViewFlattensOnlyTheExpandedNodes()
    {
        var tree = NewTree();
        var root = new XPlatTreeNode("root");
        var childA = new XPlatTreeNode("a");
        var childB = new XPlatTreeNode("b");
        root.Nodes.Add(childA);
        root.Nodes.Add(childB);
        tree.Nodes.Add(root);

        using var scene = HeadlessScene.Create(tree, 240f, 200f);

        Assert.AreEqual(1, tree.ItemCount, "a collapsed root is the only visible row");

        root.Expand();

        Assert.AreEqual(3, tree.ItemCount, "expanding the root shows both children");
        Assert.AreEqual("a", tree.GetItemText(1));
        Assert.AreEqual(2, tree.IndexOf(childB), "the visible rows are flattened in tree order");

        root.Collapse();
        Assert.AreEqual(1, tree.ItemCount);
    }

    [TestMethod]
    public void SelectingATreeNodeSetsSelectedNode()
    {
        var tree = NewTree();
        var root = new XPlatTreeNode("root") { IsExpanded = true };
        var child = new XPlatTreeNode("child");
        root.Nodes.Add(child);
        tree.Nodes.Add(root);

        using var scene = HeadlessScene.Create(tree, 240f, 120f);

        tree.SelectedIndex = 1;

        Assert.AreSame(child, tree.SelectedNode);
        Assert.IsTrue(child.Selected);
        Assert.IsFalse(root.Selected);
    }

    [TestMethod]
    public void ATreeNodesLevelFollowsItsParent()
    {
        var tree = NewTree();
        var root = new XPlatTreeNode("root") { IsExpanded = true };
        var child = new XPlatTreeNode("child") { IsExpanded = true };
        var grandchild = new XPlatTreeNode("grandchild");
        child.Nodes.Add(grandchild);
        root.Nodes.Add(child);
        tree.Nodes.Add(root);

        Assert.AreEqual(0, root.Level);
        Assert.AreEqual(1, child.Level);
        Assert.AreEqual(2, grandchild.Level);
        Assert.IsTrue(grandchild.IsVisible, "a visible chain of expanded parents makes a node visible");
    }

    [TestMethod]
    public void ATreeWithCheckBoxesReportsAndRaisesAfterCheck()
    {
        var tree = NewTree();
        tree.CheckBoxes = true;
        var node = new XPlatTreeNode("option");
        tree.Nodes.Add(node);

        using var scene = HeadlessScene.Create(tree, 240f, 120f);
        int checks = 0;
        tree.AddAfterCheckHandler(_ => checks++);

        // The checkbox glyph sits at the row's left edge, inset by the list's text inset and 13 px
        // wide; clicking its centre is what a user does.
        var row = tree.GetItemRect(0);
        var origin = tree.GetPositionInHost();
        var glyph = new SKPoint(origin.X + row.Left + 14f, origin.Y + row.MidY);

        scene.ClickAt(glyph);

        Assert.AreEqual(XPlatCheckState.Checked, node.CheckState, "a click on the glyph should check the node");
        Assert.AreEqual(1, checks, "and raise AfterCheck once");

        scene.ClickAt(glyph);
        Assert.AreEqual(XPlatCheckState.Unchecked, node.CheckState, "a second click should clear it");
        Assert.AreEqual(2, checks);

        node.Checked = true;
        Assert.IsTrue(node.Checked, "the state is still settable by the application");
    }

    /// <summary>Builds a list box with the given rows.</summary>
    private static XPlatListBox NewList(params string[] rows)
    {
        var list = new XPlatListBox { Dock = DockStyle.Fill };
        foreach (var row in rows)
        {
            list.Items.Add(row);
        }

        return list;
    }

    private static XPlatListView NewListView() => new() { Dock = DockStyle.Fill };

    private static XPlatTreeView NewTree() => new() { Dock = DockStyle.Fill };

    /// <summary>The centre of one row, in host coordinates.</summary>
    private static SKPoint RowCentre(XPlatVirtualList list, int index)
    {
        var rect = list.GetItemRect(index);
        var origin = list.GetPositionInHost();
        return new SKPoint(origin.X + rect.MidX, origin.Y + rect.MidY);
    }
}
