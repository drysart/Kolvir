using SkiaSharp;

namespace XPlatUI.Tests;

/// <summary>
/// The image format seam: what a provider may be, how two of them settle the same bytes, and what
/// a failing one does to a load.
/// </summary>
/// <remarks>
/// These are core behaviours, so they live in the core suite rather than in a feature's. An SVG
/// provider is one caller of the seam; the seam's own rules have to hold for a provider the
/// library has never heard of, which is what the stand-ins here are.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class ImageFormatProviderTests
{
    private readonly List<IDisposable> _registrations = [];

    [TestCleanup]
    public void UnregisterEverything()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    [TestMethod]
    public void TheBuiltInRasterDecoderIsAlwaysThereAndCannotBeRemoved()
    {
        // The control has to read a PNG with nothing installed, so the built-in provider is on the
        // list from the start and a provider list that could lose it would be a broken control.
        using var image = new XPlatImage();
        Assert.IsTrue(image.Load(new MemoryStream(Png(4, 2))), "a PNG loads with nothing registered");

        // There is no public handle for it, but a provider that claims everything can still outrank
        // it - which is the intended way to replace it.
        Assert.IsNotNull(image.Picture, "and produces a picture");
    }

    [TestMethod]
    public void AHigherPriorityProviderWins()
    {
        var lower = new StandIn("lower", priority: 1, magic: "MAGIC", width: 11f);
        var higher = new StandIn("higher", priority: 2, magic: "MAGIC", width: 22f);
        Register(lower);
        Register(higher);

        using var image = new XPlatImage();
        Assert.IsTrue(image.Load(Stream("MAGIC")), "the bytes load");
        Assert.AreEqual(22f, image.Picture!.CullRect.Width, 0.01f, "the higher priority provider decoded them");
        Assert.AreEqual(0, lower.Decodes, "and the other one was never asked");
        Assert.AreEqual(1, higher.Decodes, "exactly once");
    }

    [TestMethod]
    public void AtEqualPriorityTheLaterRegistrationWins()
    {
        var first = new StandIn("first", priority: 5, magic: "MAGIC", width: 11f);
        var second = new StandIn("second", priority: 5, magic: "MAGIC", width: 22f);
        Register(first);
        Register(second);

        using var image = new XPlatImage();
        Assert.IsTrue(image.Load(Stream("MAGIC")), "the bytes load");
        Assert.AreEqual(22f, image.Picture!.CullRect.Width, 0.01f, "the most recent registration decoded them");
    }

    [TestMethod]
    public void AProviderThatClaimsNothingIsNeverAskedToDecode()
    {
        var provider = new StandIn("shy", priority: 100, magic: "OTHER", width: 11f);
        Register(provider);

        using var image = new XPlatImage();
        Assert.IsFalse(image.Load(Stream("MAGIC")), "nothing claims these bytes");
        Assert.AreEqual(0, provider.Decodes, "a provider that did not claim was not consulted");
    }

    [TestMethod]
    public void AProviderThatThrowsFailsTheLoadInsteadOfFallingThrough()
    {
        // Claiming the bytes is a claim: the load does not fall through to the next provider,
        // because that is how a document one parser refused gets read by another anyway.
        var thrower = new StandIn("thrower", priority: 3, magic: "MAGIC", width: 11f) { Throws = true };
        var fallback = new StandIn("fallback", priority: 2, magic: "MAGIC", width: 22f);
        Register(thrower);
        Register(fallback);

        using var image = new XPlatImage();
        Exception? reported = null;
        using var subscription = image.AddLoadFailedHandler((_, e) => reported = e);

        Assert.IsFalse(image.Load(Stream("MAGIC")), "the load fails");
        Assert.IsInstanceOfType<XPlatImageFormatException>(reported, "and reports the provider's failure");
        Assert.AreEqual("thrower", ((XPlatImageFormatException)reported!).ProviderName, "naming the provider that claimed the bytes");
        Assert.AreEqual(0, fallback.Decodes, "the next claim was not tried");
        Assert.IsNull(image.Picture, "and nothing was installed");
    }

    [TestMethod]
    public void AProviderThatReturnsNoPictureFailsTheLoad()
    {
        var provider = new StandIn("empty", priority: 3, magic: "MAGIC", width: 11f) { ReturnsNull = true };
        Register(provider);

        using var image = new XPlatImage();
        Exception? reported = null;
        using var subscription = image.AddLoadFailedHandler((_, e) => reported = e);

        Assert.IsFalse(image.Load(Stream("MAGIC")), "the load fails");
        Assert.IsInstanceOfType<XPlatImageFormatException>(reported, "with the provider's name in the failure");
        Assert.IsNull(reported!.InnerException, "and no exception, because none was thrown");
    }

    [TestMethod]
    public void UnregisteringDoesNotTouchAPictureThatIsAlreadyLoaded()
    {
        var provider = new StandIn("temporary", priority: 3, magic: "MAGIC", width: 11f);
        var registration = Register(provider);

        using var image = new XPlatImage { Dock = DockStyle.Fill, SizeMode = XPlatSizeMode.StretchImage };
        Assert.IsTrue(image.Load(Stream("MAGIC")), "the picture loads");

        registration.Dispose();
        _registrations.Remove(registration);

        using var scene = HeadlessScene.Create(image, 40f, 40f);
        Assert.AreEqual(provider.Colour, scene.PixelAt(20, 20), "and the control still draws it");

        using var after = new XPlatImage();
        Assert.IsFalse(after.Load(Stream("MAGIC")), "while a later load no longer has the provider");
    }

    [TestMethod]
    public void TheExtensionIsPassedToTheProviderAsAHint()
    {
        var provider = new StandIn("hinted", priority: 3, magic: "MAGIC", width: 11f);
        Register(provider);

        string path = Path.Combine(Path.GetTempPath(), $"xplatui-hint-{Guid.NewGuid():N}.weird");
        File.WriteAllBytes(path, "MAGIC"u8.ToArray());
        try
        {
            using var image = new XPlatImage();
            Assert.IsTrue(image.Load(path), "a file whose extension is not a format still loads");
            Assert.AreEqual("weird", provider.LastHint, "and the extension arrives without its dot");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AProviderThatThrowsWhileSniffingHasNotClaimedTheBytes()
    {
        // Identifying a stream runs for every provider, so a bug there must not be able to turn a
        // load into an exception on the paint thread. It counts as "not mine" and the next provider
        // - here the built-in one - continues to answer.
        var broken = new StandIn("broken-sniff", priority: 9, magic: "MAGIC", width: 11f) { ThrowsFromCanDecode = true };
        Register(broken);

        using var image = new XPlatImage();
        Assert.IsTrue(image.Load(new MemoryStream(Png(4, 2))), "a PNG still loads past a provider that threw");

        using var magic = new XPlatImage();
        Assert.IsFalse(magic.Load(Stream("MAGIC")), "and bytes nobody else claims still fail quietly");
        Assert.AreEqual(0, broken.Decodes, "the broken provider was never asked to decode");
    }

    private IDisposable Register(IXPlatImageFormatProvider provider)
    {
        var registration = XPlatImageFormats.Add(provider);
        _registrations.Add(registration);
        return registration;
    }

    private static MemoryStream Stream(string magic) => new([.. System.Text.Encoding.ASCII.GetBytes(magic), .. new byte[32]]);

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A provider whose behaviour a test can read back.</summary>
    private sealed class StandIn(string name, int priority, string magic, float width) : IXPlatImageFormatProvider
    {
        internal SKColor Colour { get; } = SKColors.Magenta;

        internal int Decodes { get; private set; }

        internal string? LastHint { get; private set; }

        internal bool Throws { get; init; }

        internal bool ThrowsFromCanDecode { get; init; }

        internal bool ReturnsNull { get; init; }

        public string Name => name;

        public int Priority => priority;

        public bool CanDecode(ReadOnlySpan<byte> header) =>
            ThrowsFromCanDecode
                ? throw new InvalidOperationException("this provider cannot answer questions")
                : header.StartsWith(System.Text.Encoding.ASCII.GetBytes(magic));

        public SKPicture? Decode(Stream stream, string? extensionHint)
        {
            Decodes++;
            LastHint = extensionHint;

            if (Throws)
            {
                throw new InvalidOperationException("this provider is broken on purpose");
            }

            if (ReturnsNull)
            {
                return null;
            }

            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0f, 0f, width, width));
            using var paint = new SKPaint { Color = Colour };
            canvas.DrawRect(0f, 0f, width, width, paint);
            return recorder.EndRecording();
        }
    }
}
