# Fonts - design note

Status: **design, not built.** Nothing under `src/` changes in the pull request that lands this note;
it is the record of a design settled in discussion, written down before the work starts so the work
can be reviewed against it. It stands on `develop` at `b8c39b2`.

`Font` is used by every text-bearing control in the library, so this is a core change and the note is
written as one. Where a section says what the code does today it names the file and the member, and
that statement is meant to be checkable rather than remembered. Where a section is an argument rather
than a decision it says so. Section 10 is the rejected alternatives in one place; section 11 is what
is genuinely still open.

Every type and member name in this note is a proposed one - `RealizedFont`, `FontKey`, `FontManager`,
`FontRegistry`, `FontFace`, `FontOverride` - and none of them exists yet. Where a name is used it is
used consistently, so the implementation can rename one thing rather than reason about a paragraph.

The register is the one `ASSET-LOADING.md` uses: present tense, each decision recorded with the reason
it was made, and no open question quietly decided inside a body section.

## 1. The problem

Three costs, and the first two are the ones the change is for.

**Creating a `Font` is heavy, and creating one happens per node per render pass.** Today
`src/core/Kolvir/Core/Font.cs` resolves a typeface in its constructor path (`ResolveFamilyFace` calls
`SKTypeface.FromFamilyName`), constructs an `SKFont`, reads `_font.Metrics` for `_baselineOffset` and
`_lineHeight`, and calls `CoversAscii`, which is one native `GetGlyphs` over a 95-character probe.
Three native calls and two managed objects before the font has been used for anything, and a Virtual
DOM render that builds a font per text node per pass pays it once per node per pass.

**`Control.Font` compares by reference.** In `src/core/Kolvir/Core/Control.cs` the setter is
`if (_font.TryGetValue<Font>(out var current) && ReferenceEquals(current, font)) return;`. A render
that builds an equal-but-new `Font` therefore misses the early return and runs
`InvalidateAppearanceTree()` and `UpdateLayoutAfterAppearanceChange()`, which drops the inherited
appearance cache of the whole subtree and schedules a layout. The font did not change; the object
identity did.

**Measurement caches do not cross equal fonts.** `Font` owns `_widths` (8192 entries), `_runs` (4096),
`_coverage` (512), `_faceFonts` and `_ownedFaces`, all per instance. Two `Font`s with the same
description therefore shape the same word twice and split the same string into runs twice. On a
Virtual DOM render that is the *steady state*, not an edge case: every pass that builds a font gets an
empty set of caches.

Nothing in the third cost is a cache limit's fault; it is that the cache is attached to the wrong
object.

## 2. The description and the realization

### 2.1 `Font` becomes a description

