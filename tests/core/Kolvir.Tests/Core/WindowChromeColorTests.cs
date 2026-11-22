using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The two window colours as the application sees them: a literal, a palette role or inherit, and
/// the value the platform is handed in each case.
/// </summary>
/// <remarks>
/// The platform owns these pixels, so there is no <c>Effective*</c> to read: the assertion is what
/// reached the backend. The recorder in <c>TestSupport</c> is a headless window that reports the
/// window-colours capability and keeps the last value it was given.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class WindowChromeColorTests : HeadlessTestBase
{
    private static void WithWindow(Action<Window, ChromeColorWindowBackend> body)
    {
        var backend = new ChromeColorApplicationBackend();
        var app = new Application(
            backend,
            new ApplicationOptions
            {
                ApplicationName = "tests",
                ShutdownMode = ShutdownMode.OnLastWindowClose,
            });

        try
        {
            var window = new Window("chrome", new Panel());
            window.Show();
            body(window, backend.Window);
        }
        finally
        {
            app.Dispose();
        }
    }

    [TestMethod]
    public void TheChromeColoursAcceptAColourARoleOrInherit()
    {
        WithWindow((window, backend) =>
        {
            Assert.IsTrue(
                window.CaptionColor.TryGetValue<InheritedValue>(out _),
                "an unassigned caption colour reads back as inherit");
            Assert.IsNull(backend.CaptionColor, "and the platform keeps its own default");

            window.CaptionColor = new SKColor(0x12, 0x34, 0x56);
            Assert.IsTrue(window.CaptionColor.TryGetValue<SKColor>(out _), "the property reports the colour it was given");
            Assert.AreEqual<SKColor?>(new SKColor(0x12, 0x34, 0x56), backend.CaptionColor, "and the platform is given it");

            window.CaptionColor = ThemeColor.Surface;
            Assert.IsTrue(window.CaptionColor.TryGetValue<ThemeColor>(out var captionRole));
            Assert.AreEqual(ThemeColor.Surface, captionRole, "a role is reported as the role");
            Assert.AreEqual<SKColor?>(Theme.Surface, backend.CaptionColor, "and resolved when it is assigned");

            window.CaptionColor = InheritedValue.Instance;
            Assert.IsNull(backend.CaptionColor, "and inherit hands the surface back to the platform");

            window.BorderColor = ThemeColor.Border;
            Assert.AreEqual<SKColor?>(Theme.Border, backend.BorderColor, "the border follows the same rules");

            window.BorderColor = InheritedValue.Instance;
            Assert.IsNull(backend.BorderColor, "including handing its line back");
        });
    }

    [TestMethod]
    public void AnAssignedRoleIsResolvedAgainWhenThePaletteChanges()
    {
        WithWindow((window, backend) =>
        {
            window.CaptionColor = ThemeColor.Surface;
            window.BorderColor = ThemeColor.Border;

            Assert.AreEqual<SKColor?>(Theme.Surface, backend.CaptionColor);
            Assert.AreEqual<SKColor?>(Theme.Border, backend.BorderColor);

            Theme.Palette = Palette.Dark;

            Assert.AreEqual<SKColor?>(
                Theme.Surface,
                backend.CaptionColor,
                "a role follows the palette the platform cannot read");
            Assert.AreEqual<SKColor?>(Theme.Border, backend.BorderColor, "for both colours");
        });
    }

    [TestMethod]
    public void ALiteralIsNotReResolvedWhenThePaletteChanges()
    {
        WithWindow((window, backend) =>
        {
            var literal = new SKColor(0x12, 0x34, 0x56);
            window.CaptionColor = literal;
            window.BorderColor = literal;

            Assert.AreEqual(1, backend.CaptionColorPushes, "the literal is handed over once");
            Assert.AreEqual(1, backend.BorderColorPushes, "and so is the border");

            Theme.Palette = Palette.Dark;

            Assert.AreEqual<SKColor?>(literal, backend.CaptionColor, "a literal has nothing to re-resolve");
            Assert.AreEqual<SKColor?>(literal, backend.BorderColor);
            Assert.AreEqual(1, backend.CaptionColorPushes, "so the platform is not handed it again");
            Assert.AreEqual(1, backend.BorderColorPushes, "for either colour");
        });
    }

    [TestMethod]
    public void ARoleThatResolvesToTheSameColourIsNotPushedAgain()
    {
        WithWindow((window, backend) =>
        {
            var surface = Theme.Surface;
            window.CaptionColor = ThemeColor.Surface;

            Assert.AreEqual(1, backend.CaptionColorPushes);

            // A dark palette that keeps the light surface: the role still resolves to the colour
            // the backend already holds, so there is nothing to hand over.
            Theme.Palette = Palette.Dark with { Surface = surface };

            Assert.AreEqual<SKColor?>(surface, backend.CaptionColor, "the role still resolves to the same colour");
            Assert.AreEqual(1, backend.CaptionColorPushes, "and the window does not churn the platform");
        });
    }

    [TestMethod]
    public void ARefusedRequestLeavesTheColourUnchanged()
    {
        var app = new Application(
            new Kolvir.Backend.Headless.HeadlessApplicationBackend(),
            new ApplicationOptions
            {
                ApplicationName = "tests",
                ShutdownMode = ShutdownMode.OnLastWindowClose,
            });

        try
        {
            var window = new Window("chrome", new Panel());

            Assert.ThrowsExactly<NotSupportedException>(() => window.CaptionColor = ThemeColor.Accent);
            Assert.ThrowsExactly<NotSupportedException>(() => window.BorderColor = ThemeColor.Accent);

            Assert.IsTrue(
                window.CaptionColor.TryGetValue<InheritedValue>(out _),
                "the refused role is not remembered as an assignment");
            Assert.IsTrue(window.BorderColor.TryGetValue<InheritedValue>(out _));
        }
        finally
        {
            app.Dispose();
        }
    }

    [TestMethod]
    public void AnUnspecifiedChromeColourIsRefused()
    {
        WithWindow((window, backend) =>
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => window.CaptionColor = default);
            Assert.ThrowsExactly<InvalidOperationException>(() => window.BorderColor = default);
            Assert.IsNull(backend.CaptionColor, "and nothing reached the platform");
            Assert.IsNull(backend.BorderColor);
        });
    }
}
