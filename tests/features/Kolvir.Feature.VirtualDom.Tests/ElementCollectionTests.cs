using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// Get-only element collections on a node: inline descriptions, patching retained elements, removing
/// the shortfall, handler churn, keyed moves and the control a span carries.
/// </summary>
/// <remarks>
/// The RichLabel cases are the real ones this feature exists for. The <see cref="ContentControl"/>
/// cases use a control of the suite's own so that the collection itself can count every structural
/// change: "an unchanged render did nothing" is otherwise an assertion about a list nobody can see.
/// </remarks>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class ElementCollectionTests
{
    [TestMethod]
    public void ElementsAreCreatedInlineAndInOrder()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VContentControl
        {
            Items =
            [
                new VTestElement { Text = "one" },
                new VTestElement { Text = "two", Control = new VNote { Note = "inline" } },
            ],
        });

        var control = (ContentControl)mount.Root!;

        Assert.AreEqual(2, control.Items.Count);
        Assert.AreEqual("one", control.Items[0].Text);
        Assert.AreEqual("two", control.Items[1].Text);
        Assert.IsInstanceOfType<NoteControl>(control.Items[1].Control);
        Assert.AreEqual("inline", ((NoteControl)control.Items[1].Control!).Note);
    }

    [TestMethod]
    public void ARetainedElementIsPatchedRatherThanReplaced()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VContentControl
        {
            Items = [new VTestElement { Text = "before" }],
        });

        var control = (ContentControl)mount.Root!;
        var element = control.Items[0];

        mount.Update(new VContentControl
        {
            Items = [new VTestElement { Text = "after" }],
        });

        Assert.AreEqual(1, control.Items.Count);
        Assert.AreSame(element, control.Items[0], "the element instance is retained");
        Assert.AreEqual("after", element.Text);
        Assert.AreEqual(0, control.Items.RemoveCount, "nothing was removed");
        Assert.AreEqual(1, control.Items.InsertCount, "and nothing was added a second time");
    }

    [TestMethod]
    public void AnElementAddedInTheMiddleAndOneRemovedLeaveTheRightSequence()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VContentControl
        {
            Items =
            [
                new VTestElement { Key = "a", Text = "A" },
                new VTestElement { Key = "b", Text = "B" },
                new VTestElement { Key = "c", Text = "C" },
            ],
        });

        var control = (ContentControl)mount.Root!;
        var a = control.Items[0];
        var b = control.Items[1];
        var c = control.Items[2];

        mount.Update(new VContentControl
        {
            Items =
            [
                new VTestElement { Key = "a", Text = "A" },
                new VTestElement { Key = "x", Text = "X" },
                new VTestElement { Key = "c", Text = "C" },
            ],
        });

        Assert.AreEqual(3, control.Items.Count);
        Assert.AreSame(a, control.Items[0], "the first element survived");
        Assert.AreEqual("A", a.Text);
        Assert.AreEqual("X", control.Items[1].Text, "the new element is in the middle");
        Assert.AreNotSame(b, control.Items[1], "the removed element was replaced");
        Assert.AreSame(c, control.Items[2], "the last element survived");
        Assert.AreEqual("C", c.Text);
        Assert.AreEqual(1, control.Items.RemoveCount, "only the shortfall was removed");
    }

    [TestMethod]
    public void AHandlerIsAttachedReplacedAndDetached()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        int oldCalls = 0;
        int newCalls = 0;
        Action<object> oldHandler = _ => oldCalls++;
        Action<object> newHandler = _ => newCalls++;

        using var mount = VirtualDomRuntime.Mount(host, new VContentControl
        {
            Items = [new VTestElement { Click = oldHandler }],
        });

        var control = (ContentControl)mount.Root!;
        var element = control.Items[0];

        Assert.AreEqual(1, element.AttachCount);
        element.Raise();
        Assert.AreEqual(1, oldCalls);

        mount.Update(new VContentControl
        {
            Items = [new VTestElement { Click = newHandler }],
        });

        Assert.AreSame(element, control.Items[0]);
        Assert.AreEqual(1, element.DetachCount, "the replaced handler was detached");
        element.Raise();
        Assert.AreEqual(1, oldCalls, "the old handler no longer runs");
        Assert.AreEqual(1, newCalls, "the new handler does");

        mount.Update(new VContentControl
        {
            Items = [new VTestElement()],
        });

        Assert.AreEqual(2, element.DetachCount, "omitting the handler detaches it");
        element.Raise();
        Assert.AreEqual(1, newCalls, "and nothing runs");
    }

    [TestMethod]
    public void AnUnchangedRenderDoesNoCollectionOrMemberWork()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        Action<object> handler = _ => { };

        using var mount = VirtualDomRuntime.Mount(host, new VContentControl
        {
            Items = [new VTestElement { Text = "same", Click = handler }],
        });

        var control = (ContentControl)mount.Root!;
        var element = control.Items[0];
        int inserts = control.Items.InsertCount;
        int removes = control.Items.RemoveCount;
        int moves = control.Items.MoveCount;
        int textSets = element.TextSetCount;
        int attaches = element.AttachCount;
        int detaches = element.DetachCount;

        mount.Update(new VContentControl
        {
            Items = [new VTestElement { Text = "same", Click = handler }],
        });

        Assert.AreEqual(inserts, control.Items.InsertCount, "no insert");
        Assert.AreEqual(removes, control.Items.RemoveCount, "no remove");
        Assert.AreEqual(moves, control.Items.MoveCount, "no move");
        Assert.AreEqual(textSets, element.TextSetCount, "no property assignment");
        Assert.AreEqual(attaches, element.AttachCount, "no handler attach");
        Assert.AreEqual(detaches, element.DetachCount, "no handler detach");
    }

    [TestMethod]
    public void AKeyedReorderMovesTheElementsWithoutRecreatingThem()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VContentControl
        {
            Items =
            [
                new VTestElement { Key = "a", Text = "A" },
                new VTestElement { Key = "b", Text = "B" },
                new VTestElement { Key = "c", Text = "C" },
            ],
        });

        var control = (ContentControl)mount.Root!;
        var a = control.Items[0];
        var b = control.Items[1];
        var c = control.Items[2];
        int inserts = control.Items.InsertCount;
        int removes = control.Items.RemoveCount;

        mount.Update(new VContentControl
        {
            Items =
            [
                new VTestElement { Key = "c", Text = "C" },
                new VTestElement { Key = "a", Text = "A" },
                new VTestElement { Key = "b", Text = "B" },
            ],
        });

        Assert.AreSame(c, control.Items[0]);
        Assert.AreSame(a, control.Items[1]);
        Assert.AreSame(b, control.Items[2]);
        Assert.AreEqual(inserts, control.Items.InsertCount, "a move is not an insert");
        Assert.AreEqual(removes, control.Items.RemoveCount, "and not a remove");
        Assert.AreEqual(1, control.Items.MoveCount, "one move did the reorder");
    }

    [TestMethod]
    public void RichLabelSpansArriveOnTheControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VRichLabel
        {
            Spans =
            [
                new VTextSpan { Text = "hello " },
                new VTextSpan { Text = "click", Bold = true, Control = new VButton { Text = "go" } },
            ],
        });

        var label = (RichLabel)mount.Root!;

        Assert.AreEqual(2, label.Spans.Count);
        Assert.AreEqual("hello ", label.Spans[0].Text);
        Assert.AreEqual("click", label.Spans[1].Text);
        Assert.IsTrue(label.Spans[1].Bold);
        Assert.IsInstanceOfType<Button>(label.Spans[1].Control);
        Assert.AreSame(label, label.Spans[1].Control!.Parent, "the inline control is parented into the label");
        Assert.AreEqual(1, label.Controls.Count, "and parented exactly once");
    }

    [TestMethod]
    public void ASpanAndItsControlAreRetainedAcrossAPatch()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VRichLabel
        {
            Spans = [new VTextSpan { Text = "before", Control = new VButton { Text = "go" } }],
        });

        var label = (RichLabel)mount.Root!;
        var span = label.Spans[0];
        var button = span.Control!;

        mount.Update(new VRichLabel
        {
            Spans = [new VTextSpan { Text = "after", Control = new VButton { Text = "go" } }],
        });

        Assert.AreSame(span, label.Spans[0], "the TextSpan instance is retained");
        Assert.AreEqual("after", span.Text);
        Assert.AreSame(button, span.Control, "and so is the control the span carries");
        Assert.AreSame(label, button.Parent);
        Assert.AreEqual(1, label.Controls.Count, "the control was not parented a second time");
    }

    [TestMethod]
    public void ReplacingASpansControlRemovesTheOldOne()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VRichLabel
        {
            Spans = [new VTextSpan { Text = "before", Control = new VButton { Text = "go" } }],
        });

        var label = (RichLabel)mount.Root!;
        var span = label.Spans[0];
        var button = span.Control!;

        mount.Update(new VRichLabel
        {
            Spans = [new VTextSpan { Text = "after", Control = new VNote { Note = "replacement" } }],
        });

        Assert.AreSame(span, label.Spans[0]);
        Assert.AreNotSame(button, span.Control);
        Assert.IsNull(button.Parent, "the replaced control left the label");
        Assert.AreSame(label, span.Control!.Parent, "the replacement is parented");
        Assert.AreEqual(1, label.Controls.Count, "and the old one did not leak into Controls");
    }

    [TestMethod]
    public void RemovingASpanRemovesItsControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VRichLabel
        {
            Spans = [new VTextSpan { Text = "gone", Control = new VButton { Text = "go" } }],
        });

        var label = (RichLabel)mount.Root!;
        var button = label.Spans[0].Control!;

        mount.Update(new VRichLabel { Spans = [] });

        Assert.AreEqual(0, label.Spans.Count);
        Assert.IsNull(button.Parent);
        Assert.AreEqual(0, label.Controls.Count);
    }

    [TestMethod]
    public void AKeyedReorderKeepsTheSpansAndTheirControls()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VRichLabel
        {
            Spans =
            [
                new VTextSpan { Key = "a", Text = "A", Control = new VButton { Text = "a" } },
                new VTextSpan { Key = "b", Text = "B", Control = new VButton { Text = "b" } },
                new VTextSpan { Key = "c", Text = "C", Control = new VButton { Text = "c" } },
            ],
        });

        var label = (RichLabel)mount.Root!;
        var a = label.Spans[0];
        var b = label.Spans[1];
        var c = label.Spans[2];
        var controls = new[] { a.Control, b.Control, c.Control };

        mount.Update(new VRichLabel
        {
            Spans =
            [
                new VTextSpan { Key = "c", Text = "C", Control = new VButton { Text = "c" } },
                new VTextSpan { Key = "a", Text = "A", Control = new VButton { Text = "a" } },
                new VTextSpan { Key = "b", Text = "B", Control = new VButton { Text = "b" } },
            ],
        });

        Assert.AreSame(c, label.Spans[0]);
        Assert.AreSame(a, label.Spans[1]);
        Assert.AreSame(b, label.Spans[2]);
        Assert.AreSame(controls[2], label.Spans[0].Control);
        Assert.AreSame(controls[0], label.Spans[1].Control);
        Assert.AreSame(controls[1], label.Spans[2].Control);
        Assert.AreEqual(3, label.Controls.Count);

        foreach (var span in label.Spans)
        {
            Assert.AreSame(label, span.Control!.Parent, "a move does not unparent a control");
        }
    }

    [TestMethod]
    public void AnImperativeRichLabelStillWorksWithoutAnyNode()
    {
        using var label = new RichLabel();
        var text = new TextSpan("hello") { Bold = true };
        var inline = TextSpan.ForControl(new Button());

        label.Spans.Add(text);
        label.Spans.Add(inline);

        Assert.AreSame(label, text.Owner);
        Assert.AreSame(label, inline.Owner);
        Assert.AreEqual("hello", text.Text);
        Assert.IsTrue(text.Bold);
        Assert.AreSame(label, inline.Control!.Parent);

        label.Spans.Move(1, 0);

        Assert.AreSame(inline, label.Spans[0]);
        Assert.AreSame(text, label.Spans[1]);
        Assert.AreSame(label, text.Owner, "a move keeps ownership");
        Assert.AreSame(label, inline.Control!.Parent, "and keeps the control parented");

        label.Spans.Remove(text);

        Assert.IsNull(text.Owner);
        Assert.AreEqual(1, label.Spans.Count);
        Assert.AreSame(inline, label.Spans[0]);
    }
}
