using System.Security.Cryptography;
using SkiaSharp;
using Svg.Skia;

namespace Kolvir.Svg;

/// <summary>
/// One parsed document, reused for every load that presents the same bytes and the same
/// <c>currentColor</c>.
/// </summary>
/// <remarks>
/// The entry keeps the parsed document, not the picture the control draws. A load hands out a fresh
/// recording of it - a few hundred bytes of display list - so that the control's ownership rule
/// (a loaded picture belongs to the control) never disposes something the cache is still handing
/// out. The nested picture a recording refers to is reference counted natively, so evicting the
/// entry while a control is drawing its recording is safe.
/// </remarks>
internal sealed class SvgCacheEntry : IDisposable
{
    private SKSvg? _svg;

    internal SvgCacheEntry(SKSvg svg) => _svg = svg;

    /// <summary>A recording the caller owns and disposes.</summary>
    internal SKPicture Record() => SvgDocument.Record(_svg ?? throw new ObjectDisposedException(nameof(SvgCacheEntry)));

    public void Dispose()
    {
        var svg = _svg;
        _svg = null;
        svg?.Dispose();
    }
}

/// <summary>
/// The parse cache: one parse per distinct document, and one parse per distinct
/// <c>currentColor</c> for that document.
/// </summary>
/// <remarks>
/// <para>
/// The key is the document's content, not its path, because a provider is handed a stream and has
/// no path to key on. Two icons that are byte-identical therefore share one parse wherever they
/// came from, and a file that changed on disk is a different document rather than a stale hit.
/// </para>
/// <para>
/// A recolour is a second entry rather than a second entry per control. Changing the tint
/// re-parses once and then serves every further load at that tint from the cache, so a theme that
/// alternates between two colours settles at two entries and stops parsing. That is the whole
/// reason the tint is part of the key: <c>currentColor</c> is resolved while the document is parsed,
/// so a parse cannot be recoloured afterwards without walking the model.
/// </para>
/// <para>
/// Parsing is not thread-affine, and a control may be loaded from a worker. The cache is locked for
/// the dictionary only; a parse happens outside the lock and the loser of a race is disposed, so
/// two threads asking for the same new document parse it twice at worst, never once between them.
/// </para>
/// </remarks>
internal static class SvgCache
{
    /// <summary>How many distinct (document, tint) pairs are kept.</summary>
    /// <remarks>
    /// An application's icon set is tens of entries, and a theme is a handful of colours, so this is
    /// generous. The cap is what stops a program that renders one SVG per data row from turning the
    /// cache into a leak.
    /// </remarks>
    internal const int Capacity = 64;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, SvgCacheEntry> Entries = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    private static int _parses;

    /// <summary>How many documents have been parsed, for the tests that claim "parsed once".</summary>
    internal static int ParseCount => Volatile.Read(ref _parses);

    /// <summary>How many entries are held.</summary>
    internal static int Count
    {
        get
        {
            lock (Gate)
            {
                return Entries.Count;
            }
        }
    }

    /// <summary>The entry for these bytes at this colour, parsing only on a miss.</summary>
    internal static SvgCacheEntry Get(ReadOnlySpan<byte> content, SKColor? currentColor)
    {
        string key = KeyFor(content, currentColor);

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var existing))
            {
                return existing;
            }
        }

        var parsed = SvgDocument.Parse(content, currentColor);
        Interlocked.Increment(ref _parses);

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var existing))
            {
                // Another thread parsed the same document while this one was parsing: keep theirs
                // so both loads share one entry's lifetime.
                parsed.Dispose();
                return existing;
            }

            var entry = new SvgCacheEntry(parsed);
            Entries.Add(key, entry);
            Order.Enqueue(key);

            while (Entries.Count > Capacity && Order.Count > 0)
            {
                string oldest = Order.Dequeue();
                if (Entries.Remove(oldest, out var evicted) && !ReferenceEquals(evicted, entry))
                {
                    evicted.Dispose();
                }
                else if (evicted is not null)
                {
                    // The oldest entry is the one just added, which only happens when the capacity
                    // was lowered: leave it in place rather than handing back a disposed entry.
                    Entries.Add(oldest, evicted);
                    break;
                }
            }

            return entry;
        }
    }

    /// <summary>Empties the cache. The controls that already hold recordings keep drawing.</summary>
    internal static void Clear()
    {
        lock (Gate)
        {
            foreach (var entry in Entries.Values)
            {
                entry.Dispose();
            }

            Entries.Clear();
            Order.Clear();
        }
    }

    /// <summary>The cache key: the content's hash and the resolved <c>currentColor</c>.</summary>
    internal static string KeyFor(ReadOnlySpan<byte> content, SKColor? currentColor)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(content, hash);

        // The tint is part of the key rather than the value, because it changes what the parse
        // produces rather than how the result is drawn.
        return currentColor is { } color
            ? string.Concat(Convert.ToHexString(hash), "|", color.ToString())
            : string.Concat(Convert.ToHexString(hash), "|none");
    }
}
