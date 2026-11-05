using SkiaSharp;
using XPlatUI;
using XPlatUI.AutoComplete;

namespace XPlatUI.Tests;

/// <summary>
/// The text box's autocomplete extension: the opt-in suggestion source, the append that fills the
/// rest of the word in and selects it, and what happens when nothing matches.
/// </summary>
/// <remarks>
/// The extension lives on the text box itself, so these tests also pin the per-control part of it:
/// two boxes with different modes and sources do not see each other's suggestions.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class AutoCompleteTests : HeadlessTestBase
{
    [TestMethod]
    public void AppendFillsInTheRestOfTheFirstMatch()
    {
        var box = NewBox(XPlatAutoCompleteMode.Append, "Amsterdam", "Rotterdam");

        using var scene = HeadlessScene.Create(box, 300f, 60f);
        box.Focus();

        scene.Type("Ams");

        Assert.AreEqual("Amsterdam", box.Text, "the rest of the match should be appended");
        Assert.AreEqual("terdam", box.SelectedText, "and selected, so the next keystroke replaces it");
    }

    [TestMethod]
    public void TheNextKeystrokeReplacesTheCompletion()
    {
        var box = NewBox(XPlatAutoCompleteMode.Append, "Amsterdam");

        using var scene = HeadlessScene.Create(box, 300f, 60f);
        box.Focus();

        scene.Type("Ams");
        scene.Type("x");

        Assert.AreEqual("Amsx", box.Text, "typing over the selected completion should replace it");
    }

    [TestMethod]
    public void NoMatchLeavesWhatWasTyped()
    {
        var box = NewBox(XPlatAutoCompleteMode.Append, "Amsterdam");

        using var scene = HeadlessScene.Create(box, 300f, 60f);
        box.Focus();

        scene.Type("Zzz");

        Assert.AreEqual("Zzz", box.Text);
        Assert.AreEqual(3, box.CaretIndex);
    }

    [TestMethod]
    public void TheMatchIsCaseInsensitive()
    {
        var box = NewBox(XPlatAutoCompleteMode.Append, "Amsterdam");

        using var scene = HeadlessScene.Create(box, 300f, 60f);
        box.Focus();

        scene.Type("ams");

        Assert.AreEqual("Amsterdam", box.Text, "a prefix matches regardless of case");
    }

    [TestMethod]
    public void AutoCompleteOffDoesNothing()
    {
        var box = NewBox(XPlatAutoCompleteMode.None, "Amsterdam");

        using var scene = HeadlessScene.Create(box, 300f, 60f);
        box.Focus();

        scene.Type("Ams");

        Assert.AreEqual("Ams", box.Text);
    }

    [TestMethod]
    public void TheCustomSourceBelongsToItsOwnTextBox()
    {
        var first = NewBox(XPlatAutoCompleteMode.Append, "Amsterdam");
        var second = NewBox(XPlatAutoCompleteMode.Append, "Rotterdam");

        using var firstScene = HeadlessScene.Create(first, 300f, 60f);
        first.Focus();
        firstScene.Type("Ams");

        Assert.AreEqual("Amsterdam", first.Text);
        Assert.AreEqual(1, second.AutoCompleteCustomSource.Count, "the second box keeps its own source");
        Assert.AreEqual("Rotterdam", second.AutoCompleteCustomSource[0]);
    }

    [TestMethod]
    public void TheModeAndSourceAreReadableBackAsAssigned()
    {
        var box = new XPlatTextBox();

        Assert.AreEqual(XPlatAutoCompleteMode.None, box.AutoCompleteMode, "autocomplete is opt-in");
        Assert.AreEqual(XPlatAutoCompleteSource.CustomSource, box.AutoCompleteSource);

        box.AutoCompleteMode = XPlatAutoCompleteMode.SuggestAppend;
        box.AutoCompleteSource = XPlatAutoCompleteSource.HistoryList;

        Assert.AreEqual(XPlatAutoCompleteMode.SuggestAppend, box.AutoCompleteMode);
        Assert.AreEqual(XPlatAutoCompleteSource.HistoryList, box.AutoCompleteSource);
        Assert.AreEqual(0, box.AutoCompleteCustomSource.Count, "a box starts with no suggestions of its own");
    }

    [TestMethod]
    public void AReadOnlyBoxTakesNoCompletion()
    {
        var box = NewBox(XPlatAutoCompleteMode.Append, "Amsterdam");
        box.ReadOnly = true;

        using var scene = HeadlessScene.Create(box, 300f, 60f);
        box.Focus();

        scene.Type("Ams");

        Assert.AreEqual(string.Empty, box.Text, "a read-only field accepts no text, so there is nothing to complete");
    }

    /// <summary>One text box with autocomplete on and the given custom source.</summary>
    private static XPlatTextBox NewBox(XPlatAutoCompleteMode mode, params string[] source)
    {
        var box = new XPlatTextBox
        {
            Dock = DockStyle.Fill,
            AutoCompleteMode = mode,
            AutoCompleteSource = XPlatAutoCompleteSource.CustomSource,
        };

        foreach (var value in source)
        {
            box.AutoCompleteCustomSource.Add(value);
        }

        return box;
    }
}
