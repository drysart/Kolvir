using XPlatUI.DragDrop;

namespace XPlatUI.DragDrop.Tests;

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
        var format = XPlatDataFormats.Custom("MyFormat");

        Assert.AreEqual("MyFormat", format.Name);
        Assert.AreEqual("MyFormat", format.ToString());
    }

    [TestMethod]
    public void FormatsAreComparedWithoutCase()
    {
        var upper = XPlatDataFormats.Custom("TEXT");
        var lower = XPlatDataFormats.Custom("text");

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
        var one = XPlatDataFormats.Custom("One");

        Assert.AreNotEqual(one, XPlatDataFormats.Custom("Two"));
        Assert.IsFalse(one == XPlatDataFormats.Custom("Two"));
        Assert.IsFalse(one.Equals("One"), "a format is not equal to a string that happens to name it");
    }

    [TestMethod]
    public void TheWellKnownFormatsAreDistinct()
    {
        var formats = new[]
        {
            XPlatDataFormats.Text,
            XPlatDataFormats.Html,
            XPlatDataFormats.FileDrop,
        };

        Assert.AreEqual(3, formats.Distinct().Count());
        Assert.AreEqual("Text", XPlatDataFormats.Text.Name);
        Assert.AreEqual("Html", XPlatDataFormats.Html.Name);
        Assert.AreEqual("FileDrop", XPlatDataFormats.FileDrop.Name);
    }

    [TestMethod]
    public void ANamelessFormatIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new XPlatDataFormat(""));
        Assert.ThrowsExactly<ArgumentException>(() => _ = new XPlatDataFormat("   "));
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new XPlatDataFormat(null!));
    }

    [TestMethod]
    public void TheDefaultFormatIsEmptyAndEqualToItself()
    {
        XPlatDataFormat empty = default;

        Assert.AreEqual(string.Empty, empty.Name, "an uninitialized format reports no name rather than null");
        Assert.AreEqual(string.Empty, empty.ToString());
        Assert.AreEqual(empty, default(XPlatDataFormat));
        Assert.AreNotEqual(empty, XPlatDataFormats.Text);
    }
}
