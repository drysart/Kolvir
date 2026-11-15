using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.VirtualDom.Generators.Tests;

/// <summary>
/// The mistakes a manifest can contain and what the build says about them. Each case is a registration
/// that looks plausible and would otherwise fail at run time, or worse, produce a tree that is quietly
/// wrong.
/// </summary>
[TestClass]
public sealed class GeneratorDiagnosticTests
{
    [TestMethod]
    public void NotAControlIsReportedAsVD001()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(string))]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD001");
        Assert.IsEmpty(generated.Sources);
    }

    [TestMethod]
    public void AControlWithNoParameterlessConstructorIsReportedAsVD002()
    {
        // A node creates its control through the NodeType delegate, so this is a constructor call the
        // compiler has to be able to resolve - which is what keeps creation trim-safe.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                public HostControl(int value) { }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD002");
    }

    [TestMethod]
    public void TwoRegistrationsWithTheSameNodeNameAreReportedAsVD003()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            public sealed class First : Kolvir.Control { }

            public sealed class Second : Kolvir.Control { }

            [VirtualDom(typeof(First), Name = "Same")]
            [VirtualDom(typeof(Second), Name = "Same")]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD003");
    }

    [TestMethod]
    public void RegisteringOneControlTwiceInOneManifestIsReportedAsVD004()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            [VirtualDom(typeof(Label), Name = "Other")]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD004");
    }

    [TestMethod]
    public void RegisteringOneControlInTwoManifestsIsReportedAsVD004()
    {
        // Two node classes for one control would be a silent trap: the reconciler identifies a control
        // by its node type, so the two would never patch each other and every switch would rebuild.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            internal partial class First;

            [VirtualDom(typeof(Label), Name = "Other")]
            internal partial class Second;
            """);

        CollectionAssert.Contains(generated.Ids, "VD004");
    }

    [TestMethod]
    public void ANameThatIsNotAnIdentifierIsReportedAsVD005()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label), Name = "not a name")]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD005");
        Assert.IsEmpty(generated.Sources);
    }

    [TestMethod]
    public void AManifestThatIsNotPartialIsReportedAsVD007()
    {
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            [VirtualDom(typeof(Label))]
            internal class App
            {
            }
            """);

        CollectionAssert.Contains(generated.Ids, "VD007");
    }

    [TestMethod]
    public void APropertyThatCollidesWithTheNodeBaseIsReportedAsVD012()
    {
        // Flattening puts control properties beside the node's own members, so a control property
        // named Key has nowhere to go. Dropping it silently would leave a property that cannot be set
        // declaratively, which is why it is an error.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                public string? Key { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        CollectionAssert.Contains(generated.Ids, "VD012");
        StringAssert.DoesNotMatch(generated.Source, new System.Text.RegularExpressions.Regex(@"public string\? Key"));
        Assert.IsEmpty(generated.CompileErrors);
    }

    [TestMethod]
    public void AHiddenBasePropertyIsNotACollision()
    {
        // A derived control that hides a base property with `new` is normal - TextBox and RichLabel
        // both do it - and the node keeps the derived declaration, which is the one a caller sees.
        var generated = VirtualDomCompiler.Build("""
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            public class BaseControl : Kolvir.Control
            {
                public object? Value { get; set; }
            }

            public sealed class HostControl : BaseControl
            {
                public new string? Value { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        // One property, typed by the hiding declaration rather than the hidden one.
        StringAssert.Contains(generated.Source, "public string? Value");
        Assert.IsFalse(generated.Source.Contains("public object? Value", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AHandlerPairReachedTwiceIsOneEvent()
    {
        // An inherited handler pair is found on both the base and the derived type. That is one event,
        // not a collision: reporting it would fail the build for a control that did nothing wrong.
        var generated = VirtualDomCompiler.Build("""
            using System;
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            public class BaseControl : Kolvir.Control
            {
                public void AddClickedHandler(Action handler) { }

                public void RemoveClickedHandler(Action handler) { }
            }

            public sealed class HostControl : BaseControl
            {
                public new void AddClickedHandler(Action handler) { }

                public new void RemoveClickedHandler(Action handler) { }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));

        var occurrences = generated.Source.Split("public global::System.Action? Clicked").Length - 1;
        Assert.AreEqual(1, occurrences);
    }

    [TestMethod]
    public void AGenericAndNullablePropertyKeepsItsExactType()
    {
        // The emitter writes the property's fully-qualified type with its nullable annotation, which
        // is what lets the analyzer trust the generated surface for the null question.
        var generated = VirtualDomCompiler.Build("""
            using System.Collections.Generic;
            using Kolvir;
            using Kolvir.VirtualDom;

            namespace TestApp;

            public sealed class HostControl : Kolvir.Control
            {
                public IReadOnlyList<string>? Rows { get; set; }
            }

            [VirtualDom(typeof(HostControl))]
            internal partial class App;
            """);

        Assert.IsEmpty(generated.Ids, string.Join("\n", generated.Ids));
        Assert.IsEmpty(generated.CompileErrors, string.Join("\n", generated.CompileErrors));
        StringAssert.Contains(generated.Source, "public global::System.Collections.Generic.IReadOnlyList<string>? Rows");
    }
}
