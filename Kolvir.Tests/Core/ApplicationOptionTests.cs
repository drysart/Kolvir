using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Tests;

/// <summary>
/// The application options' defaults and the command-line contract. The hardware-acceleration
/// option is read by the Win32 and AppKit backends at window creation, so what the constructor
/// resolves it to - the platform default or the switch - decides whether a window ever asks for a
/// GL or Metal surface.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class ApplicationOptionTests
{
    [TestMethod]
    public void HardwareAccelerationDefaultsPerPlatform()
    {
        // Windows and Linux default to acceleration; macOS defaults to the CPU path because the
        // measured Metal path is slower on light text-heavy content. Written with the same
        // predicate the constructor uses, so the assertion is meaningful on all three CI legs.
        var options = new ApplicationOptions([]);

        Assert.AreEqual(!OperatingSystem.IsMacOS(), options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void MacOsDefaultsHardwareAccelerationOff()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("The macOS default is asserted by the macOS CI leg and the Mac test host.");
        }

        // This is the whole reason the platform default exists: Metal is 1.6x slower than the CPU
        // path on light content, so an unopted-in macOS application must not get it.
        var options = new ApplicationOptions([]);

        Assert.IsFalse(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void TheSwitchTurnsAccelerationOffOnEveryPlatform()
    {
        var options = new ApplicationOptions(["--no-hardware-acceleration"]);

        Assert.IsFalse(options.GfxHardwareAcceleration);

        // Even where the default is already off, the switch must not turn it back on.
        if (OperatingSystem.IsMacOS())
        {
            Assert.IsFalse(new ApplicationOptions([]).GfxHardwareAcceleration);
        }
    }

    [TestMethod]
    public void TheConstructorReadsTheSuppliedCommandLine()
    {
        var options = new ApplicationOptions(["--no-hardware-acceleration"]);

        Assert.IsFalse(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void CommandLineAssignedAfterConstructionDoesNotChangeTheDefault()
    {
        // The constructor is where the platform default and the switch are decided. Setting
        // CommandLine later must not reach back into GfxHardwareAcceleration: a launcher that owns
        // its arguments uses the constructor overload or assigns the option itself.
        var options = new ApplicationOptions
        {
            CommandLine = ["--no-hardware-acceleration"],
        };

        Assert.AreEqual(!OperatingSystem.IsMacOS(), options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void AnExplicitAssignmentWinsOverTheSwitch()
    {
        var options = new ApplicationOptions(["--no-hardware-acceleration"])
        {
            GfxHardwareAcceleration = true,
        };

        Assert.IsTrue(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void AnExplicitAssignmentIsNotUndoneByALaterCommandLine()
    {
        var options = new ApplicationOptions
        {
            GfxHardwareAcceleration = true,
            CommandLine = ["--no-hardware-acceleration"],
        };

        Assert.IsTrue(options.GfxHardwareAcceleration);

        // And the other direction: an explicit false stays false even for a command line with no
        // switch.
        var disabled = new ApplicationOptions
        {
            GfxHardwareAcceleration = false,
            CommandLine = [],
        };

        Assert.IsFalse(disabled.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void TheSwitchIsCaseInsensitiveAndIgnoresOtherArguments()
    {
        var options = new ApplicationOptions(["--page", "3", "--NO-HARDWARE-ACCELERATION"]);

        Assert.IsFalse(options.GfxHardwareAcceleration);

        options.GfxHardwareAcceleration = true;
        options.CommandLine = ["--page", "3"];

        Assert.IsTrue(options.GfxHardwareAcceleration, "an explicit assignment is not undone by a later CommandLine");
    }

    [TestMethod]
    public void ResolveCommandLineFallsBackToTheProcessArguments()
    {
        var options = new ApplicationOptions();

        CollectionAssert.AreEqual(
            Environment.GetCommandLineArgs()[1..],
            options.ResolveCommandLine().ToArray());
    }
}
