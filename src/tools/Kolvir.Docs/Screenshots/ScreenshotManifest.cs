using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Kolvir.Docs.Screenshots;

/// <summary>One rendered image and the arguments that produce it.</summary>
internal sealed class ScreenshotEntry
{
    /// <summary>The image, relative to <c>docs/assets/</c>, such as <c>screenshots/demo-window.png</c>.</summary>
    [YamlMember(Alias = "image")]
    public string Image { get; set; } = string.Empty;

    /// <summary>The sample's page id, as <c>--page</c> accepts it.</summary>
    [YamlMember(Alias = "page")]
    public string Page { get; set; } = string.Empty;

    /// <summary>The rendered width in logical pixels.</summary>
    [YamlMember(Alias = "width")]
    public int Width { get; set; } = 1280;

    /// <summary>The rendered height in logical pixels.</summary>
    [YamlMember(Alias = "height")]
    public int Height { get; set; } = 820;

    /// <summary><c>light</c> or <c>dark</c>, so an image does not depend on the machine that made it.</summary>
    [YamlMember(Alias = "appearance")]
    public string Appearance { get; set; } = "light";
}

/// <summary>
/// Reads <c>docs/assets/screenshots/manifest.yml</c>.
/// </summary>
/// <remarks>
/// The manifest is what makes a screenshot reproducible: the image is rendered by the sample's own
/// headless renderer, so the page id and the arguments are the whole recipe and a picture can be
/// re-taken from any checkout without a window, a display or a person at the keyboard.
/// </remarks>
internal static class ScreenshotManifest
{
    /// <summary>Reads the manifest.</summary>
    /// <param name="repository">The repository holding it.</param>
    /// <returns>Its entries, in order; empty when there is no manifest yet.</returns>
    internal static List<ScreenshotEntry> Read(DocsRepository repository)
    {
        if (!File.Exists(repository.ScreenshotManifest))
        {
            return [];
        }

        var deserializer = new DeserializerBuilder().Build();

        try
        {
            return deserializer.Deserialize<List<ScreenshotEntry>>(File.ReadAllText(repository.ScreenshotManifest)) ?? [];
        }
        catch (YamlException ex)
        {
            throw new ToolException($"{repository.Relative(repository.ScreenshotManifest)} is not readable YAML: {ex.Message}");
        }
    }
}
