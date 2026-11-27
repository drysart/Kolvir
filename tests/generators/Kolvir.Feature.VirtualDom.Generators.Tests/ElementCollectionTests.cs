using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// A get-only collection of an element type, on the node that describes its control.
/// </summary>
/// <remarks>
/// The element is not a control, so the generated member is not a property assignment: the node holds
/// a sequence of description objects and the reconciler reconciles it against the live collection.
/// What is asserted here is the shape of that generated surface; the behaviour is in the feature
/// suite's <c>ElementCollectionTests</c>.
/// </remarks>
[TestClass]
public sealed class ElementCollectionTests
{
    [TestMethod]
    public void AGetOnlyCollectionOfAMarkedElementBecomesADescriptionSequence()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDomElement]
            public sealed class Item
            {
                public string Text { get; set; } = "";
            }

            public sealed class HostControl : Control
            {
                public System.Collections.Generic.IList<Item> Items { get; } = new System.Collections.Generic.List<Item>();
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        // The element gets a description class rather than a node class: it is not a Control.
        StringAssert.Contains(source, "public sealed class VItem : global::Kolvir.Feature.VirtualDom.VElementDescription");
        StringAssert.Contains(source, "new global::Kolvir.Feature.VirtualDom.ElementType(typeof(global::TestApp.Item), () => new global::TestApp.Item())");

        // The collection member is a sequence of those descriptions.
        StringAssert.Contains(
            source,
            "public global::System.Collections.Generic.IReadOnlyList<global::TestApp.VItem>? Items");

        // It is reconciled, not assigned: the live collection is handed to the reconciler.
        StringAssert.Contains(
            source,
            "public override global::Kolvir.Feature.VirtualDom.VNodeCollection[]? GetCollections(global::Kolvir.Control control)");
        StringAssert.Contains(
            source,
            "global::Kolvir.Feature.VirtualDom.VNodeCollection.Create(target.Items, global::TestApp.VItem.Kind, _items.IsSpecified ? _items.Value : null)");

        // And the ordinary paths leave it alone.
        Assert.IsFalse(source.Contains("target.Items =", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ACollectionWithAMovePrimitiveGetsItPassedToTheReconciler()
    {
        var generated = VirtualDomCompiler.Build("""
            using System.Collections;
            using System.Collections.Generic;
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDomElement]
            public sealed class Item
            {
                public string Text { get; set; } = "";
            }

            public sealed class Movable : IList<Item>
            {
                private readonly System.Collections.Generic.List<Item> _items = new();

                public Item this[int index] { get => _items[index]; set => _items[index] = value; }
                public int Count => _items.Count;
                public bool IsReadOnly => false;
                public void Add(Item item) => _items.Add(item);
                public void Clear() => _items.Clear();
                public bool Contains(Item item) => _items.Contains(item);
                public void CopyTo(Item[] array, int index) => _items.CopyTo(array, index);
                public IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();
                public int IndexOf(Item item) => _items.IndexOf(item);
                public void Insert(int index, Item item) => _items.Insert(index, item);
                public bool Remove(Item item) => _items.Remove(item);
                public void RemoveAt(int index) => _items.RemoveAt(index);
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

                public void Move(int from, int to)
                {
                    var item = _items[from];
                    _items.RemoveAt(from);
                    _items.Insert(to, item);
                }
            }

            public sealed class HostControl : Control
            {
                public Movable Items { get; } = new();
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        StringAssert.Contains(
            generated.Source,
            "global::Kolvir.Feature.VirtualDom.VNodeCollection.Create(target.Items, global::TestApp.VItem.Kind, _items.IsSpecified ? _items.Value : null, (from, to) => target.Items.Move(from, to))");
    }

    [TestMethod]
    public void TheSweepPicksUpRichLabelsSpans()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDomAll]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        StringAssert.Contains(source, "public sealed class VRichLabel");
        StringAssert.Contains(source, "public sealed class VTextSpan : global::Kolvir.Feature.VirtualDom.VElementDescription");
        StringAssert.Contains(
            source,
            "public global::System.Collections.Generic.IReadOnlyList<global::TestApp.VTextSpan>? Spans");
        StringAssert.Contains(source, "GetCollections(global::Kolvir.Control control)");
    }

    [TestMethod]
    public void AnElementTypeFromAReferencedAssemblyIsFound()
    {
        var library = VirtualDomCompiler.Library("Elements", """
            using Kolvir;

            namespace Elements;

            [VirtualDomElement]
            public sealed class Item
            {
                public string Text { get; set; } = "";
            }
            """, referencesKolvir: true);

        var generated = VirtualDomCompiler.Build([library], """
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Control
            {
                public System.Collections.Generic.IList<Elements.Item> Items { get; } = new System.Collections.Generic.List<Elements.Item>();
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));
        StringAssert.Contains(generated.Source, "public sealed class VItem : global::Kolvir.Feature.VirtualDom.VElementDescription");
    }

    [TestMethod]
    public void AnElementDescriptionNameThatAControlAlreadyUsesIsReported()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDomElement]
            public sealed class Item
            {
                public string Text { get; set; } = "";
            }

            public sealed class OtherControl : Control
            {
            }

            public sealed class HostControl : Control
            {
                public System.Collections.Generic.IList<Item> Items { get; } = new System.Collections.Generic.List<Item>();
            }

            [VirtualDom(typeof(HostControl))]
            [VirtualDom(typeof(OtherControl), Name = "Item")]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD017");
        Assert.IsFalse(
            generated.Source.Contains("class VItem : global::Kolvir.Feature.VirtualDom.VElementDescription", StringComparison.Ordinal),
            "no description class was emitted");
        Assert.IsFalse(
            generated.Source.Contains("IReadOnlyList<global::TestApp.VItem>? Items", StringComparison.Ordinal),
            "and the property that would have needed it is left off");
    }
}
