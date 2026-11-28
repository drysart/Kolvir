using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The optional-argument wrapper: omitted, supplied, and supplied as null are three different
/// things, and equality says so.
/// </summary>
/// <remarks>
/// The type exists because a method parameter cannot otherwise tell an omitted argument from one
/// passed as null. Nothing here touches a font; the font tests use it through <c>With</c>.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class OptionalTests
{
    [TestMethod]
    public void OmittedIsUnspecified()
    {
        Optional<string?> omitted = default;
        Optional<int> alsoOmitted = new();

        Assert.IsFalse(omitted.IsSpecified, "default is the omitted state");
        Assert.IsFalse(alsoOmitted.IsSpecified);
        Assert.AreEqual("unspecified", omitted.ToString());
    }

    [TestMethod]
    public void AValueIsSpecifiedAndKeepsTheValue()
    {
        Optional<int> weight = 700;

        Assert.IsTrue(weight.IsSpecified, "a converted value is a supplied argument");
        Assert.AreEqual(700, weight.Value);
        Assert.AreEqual("700", weight.ToString());
    }

    [TestMethod]
    public void ASuppliedNullIsSpecifiedAndHoldsNull()
    {
        string? value = null;
        Optional<string?> family = value;

        Assert.IsTrue(family.IsSpecified, "null is a value here, not an omission");
        Assert.IsNull(family.Value);
        Assert.AreEqual("null", family.ToString());
    }

    [TestMethod]
    public void OmittedIsNotTheSameAsSuppliedNull()
    {
        string? value = null;
        Optional<string?> omitted = default;
        Optional<string?> suppliedNull = value;

        Assert.AreNotEqual(omitted, suppliedNull, "the flag is what separates the two");
        Assert.IsFalse(omitted == suppliedNull);
        Assert.IsTrue(omitted != suppliedNull);
    }

    [TestMethod]
    public void EqualityComparesTheFlagAndTheValue()
    {
        Optional<int> seven = 7;
        Optional<int> otherSeven = 7;
        Optional<int> eight = 8;
        Optional<int> zero = 0;
        Optional<int> omitted = default;
        Optional<string?> suppliedNull = (string?)null;
        Optional<string?> otherNull = (string?)null;

        Assert.AreEqual(seven, otherSeven);
        Assert.AreNotEqual(seven, eight);
        Assert.AreEqual(omitted, default(Optional<int>), "two omitted arguments are the same state");
        Assert.AreNotEqual(omitted, zero, "a supplied zero is not an omitted argument");
        Assert.AreEqual(suppliedNull, otherNull);
        Assert.AreEqual(suppliedNull.GetHashCode(), otherNull.GetHashCode());
        Assert.AreEqual(0, omitted.GetHashCode());
    }

    [TestMethod]
    public void OperatorsAgreeWithEquals()
    {
        Optional<int> one = 1;
        Optional<int> anotherOne = 1;
        Optional<int> two = 2;

        Assert.IsTrue(one == anotherOne);
        Assert.IsFalse(one == two);
        Assert.IsTrue(one != two);
        Assert.IsFalse(one != anotherOne);
    }
}
