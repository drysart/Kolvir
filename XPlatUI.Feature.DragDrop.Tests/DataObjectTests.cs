using XPlatUI.DragDrop;

namespace XPlatUI.DragDrop.Tests;

/// <summary>
/// The payload of a drag: several representations of the same content, typed by format, with a
/// removal rule that is the same for every format.
/// </summary>
/// <remarks>
/// Null meaning "remove this representation" is the whole of the write rule, and it is what makes
/// <c>SetText(null)</c> the way to take text out of a drag that still carries HTML. The typed
/// readers are deliberately forgiving: a wrong-shaped value is present but not readable as that
/// shape, which is what lets a target skip a representation it cannot use and try the next one.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("DragDrop")]
public sealed class DataObjectTests
{
    [TestMethod]
    public void ANewDataObjectCarriesNothing()
    {
        var data = new XPlatDataObject();

        Assert.IsTrue(data.IsEmpty);
        Assert.AreEqual(0, data.Formats.Count);
        Assert.IsNull(data.GetText());
        Assert.IsNull(data.GetHtml());
        Assert.AreEqual(0, data.GetFiles().Length, "no files reads as an empty list rather than null");
        Assert.IsFalse(data.Contains(XPlatDataFormats.Text));
    }

    [TestMethod]
    public void TextRoundTrips()
    {
        var data = new XPlatDataObject { };

        data.SetText("hello");

        Assert.IsFalse(data.IsEmpty);
        Assert.AreEqual("hello", data.GetText());
        Assert.AreEqual("hello", data.GetData(XPlatDataFormats.Text), "the raw value is the string itself");
        Assert.IsTrue(data.Contains(XPlatDataFormats.Text));
    }

    [TestMethod]
    public void TheConstructorWithTextCarriesJustThatText()
    {
        var data = new XPlatDataObject("carried");

        Assert.AreEqual("carried", data.GetText());
        Assert.AreEqual(1, data.Formats.Count);
    }

    [TestMethod]
    public void SettingNullRemovesARepresentation()
    {
        var data = new XPlatDataObject();
        data.SetText("text");
        data.SetHtml("<b>html</b>");
        Assert.AreEqual(2, data.Formats.Count);

        data.SetText(null);

        Assert.IsNull(data.GetText());
        Assert.IsTrue(data.Contains(XPlatDataFormats.Html), "removing the text leaves the HTML alone");
        Assert.AreEqual(1, data.Formats.Count);

        data.SetHtml(null);
        Assert.IsTrue(data.IsEmpty, "and removing the last representation empties the object");
    }

    [TestMethod]
    public void FilesRoundTripAndBlankPathsAreDropped()
    {
        var data = new XPlatDataObject();

        data.SetFiles(["C:/one.txt", "", "C:/two.txt", null!]);

        CollectionAssert.AreEqual(
            new[] { "C:/one.txt", "C:/two.txt" },
            data.GetFiles(),
            "a path that names nothing is not a file being dragged");
        Assert.IsTrue(data.Contains(XPlatDataFormats.FileDrop));
    }

    [TestMethod]
    public void AnEmptyFileListRemovesTheFormat()
    {
        var data = XPlatDataObject.FromFiles(["C:/one.txt"]);
        Assert.AreEqual(1, data.GetFiles().Length);

        data.SetFiles([]);
        Assert.IsFalse(data.Contains(XPlatDataFormats.FileDrop), "an empty list is no representation at all");

        data.SetFiles(["C:/one.txt"]);
        data.SetFiles(null);
        Assert.IsFalse(data.Contains(XPlatDataFormats.FileDrop));
    }

    [TestMethod]
    public void FromFilesAndFromTextCarryDifferentRepresentations()
    {
        var files = XPlatDataObject.FromFiles(["C:/one.txt"]);
        var text = XPlatDataObject.FromText("C:/one.txt");

        Assert.IsTrue(files.Contains(XPlatDataFormats.FileDrop));
        Assert.IsFalse(files.Contains(XPlatDataFormats.Text), "a file drag is not also a text drag");
        Assert.IsTrue(text.Contains(XPlatDataFormats.Text));
        Assert.IsFalse(text.Contains(XPlatDataFormats.FileDrop));
    }

    [TestMethod]
    public void ATypedReadOfTheWrongShapeGivesNothing()
    {
        var data = new XPlatDataObject();
        data.SetData(XPlatDataFormats.Text, 42);

        Assert.IsTrue(data.Contains(XPlatDataFormats.Text), "the representation is there");
        Assert.IsNull(data.GetText(), "but it is not readable as text");

        data.SetData(XPlatDataFormats.FileDrop, "C:/one.txt");
        Assert.AreEqual(0, data.GetFiles().Length);
    }

    [TestMethod]
    public void CustomDataKeepsItsIdentity()
    {
        var payload = new object();
        var format = XPlatDataFormats.Custom("MyPayload");
        var data = new XPlatDataObject();

        data.SetData(format, payload);

        Assert.AreSame(payload, data.GetData(XPlatDataFormats.Custom("mypayload")), "read back by name, not by reference");
    }

    [TestMethod]
    public void TheFormatsListWhatWasSetWhateverTheCaseOfTheName()
    {
        var data = new XPlatDataObject();
        data.SetText("text");
        data.SetData(XPlatDataFormats.Custom("Thing"), 1);

        data.SetData(XPlatDataFormats.Custom("THING"), 2);

        Assert.AreEqual(2, data.Formats.Count, "the same format set twice is one representation");
        Assert.AreEqual(2, data.GetData(XPlatDataFormats.Custom("thing")));
    }

    [TestMethod]
    public void ReplacingARepresentationReplacesItsValue()
    {
        var data = new XPlatDataObject();
        data.SetHtml("<b>one</b>");
        data.SetHtml("<i>two</i>");

        Assert.AreEqual("<i>two</i>", data.GetHtml());
        Assert.AreEqual(1, data.Formats.Count);
    }
}
