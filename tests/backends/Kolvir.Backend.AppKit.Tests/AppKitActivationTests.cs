namespace Kolvir.Backend.AppKit.Tests;

/// <summary>
/// Which application taking the activation a shown popup survives, and the default everything else
/// takes.
/// </summary>
/// <remarks>
/// The palette's identifier is spelled out here rather than read from
/// <see cref="AppKitActivation.CharacterPalette"/>, because the identifier is the part that belongs to
/// macOS and not to this repository: if the system ever renames the character palette, this suite is
/// what should say so, and a test that asked the implementation for its own constant would keep
/// passing while every emoji picked into a popup dropped it again. The positive rows below are the
/// measurement - clicking a character in the palette deactivates the application to
/// <c>com.apple.CharacterPaletteIM</c> and hands the commit back half a second later - and the
/// negative rows are the cases that must keep dismissing: the desktop, another application, and an
/// application this does not know.
/// </remarks>
[TestClass]
[TestCategory("Platform.AppKit")]
public sealed class AppKitActivationTests
{
    [TestMethod]
    public void TheCharacterPaletteIsTheOneTextInputSurface()
    {
        Assert.IsTrue(
            AppKitActivation.IsTextInputSurface("com.apple.CharacterPaletteIM"),
            "clicking a character in the palette is a commit into the focused field, not the user leaving");
    }

    [TestMethod]
    [DataRow("com.apple.finder", DisplayName = "the desktop, which Finder takes the activation for")]
    [DataRow("com.apple.dock", DisplayName = "the Dock")]
    [DataRow("com.apple.Safari", DisplayName = "another application")]
    [DataRow("dev.kolvir.demo", DisplayName = "an application of our own")]
    [DataRow("com.apple.CharacterPaletteIM.evil", DisplayName = "an identifier that only starts like the palette")]
    [DataRow("", DisplayName = "an empty identifier")]
    [DataRow(null, DisplayName = "no frontmost application at all")]
    public void EveryOtherApplicationDismissesThePopup(string? bundleIdentifier)
    {
        Assert.IsFalse(
            AppKitActivation.IsTextInputSurface(bundleIdentifier),
            $"{bundleIdentifier ?? "null"} is not a text input surface, so losing the activation to it "
            + "is the user leaving and the popup must close");
    }
}
