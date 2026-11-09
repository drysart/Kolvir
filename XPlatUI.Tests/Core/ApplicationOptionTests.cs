using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XPlatUI.Tests;

/// <summary>
/// The application options' defaults and the command-line contract. The hardware-acceleration
/// option is read by the Win32 backend at window creation, so what it resolves to - and the order
/// in which <see cref="XPlatApplicationOptions.CommandLine"/> and the option itself are assigned -
/// decides whether a window ever asks for a GL surface.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class ApplicationOptionTests
{
    [TestMethod]
    public void HardwareAccelerationDefaultsToTrue()
    {
        var options = new XPlatApplicationOptions([]);

        Assert.IsTrue(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void TheConstructorReadsTheSuppliedCommandLine()
    {
        var options = new XPlatApplicationOptions(["--no-hardware-acceleration"]);

        Assert.IsFalse(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void CommandLineAssignedAfterConstructionAppliesTheSwitch()
    {
        var options = new XPlatApplicationOptions
        {
            CommandLine = ["--no-hardware-acceleration"],
        };

        Assert.IsFalse(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void AnExplicitAssignmentWinsOverTheSwitch()
    {
        var options = new XPlatApplicationOptions(["--no-hardware-acceleration"])
        {
            GfxHardwareAcceleration = true,
        };

        Assert.IsTrue(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void AnExplicitAssignmentAlsoWinsWhenItComesBeforeTheCommandLine()
    {
        var options = new XPlatApplicationOptions
        {
            GfxHardwareAcceleration = true,
            CommandLine = ["--no-hardware-acceleration"],
        };

        Assert.IsTrue(options.GfxHardwareAcceleration);
    }

    [TestMethod]
    public void TheSwitchIsCaseInsensitiveAndIgnoresOtherArguments()
    {
        var options = new XPlatApplicationOptions(["--page", "3", "--NO-HARDWARE-ACCELERATION"]);

        Assert.IsFalse(options.GfxHardwareAcceleration);

        options.GfxHardwareAcceleration = true;
        options.CommandLine = ["--page", "3"];

        Assert.IsTrue(options.GfxHardwareAcceleration, "an unrelated command line leaves the default alone");
    }

    [TestMethod]
    public void ResolveCommandLineFallsBackToTheProcessArguments()
    {
        var options = new XPlatApplicationOptions();

        CollectionAssert.AreEqual(
            Environment.GetCommandLineArgs()[1..],
            options.ResolveCommandLine().ToArray());
    }
}
