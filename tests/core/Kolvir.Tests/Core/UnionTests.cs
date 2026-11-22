using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The union value types: one case at a time, read back only by its exact case type, and dispatched
/// exhaustively.
/// </summary>
/// <remarks>
/// The behaviour tested here is the contract every arity shares, so the detailed cases run against
/// <c>Union&lt;SKColor, ThemeColor, InheritedValue&gt;</c> - the shape the colour properties were
/// designed around - and each arity is covered where its own shape is the point.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class UnionTests
{
    private static readonly SKColor Direct = new(0xFF404040);

    [TestMethod]
    public void AssignmentFillsEveryArm()
    {
        Union<SKColor, ThemeColor> pair = Direct;
        Assert.IsTrue(pair.TryGetValue<SKColor>(out SKColor pairDirect));
        Assert.AreEqual(Direct, pairDirect);

        pair = ThemeColor.MutedForeground;
        Assert.IsTrue(pair.TryGetValue<ThemeColor>(out ThemeColor pairRole));
        Assert.AreEqual(ThemeColor.MutedForeground, pairRole);

        Union<SKColor, ThemeColor, InheritedValue> triple = Direct;
        Assert.IsTrue(triple.TryGetValue<SKColor>(out SKColor tripleDirect));
        Assert.AreEqual(Direct, tripleDirect);

        triple = ThemeColor.MutedForeground;
        Assert.IsTrue(triple.TryGetValue<ThemeColor>(out ThemeColor tripleRole));
        Assert.AreEqual(ThemeColor.MutedForeground, tripleRole);

        triple = PropertyValue.Inherited;
        Assert.IsTrue(triple.TryGetValue<InheritedValue>(out _));

        Union<SKColor, ThemeColor, InheritedValue, int> quadruple = Direct;
        Assert.IsTrue(quadruple.TryGetValue<SKColor>(out SKColor quadrupleDirect));
        Assert.AreEqual(Direct, quadrupleDirect);

        quadruple = ThemeColor.Accent;
        Assert.IsTrue(quadruple.TryGetValue<ThemeColor>(out ThemeColor quadrupleRole));
        Assert.AreEqual(ThemeColor.Accent, quadrupleRole);

        quadruple = PropertyValue.Inherited;
        Assert.IsTrue(quadruple.TryGetValue<InheritedValue>(out _));

        quadruple = 12;
        Assert.IsTrue(quadruple.TryGetValue<int>(out int quadrupleNumber));
        Assert.AreEqual(12, quadrupleNumber);
    }

    [TestMethod]
    public void ExplicitConstructionIsTheSameAsAssignment()
    {
        var pairFromDirect = new Union<SKColor, ThemeColor>(Direct);
        Assert.AreEqual((Union<SKColor, ThemeColor>)Direct, pairFromDirect);

        var pairFromRole = new Union<SKColor, ThemeColor>(ThemeColor.MutedForeground);
        Assert.AreEqual((Union<SKColor, ThemeColor>)ThemeColor.MutedForeground, pairFromRole);

        var tripleFromDirect = new Union<SKColor, ThemeColor, InheritedValue>(Direct);
        Assert.AreEqual((Union<SKColor, ThemeColor, InheritedValue>)Direct, tripleFromDirect);

        var tripleFromRole = new Union<SKColor, ThemeColor, InheritedValue>(ThemeColor.MutedForeground);
        Assert.AreEqual((Union<SKColor, ThemeColor, InheritedValue>)ThemeColor.MutedForeground, tripleFromRole);

        var tripleFromInherit = new Union<SKColor, ThemeColor, InheritedValue>(PropertyValue.Inherited);
        Assert.AreEqual((Union<SKColor, ThemeColor, InheritedValue>)PropertyValue.Inherited, tripleFromInherit);

        var quadrupleFromDirect = new Union<SKColor, ThemeColor, InheritedValue, int>(Direct);
        Assert.AreEqual((Union<SKColor, ThemeColor, InheritedValue, int>)Direct, quadrupleFromDirect);

        var quadrupleFromRole = new Union<SKColor, ThemeColor, InheritedValue, int>(ThemeColor.MutedForeground);
        Assert.AreEqual((Union<SKColor, ThemeColor, InheritedValue, int>)ThemeColor.MutedForeground, quadrupleFromRole);

        var quadrupleFromInherit = new Union<SKColor, ThemeColor, InheritedValue, int>(PropertyValue.Inherited);
        Assert.AreEqual((Union<SKColor, ThemeColor, InheritedValue, int>)PropertyValue.Inherited, quadrupleFromInherit);

        var quadrupleFromNumber = new Union<SKColor, ThemeColor, InheritedValue, int>(12);
        Assert.AreEqual((Union<SKColor, ThemeColor, InheritedValue, int>)12, quadrupleFromNumber);
    }

    [TestMethod]
    public void TryGetValueReadsTheStoredCase()
    {
        Union<SKColor, ThemeColor, InheritedValue> union = ThemeColor.MutedForeground;

        Assert.IsTrue(union.TryGetValue<ThemeColor>(out ThemeColor role));
        Assert.AreEqual(ThemeColor.MutedForeground, role);
    }

    [TestMethod]
    public void TryGetValueMissesADifferentArmAndLeavesTheValueDefault()
    {
        Union<SKColor, ThemeColor, InheritedValue> union = ThemeColor.MutedForeground;

        Assert.IsFalse(union.TryGetValue<SKColor>(out SKColor direct));
        Assert.AreEqual(default(SKColor), direct, "a miss leaves the value at its default");
        Assert.IsTrue(direct == default, "and nothing was written into it");

        Assert.IsFalse(union.TryGetValue<InheritedValue>(out _));
    }

    [TestMethod]
    public void TryGetValueMatchesTheExactCaseTypeAndNothingAssignableToIt()
    {
        Union<object, string> heldAsString = "text";

        Assert.IsTrue(heldAsString.TryGetValue<string>(out string? text));
        Assert.AreEqual("text", text);

        Assert.IsFalse(
            heldAsString.TryGetValue<object>(out object? asObject),
            "object is a case type, but this arm holds the string one");
        Assert.IsNull(asObject);

        Union<object, string> heldAsObject = (object)"text";

        Assert.IsTrue(heldAsObject.TryGetValue<object>(out object? boxed));
        Assert.AreEqual("text", boxed);
        Assert.IsFalse(
            heldAsObject.TryGetValue<string>(out _),
            "the string arm is empty even though the payload is a string");
    }

    [TestMethod]
    public void TryGetValueRefusesATypeThatIsNotACase()
    {
#pragma warning disable KOLVIR0001 // The three calls below are deliberately wrong: this is the run-time guard's own test.
        Union<SKColor, ThemeColor, InheritedValue> union = ThemeColor.MutedForeground;

        var exception = Assert.ThrowsExactly<ArgumentException>(() => union.TryGetValue<object>(out _));

        StringAssert.Contains(exception.Message, "Union<SKColor, ThemeColor, InheritedValue>");
        StringAssert.Contains(exception.Message, "Object");
        StringAssert.Contains(exception.Message, "SKColor, ThemeColor, InheritedValue");

        Assert.ThrowsExactly<ArgumentException>(() => new Union<SKColor, ThemeColor>(Direct).TryGetValue<int>(out _));
        Assert.ThrowsExactly<ArgumentException>(
            () => new Union<SKColor, ThemeColor, InheritedValue, int>(12).TryGetValue<double>(out _));
#pragma warning restore KOLVIR0001
    }

    [TestMethod]
    public void DefaultHoldsNoCaseAndSaysSo()
    {
        Union<SKColor, ThemeColor> pair = default;
        Union<SKColor, ThemeColor, InheritedValue> triple = default;
        Union<SKColor, ThemeColor, InheritedValue, int> quadruple = default;

        Assert.IsTrue(pair.IsUnspecified);
        Assert.IsTrue(triple.IsUnspecified);
        Assert.IsTrue(quadruple.IsUnspecified);

        var pairException = Assert.ThrowsExactly<InvalidOperationException>(pair.ThrowIfUnspecified);
        StringAssert.Contains(pairException.Message, "default-initialised");
        StringAssert.Contains(pairException.Message, "Union<SKColor, ThemeColor>(unspecified)");

        StringAssert.Contains(
            Assert.ThrowsExactly<InvalidOperationException>(triple.ThrowIfUnspecified).Message,
            "Union<SKColor, ThemeColor, InheritedValue>(unspecified)");
        StringAssert.Contains(
            Assert.ThrowsExactly<InvalidOperationException>(quadruple.ThrowIfUnspecified).Message,
            "Union<SKColor, ThemeColor, InheritedValue, Int32>(unspecified)");
    }

    [TestMethod]
    public void AValidUnionIsNotUnspecifiedAndDoesNotThrow()
    {
        Union<SKColor, ThemeColor, InheritedValue> union = ThemeColor.MutedForeground;

        Assert.IsFalse(union.IsUnspecified);
        union.ThrowIfUnspecified();
    }

    [TestMethod]
    public void SwitchRunsExactlyTheStoredHandler()
    {
        var log = new List<string>();
        Union<SKColor, ThemeColor, InheritedValue> union = ThemeColor.MutedForeground;

        union.Switch(
            _ => log.Add("direct"),
            _ => log.Add("theme"),
            _ => log.Add("inherit"));

        CollectionAssert.AreEqual(new[] { "theme" }, log);
    }

    [TestMethod]
    public void SwitchWithStatePassesTheStateAndRunsOnce()
    {
        var log = new List<string>();
        Union<SKColor, ThemeColor, InheritedValue> union = PropertyValue.Inherited;

        union.Switch(
            log,
            static (list, _) => list.Add("direct"),
            static (list, _) => list.Add("theme"),
            static (list, _) => list.Add("inherit"));

        CollectionAssert.AreEqual(new[] { "inherit" }, log);
    }

    [TestMethod]
    public void SwitchReachesEveryArmOfTheFourCaseShape()
    {
        var union = new Union<SKColor, ThemeColor, InheritedValue, int>(12);

        var seen = new List<string>();
        union.Switch(
            seen,
            static (list, _) => list.Add("direct"),
            static (list, _) => list.Add("theme"),
            static (list, _) => list.Add("inherit"),
            static (list, _) => list.Add("number"));

        CollectionAssert.AreEqual(new[] { "number" }, seen);
    }

    [TestMethod]
    public void SwitchRefusesADefaultUnion()
    {
        Union<SKColor, ThemeColor, InheritedValue> union = default;

        Assert.ThrowsExactly<InvalidOperationException>(
            () => union.Switch(_ => { }, _ => { }, _ => { }));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => union.Switch(0, static (_, _) => { }, static (_, _) => { }, static (_, _) => { }));
    }

    [TestMethod]
    public void MatchReturnsTheResultOfTheStoredCase()
    {
        Union<SKColor, ThemeColor, InheritedValue> union = ThemeColor.MutedForeground;

        string text = union.Match(
            0,
            static (_, _) => "direct",
            static (_, _) => "theme",
            static (_, _) => "inherit");

        Assert.AreEqual("theme", text);

        string withState = union.Match(
            "state",
            static (state, direct) => $"{state}:{direct}",
            static (state, role) => $"{state}:{role}",
            static (state, _) => $"{state}:inherit");

        Assert.AreEqual("state:MutedForeground", withState);

        var quadruple = new Union<SKColor, ThemeColor, InheritedValue, int>(7);
        Assert.AreEqual(
            7,
            quadruple.Match(
                0,
                static (_, _) => 0,
                static (_, _) => 0,
                static (_, _) => 0,
                static (_, number) => number));
    }

    [TestMethod]
    public void MatchRefusesADefaultUnion()
    {
        Union<SKColor, ThemeColor> union = default;

        Assert.ThrowsExactly<InvalidOperationException>(
            () => union.Match(0, static (_, _) => 0, static (_, _) => 0));
    }

    [TestMethod]
    public void EqualityComparesTheArmAndThenThePayload()
    {
        Union<SKColor, ThemeColor> a = ThemeColor.MutedForeground;
        Union<SKColor, ThemeColor> b = ThemeColor.MutedForeground;
        Union<SKColor, ThemeColor> c = ThemeColor.Accent;

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.IsTrue(a.Equals((object)b));

        Assert.AreNotEqual(a, c);
        Assert.IsTrue(a != c);
        Assert.IsFalse(a == c);
    }

    [TestMethod]
    public void ADifferentArmIsNotEqualEvenWithAnIdenticalPayload()
    {
        Union<object, string> asObject = (object)"same";
        Union<object, string> asString = "same";

        Assert.AreNotEqual(asObject, asString);
        Assert.IsFalse(asObject == asString);
        Assert.IsTrue(asObject != asString);
        Assert.IsFalse(asObject.Equals((object)asString));
    }

    [TestMethod]
    public void TwoDefaultUnionsAreEqual()
    {
        Union<SKColor, ThemeColor, InheritedValue> left = default;
        Union<SKColor, ThemeColor, InheritedValue> right = default;

        Assert.AreEqual(left, right);
        Assert.IsTrue(left == right);
        Assert.IsFalse(left != right);
        Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        Assert.IsTrue(left.Equals((object)right));

        Union<SKColor, ThemeColor, InheritedValue> assigned = ThemeColor.MutedForeground;
        Assert.AreNotEqual(left, assigned);
    }

    [TestMethod]
    public void ToStringNamesTheStoredCase()
    {
        var roleFirst = new Union<ThemeColor, SKColor>(ThemeColor.MutedForeground);
        Assert.AreEqual("ThemeColor.MutedForeground", roleFirst.ToString(), "the first case reads as a named constant");

        var directFirst = new Union<SKColor, ThemeColor>(Direct);
        Assert.AreEqual($"SKColor.{Direct}", directFirst.ToString(), "even when the first case has text of its own");

        var directSecond = new Union<ThemeColor, SKColor>(Direct);
        Assert.AreEqual($"SKColor({Direct})", directSecond.ToString(), "every other case reads as a type and a value");

        var inherit = new Union<SKColor, ThemeColor, InheritedValue>(PropertyValue.Inherited);
        Assert.AreEqual("InheritedValue(Inherit)", inherit.ToString());

        Assert.AreEqual(
            "Union<SKColor, ThemeColor, InheritedValue>(unspecified)",
            default(Union<SKColor, ThemeColor, InheritedValue>).ToString());

        var quadruple = new Union<SKColor, ThemeColor, InheritedValue, int>(12);
        Assert.AreEqual("Int32(12)", quadruple.ToString());
    }

    [TestMethod]
    public void ANullReferenceIsAValidCase()
    {
        // The case type is annotated nullable, which is how a caller says a null payload is
        // expected. The union does not decide that for anyone.
        Union<object, string?, InheritedValue> union = (string?)null;

        Assert.IsFalse(union.IsUnspecified);
        Assert.IsTrue(union.TryGetValue<string>(out string? text));
        Assert.IsNull(text);
    }

    [TestMethod]
    public void TryGetValueDoesNotAllocate()
    {
        Union<SKColor, ThemeColor, InheritedValue> union = Direct;

        // Warm the path up so the measurement does not include first-call costs.
        for (int i = 0; i < 1_000; i++)
        {
            union.TryGetValue<SKColor>(out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000_000; i++)
        {
            union.TryGetValue<SKColor>(out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0L, allocated, "a matching TryGetValue reinterprets the payload and must not box it");
    }
}
