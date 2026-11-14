using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarfBuzzSharp;

namespace Kolvir.Svg;

/// <summary>
/// Makes HarfBuzzSharp's native library resolvable when a NativeAOT publish has folded it into the
/// executable.
/// </summary>
/// <remarks>
/// <para>
/// Svg.Skia shapes <c>&lt;text&gt;</c> through HarfBuzz, so the SVG feature needs a native library
/// that is not part of the AOT image. The build assets shipped with this package
/// (<c>buildTransitive/Kolvir.Feature.Svg.targets</c>) embed a compressed copy in the application and
/// this extracts it into a per-user cache and redirects HarfBuzzSharp's P/Invoke at it, the same way
/// <c>SkiaNativeLoader</c> does for SkiaSharp.
/// </para>
/// <para>
/// The two resolvers coexist because they are registered against different assemblies:
/// <see cref="Blob"/> is HarfBuzzSharp's, and the core library registers its own against SkiaSharp's.
/// That is also why <see cref="ModuleInitializerAttribute"/> is the entry point rather than a call
/// an application has to make in <c>Main</c>: the initializer owns the timing. A
/// framework-dependent run triggers it the first time the feature is used; a NativeAOT image runs
/// it during startup, before <c>Main</c>. Either way it lands before the first shaped text rather
/// than wherever an application happened to put a call.
/// </para>
/// <para>
/// The resource belongs to the <em>application</em>, not to this assembly, because the copy to embed
/// is the one the application's build resolved for its platform.
/// </para>
/// </remarks>
internal static class HarfBuzzNativeLoader
{
    /// <summary>The name HarfBuzzSharp's <c>DllImport</c> attributes ask for, without a suffix.</summary>
    private const string ImportName = "libHarfBuzzSharp";

    /// <summary>The file the native asset package ships on this platform.</summary>
    internal static string NativeLibraryFileName { get; } =
        OperatingSystem.IsWindows() ? "libHarfBuzzSharp.dll"
        : OperatingSystem.IsMacOS() ? "libHarfBuzzSharp.dylib"
        : "libHarfBuzzSharp.so";

    /// <summary>The logical name <c>buildTransitive/Kolvir.Feature.Svg.targets</c> embeds it under.</summary>
    internal static string ResourceName => $"Kolvir.Feature.Svg.Native.{NativeLibraryFileName}.gz";

    /// <summary>Serializes <see cref="EnsureLoaded(Assembly?)"/> so extraction runs once per process.</summary>
    private static readonly object Gate = new();

    private static bool _ran;

    private static bool _registered;

    /// <summary>
    /// Runs when the feature assembly is first touched, which is the earliest point at which the
    /// library can be needed and the latest at which it is still early enough.
    /// </summary>
    /// <remarks>
    /// Nothing here may throw: a module initializer that does turns a resolvable library into a
    /// <see cref="TypeInitializationException"/> on the assembly's first type, with a message that
    /// names no cause. If anything goes wrong the library stays unresolved, and the first call into
    /// HarfBuzz reports that precisely.
    /// </remarks>
    // A feature package is exactly the library case the rule exempts in spirit: this assembly is only
    // ever loaded when an application opts into it, and the initializer is what keeps the resolver in
    // place before the first shaped text without an application calling anything.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize()
#pragma warning restore CA2255
    {
        try
        {
            EnsureLoaded(Assembly.GetEntryAssembly());
        }
        catch (Exception)
        {
            // See the remarks: default probing, or a failure at the call site, is the better outcome.
        }
    }

    /// <summary>
    /// Makes sure the native HarfBuzz library can be resolved by the P/Invoke loader.
    /// </summary>
    /// <param name="resourceAssembly">
    /// The assembly the embedded library was compiled into, which is the application's own assembly.
    /// </param>
    /// <returns>Whether a resolver was registered.</returns>
    internal static bool EnsureLoaded(Assembly? resourceAssembly)
    {
        if (resourceAssembly is null)
        {
            return false;
        }

        lock (Gate)
        {
            if (_ran)
            {
                return _registered;
            }

            _registered = EnsureLoaded(
                resourceAssembly,
                DefaultCacheRoot,
                AppContext.BaseDirectory,
                assembly => assembly.GetManifestResourceStream(ResourceName),
                RegisterResolver);
            _ran = true;
            return _registered;
        }
    }

    /// <summary>
    /// The whole of <see cref="EnsureLoaded(Assembly?)"/> with every outside dependency passed in, so
    /// the cache, the early exit for a loose library and the resolver conflict can all be driven
    /// without publishing and without a real <c>dlopen</c>.
    /// </summary>
    /// <param name="resourceAssembly">Where to look for the archive and where to register the resolver.</param>
    /// <param name="cacheRoot">The directory the per-user cache lives under.</param>
    /// <param name="baseDirectory">The directory the executable is in.</param>
    /// <param name="openResource">Returns the compressed archive, or null when there is none.</param>
    /// <param name="registerResolver">Registers the resolver, returning whether it succeeded.</param>
    internal static bool EnsureLoaded(
        Assembly resourceAssembly,
        string cacheRoot,
        string baseDirectory,
        Func<Assembly, Stream?> openResource,
        Func<Assembly, DllImportResolver, bool> registerResolver)
    {
        // A regular build copies the library next to the executable, so there is nothing to do -
        // and nothing to do differently if an application deliberately shipped it loose.
        if (File.Exists(Path.Combine(baseDirectory, NativeLibraryFileName)))
        {
            return false;
        }

        using var resource = openResource(resourceAssembly);
        if (resource is null)
        {
            // Nothing embedded: let the default probing find it (e.g. an installed copy).
            return false;
        }

        var directory = Path.Combine(cacheRoot, NativeArchiveCache.Key(resourceAssembly.GetName().Version, resource.Length));
        var path = Path.Combine(directory, NativeLibraryFileName);

        if (!File.Exists(path))
        {
            NativeArchiveCache.Write(resource, path);
        }

        // Loading by full path is not enough on its own: a later dlopen("libHarfBuzzSharp.so") would
        // search the library paths instead of reusing the module already in memory. Redirecting the
        // import is the same mechanism on Windows, where the library is otherwise looked up by name
        // next to the executable.
        return registerResolver(
            typeof(Blob).Assembly,
            (name, _, _) =>
                string.Equals(name, ImportName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, NativeLibraryFileName, StringComparison.OrdinalIgnoreCase)
                    ? NativeLibrary.Load(path)
                    : IntPtr.Zero);
    }

    /// <summary>Where the per-user cache lives: the same root the core library uses, its own feature.</summary>
    private static string DefaultCacheRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kolvir",
            "native",
            "harfbuzz");

    /// <summary>
    /// Registers the resolver, treating a resolver someone else already registered as "not ours to
    /// take over" rather than as a failure.
    /// </summary>
    /// <remarks>
    /// <see cref="NativeLibrary.SetDllImportResolver"/> allows one resolver per assembly and throws
    /// <see cref="InvalidOperationException"/> when there is one already. An application that wants
    /// to control where HarfBuzzSharp's native comes from registers its own before this runs; the
    /// library is extracted either way, and that application's resolver keeps deciding. Crashing at
    /// startup because somebody else got there first would be the worst possible answer, and the
    /// static guard above only protects this type against itself.
    /// </remarks>
    internal static bool RegisterResolver(Assembly assembly, DllImportResolver resolver)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, resolver);
            return true;
        }
        catch (InvalidOperationException)
        {
            // The one resolver this API allows is already there, usually the application's own. The
            // library is extracted either way; that resolver keeps deciding where it comes from.
            return false;
        }
    }
}