`Font` keeps only what describes a face: family name (nullable, meaning the platform's default),
size, weight and italic. It holds no `SKTypeface`, no `SKFont`, no metrics and no caches. It is a
class rather than a struct for one reason, and it is the reason the note records even though the
shape is otherwise struct-like: it carries a field that is populated lazily, and a struct with a
lazily filled field is a copy hazard - a copy is made, the copy realizes, the original stays empty,
and every `Font` that was copied out of a field now has two lives. A class gives one object with one
lazy field, and copies are references to it.

Two distinct instances with the same description compare equal (section 3).

`Size`, `Weight` and `Italic` are data and answer without touching Skia. `FamilyName` is data when
the description names a family, and a realization when it does not: `Font(null, 13f)` asks the
platform for its default face, and only the platform can say which family that is. Today the property
is documented as "a report of the face rather than an echo of the constructor", and that stays true -
for a named family the name *is* the report, because the name is what resolution uses (section 4.2).

`WithSize` and the internal `Derive` stay on `Font` and become pure description arithmetic. Today
`Derive` has a branch that reuses the `SKFont`'s typeface so a derived font keeps a loaded face; with
no face in the description there is nothing to keep, and the branch disappears.

### 2.2 `RealizedFont` becomes the heavyweight object

Everything `Font` holds today that is not description moves to `RealizedFont`: the `SKFont`, the
typeface it resolved and the one it owns, the fallback `SKFont`s and their typefaces, `_style`,
`_coversAscii`, and all five caches. The member split:

| Member | Today | After |
| --- | --- | --- |
| `Size` | `Font` | `Font` (data) |
| `Weight` | `Font`, read from the face for loaded faces | `Font` (data, the requested weight) |
| `Italic` | `Font` | `Font` (data) |
| `FamilyName` | `Font`, from the typeface | `Font` (data when named; realized when null) |
| `LineHeight` | `Font`, from `_font.Metrics` | `Font` forwards to `RealizedFont` |
| `BaselineOffset` | same | same |
| `MeasureWidth` | `Font` | `Font` forwards to `RealizedFont` |
| `internal TextRuns` | `Font` | `RealizedFont`; `Font` has no forwarding member |
| `internal TextRunCount` | `Font` | `RealizedFont` (a test seam) |
| `internal DrawText` | `Font` | `RealizedFont` |
| `internal SkFont` | `Font` | `RealizedFont` |
| `internal TextRun` | nested in `Font` | nested in `RealizedFont` |
| `WithSize`, `Derive` | `Font` | `Font`, and no longer touch Skia |
| `_widths`, `_runs`, `_coverage`, `_faceFonts`, `_ownedFaces`, `_style`, `_coversAscii`, `_fixedFace` | `Font` | `RealizedFont` |
| `FromFile`, `FromStream`, `FromBytes` | `Font` | gone from `Font`; section 4 |
| `Dispose` | `Font : IDisposable` | gone; section 2.4 |

The public forwarders exist because the alternative is a source break on every call site for a
function that still has exactly one meaning: `TextRenderer` (`src/core/Kolvir/Core/TextRenderer.cs`)
calls `font.MeasureWidth`, `font.LineHeight`, `font.BaselineOffset` and `font.DrawText` throughout,
and `Label`, `TextBox`, `RichLabel`, `MenuRenderer`, `ListView` and the rest do the same. There
is no gain in making them say `.Realize().MeasureWidth(...)`; the gain was the pooling, and it does
not depend on the spelling.

The internal members move rather than forward because they are the realization: `RichLabel.cs:1084`
and `TextBoxSpanFeature.cs:234` read `font.SkFont.Metrics`, and both are in the same assembly, so they
say `font.Realize().SkFont.Metrics`. Tests that assert on runs (`FontFallbackTests` uses the internal
`TextRunCount`) call the same accessor.

`RealizedFont` is internal. An application draws and measures through `Font`, which is the surface it
already has; exposing the realization would make the registration epoch of section 4.4 part of the
public contract, and nothing in the library needs it. Whether that is right long term is open
question 2.

### 2.3 The key, and why it cannot be a `Font`

`FontManager` resolves a description to a realization, once per description, through an
`ImmutableDictionary<FontKey, WeakReference<RealizedFont>>` read lock-free on the happy path, with
`ImmutableInterlocked` as the slow path when something has to be created.

`FontKey` is the description as a `readonly struct`: the family name, the size, the weight and the
italic flag, with value equality over exactly those four fields. Family names compare `Ordinal`, which
is the comparison the rest of the library uses for a family name (`RichLabel.ResolveFont` compares a
span's family to `EffectiveFont.FamilyName` with `StringComparison.Ordinal`), and a null family
normalises to null so the platform default is one key rather than several.

The size is compared with exact `float` equality, not with a tolerance. A tolerance would merge two
descriptions that measure differently, and the key's whole job is to say when two descriptions are
the same face at the same size. The one place the library already uses a tolerance -
`RichLabel.ResolveFont` deciding whether to derive at all - is a different question ("is this span
asking for a different font") and stays where it is.

**The dictionary cannot be keyed by `Font`, and this is the trap the key exists to avoid.** The
manager would hold the `Font` strongly; the `Font` holds its `RealizedFont` strongly; so the
`RealizedFont` is permanently reachable from the manager, its finalizer never runs, the entry is
never removed, and the `Font` is never collected either. One entry leaks per distinct description,
forever. The key being a value means the only strong reference to a realization is the `Font`
instances that used it, which is what makes section 2.4 work at all.

The same argument applies to anything else the key might want to hold. A key that carried a
reference to the loaded face (section 4) would root that face and its bytes in the dictionary for the
life of the process, which is why the registry is keyed by *name* rather than by face.

### 2.4 Liveness: no refcount, no `IDisposable`, one finalizer

`Font` has a field holding the `RealizedFont` it realized, strongly held and populated on first use.
No reference counting and no `IDisposable`: the realization is kept alive by the `Font` instances
that realized it, and last-one-out is the garbage collector's job. That is the whole lifetime rule,
and it is why the design has no dispose ordering to get wrong.

`RealizedFont` gets a finalizer, and it does exactly one thing: remove its own entry from the
manager, after verifying that the entry is still its own `WeakReference` before removing (section
6). It disposes nothing. Every SkiaSharp object it holds - the `SKFont`, the typefaces - is itself
finalizable (`SKObject` closes its handle from its own finalizer), so dropping the reference is
enough and no native call is made on our finalizer.

### 2.5 What `FontManager` is, and what it is not

`FontManager` is a static class holding one dictionary and one epoch counter (section 4.4). It owns
resolution and nothing else: it does not own faces, does not decide what a family means (the registry
of section 4 does), does not size anything and does not know what a control is. A single
`RealizedFont` per description is created on the first `Font` that needs it, and thereafter a
dictionary read.

Controls keep caching layout against the realization, which is the point of pooling: `RealizedFont`
holds the width, run and coverage caches, so a control that measures the same word against an equal
font hits the cache the first font filled.

### 2.6 What the per-pass caches actually do

One correction to a common assumption, because the code says something else. `Control.Measure`
(`src/core/Kolvir/Core/Control.cs:2408`) does **not** key its per-pass cache on the font object. It
keys on the layout epoch (`Control.LayoutEpoch`), on `Theme.FontVersion`, and on the available size:

```csharp
if (_measureEpoch == LayoutEpoch &&
    _measureFontVersion == Theme.FontVersion &&
    (MeasureIgnoresHeight
        ? MathF.Abs(_measureAvailable.Width - availableSize.Width) < 0.01f
        : _measureAvailable.Equals(availableSize)))
```

So pooling does not "keep `Control.Measure` working unchanged" - it was unaffected to begin with,
because what the pass remembers is a size, not a font. What *is* keyed on a font object in core is
`TextBoxMultiline._linesFont` (`src/core/Kolvir/Multiline/TextBoxMultiline.cs:656`), which validates
its cached line breaks with `ReferenceEquals(_linesFont, font)`. That one matters, and section 4.4
states the rule it needs.

Two places *were* sitting on reference equality without needing to, and value equality improves them
by itself - `ToolStripItem.Font` (`src/core/Kolvir/Controls/ToolStrip.cs`, whose setter already reads
`if (_font.Equals(value)) return;`) and the generated reconciler, section 3.

## 3. Equality, and the one behaviour change

`Font` implements `IEquatable<Font>` over the four description fields, with a matching `GetHashCode`.
Two instances with the same family, size, weight and italic are equal, and equal instances are
interchangeable: they resolve to the same `RealizedFont` by construction.

Everything downstream of `Union<Font, InheritedValue>` inherits the new comparison for free, because
`Union<T1, T2>.Equals` (`src/core/Kolvir/Core/Union.cs`) compares payloads with
`EqualityComparer<T1>.Default.Equals`. That has two consequences worth naming:

- **The Virtual DOM reconciler already compares.** The generated setter body is
  `if (!_font.Equals(old._font)) target.Font = _font.IsSpecified ? _font.Value : PristineFont;`
  (`src/generators/Kolvir.Feature.VirtualDom.Generators/NodeEmitter.cs`, and the same shape is
  written out in `dev-docs/VIRTUAL-DOM.md`). With value equality a re-render that builds an equal
  `Font` skips the assignment at the reconciler, before `Control.Font`'s setter is reached at all.
  The node property, the generator and the emitter are untouched - the union stays exactly as it is.
- **`ToolStripItem.Font` becomes value-comparing** without a line of change, because it was already
  written against `Union.Equals`.

The change to `Control.Font`'s setter is therefore one line: `ReferenceEquals(current, font)` becomes
`EqualityComparer<Font>.Default.Equals(current, font)`, or equivalently `font.Equals(current)`.

**This is a behaviour change, and it is most of the benefit in the Virtual DOM case.** What an
application loses is the ability to force a re-measure by assigning an equal font - and that was
never a documented capability, only a consequence of the comparison. It was reachable only by
rebuilding the same font, which is the pattern this change is trying to make free; an application
that genuinely needs text re-measured because the face behind a name changed has `FontRegistry`
(section 4.4), which invalidates for it. The `Control.Measure` cache is unaffected in either
direction: it does not key on the font.

Two things deliberately do **not** become value-equal: `RealizedFont` (identity is the point - two
realizations of the same description cannot exist simultaneously for a key, but a stale one survives
in `Font`s that have not re-realized yet, section 4.4) and the per-control caches that name a
realization.

## 4. Faces that are not system families

### 4.1 Files and bytes leave `Font`

A `Font` is a family name, a size, a weight and an italic flag, full stop. `Font.FromFile`,
`Font.FromStream` and `Font.FromBytes` are removed. A face that is not one the operating system
provides enters the library through a registry: the application reads a file or a byte array, gives
it a family name, and names that.

The reasons, in order:

- **A font that owns a face cannot be pooled by description.** Two `Font`s made from the same bytes
  are the same face, but nothing in the description says so, and comparing the bytes to find out is a
  hash of the whole file per creation - more expensive than the eager work this change removes.
- **The key would have to hold the face**, which is the leak of section 2.3 with data attached.
- **Ownership stops being a rule anyone can follow.** Today the caller owns a loaded `Font` and
  controls never dispose one they did not create; a `Dispose` on a pooled, per-render object cannot
  be kept straight, and the note would have to write an ownership contract for it. With the face in a
  registry and the description carrying only a name, there is no ownership left to get wrong: nothing
  in the library is disposable and nothing is owned.

It also turns out to be the better model for the loader that already exists. `FontLoader` and the
`Kolvir.Feature.WoffFonts` package are format dispatch - bytes in, an OpenType face out - and a face
has no size. Section 4.7 is where that lands.

### 4.2 The registry, and why it is not just a container service

`FontRegistry` goes in `src/core/Kolvir/Typography/` beside `FontLoader`, and holds the faces the
application supplied: family name to a set of weights and slants, each carrying the face's OpenType
bytes and the style read from them. `IFontRegistry` is the interface; the object registered in the
container is the same one `FontRegistry.Current` returns.

```csharp
public interface IFontRegistry
{
    string Register(string family, ReadOnlySpan<byte> data, int? weight = null, bool? italic = null);
    string Register(string family, FontFace face);
    string RegisterFromFile(string path, string? family = null);      // 4.7
    bool Unregister(string family);                                   // 4.4
    IReadOnlyList<RegisteredFontFamily> Families { get; }             // 4.8
}
```

**Why a static rather than a service resolved from `Control.Services`.** Realization happens with no
control in sight. `Theme`'s five font fields (`src/core/Kolvir/Core/Theme.cs`) are static and are
realized the first time anything measures text with them - `Win32WindowBackend` reads
`Theme.DefaultFont.LineHeight` for its own chrome
(`src/backends/Kolvir.Backend.Win32/Win32WindowBackend.cs:2950`) before a control is involved.
`Control.Services` is `Host?.Services` (`src/core/Kolvir/Core/Control.cs`), which is null for exactly
those cases. A resolution path that needs the container therefore needs a container that may not be
there.

The precedent for a service publishing itself to a static is
`Application.InstallProcessServices` (`src/core/Kolvir/Application/Application.cs`), which assigns
`Clipboard.Current`, `Launcher.Current` and `SystemTheme.Current`. That precedent does not fit here,
because each of those is backend-supplied and genuinely absent on a headless host, so an unset static
means "this platform has no clipboard". The registry is portable managed state plus Skia; there is
nothing platform-specific for a backend to supply and no state in which it should be absent. Rather
than publish it during startup and thereby create the window in which a face registered *before*
`InstallProcessServices` went to a different instance than the one realization uses, the registry is
one lazy static for the life of the process, and `Application` registers that same instance:

```csharp
services.AddSingleton<IFontRegistry>(FontRegistry.Current);
```

so an application can inject it as the user asked and a test can register a face with no `Application`
at all.

**Nothing registered must not mean no fonts.** The registry starting empty is the normal state: every
description resolves through Skia, exactly as today. An empty registry costs one dictionary probe on
the realization path (section 4.3) and nothing else.

### 4.3 Resolution order, and shadowing is intended

`RealizedFont` creation resolves a family in one order:

1. the registry, by family name;
2. `SKTypeface.FromFamilyName` with the requested style, which is what `Font.ResolveFamilyFace` does
   today, including its fall back to `SKTypeface.Default` when the platform has no such family;
3. nothing else - `SKFontManager` is not consulted, because it cannot see the registry (4.6).

**A registered name wins over a system name of the same name.** This is a capability rather than a
tolerated accident, and it is the reason resolution is ours rather than Skia's: an application that
wants its own "Inter", or its own "Segoe UI", registers that name and gets its face everywhere,
including in `Theme`'s fields, without changing a single `Font` in its code.

Shadowing is worth more than it first looks for one reason: the family an application puts in
`ApplicationOptions.FontFamily` is usually the platform's own UI family - the demo application passes
`PreferredFontFamily()` from `AppKitSystemTheme` or `Gtk4SystemTheme` into it
(`src/demo/Kolvir.Demo.App/Program.cs:112`), and `InstallProcessServices` assigns it to
`Theme.FontFamily` (`src/core/Kolvir/Application/Application.cs:746`). An application that registers
that same name gets its face with no theme change at all. Note that the backends only *report* the
preferred family; they do not assign the theme, whatever `FontFamily`'s own summary says.

Within a family the registry holds one face per (weight, italic). A description that asks for a weight
the family does not have gets the nearest registered weight, preferring a matching slant, and a family
that registered only a regular face serves every request with that face - which is today's
non-synthesis rule (`Font`'s remarks: "no synthetic bold") carried over rather than re-invented. The
realization reports the style it actually used; `Font.Weight`/`Italic` report what was asked for,
which is a genuine change of meaning for a loaded face (today they report the file's style) and is
recorded in section 9.

### 4.4 Registration and unregistration are legal at any time

Restricting registration to "before the name is first used" would forbid exactly the case the
capability exists for - an application whose theme already realized the system family it now wants to
shadow. So both directions are legal at any time, and what they have to invalidate is everything that
was resolved before.

**The mechanism is a per-`Font` epoch.** `FontManager` holds a global `int` epoch. `Font` stores the
epoch it realized under beside the realization and compares it on the happy path: equal, use the
cached realization; different, re-resolve before using it. One integer compare and one branch, no
lock, and it handles the shared-realization case correctly because every `Font` recorded the epoch
its own realization was made under.

**Registration and unregistration are one event: "resolution of family X changed."** One path,
`FontManager.InvalidateFamily(family)`, called by both entry points, which does two things:

- increments the epoch, which is what makes every `Font` look again;
- removes every entry whose `FontKey` names that family, which is what makes the look succeed at
  something new rather than find the old entry. Removing only this family's entries is the one place
  precision is free; the epoch is global and blunt because a per-family epoch would have to be
  recomputed per `Font` against a set of changed families.

**An existing realization keeps the face it already holds.** Unregistration does not reach into a
`RealizedFont`, does not invalidate it, does not dispose it, and does not make it unusable. It stays
valid and shareable until the last `Font` that realized it goes away, exactly as it would have without
the unregistration. There is nothing to tell it and nothing to block on. This is the easiest correct
answer and it is the one the design already implies, because the strong edge runs from `Font` to
`RealizedFont` and the manager holds only a weak one.

**Who owns the face.** The registry owns *bytes*, not a typeface. A `RealizedFont` builds its own
`SKTypeface` from the registered bytes when it is created, and owns it; unregistration simply drops
the registry's copy of the bytes. So no reference counting appears after all: a face lives exactly as
long as the last realization built from it, and a realization lives exactly as long as the last `Font`
that realized it. The two alternatives are both worse. Disposing the typeface at unregister is a
use-after-free for every live realization. Handing one shared typeface to every realization makes it
impossible to release at all - the registry cannot dispose it because someone else holds it, and
nobody else knows when the last one is done - which is the leak to avoid, on the *large* case where it
matters: a WOFF 2 file decompresses to a face of hundreds of kilobytes, and an application that
registers and unregisters a set of them would keep every one.

The cost the registry does pay is a parse per realization rather than a parse per registration, and
that is acceptable for the same reason the whole design is: a realization is created once per
description and shared by every equal `Font`, so the parse count is the number of distinct
descriptions in use, which is what `new Font(...)` already cost on every construction. Re-registering
the same file pays a parse to read its style and one per realization, which is a bounded, startup-
shaped cost rather than a leak; the registration half of it is left to open question 4.

**What changes is resolution going forward.** A `Font` for that family realized *after* the
unregistration resolves the way it would have if the family had never been registered: the system
face it was shadowing, or the ordinary no-match path. Because `Font`s re-resolve on the epoch, this
also applies to a `Font` that already existed: the same `Font` instance reports the system face's
measurements again. That is the testable behaviour - register the embedded `TestFont` data under the
name of a family the machine has, measure, unregister, measure again, and the second number is the
system family's - and it is what makes unregistration worth having rather than a bookkeeping call.

**The consequence for the finalizer's verification, now load-bearing.** Invalidation removes entries
while realizations that referenced them are still alive. A removed realization's finalizer will later
try to remove the entry for its key, and by then that entry may hold a *newer* realization's
`WeakReference`. The check the finalizer makes - is this entry still my own `WeakReference` - is what
stops it deleting its successor. It was a nicety in the design as first described; with invalidation
it is required for correctness.

**Measurement caches.** `Theme.FontVersion` already means "the fonts changed, drop the measurements":
`Control.Measure` compares it, and `UiHost` compares it in two places
(`src/core/Kolvir/UiHost.cs`: the layout pass drops the appearance caches of the tree and re-lays it
out, and the paint path re-lays out and repaints the whole surface). Registration and unregistration
both bump it, by the same route `Theme.FontFamily`'s setter does - `Theme` gains an internal
"the fonts changed" notify that increments the version and raises `Theme.AddChangedHandler`, which
`Application` already subscribes to (`Application.cs:128`), which invalidates every window.

Blunt is the right choice over a per-family equivalent, for four reasons: the mechanism that has to
run anyway - drop derived appearance caches, re-measure, repaint in one piece - is already written and
already correct for the harder case; a per-family version would require every control to remember
which families it used, which is exactly the per-control font state this change exists to stop
keeping; registrations are a startup-shaped operation, so the number of blunt passes is tiny; and the
whole job is one integer increment on the path a `Theme.FontFamily` assignment already takes, so
precision would buy nothing measurable while adding surface to `Control` and `UiHost`.

**One cache in core has to learn the rule, and it is not optional.** `Theme.FontVersion` covers
caches that are dropped by a re-layout, and it does not cover a cache that decides for itself whether
it is stale. `TextBoxMultiline._linesFont` is one: it holds the `Font` the line breaks were computed
for and validates with `ReferenceEquals(_linesFont, font)`. A registration leaves the `Font` object
identical - only what it resolves to changed - so that cache would consider itself current and keep
line breaks measured against the old face. The rule that fixes it is the rule for every such cache:
**a cache whose validity depends on a description resolving to the same face must record the epoch
beside the description and compare it.** Caches inside `RealizedFont` need nothing, because they are
keyed on the realization and die with it, which is the free half of this design.

**What an application sees.** After registering or unregistering, a control using that family changes
shape on its next measure rather than at any particular instant, and nothing needs to be rebuilt -
no control, no font object, no theme re-assignment. In an interactive application the next frame is
that measure, because the version bump invalidates the window.

### 4.5 Validation, and what a name means

- **Same family, same weight and slant, registered twice: replace.** `Register` is idempotent by
  (family, weight, italic), and the later call wins. The alternative - refuse - makes correctness
  depend on call order, and a second registration from a second code path is not a mistake worth
  failing the process over.
- **Same family, different weight or slant: a second face of that family.** This is the reason the
  registry holds a set per family rather than one face: registering a regular and a bold file under
  one name is how an application gets real emphasis from its own faces. Today one file is one face
  and no weight can be selected from it; this is a strict improvement and it is why `Theme.FontFamily`
  plus a registered family can serve `EmphasizedFont` honestly if the application registers the
  semibold file.
- **The same file registered twice** under the same name parses twice and replaces once. Idempotent
  in effect; the extra parse is the price of not hashing every face at registration, which is a
  measurement to revisit rather than a rule to add now (open question 3).
- **A file whose internal family name differs from the name it is registered under: the registered
  name wins for resolution.** The file's internal name is kept in the registry entry, and reported by
  `RegisteredFontFamily`, for diagnostics. Two reasons: the application's reason to register is that
  the name its code uses is the name it wants, and two builds of the same internal family
  ("Inter Regular", say) must be distinguishable. `Font.FamilyName` reports the registered name, so
  `new Font(f.FamilyName, f.Size, f.Weight, f.Italic)` reproduces the same face - the round trip is
  the property the property is for.

Unregistration takes a family name and removes every weight and slant of that family. It returns
`bool`: `true` when something was registered under that name, `false` otherwise, and it never throws
for an unknown or already-removed family. **It is idempotent** - the second call returns `false` and
does nothing, including no version bump when nothing changed. A family name that was never registered
is not an error: an application that unregisters in a `finally` block is doing the right thing.

Names compare `Ordinal`, like every other family-name comparison in the library.

### 4.6 Glyph fallback, stated as a limitation

`RealizedFont`'s fallback machinery - `FallbackFontFor`, `ResolveCoveringFont`,
`FamilyForFallback` - asks `SKFontManager.Default.MatchCharacter` for a face covering a code point.
`SKFontManager` has no idea the registry exists, so a character a registered face does not cover is
drawn with a *system* face. In a UI whose every family is registered, an emoji or a CJK character
therefore comes from the operating system, not from the application.

That is acceptable and it is what happens today: with `Theme.UseFont`, the loaded face's family is
unknown to the OS, so `MatchCharacter` already leaves it (the fallback only ever reached Skia for the
file-loaded case too). It is stated rather than fixed because the fix is the fallback search learning
about the registry, which is a second resolution path and a second thing to invalidate.

What the change does *not* do is make this worse: a registered family is treated exactly as a
file-loaded one was.

### 4.7 `IFontLoader`, `IFontFormatProvider`, and where WOFF plugs in

`IFontLoader.LoadFromPath`, `LoadFromStream` and `LoadFromBytes` return a `Font` today, and that is
the member this change takes away. They become face-producing:

```csharp
public interface IFontLoader
{
    FontFace LoadFromPath(string path);
    FontFace LoadFromStream(Stream stream);
    FontFace LoadFromBytes(ReadOnlySpan<byte> data);
}
```

A `FontFace` is the loaded thing: its OpenType bytes, the family name recorded inside it, and its
weight and italic, read once when it is created. It is a value - no Skia object is retained, so it is
not disposable and a registry can hold it. The `size` parameter disappears from all three members,
because a face has no size; that is why a loader that returned a `Font` was a category error, and it
is why `FontLoader.DefaultSize` (`src/core/Kolvir/Typography/FontLoader.cs`) goes away with it.

`FontLoader` keeps its job: it is the format dispatch - providers first, the built-in Skia path
second, `FontFormatException` naming the provider that claimed the data - and only the last step
changes. Today `LoadFromBytes` ends at line 176 and 188 of `FontLoader.cs` with `Font.FromBytes`;
after the change it ends with a `FontFace`.

`IFontFormatProvider` does not change at all. A provider still claims a byte header and returns the
OpenType bytes inside the container, which is the one thing `Kolvir.Feature.WoffFonts` does:
`WoffFontFormatProvider.CanLoad` matches the WOFF and WOFF 2 signatures and `Decode` unwraps to an
Sfnt (`WoffFormat.Unwrap`, `Woff2Format.Unwrap`, `SfntBuilder`), and `WoffServiceCollectionExtensions`
registers the provider so the composed `FontLoader` consults it. The end-to-end case the change has to
carry is therefore already in the right place:

```csharp
services.AddWoffFonts();                                              // the provider
var family = registry.RegisterFromFile("Fonts/Inter.woff2");           // unwrap, name, store
Theme.FontFamily = family;
```

`RegisterFromFile` resolves `IFontLoader` from the container, calls `LoadFromBytes`, and registers the
result; when `family` is null it uses the face's internal family name. Whether that resolution should
be a registry dependency in the container or an explicit loader argument is implementation detail; the
point is that a face decompressed from a WOFF file is stored exactly like a face read from a `.ttf`,
because by the time it reaches the registry it *is* one. Nothing in the registry knows what a
container format is.

### 4.8 Enumeration, and the font dialog

`FontRegistry.Families` lists the registered families with their faces, their internal family names
and their styles. That is the enumeration surface.

**Registered families do not appear in a font dialog**, and cannot. `IDialogService.PickFont` shows
the platform's own picker (`ChooseFont` on Windows, the GTK font chooser, the macOS font panel), and
the platform lists what its font manager knows. `FontDialogOptions`'s remarks already say this about
a loaded face - "the backend cannot tell an arbitrary loaded face from an installed one, and inventing
a way to add it would promise a system-wide answer the platform does not give" - and that paragraph
survives the change almost word for word: a registered face is still not a system face. What the
change adds is that the *answer* is now enumerable, so an application that wants to offer its own
families builds its own picker over `FontRegistry.Families` and constructs
`new Font(chosen.FamilyName, size, weight, italic)` from the choice.
`FontDialogOptions`'s remarks need that sentence added when this lands; this note does not edit it.

Note that `PickFont`'s result is already a `FontSelection` - a value of family, size, weight and
italic - and not a `Font`, for the same reason the registry exists: the caller builds the font.

### 4.9 `Theme.UseFont` goes away

`Theme.UseFont(Font)`'s job was "draw everything with this loaded face", and with `Font` carrying no
face there is no face to hand it. The same intent is now:

```csharp
Theme.FontFamily = registry.RegisterFromFile("Fonts/Inter-Regular.ttf");
```

`_fontFace` and the `face.WithSize(size)` branch in `Theme.ThemeFont` disappear with it, so
`RebuildFonts` becomes five `new Font(_fontFamily, size, weight)` calls and no Skia work at all. A
registered family with only a regular face still serves `EmphasizedFont`'s weight-600 request with
that face, which is today's documented behaviour for `UseFont` ("the weight cannot be selected"),
now expressed by the registry having only the faces the application registered.

`Theme`'s statics are a reason the registry is portable and always present, not a problem for it: the
theme fonts have no realization until something measures text.

## 5. Inherited fonts with overrides

The design question: `label.Font = InheritedFont.WithSize(14f)` - a font that takes the parent chain's
font except for the parts it names. Today there is no such member; `Control.Font` is
`Union<Font, InheritedValue>` and inheritance is all-or-nothing: a control assigns a complete `Font`
or nothing. What exists is `Theme.DefaultFont.WithSize(14f)`, which names its base explicitly and so
is not an override of the inherited font.

**A partial font is a different type, not a `Font` with some members unset.** `Font` is a total
description and has to stay one: it is a key (section 2.3), it has a `MeasureWidth` and a
`DrawText`, and it can be assigned to `Theme.FontFamily` or stored in a field where there is no parent
chain to inherit from. A `Font` whose size is unset would have to refuse to measure or invent a size,
and a `Font` whose family is unset already means something else - "the platform's default" - so
"unset" and "default" would be the same value with two meanings. That ambiguity in a dictionary key
is a defect, not a style question.

So the override is `FontOverride`: nullable family, nullable size, nullable weight, nullable italic,
with no measuring or drawing surface at all. It is meaningful only as a case of `Control.Font`.

**`EffectiveFont` is where it composes, and it already is the only producer of a complete
description.** Its rule today - own assignment, else the nearest ancestor's, else `DefaultFont` - is
the whole resolution, and it returns a `Font`. It gains one case: an override composes the inherited
complete `Font` with the override's set members and returns the complete result. The rule that goes
with it, stated so no implementation can miss it: **`RealizedFont` is never asked to realize a
partial description.** Only a `Font` reaches `FontManager`, and a `Font` only comes from a caller or
from `EffectiveFont`.

**`Union<Font, InheritedValue>` stays exactly as it is for this change.** The override type is a
separate change; when it lands, `Control.Font` becomes `Union<Font, FontOverride, InheritedValue>`,
which `Union` already covers (the type has arities two to four), and the generated node property
picks that up because the generator is generic over the property's type and names no font type
anywhere. Today's union being untouched is what makes this note's change safe for the Virtual DOM and
for `ToolStripItem.Font`, and it is worth a great deal.

Whether to build the override type at all, and the names of its members, is open question 1: the
design has a clean answer for *how* it would work, and no answer for whether the added union arity is
worth it to an application that can already write `parent.EffectiveFont.WithSize(14f)` where it has
the parent in hand.

## 6. Threading

`FontManager` is the only structure in the library touched from a finalizer thread. `Font`,
`RealizedFont` and every cache they hold stay UI-thread, exactly as `Control` does today: `Font`
records the epoch it realized under in a plain field, and the compare is a UI-thread compare.

The rules for the manager:

- **The dictionary and the epoch are read with one volatile read each and no lock.** A hit is a
  dictionary probe plus a generation compare.
- **A miss creates under the manager's lock.** Two threads asking for the same description at once
  must produce one realization; the lock covers creation and nothing else.
- **Publication is a single reference write** of the new dictionary (and of the epoch, for
  invalidation), so a reader sees either the old table or the new one, never a half-built entry. A
  `WeakReference<RealizedFont>` is never replaced piecemeal: a new realization gets a new
  `WeakReference`, and the old one is either in the table or not.
- **The finalizer must not throw.** It runs on a thread with no one to catch anything, and a throw
  there kills the process. Its body is a read of the current table, a reference comparison, and a
  compare-and-swap that produces a smaller table; the whole thing is wrapped so that an allocation
  failure while copying the table (the one realistic failure - an `OutOfMemoryException` at the
  worst possible moment) ends the attempt rather than the process.
- **The finalizer must tolerate a torn-down process.** At shutdown static state may already be gone;
  the removal is therefore written to read a possibly-null table and do nothing if it cannot, rather
  than assume the manager is alive. It also touches no Skia object, so a half-torn-down native
  library cannot fault it (section 2.4).

## 7. Diagnostics

`Font.ToString` reports the description and never realizes: the family, the size, the weight and the
italic flag, with the null family spelled out (`Font(platform default, 13, w400, upright)`). It is
reached from debuggers, log lines and test failure messages, and a `ToString` that allocates native
objects is a `ToString` that changes what it is observing.

`FontManager` reports one number: a count of the entries in its table. It is the obvious minimum and
it is cheap - one field read of the dictionary's count. It is *approximate* and should say so in its
name and its documentation: an entry is removed when a realization is finalized, so the count
includes realizations that are unreachable but not yet collected, and drops when a collection runs.
Per-entry diagnostics are out of scope: nothing consumes them yet, and a public dump of every live
realization where a stale one is still held by an un-re-realized `Font` would invite readings that are
not true.

`RegisteredFontFamily` reports each registered family with its faces, their internal family names and
their styles. That is the one diagnostic that answers a real question - "why is my text this face" -
and it is what section 4.8's enumeration is built from.

## 8. Cost, and how it will be measured

`FEATURE-COST-POLICY.md` asks four things of a change (section 8: a size measurement, a warning
review, a startup check, a use check), and this change is not a feature, so its rungs (section 4) do
not apply - the "could it be on a lower rung" question has the answer "no, by definition", because
every application that draws text uses `Font`. What the policy's checklist does ask, and this change
answers, is the first bullet: *does an always-retained type gain a member?* Yes, deliberately, and
that is the change. The remaining checklist items are the measurements, and they are not optional
because the type is always retained.

**What is expected to get cheaper, qualitatively.** Five theme fonts are constructed when `Theme` is
first touched (`_defaultFont` .. `_sectionFont`, static field initialisers in
`src/core/Kolvir/Core/Theme.cs`). Today that is five typeface resolutions, five `SKFont`s, five metric
reads and five ASCII coverage probes. After the change it is five four-field objects and no Skia call
at all, so startup should improve. A `Font` instance holds four fields and one nullable reference
instead of a typeface, an `SKFont`, five collections (four `Dictionary`s, a `List` and their
backing storage), a style and two flags. The caches move from per instance to per description.

**What is expected to get more expensive.** The manager is new always-retained code, and it wants
`System.Collections.Immutable`, which the core assembly does not use today - it appears only in
`Kolvir.Feature.Observables` and in the generators. Since `Font` is always retained, an application
that never looks at a font still carries the manager. That is the one cost this change adds to
applications that use nothing new, and it is the reason the size measurement is not a formality.

**What will be measured, and against what:**

1. **Publish size.** `Kolvir.Minimal` by the policy's own command, on all three RIDs, before and
   after, from build roots of equal length:
   `dotnet publish src/demo/Kolvir.Minimal/Kolvir.Minimal.csproj -c Release -r <rid> -p:EmbedSkiaSharpNative=false`,
   with `-p:TrimmerSingleWarn=false` for the warning review, using the 512-byte resolution rule and
   re-publishing any delta that lands on a single alignment step. The number is quoted in the pull
   request per platform. If the manager's contribution cannot be attributed between our code and
   `System.Collections.Immutable` by reading the warning output, the fallback shape is the same
   lock-free-read discipline over a plain `Dictionary` swapped by `Volatile` - copy-on-write, no new
   dependency - and that is a decision for the measurement to force, not one this note takes (open
   question 3).
2. **Per-instance allocation.** A count of managed bytes for N `new Font(family, size, weight,
   italic)` calls, before and after, measured with `GC.GetAllocatedBytesForCurrentThread` in a
   harness that also does the native work outside the measured region. Today the number is dominated
   by the four `Dictionary`s and the `List`; after the change the first instance for a description
   still pays for the realization and the rest pay four fields. The claim to demonstrate is "a
   font that is described and thrown away allocates one small object", which is what a Virtual DOM
   render does.
3. **`Control` size.** Expected to be unchanged: the two fields that matter (`_font` and
   `_inheritedFont`, `src/core/Kolvir/Core/Control.cs`) hold the same `Union` and the same reference.
   Measured with `Unsafe.SizeOf<Control>` on a trivial subclass before and after, and reported as a
   number rather than a claim.
4. **Startup work.** The `Kolvir.Minimal` path with text on screen, confirming no new native call at
   window creation and that the theme's five fonts cost no Skia call until text is measured. This is
   the one place the change is expected to be a *win* on a number, and a win is still reported rather
   than assumed.

No number is invented here. The pull request that implements the change carries the measurements, as
the policy's status line asks of every change.

## 9. Migration

`Font`'s public surface today: four constructors, three static factories, `IDisposable`, and
`Size`/`Weight`/`Italic`/`FamilyName`/`LineHeight`/`BaselineOffset`/`MeasureWidth`/`WithSize`. After
the change the constructors and the four data properties and `MeasureWidth`/`WithSize` stay, and the
factories and `Dispose` go.

**Call sites in the tree today**, so the size of the change is not a guess:

| What | Where | Count |
| --- | --- | --- |
| `new Font(...)` outside `Font.cs` itself | `Theme.cs` (five statics plus `ThemeFont`), `RichLabel.cs`, `AppKitSkiaView.cs`, demo `FontsFromFilePage.cs` | 9 |
| `Font.FromFile`/`FromStream`/`FromBytes` in non-documentation code | `FontLoader.cs` (2), demo `Program.cs` (2), `FontsFromFilePage.cs`, `InheritedAppearancePage.cs` (3) | 8 |
| Font construction in tests (`Font.From*` or `TestFont.Create`) | 7 test files, 48 sites | 48 |
| `using`/`using var` on a font | 33 sites across `AppearanceUnionTests` (9), `FontFallbackTests` (7), `FontLoadingTests` (6), `FontLoaderTests` (5), `InheritedAppearanceTests` (5), `AppKitSkiaView` (1) | 33 |
| `Theme.UseFont` | demo `Program.cs` (2), demo `InheritedAppearancePage.cs` (2), tests `FontLoadingTests` (2) | 6 |

**What breaks for an application, and what it does instead:**

- **`Font` stops being `IDisposable`.** A `using var font = Font.FromBytes(...)` stops compiling
  (CS1674), and `font.Dispose()` stops compiling (CS1061). The fix is deleting them, which is the
  point: nothing is owned and nothing needs releasing. The two tests that exist only because of
  disposal - `ADerivedFontOutlivesTheFontItCameFrom` and `DisposingAFontTwiceDoesNothing` in
  `tests/core/Kolvir.Tests/Ported/FontLoadingTests.cs` - go away, and so does
  `RichLabel.ClearDerivedFonts`'s disposal loop (`_derivedFonts` stays as a lookup cache but holds
  descriptions, which re-realize themselves). `tests/core/Kolvir.Tests/TestSupport/TestFont.cs` loses
  `Create` and `Pin`: it registers the embedded face once and hands back its family name, and the 48
  test construction sites become `new Font(TestFont.Family, size)`.
- **`Font.FromFile`/`FromStream`/`FromBytes` disappear.** Three lines replace them:
  ```csharp
  var family = registry.RegisterFromFile("Fonts/Inter-Regular.ttf");  // or Register(name, bytes)
  Theme.FontFamily = family;
  var bold = new Font(family, 14f, bold: true);
  ```
  There is no convenience that returns a `Font`, because the name is the application's choice and a
  convenience would have to invent one from the file's internal name - which is precisely the
  ambiguity section 4.5 keeps out of resolution. `RegisterFromFile` returning the registered name is
  the one-liner, and it composes with `new Font(...)`.
- **`Theme.UseFont` disappears**, replaced by `Theme.FontFamily = registeredName` (4.9).
- **A reference comparison becomes a value comparison.** Section 3. The visible effect is that
  assigning an equal font no longer forces a re-measure; nothing in the library relied on it, and a
  registration is the supported way to force one.
- **`Font.Weight`/`Italic` mean "asked for", not "in the file".** For a registered family that holds
  only a regular face, `new Font(family, 13f, bold: true)` reports 700 and draws the regular face -
  the same non-synthesis rule as today, now visible on a value that used to echo the file. The actual
  style is reported by the registry entry and the realization.
- **`FontDialogOptions`'s remarks need one sentence** (4.8): registered families resolve but are not
  listed by the platform dialog.

**Compatibility.** No version is pinned in `Directory.Build.props` or `Directory.Build.targets`, and
no compatibility promise is written down anywhere in `dev-docs/`, so this is a clean break rather
than a deprecation: the members go rather than gaining `[Obsolete]` shims. The reason is section 4.5's
name ambiguity - a shim for `Font.FromBytes` would have to invent a family name, so an application
would end up with two names for one face and no way to tell them apart. The migration table above is
the compatibility story: every break has a mechanical replacement of one to three lines, and the two
things that are not mechanical - a disposable font and a reference comparison - are behaviour
removals the note argues are improvements.

## 10. The rejected alternatives, in one place

| Alternative | Why it lost |
| --- | --- |
| Keep `Font` heavyweight and cache fonts at the call site | The call site is a render pass in application code; the library cannot know which of two equal fonts are meant to be the same. A cache above it is a manager with an extra hop and no shared lifetime rule. |
| A struct `Font` with a lazily filled realization | A copy of a struct copies the lazy field mid-life: the copy realizes and the original stays empty, or the realization is created twice. The class is the reason one object has one answer (2.1). |
| Key the manager by `Font` | The manager holds the `Font`, the `Font` holds the realization, so the realization is always reachable, its finalizer never runs, and one entry leaks per description forever (2.3). |
| Key by the description's hash alone | A hash is not a key: two descriptions can collide, and the entry would have to be verified against the description anyway. The readonly struct is the same information with no collision. |
| Reference counting, or `IDisposable` on `Font` | A per-render object cannot carry an ownership contract that every call site keeps; a missed release leaks native memory and a double release is a crash. Liveness follows the last reader instead (2.4). |
| `RealizedFont` disposing its Skia objects in its finalizer | Native disposal on a finalizer thread during shutdown is the classic crash, and SkiaSharp's own objects are already finalizable, so the work is redundant (2.4, 6). |
| A `ConditionalWeakTable` instead of the dictionary | Its keys compare by reference and must be reference types, so a description cannot be a key without wrapping it in a per-description object that then has to be found somehow - which is the dictionary it was replacing. |
| Asking the registry for its version on every measurement, instead of copying the epoch once at realization | A volatile read or a lock on the measure path, for a change that happens at startup. The `Font`'s own epoch field is one compare with no synchronisation (4.4). |
| Registering only before first use | Forbids the case registration exists for: an application shadowing the system family its theme already realized at startup (4.4). |
| Per-family invalidation versions instead of one global epoch | Every control would have to record which families it used, which is the per-control font state this change removes (4.4). |
| Refusing to unregister a family that has live realizations | Those realizations are valid and usable; blocking or disposing them would make unregistration a lifecycle protocol. They keep their face and resolution changes for later fonts (4.4). |
| Two invalidation paths, one for register and one for unregister | They are the same event - "resolution of family X changed" - so one path with one bump (4.4). |
| A per-family measure-cache version instead of `Theme.FontVersion` | Already written, already correct for the harder case, and precision would need the per-control family bookkeeping above (4.4). |
| Face objects owned by the registry, disposed at unregister | Live realizations hold the typeface; disposing it at unregister is a use-after-free. Each realization builds its own typeface from the registered bytes, so nothing is shared to dispose (4.4, 4.5). |
| A face-holding `Font` as a second kind of font (`FontFace` inside the description) | Two things that resolve differently inside one type, and the key grows a case that cannot be compared. The registry is one name for both cases (4.1). |
| Content-hash identity for a registered face, so the same bytes registered twice share one face | Hashes the whole file at registration - more expensive than the parse it saves on the path this change already made cheap - and buys sharing only for an application that registers one file from two code paths. Open question 4 is where the same hash would have to be taken anyway, and it should be decided once. |
| `Theme.UseFont(Font)` kept as a convenience | It would have to resolve the `Font` back to a face, which is the registry's job, and the face it needs may be one the registry was never told about (4.9). |
| A `Font` returned from registration | The name is the application's choice, so a returned `Font` would need a name invented from the file, and an invented name is a second name for one face (9). |
| A per-family face picker beyond nearest weight/slant | Today's rule is "nearest available, no synthesis"; a second rule inside our resolver would disagree with Skia's for system families (4.3). |
| Registered families drawn into the platform font dialog | The dialog is the platform's and lists its own font manager. An answer that cannot be given on all three platforms is worse than one that is honestly absent (4.8). |
| `FontManager` as an injected singleton with resolution taking a resolver argument | `Theme` realizes with no control and therefore no services; a resolver argument would thread the container through every measurement call (4.2). |
| Making `RealizedFont` public | It would make the registration epoch and the invalidation rule part of the public contract, for a caller that can already draw and measure through `Font`. Open question 2. |

## 11. Open questions

Genuinely open. Each is something this note does not decide, with the reading I would start from, so a
reviewer can disagree with the reading rather than with a decision presented as made.

1. **Is the inherited-override type worth building at all?** Section 5 settles how it would work - a
   distinct `FontOverride`, composed in `EffectiveFont`, never seen by realization, `Control.Font`
   growing to `Union<Font, FontOverride, InheritedValue>` - and does not settle whether the third
   union case earns its keep. What the decision needs is one real consumer: an application whose
   controls inherit a font assigned high in the tree and want "a bit bigger here" without writing a
   second theme role. The reading to start from is to wait for that consumer and ship only the
   registry and the description/realization split, because every application pays for the union arity
   (a wider value, a case to test for on read) whether or not it uses an override.
2. **Should an application be able to reach the realization?** `RealizedFont` is proposed internal,
   with `Font` as the whole surface. What an application cannot do today either - draw with a
   `Font` directly, or read `SKFont.Metrics.Descent` for its own layout - it still cannot do. The
   question is whether that stays right: the reading to start from is yes, because exposing the
   realization makes the epoch and the invalidation rule public, and a caller that reaches an
   object which can be replaced under it needs the whole section 4.4 contract documented as public
   API.
3. **`ImmutableDictionary`, or a swapped `Dictionary`?** The agreed shape is
   `ImmutableDictionary<FontKey, WeakReference<RealizedFont>>` read lock-free. It is the first use of
   `System.Collections.Immutable` in the core assembly, and since `Font` is always retained the
   container ships to every application. The reading to start from is to measure it - section 8's
   step 1 attributes the delta - and to fall back to a plain `Dictionary` swapped by `Volatile` under
   a lock if the delta is more than one alignment step, because the read pattern the immutable table
   buys is exactly the copy-on-write pattern a swapped dictionary gives.
4. **Does re-registering a face need to be cheap?** Registration parses the file once to read its
   internal family name and style, and the bytes are kept. An application that registers the same
   file from two code paths pays that parse twice. The reading to start from is to leave it: parsing
   happens at startup, and a content hash at registration is the natural way to de-duplicate it, which
   is the same decision as content-hash identity and should be taken once rather than twice.
5. **What is the exact `Register` overload set?** `(family, data)`, `(family, FontFace)`,
   `RegisterFromFile(path, family?)` and the optional weight and italic overrides are what this note
   assumes. Whether the overrides belong at registration at all - rather than always being read from
   the file - is surface that has no caller yet, so it is listed rather than fixed.
