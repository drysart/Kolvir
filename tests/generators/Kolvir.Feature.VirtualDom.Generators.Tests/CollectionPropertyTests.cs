using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Generators.Tests;

/// <summary>
/// A control's collection of row or column styles, on its node.
/// </summary>
/// <remarks>
/// <para>
/// There is nothing special about these two properties and that is the point: a table exposes
/// <c>RowStyles</c> and <c>ColumnStyles</c> as settable <c>IList&lt;T&gt;</c> properties that lay the
/// control out when they change, so the ordinary settable-property path emits them - one field, one
/// assignment in <c>ApplyTo</c>, one comparison in <c>PatchFrom</c>. What is asserted here is exactly
/// that, plus the one thing the ordinary path leaves to the control: the comparison the node does is
/// by reference, so a render that rebuilds its styles always reaches the setter, and the control is
/// what decides the sequence is unchanged and does nothing.
/// </para>
/// <para>
/// The behaviour those assignments produce - the counts, the layout passes, the instances that survive
/// an equal render - is <c>TableStyleTests</c> in the core suite, and the demo's <c>--check</c>
/// asserts it end to end through a real window.
/// </para>
/// </remarks>
[TestClass]
public sealed class CollectionPropertyTests
{
    [TestMethod]
    public void ATableNodesRowAndColumnStylesAreOrdinarySettableProperties()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(TableLayoutPanel))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var source = generated.Source;

        // The member itself: the control's own type, so a caller writes the sequence a table takes.
        StringAssert.Contains(
            source,
            "public global::System.Collections.Generic.IList<global::Kolvir.RowStyle> RowStyles");
        StringAssert.Contains(
            source,
            "public global::System.Collections.Generic.IList<global::Kolvir.ColumnStyle> ColumnStyles");

        // Applied by assignment, on the ordinary path - no loop, no Add per element, no special case.
        StringAssert.Contains(source, "target.RowStyles = _rowStyles.Value;");
        StringAssert.Contains(source, "target.ColumnStyles = _columnStyles.Value;");

        // Patched by the ordinary comparison. It is a reference comparison, deliberately: a render that
        // builds a fresh list always reaches the setter, and the control is what compares by value and
        // does nothing. A node-level value comparison would be a second definition of the same rule.
        StringAssert.Contains(source, "if (!_rowStyles.Equals(old._rowStyles))");
        StringAssert.Contains(source, "if (!_columnStyles.Equals(old._columnStyles))");

        // Dropped by a render, it goes back to what the control's constructor leaves: an empty table.
        StringAssert.Contains(
            source,
            "private static readonly global::System.Collections.Generic.IList<global::Kolvir.RowStyle> PristineRowStyles = Pristine.RowStyles;");
        StringAssert.Contains(
            source,
            "target.RowStyles = _rowStyles.IsSpecified ? _rowStyles.Value : PristineRowStyles;");
    }

    [TestMethod]
    public void AReadOnlyCollectionIsStillNotAProperty()
    {
        // The getter alone is not enough, which is why these two needed a setter to be exposed at all:
        // a node can only assign what a caller could assign.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.Feature.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                private readonly System.Collections.Generic.List<int> _items = new();

                public System.Collections.Generic.IList<int> Items => _items;

                public int Written { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsFalse(generated.Source.Contains("public global::System.Collections.Generic.IList<int> Items", StringComparison.Ordinal));
        StringAssert.Contains(generated.Source, "public int Written");
    }
}
