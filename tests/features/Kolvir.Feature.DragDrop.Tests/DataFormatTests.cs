using Kolvir.DragDrop;

namespace Kolvir.DragDrop.Tests;

/// <summary>
/// The three well-known data formats and the rule that a format is a name compared without case.
/// </summary>
/// <remarks>
/// A data object is written by one application and read by another, or by another part of the same
/// one, so the identity of a format is the only thing the two sides agree on. These tests pin that
/// identity down: case-insensitive equality with a matching hash code (or a dictionary keyed by
/// format would miss), and nothing else.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("DragDrop")]
public sealed class DataFormatTests
{
    [TestMethod]
    public void AFormatIsItsName()
    {
        var format = DataFormats.Custom("MyFormat");

        Assert.AreEqual("MyFormat", format.Name);
        Assert.AreEqual("MyFormat", format.ToString());
    }

    [TestMethod]
    public void FormatsAreComparedWithoutCase()
    {
        var upper = DataFormats.Custom("TEXT");
        var lower = DataFormats.Custom("text");

        Assert.AreEqual(upper, lower, "the same format named in another case is the same format");
        Assert.IsTrue(upper == lower);
        Assert.AreEqual(
            upper.GetHashCode(),
            lower.GetHashCode(),
            "equal formats must hash alike, or a dictionary keyed by format would miss");
        Assert.IsFalse(upper != lower);
    }

    [TestMethod]
    public void DifferentNamesAreDifferentFormats()
    {
        var one = DataFormats.Custom("One");

        Assert.AreNotEqual(one, DataFormats.Custom("Two"));
        Assert.IsFalse(one == DataFormats.Custom("Two"));
        Assert.IsFalse(one.Equals("One"), "a format is not equal to a string that happens to name it");
    }

    [TestMethod]
    public void TheWellKnownFormatsAreDistinct()
    {
        var formats = new[]
        {
            DataFormats.Text,
            DataFormats.Html,
            DataFormats.FileDrop,
        };

        Assert.AreEqual(3, formats.Distinct().Count());
        Assert.AreEqual("Text", DataFormats.Text.Name);
        Assert.AreEqual("Html", DataFormats.Html.Name);
        Assert.AreEqual("FileDrop", DataFormats.FileDrop.Name);
    }

    [TestMethod]
    public void ANamelessFormatIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new DataFormat(""));
        Assert.ThrowsExactly<ArgumentException>(() => _ = new DataFormat("   "));
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new DataFormat(null!));
    }

    [TestMethod]
    public void TheDefaultFormatIsEmptyAndEqualToItself()
    {
        DataFormat empty = default;

        Assert.AreEqual(string.Empty, empty.Name, "an uninitialized format reports no name rather than null");
        Assert.AreEqual(string.Empty, empty.ToString());
        Assert.AreEqual(empty, default(DataFormat));
        Assert.AreNotEqual(empty, DataFormats.Text);
    }
}
