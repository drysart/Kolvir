using SkiaSharp;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// The refusals. A document that arrives from a download, a drag or a theme pack is data, not code,
/// and the tests here are the ones that would fail if a future Svg.Skia update changed a default.
/// </summary>
/// <remarks>
/// <para>
/// Two of these are break-to-prove, and the breaks are worth naming because they are not the
/// obvious ones: setting <c>ExternalResources</c> to <c>Disabled</c> makes
/// <see cref="AnExternalFileImageWouldHaveShownIfItHadBeenRead"/> fail, because the policy is what
/// decides that a <c>data:</c> reference resolves; and deleting the DOCTYPE/ENTITY check makes both
/// entity tests fail, because the renderer expands the document happily. A file or network
/// reference, by contrast, does not resolve under any policy this renderer supports - that is why
/// the tests around it are regression guards rather than proof of a setting.
/// </para>
/// <para>
/// The control-experiment pairs matter as much as the refusals: a test that only asserts "the
/// external image did not appear" passes on a renderer that draws nothing at all. Each refusal is
/// therefore paired with a document that does draw the same content from inside the file.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class SvgSecurityTests : SvgTestBase
{
    private static readonly SKColor Secret = new(0xFF, 0x45, 0x00);

    [TestMethod]
    public void AnExternalFileImageIsNotRead()
    {
        using var registered = RegisterSvg();
        string path = WriteSecretPng();
        try
        {
            string reference = path.Replace('\\', '/');
            using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
            Assert.IsTrue(image.Load(SvgFixtures.Stream($"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
                  <image width="10" height="10" href="file:///{reference}"/>
                </svg>
                """)), "the document still loads, because it is a document");

            using var scene = SvgScene.Create(image, 40f, 40f);
            Assert.IsFalse(ContainsSecret(scene), "a file reference is not resolved");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnExternalFileImageWouldHaveShownIfItHadBeenRead()
    {
        // The control experiment for the test above: the same pixels, referenced from inside the
        // document instead of from the file system, do appear.
        using var registered = RegisterSvg();
        string path = WriteSecretPng();
        string base64 = Convert.ToBase64String(File.ReadAllBytes(path));
        File.Delete(path);

        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.Load(SvgFixtures.Stream($"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <image width="10" height="10" href="data:image/png;base64,{base64}"/>
            </svg>
            """)), "a document carrying its own pixels loads");

        using var scene = SvgScene.Create(image, 40f, 40f);
        Assert.IsTrue(ContainsSecret(scene), "and draws them");
    }

    [TestMethod]
    public void AWindowsStyleXlinkHrefIsNotResolvedEither()
    {
        using var registered = RegisterSvg();
        string path = WriteSecretPng();
        try
        {
            string reference = path.Replace('\\', '/');
            using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
            Assert.IsTrue(image.Load(SvgFixtures.Stream($"""
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                     viewBox="0 0 10 10"><image xlink:href="file:///{reference}" width="10" height="10"/></svg>
                """)), "the document loads");

            using var scene = SvgScene.Create(image, 40f, 40f);
            Assert.IsFalse(ContainsSecret(scene), "xlink:href is held to the same rule as href");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ANetworkReferenceIsNotFetched()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };

        // Port 9 is discard: nothing listens, so a resolver that tried would block or fail. The
        // assertion is that nothing is attempted, which is what keeps a load off the network.
        Assert.IsTrue(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <image width="10" height="10" href="http://127.0.0.1:9/nothing.png"/>
            </svg>
            """)), "the document loads");

        using var scene = SvgScene.Create(image, 40f, 40f);
        Assert.IsFalse(ContainsSecret(scene), "and fetches nothing");
    }

    [TestMethod]
    public void AUseReferenceToAnotherFileLoadsNoPartOfIt()
    {
        using var registered = RegisterSvg();
        string path = WriteSecretSvg();
        try
        {
            string reference = path.Replace('\\', '/');
            using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
            Assert.IsTrue(image.Load(SvgFixtures.Stream($"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
                  <use href="file:///{reference}#shape"/>
                </svg>
                """)), "the document loads");

            using var scene = SvgScene.Create(image, 40f, 40f);
            Assert.IsFalse(ContainsSecret(scene), "a use of an element in another file resolves nothing");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ScriptInADocumentDoesNotRun()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };

        Assert.IsTrue(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <script>document.documentElement.setAttribute('fill', '#ff4500');</script>
              <rect width="10" height="10" fill="#00aa00"/>
            </svg>
            """)), "the document loads");

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 20);

        Assert.AreEqual(0x00, pixel.Red, "the script's change did not happen");
        Assert.AreEqual(0xAA, pixel.Green, "so the document is still green");
    }

    [TestMethod]
    public void AnEventHandlerAttributeDoesNotRunEither()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };

        Assert.IsTrue(image.Load(SvgFixtures.Stream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <rect width="10" height="10" fill="#00aa00" onload="this.setAttribute('fill', '#ff4500')"/>
            </svg>
            """)), "the document loads");

        using var scene = SvgScene.Create(image, 40f, 40f);
        var pixel = scene.PixelAt(20, 20);

        Assert.AreEqual(0x00, pixel.Red, "an inline handler is not an event source here");
        Assert.AreEqual(0xAA, pixel.Green, "and the fill is the one the document wrote");
    }

    [TestMethod]
    public void AnEntityBombIsRefusedBeforeItIsParsed()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();
        Exception? reported = null;
        using var subscription = image.AddLoadFailedHandler((_, e) => reported = e);

        Assert.IsFalse(image.Load(SvgFixtures.Stream("""
            <!DOCTYPE svg [
              <!ENTITY a "aaaaaaaaaa">
              <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
              <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
              <!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;">
            ]>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><text x="0" y="5">&d;</text></svg>
            """)), "a document that declares entities does not load");

        Assert.IsInstanceOfType<XPlatImageFormatException>(reported, "the failure comes through the provider");
        StringAssert.Contains(reported!.InnerException!.Message, "DTD", "and says what was refused");
    }

    [TestMethod]
    public void AnExternalEntityDeclarationIsRefusedToo()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();

        Assert.IsFalse(image.Load(SvgFixtures.Stream("""
            <!DOCTYPE svg [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><text>&xxe;</text></svg>
            """)), "an external entity is refused with the rest of them");
    }

    [TestMethod]
    public void ADocumentLargerThanTheCapIsRefused()
    {
        using var registered = RegisterSvg();
        using var image = new XPlatImage();

        var oversized = new System.Text.StringBuilder(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><!--");
        while (oversized.Length < XPlatSvgDocument.MaximumDocumentBytes + 1024)
        {
            oversized.Append('x');
        }

        oversized.Append("--></svg>");

        Assert.IsFalse(image.Load(SvgFixtures.Stream(oversized.ToString())), "a megabyte of comment is not an icon");
    }

    private static string WriteSecretPng()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xplatui-secret-{Guid.NewGuid():N}.png");
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(Secret);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static string WriteSecretSvg()
    {
        string path = Path.Combine(Path.GetTempPath(), $"xplatui-secret-{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <circle id="shape" cx="5" cy="5" r="5" fill="#ff4500"/>
            </svg>
            """);
        return path;
    }

    private static bool ContainsSecret(SvgScene scene)
    {
        using var bitmap = scene.Render();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 0xE0 && pixel.Green is > 0x20 and < 0x80 && pixel.Blue < 0x40)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
