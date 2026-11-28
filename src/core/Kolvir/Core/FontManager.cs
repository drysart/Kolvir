using System.Diagnostics.CodeAnalysis;

namespace Kolvir;

/// <summary>
/// The pool behind <see cref="Font"/>: one <see cref="RealizedFont"/> per description, created on
/// demand and shared by every font that describes the same face.
/// </summary>
/// <remarks>
/// <para>
/// It owns resolution and nothing else. It does not own faces - <see cref="FontRegistry"/> does - it
/// does not size anything, and it does not know what a control is. A single realization per
/// description is created by the first font that needs one, and is a dictionary read from then on.
/// </para>
/// <para>
/// <b>Why the table is keyed by a value.</b> A table keyed by <see cref="Font"/> would hold the font,
/// the font holds its realization, so the realization would always be reachable, its finalizer would
/// never run and one entry would leak per description for the life of the process. The key is a
/// value, so the only strong reference to a realization is the fonts that realized it.
/// </para>
/// <para>
/// <b>Why a swapped dictionary rather than an immutable one.</b> The table is published by one
/// reference write and read with one volatile read, so a reader sees the old table or the new one and
/// never a partially built one; a published table is never mutated, so a lock-free reader is safe.
/// That is exactly the discipline an <c>ImmutableDictionary</c> gives, for one dictionary and no new
/// dependency: keying the same table with one cost 34,304 bytes of the trimmed NativeAOT image
/// (3,944,448 against 3,910,144 on win-x64), which every application would carry for a table that
/// holds a handful of entries.
/// </para>
/// <para>
/// <b>Threading.</b> A miss creates under one lock, which re-checks the table first so two threads
/// asking for the same description produce one realization. Every publication is a compare-and-swap
/// that retries against whatever it finds, so nothing a writer publishes can drop a change made
/// concurrently - including the finalizer's removal, which takes no lock because a finalizer that
/// blocks is a finalization queue that stops. In the rare interleaving where a removal does lose to a
/// publication, the entry left behind is a weak reference to a collected object: harmless, and
/// replaced the next time that description is realized.
/// </para>
/// <para>
/// Everything else in the font subsystem - the fonts, the realizations and their caches - is
/// UI-thread code, exactly as the controls are. The epoch is a plain read and the compare that uses
/// it is a UI-thread compare; only this table is touched from a finalizer thread.
/// </para>
/// </remarks>
internal static class FontManager
{
    private static readonly object Gate = new();

    private static Dictionary<FontKey, WeakReference<RealizedFont>> _table = [];

    private static int _epoch;

    /// <summary>
    /// How many times resolution has been invalidated. A font compares the epoch it realized under
    /// with this, so a registration or an unregistration makes every font look again.
    /// </summary>
    internal static int Epoch => Volatile.Read(ref _epoch);

    /// <summary>
    /// An approximate count of the descriptions the table holds.
    /// </summary>
    /// <remarks>
    /// <b>Approximate</b>: an entry is removed when its realization is finalized, so the count
    /// includes realizations that are unreachable but have not been collected yet, and it drops when
    /// a collection runs. It is a diagnostic for "is the pool doing anything at all", not a precise
    /// census.
    /// </remarks>
    internal static int ApproximateCount => Volatile.Read(ref _table).Count;

    /// <summary>The shared realization for <paramref name="font"/>'s description.</summary>
    /// <param name="font">The description to realize.</param>
    /// <returns>The one realization for that description.</returns>
    internal static RealizedFont Realize(Font font)
    {
        var key = font.Key;

        if (TryGet(key, out var existing))
        {
            return existing;
        }

        lock (Gate)
        {
            // Checked again under the lock: another thread may have realized this description between
            // the first read and here.
            if (TryGet(key, out existing))
            {
                return existing;
            }

            var realized = new RealizedFont(key);
            var reference = new WeakReference<RealizedFont>(realized);
            realized.Attach(reference);

            while (true)
            {
                var table = Volatile.Read(ref _table);
                if (table.TryGetValue(key, out var raced) && raced.TryGetTarget(out existing))
                {
                    // A removal that landed between the check above and here took the entry away and
                    // something else put one back; theirs is as good as ours.
                    return existing;
                }

                var copy = new Dictionary<FontKey, WeakReference<RealizedFont>>(table)
                {
                    [key] = reference,
                };

                if (ReferenceEquals(Interlocked.CompareExchange(ref _table, copy, table), table))
                {
                    return realized;
                }
            }
        }
    }

    /// <summary>
    /// Records that a family resolves differently now: every font re-resolves, and the entries for
    /// that family are dropped so the re-resolution finds nothing stale.
    /// </summary>
    /// <param name="family">The family whose resolution changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is null.</exception>
    /// <remarks>
    /// Registration and unregistration are the same event - "resolution of family X changed" - so this
    /// is their one path. Dropping only this family's entries is free in correctness and not in time:
    /// finding them is a scan of the table, which is acceptable because invalidation happens once per
    /// registered or unregistered family rather than per frame. The epoch is global because a
    /// per-family one would have to be recomputed per font against a set of changed families.
    /// </remarks>
    internal static void InvalidateFamily(string family)
    {
        ArgumentNullException.ThrowIfNull(family);

        Interlocked.Increment(ref _epoch);

        while (true)
        {
            var table = Volatile.Read(ref _table);

            List<FontKey>? stale = null;
            foreach (var entry in table)
            {
                if (string.Equals(entry.Key.Family, family, StringComparison.Ordinal))
                {
                    (stale ??= []).Add(entry.Key);
                }
            }

            if (stale is null)
            {
                break;
            }

            var copy = new Dictionary<FontKey, WeakReference<RealizedFont>>(table);
            foreach (var key in stale)
            {
                copy.Remove(key);
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _table, copy, table), table))
            {
                break;
            }
        }

        Theme.NotifyFontsChanged();
    }

    /// <summary>
    /// Drops a realization's entry, if and only if the table still holds that realization's own weak
    /// reference.
    /// </summary>
    /// <param name="key">The description the realization was made for.</param>
    /// <param name="reference">The weak reference the table stored for it.</param>
    /// <remarks>
    /// Called from <see cref="RealizedFont"/>'s finalizer, which has two rules this obeys. It must not
    /// throw: it runs on a thread with nobody to catch anything, and a throw there ends the process,
    /// so the whole body is wrapped. And it must not delete a successor: an invalidation can remove an
    /// entry while the realization is alive, so by the time this runs the key may hold a newer
    /// realization's reference - which the identity check refuses to touch.
    /// </remarks>
    internal static void Remove(FontKey key, WeakReference<RealizedFont> reference)
    {
        try
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var table = Volatile.Read(ref _table);

                if (!table.TryGetValue(key, out var current) || !ReferenceEquals(current, reference))
                {
                    return;
                }

                var copy = new Dictionary<FontKey, WeakReference<RealizedFont>>(table);
                copy.Remove(key);

                if (ReferenceEquals(Interlocked.CompareExchange(ref _table, copy, table), table))
                {
                    return;
                }
            }
        }
        catch (Exception)
        {
            // At shutdown the static may already be gone, and an allocation failure while copying the
            // table is the one realistic failure here. Either way the entry is a leak, not a crash,
            // and this is the wrong thread to be clever on.
        }
    }

    private static bool TryGet(FontKey key, [NotNullWhen(true)] out RealizedFont? realized)
    {
        if (Volatile.Read(ref _table).TryGetValue(key, out var reference) &&
            reference.TryGetTarget(out realized))
        {
            return true;
        }

        realized = null;
        return false;
    }
}
