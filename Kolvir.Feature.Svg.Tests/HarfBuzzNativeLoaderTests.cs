using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Kolvir.Svg.Tests;

/// <summary>
/// The half of the single-file story that can be checked without publishing: where a cached library
/// goes, that a failed write is never visible, that a second run reuses what the first extracted, and
/// that somebody else's resolver does not turn startup into a crash.
/// </summary>
/// <remarks>
/// Every case here drives the loader with an arbitrary blob and a fake registrar, so nothing depends
/// on a real library being present, on the machine's cache being empty, or on a <c>dlopen</c>
/// succeeding. The publish itself is what proves the archive name and the real redirect; see the pull
/// request for those measurements.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Svg")]
public sealed class HarfBuzzNativeLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"Kolvir-SvgNative-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A test that fails on cleanup would hide the failure it is cleaning up after.
        }
    }

    [TestMethod]
    public void TheCacheKeyNamesTheVersionAndTheCompressedLength()
    {
        Assert.AreEqual("1.2.3-1000", NativeArchiveCache.Key(new Version(1, 2, 3), 0x1000), "version and hex length");

        Assert.AreNotEqual(
            NativeArchiveCache.Key(new Version(1, 2, 3), 0x1000),
            NativeArchiveCache.Key(new Version(1, 2, 4), 0x1000),
            "a new build must not reuse the previous build's library");

        Assert.AreNotEqual(
            NativeArchiveCache.Key(new Version(1, 2, 3), 0x1000),
            NativeArchiveCache.Key(new Version(1, 2, 3), 0x1001),
            "and neither must a different payload");
    }

    [TestMethod]
    public void AnArchiveIsDecompressedIntoTheCacheWhole()
    {
        var bytes = Payload();
        var path = Path.Combine(_root, "cache", "1.0.0-1", HarfBuzzNativeLoader.NativeLibraryFileName);

        using (var archive = Gzip(bytes))
        {
            NativeArchiveCache.Write(archive, path);
        }

        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path), "every byte of the archive arrives");
        Assert.AreEqual(0, LeftoverTemporaries(path).Length, "and no temporary file is left behind");
    }

    [TestMethod]
    public void AFailedExtractionIsNeverVisibleAsTheLibrary()
    {
        var path = Path.Combine(_root, "cache", "1.0.0-1", HarfBuzzNativeLoader.NativeLibraryFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var archive = FailingMidArchive())
        {
            Assert.Throws<IOException>(
                () => NativeArchiveCache.Write(archive, path),
                "half a gzip stream is not a library");
        }

        Assert.IsFalse(File.Exists(path), "the destination never appears as a partial file");
        Assert.AreEqual(0, LeftoverTemporaries(path).Length, "and the temporary file is cleaned up");
    }

    [TestMethod]
    public void AFailedExtractionDoesNotDisturbTheLibraryAlreadyCached()
    {
        var path = Path.Combine(_root, "cache", "1.0.0-1", HarfBuzzNativeLoader.NativeLibraryFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var good = Payload();
        File.WriteAllBytes(path, good);

        using (var archive = FailingMidArchive())
        {
            Assert.Throws<IOException>(() => NativeArchiveCache.Write(archive, path), "the archive is refused");
        }

        CollectionAssert.AreEqual(good, File.ReadAllBytes(path), "the cached library is untouched");
    }

    [TestMethod]
    public void TheSecondRunReusesWhatTheFirstExtracted()
    {
        var cacheRoot = Path.Combine(_root, "cache");
        var bytes = Payload();
        int registrations = 0;
        var cachePath = Path.Combine(
            cacheRoot,
            NativeArchiveCache.Key(FeatureAssembly.GetName().Version, GzipLength(bytes)),
            HarfBuzzNativeLoader.NativeLibraryFileName);

        Assert.IsTrue(Load(cacheRoot, bytes, (_, _) => ++registrations > 0), "the first run registers the resolver");
        Assert.IsTrue(File.Exists(cachePath), "and leaves the library in the cache");

        var extractedAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(cachePath, extractedAt);

        Assert.IsTrue(Load(cacheRoot, bytes, (_, _) => true), "the second run registers it too");

        Assert.AreEqual(extractedAt, File.GetLastWriteTimeUtc(cachePath), "but it does not extract a second time");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(cachePath), "the cached copy is still the archive's");
    }

    [TestMethod]
    public void ALooseLibraryBesideTheExecutableWins()
    {
        var baseDirectory = Path.Combine(_root, "app");
        Directory.CreateDirectory(baseDirectory);
        File.WriteAllBytes(Path.Combine(baseDirectory, HarfBuzzNativeLoader.NativeLibraryFileName), [1, 2, 3]);

        bool registered = HarfBuzzNativeLoader.EnsureLoaded(
            FeatureAssembly,
            Path.Combine(_root, "cache"),
            baseDirectory,
            _ => throw new AssertFailedException("the archive must not even be looked for"),
            (_, _) => throw new AssertFailedException("no resolver is needed"));

        Assert.IsFalse(registered, "a regular build, or an opted-out publish, is left alone");
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "cache")), "and nothing is extracted");
    }

    [TestMethod]
    public void NoEmbeddedArchiveLeavesDefaultProbingAlone()
    {
        bool registered = HarfBuzzNativeLoader.EnsureLoaded(
            FeatureAssembly,
            Path.Combine(_root, "cache"),
            Path.Combine(_root, "app"),
            _ => null,
            (_, _) => throw new AssertFailedException("no resolver is needed"));

        Assert.IsFalse(registered, "an application with no embedded library resolves it the usual way");
    }

    [TestMethod]
    public void AResolverAnApplicationRegisteredFirstIsNotFatal()
    {
        var cacheRoot = Path.Combine(_root, "cache");
        var bytes = Payload();

        // The application got there first, exactly as it would if it registered its own resolver to
        // redirect HarfBuzzSharp somewhere else. The resolver declines every library, which leaves the
        // default probing in charge - the state this process was in before the test.
        Assert.IsTrue(
            HarfBuzzNativeLoader.RegisterResolver(typeof(HarfBuzzSharp.Blob).Assembly, (_, _, _) => IntPtr.Zero),
            "the application's resolver is registered");

        bool registered = Load(cacheRoot, bytes, HarfBuzzNativeLoader.RegisterResolver);

        Assert.IsFalse(registered, "the application's own resolver keeps deciding");
        Assert.IsTrue(
            File.Exists(Path.Combine(cacheRoot, NativeArchiveCache.Key(FeatureAssembly.GetName().Version, GzipLength(bytes)), HarfBuzzNativeLoader.NativeLibraryFileName)),
            "the library is extracted anyway, so that resolver can load it if it wants to");
    }

    [TestMethod]
    public void RegisteringTwiceForOneAssemblyReportsTheSecondAsSomeoneElses()
    {
        // The real API, not a simulation: it is the only way to prove the guard catches what it
        // claims to. The resolver declines everything, so it changes nothing about this process.
        Assert.IsTrue(
            HarfBuzzNativeLoader.RegisterResolver(TestAssembly, (_, _, _) => IntPtr.Zero),
            "the first registration succeeds");

        Assert.IsFalse(
            HarfBuzzNativeLoader.RegisterResolver(TestAssembly, (_, _, _) => IntPtr.Zero),
            "and the second is reported as a conflict rather than thrown");
    }

    private static bool Load(string cacheRoot, byte[] bytes, Func<Assembly, DllImportResolver, bool> register) =>
        HarfBuzzNativeLoader.EnsureLoaded(
            FeatureAssembly,
            cacheRoot,
            Path.Combine(Path.GetTempPath(), $"Kolvir-NoLoose-{Guid.NewGuid():N}"),
            _ => Gzip(bytes),
            register);

    private static byte[] Payload()
    {
        var bytes = new byte[4096];
        new Random(1234).NextBytes(bytes);
        return bytes;
    }

    private static long GzipLength(byte[] bytes)
    {
        using var archive = Gzip(bytes);
        return archive.Length;
    }

    private static MemoryStream Gzip(byte[] bytes)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        output.Position = 0;
        return output;
    }

    private static MemoryStream FailingMidArchive()
    {
        // A real gzip stream cut off mid-body: everything before the cut decodes, so the extraction
        // has written bytes to its temporary file by the time the read fails. That is the state the
        // discipline is about - a half-written file that must never be visible as the library.
        using var complete = Gzip(Payload());
        var bytes = complete.ToArray();
        return new FailingStream(bytes, bytes.Length / 2);
    }

    /// <summary>A stream that delivers part of its content and then fails, like a read error mid-way.</summary>
    private sealed class FailingStream(byte[] content, int failAfter) : MemoryStream(content)
    {
        public override int Read(Span<byte> buffer)
        {
            if (Position >= failAfter)
            {
                throw new IOException("simulated read failure");
            }

            return base.Read(buffer[..(int)Math.Min(buffer.Length, failAfter - Position)]);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= failAfter)
            {
                throw new IOException("simulated read failure");
            }

            return base.Read(buffer, offset, Math.Min(count, failAfter - (int)Position));
        }
    }

    private static string[] LeftoverTemporaries(string path) =>
        Directory.Exists(Path.GetDirectoryName(path))
            ? Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp")
            : [];

    private static Assembly FeatureAssembly => typeof(HarfBuzzNativeLoader).Assembly;

    private static Assembly TestAssembly => typeof(HarfBuzzNativeLoaderTests).Assembly;
}
