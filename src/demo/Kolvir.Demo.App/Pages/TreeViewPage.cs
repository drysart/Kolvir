using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The tree view page: the nodes, the checks and what they report.
/// </summary>
internal sealed class TreeViewPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public TreeViewPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// A tree with check boxes over a small source-tree shape. The rows are virtualized the same way
    /// the lists are - the control only knows the nodes whose ancestors are open - so the branch the
    /// "Add leaves" button grows costs nothing until it is opened.
    /// </summary>
    private Panel Build()
    {
        var card = Card();

        var selectionCaption = ScrollCaption("Select a node");
        card.Controls.Add(selectionCaption);

        var tree = new TreeView
        {
            Dock = XDock.Top,
            Height = 260f,
            CheckBoxes = true,
            SelectionMode = SelectionMode.One,
            VerticalScroll = ScrollStyle.Automatic,
            HorizontalScroll = ScrollStyle.Automatic,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        var src = new TreeNode("src");

        var core = new TreeNode("Core");
        core.Nodes.Add(new TreeNode("Control.cs"));
        core.Nodes.Add(new TreeNode("Painter.cs"));
        core.Nodes.Add(new TreeNode("Theme.cs"));
        src.Nodes.Add(core);

        var controls = new TreeNode("Controls");
        controls.Nodes.Add(new TreeNode("Button.cs"));
        controls.Nodes.Add(new TreeNode("ListView.cs"));
        controls.Nodes.Add(new TreeNode("TreeView.cs"));
        src.Nodes.Add(controls);

        var platforms = new TreeNode("Platform");
        platforms.Nodes.Add(new TreeNode("Win32"));
        platforms.Nodes.Add(new TreeNode("AppKit"));
        platforms.Nodes.Add(new TreeNode("Gtk4"));
        src.Nodes.Add(platforms);

        tree.Nodes.Add(src);
        tree.Nodes.Add(new TreeNode("README.md"));
        tree.Nodes.Add(new TreeNode("LICENSE"));

        src.Expand();
        core.Expand();
        tree.SelectedNode = core.Nodes[0];

        // Two of the three files under Controls are checked, so that branch reads as indeterminate.
        controls.Nodes[0].Checked = true;
        controls.Nodes[1].Checked = true;

        card.Controls.Add(tree);

        tree.AddSelectedIndexChangedHandler(_ =>
            selectionCaption.Text = tree.SelectedNode is { } node
                ? $"{node.Text}, level {node.Level}"
                : "no selection");

        var buttons = ButtonRow();

        var expand = new Button("Expand all")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };

        expand.AddClickHandler(_ =>
        {
            tree.ExpandAll();
            SetStatus($"{tree.ItemCount} rows visible.");
        });

        var collapse = new Button("Collapse all")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };

        collapse.AddClickHandler(_ =>
        {
            tree.CollapseAll();
            SetStatus($"{tree.ItemCount} rows visible.");
        });

        var grow = new Button("Add 5,000 leaves")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };

        grow.AddClickHandler(_ =>
        {
            // The branch is built while it is still detached, so filling it costs nothing; adding it
            // to the tree is the one rebuild.
            var generated = new TreeNode("generated");
            for (int index = 0; index < 5000; index++)
            {
                generated.Nodes.Add(new TreeNode($"leaf-{index:D4}.cs"));
            }

            tree.Nodes.Add(generated);
            generated.Expand();

            SetStatus($"{tree.Nodes.Count} roots, {tree.ItemCount} rows visible; only those are drawn.");
        });

        buttons.Controls.Add(expand);
        buttons.Controls.Add(collapse);
        buttons.Controls.Add(grow);
        card.Controls.Add(buttons);

        var checkCaption = ScrollCaption(
            $"{CountChecked(tree.Nodes)} of {CountNodes(tree.Nodes)} nodes checked");
        card.Controls.Add(checkCaption);

        tree.AddAfterCheckHandler(_ => checkCaption.Text =
            $"{CountChecked(tree.Nodes)} of {CountNodes(tree.Nodes)} nodes checked");

        return card;
    }

    private static int CountNodes(TreeView.TreeNodeCollection nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            count += 1 + CountNodes(node.Nodes);
        }

        return count;
    }

    private static int CountChecked(TreeView.TreeNodeCollection nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            if (node.Checked)
            {
                count++;
            }

            count += CountChecked(node.Nodes);
        }

        return count;
    }

    /// <summary>
    /// The measured-rendering page: the live presentation path, a rolling frame-time readout and a
    /// fixed benchmark, all driven by one 15 ms timer that runs only while the page is displayed.
    /// </summary>
    /// <remarks>
    /// The benchmark invalidates the window (or a small swatch, in partial mode) for the same
    /// number of frames under either backend, so a CPU run and a GL run are comparable. The frame
    /// time is <see cref="UiHost.LastFrameTime"/>, which is the host's paint only; a GL run's
    /// <c>SwapBuffers</c> happens after that and is not in the number, which the README states.
    /// </remarks>
}
