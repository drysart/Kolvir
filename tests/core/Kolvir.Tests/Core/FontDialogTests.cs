using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// The font dialog seam and the pure conversions behind its three native implementations. The
/// scripted half runs through the headless backend; the mapping half needs no dialog at all, which
/// is the point of keeping it out of the backends.
/// </summary>
/// <remarks>
/// What cannot be exercised here is a real native dialog: <c>ChooseFontW</c>,
/// <c>Gtk.FontChooserDialog</c> and <c>NSFontPanel</c> only exist on their own platforms and only
/// answer a person. Those are checked on a real window, not in this suite.
/// </remarks>
[TestClass]
public sealed class FontDialogTests : HeadlessTestBase
{
    [TestMethod]
    [TestCategory("Headless")]
    public void TheFontDialogReturnsTheScriptedSelection()
    {
        using var app = NewApplication();
        var service = (HeadlessDialogService)app.Dialogs;
        var options = new FontDialogOptions
        {
            Title = "Choose a font",
            InitialFamilyName = "IBM Plex Sans",
            InitialSize = 17.5f,
            InitialWeight = 600,
            InitialItalic = true,
            MinimumSize = 8f,
            MaximumSize = 40f,
        };
        var scripted = new FontSelection("Helvetica", 21f, 700, italic: true);

        service.FontHandler = (seen, owner) =>
        {
            Assert.AreSame(options, seen, "the backend should see the object the caller passed");
            Assert.IsNull(owner, "no owner was given");
            return scripted;
        };

        var result = app.Dialogs.PickFont(options, null);

        Assert.AreSame(scripted, result);
        Assert.AreEqual(1, service.FontRequests.Count);

        var request = service.FontRequests[0];
        Assert.AreEqual("Choose a font", request.Title);
        Assert.AreEqual("IBM Plex Sans", request.InitialFamilyName);
        Assert.AreEqual(17.5f, request.InitialSize);
        Assert.AreEqual(600, request.InitialWeight);
        Assert.IsTrue(request.InitialItalic);
        Assert.AreEqual(8f, request.MinimumSize);
        Assert.AreEqual(40f, request.MaximumSize);
    }

    [TestMethod]
    [TestCategory("Headless")]
    public void CancellingTheFontDialogReturnsNull()
    {
        using var app = NewApplication();
        var service = (HeadlessDialogService)app.Dialogs;

        var result = app.Dialogs.PickFont(new FontDialogOptions(), null);

        Assert.IsNull(result, "the default scripted answer is the one a dismissed dialog gives");
        Assert.AreEqual(1, service.FontRequests.Count, "the request is still recorded");
    }

    [TestMethod]
    [TestCategory("Headless")]
    public void TheFontDialogRejectsMissingOptions()
    {
        using var app = NewApplication();

        Assert.ThrowsExactly<ArgumentNullException>(() => app.Dialogs.PickFont(null!, null));
    }

    [TestMethod]
    [TestCategory("Core")]
    public void ASelectionCreatesAFontTheCallerOwns()
    {
        var selection = new FontSelection("IBM Plex Sans", 19f, 600, italic: true);

        using var font = selection.CreateFont();

        Assert.AreEqual(19f, font.Size);
        Assert.AreEqual(600, font.Weight);
        Assert.IsTrue(font.Italic);
    }

    [TestMethod]
    [TestCategory("Core")]
    public void ASelectionClampsTheWeightAndRejectsAnEmptyFamily()
    {
        Assert.AreEqual(FontMapping.MinimumWeight, new FontSelection("Inter", 13f, 0).Weight);
        Assert.AreEqual(FontMapping.MaximumWeight, new FontSelection("Inter", 13f, 5000).Weight);

        Assert.ThrowsExactly<ArgumentException>(() => new FontSelection("   ", 13f));
    }

    [TestMethod]
    [TestCategory("Core")]
    public void TheFontSizeRangeClampsWhatTheDialogReceives()
    {
        var options = new FontDialogOptions { MinimumSize = 8f, MaximumSize = 24f };

        Assert.AreEqual(8f, options.ClampSize(4f));
        Assert.AreEqual(12f, options.ClampSize(12f));
        Assert.AreEqual(24f, options.ClampSize(30f));

        // A nonsensical range collapses to the minimum rather than throwing from a dialog.
        Assert.AreEqual(8f, new FontDialogOptions { MinimumSize = 8f, MaximumSize = 2f }.ClampSize(30f));
        Assert.AreEqual(1f, new FontDialogOptions { MinimumSize = 0f, MaximumSize = 0f }.ClampSize(30f));
    }

    [TestMethod]
    [TestCategory("Core")]
    public void TheNineCommonWeightsRoundTripThroughAppKitsScale()
    {
        foreach (var weight in new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 })
        {
            var appKit = FontMapping.ToAppKitWeight(weight);
            Assert.AreEqual(weight, FontMapping.FromAppKitWeight(appKit),
                $"weight {weight} should survive the AppKit conversion");
        }
    }

    [TestMethod]
    [TestCategory("Core")]
    public void AppKitsRegularAndBoldWeightsAreTheDocumentedIndices()
    {
        Assert.AreEqual(5, FontMapping.ToAppKitWeight(400), "5 is AppKit's regular");
        Assert.AreEqual(9, FontMapping.ToAppKitWeight(700), "9 is AppKit's bold");

        Assert.AreEqual(400, FontMapping.FromAppKitWeight(5));
        Assert.AreEqual(700, FontMapping.FromAppKitWeight(9));
        Assert.AreEqual(700, FontMapping.FromAppKitWeight(8), "AppKit's other bold face maps to 700");
        Assert.AreEqual(100, FontMapping.FromAppKitWeight(0));
        Assert.AreEqual(900, FontMapping.FromAppKitWeight(15));
    }

    [TestMethod]
    [TestCategory("Core")]
    public void PointsAndLogicalPixelsConvertAtNinetySixDpi()
    {
        Assert.AreEqual(9f, FontMapping.PixelsToPoints(12f));
        Assert.AreEqual(16f, FontMapping.PointsToPixels(12f));
        Assert.AreEqual(13f, FontMapping.PointsToPixels(FontMapping.PixelsToPoints(13f)), 0.0001f);
    }

    private static Application NewApplication() =>
        new(new HeadlessApplicationBackend(), new ApplicationOptions { ApplicationName = "tests" });
}
