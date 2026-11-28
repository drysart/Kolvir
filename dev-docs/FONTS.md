# Fonts - design note

Status: **sections 1-9 are built.** Section 5 - inherited overrides - is layer 2 of a two-layer
stack: layer 1 (sections 1-4 and 6-9, `4a5b283` on `develop`) deliberately left `Control.Font` as
`Union<Font, InheritedValue>` and `EffectiveFont` returning `Font` so that layer 2 only adds a case,
and layer 2 landed on a branch stacked on it. Section 5 records what was built and where the built
shape differs from this note's design. The note was written before the
work started, on `develop` at `b8c39b2`; where a section says what the code does today it now
describes the implementation, and every place the implementation disagreed with the design is recorded
in section 9 with the reason.

`Font` is used by every text-bearing control in the library, so this is a core change and the note is
written as one. Where a section says what the code does today it names the file and the member, and
that statement is meant to be checkable rather than remembered. Where a section is an argument rather
than a decision it says so. Section 10 is the rejected alternatives in one place; section 11 is what
is genuinely still open, with the reading each question was resolved with.

**File and line references describe the tree the design was settled on (`d05540b)** unless they name a
type this layer added. The members they point at are now in `RealizedFont` (`src/core/Kolvir/Core/`) or
`FontManager`, so a reference to `Font.cs:192` is a reference to
`RealizedFont.ResolveFace` today.

The names the note proposed are the names that shipped: `RealizedFont`, `FontKey`, `FontManager`,
`FontRegistry`, `IFontRegistry`, `FontFace`, `RegisteredFontFamily`, `NotifyFontsChanged`,
`InvalidateFamily`. `FontOverride` shipped with the name this note gave it; the static class that
starts a chain is `InheritedFont`, a name the note did not have and that the user's own code supplied.

Two decisions changed during the work and are recorded where they belong rather than here: **a
description always names a real family** (section 2.1), which reverses the note's first draft and fixes
every theme font on Windows silently drawing regular; and **width is not a style axis** (section 4.5),
which is a limitation stated so it is not re-raised.

## What this layer landed, and what it cost

| | before | after |
| --- | --- | --- |
| `new Font("Verdana", 13f)` | 987.60 bytes | 48.00 bytes |
| The five theme fonts, first touch | 5,592 bytes, 1.912 ms | 488 bytes, 1.092 ms |
| First geometry read of `Theme.DefaultFont` | 0 bytes (already realized) | 1,904 bytes, 0.922 ms |
| `new BareControl()` (a `Control` subclass) | 608.0 bytes | 608.0 bytes |
| A cached `MeasureWidth`, 200,000 calls | 13.078-14.309 ms | 13.646-14.218 ms |
| A wrapped layout, 350 characters at 420 px | 25.617-28.617 ms | 25.615-27.565 ms |
| `Kolvir.Minimal`, win-x64, Skia loose | 3,886,080 bytes | 3,914,752 bytes |

The last row is the one cost every application pays: **28,672 bytes**, measured from build roots of
equal length with `-p:EmbedSkiaSharpNative=false`, and reproducible (the baseline publishes at
3,886,080 twice). It is not the manager's data structure - see open question 3, which the measurement
settled: an `ImmutableDictionary` would have cost 34,304 bytes more. Of the 28,672, **4,608** are the
always-named-family decision (section 2.1): the cached default-family lookup and the branch in the
constructor, which is the price of a description that can ask for a weight.

The two timing rows have no result to report: every sample of both trees overlaps, so the epoch
compare this layer adds in front of every `MeasureWidth` is below this method's resolution. Section 8
says why that is the expected answer and why it was measured anyway.

The per-instance allocation is the change's headline: a description that is created and thrown away
costs 48 bytes instead of 988, which is what a Virtual DOM pass that builds a font per text node per
render does.

Layer 2 (section 5, the inherited override) adds one row to that table and it is not in it: 1,536
bytes on `Kolvir.Minimal` and 16 bytes on a `Control`, measured in section 8.3.

A face that travels with the application is now named once and referred to like any other family.
`Font.FromFile`, `Font.FromStream`, `Font.FromBytes`, `Font.Dispose` and `Theme.UseFont` are gone;
`Font` is four fields, `IEquatable<Font>`, a lazy `Realize()` and forwarders, and `RealizedFont` holds
everything that used to be heavyweight.


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

`Font` keeps only what describes a face: family name, size, weight and italic - four fields, and the
name is always a real one because a description made without a family names the platform's default
family at construction (section 2.1). It holds no `SKTypeface`, no `SKFont`, no metrics and no caches.
It is a class rather than a struct for one reason, and it is the reason the note records even though
the shape is otherwise struct-like: it carries a field that is populated lazily, and a struct with a
lazily filled field is a copy hazard - a copy is made, the copy realizes, the original stays empty,
and every `Font` that was copied out of a field now has two lives. A class gives one object with one
lazy field, and copies are references to it.

Two distinct instances with the same description compare equal (section 3).

`Size`, `Weight` and `Italic` are data and answer without touching Skia. `FamilyName` **echoes the
description**: the name the font was made with. It never realizes, and it has to be an echo for two
reasons. It is what the key is (section 2.3), so a property reporting the *resolved* face would
disagree with the key that produced it. And section 4.5's round trip -
`new Font(f.FamilyName, f.Size, f.Weight, f.Italic)` reproducing the same face - is only a property if
the name is the one resolution uses.

That is a change from today. `FamilyName` is `_font.Typeface?.FamilyName ?? string.Empty`
(`src/core/Kolvir/Core/Font.cs:273`), which is the face that resolved, and `ResolveFamilyFace`
(`Font.cs:192`) falls back to `SKTypeface.Default` when the platform has no such family - so
`new Font("NoSuchFamily", 13f).FamilyName` reports the platform default's family today and
`"NoSuchFamily"` after the change. The property's own summary - "a report of the face rather than an
echo of the constructor" - is wrong under the new rule and changes with it. Section 9 lists the
behaviour change and its observable consequences.

`WithSize` and the internal `Derive` stay on `Font` and become description arithmetic. Today `Derive`
has a branch that reuses the `SKFont`'s typeface so a derived font keeps a loaded face; with no face in
the description there is nothing to keep, and the branch disappears.

**A description always names a real family.** Null, empty or whitespace does not mean "the platform's
default" as a distinct thing to key on: it means the platform's default *family*, and it is resolved to
that family's name when the description is constructed. `RealizedFont.DefaultFamilyName()` supplies the
name, once per process, and a platform that reports no default family at all leaves the description
unnamed so the degenerate case keeps working instead of throwing.

This is a decision taken during implementation, and it reverses the note's first draft, which had
resolution honour a style for an unnamed description. The evidence is that the draft's alternative was
not the status quo it was protecting:

- `ISystemTheme` has exactly one member, `bool IsDarkMode` (`src/core/Kolvir/Core/SystemTheme.cs:12`).
  There is no font family in it.
- The demo's Windows branch returns `null` from `PreferredFontFamily()`
  (`src/demo/Kolvir.Demo.App/Program.cs`) with a comment claiming the Win32 backend installs an
  `ISystemTheme` that the library reads the font from. That mechanism does not exist. `Theme.FontFamily`
  is only ever assigned from a non-blank `ApplicationOptions.FontFamily`.
- So on Windows **every theme font is unnamed**, and `FromFamilyName(null, style)` answers the regular
  face whatever style is asked for. Measured on this machine:

  | asked for | before (`null` family) | after (named family) |
  | --- | --- | --- |
  | w400 | `Segoe UI` w400 upright | `Segoe UI` w400 upright |
  | w600 | `Segoe UI` w400 upright | `Segoe UI` w600 upright |
  | w700 | `Segoe UI` w400 upright | `Segoe UI` w700 upright |
  | w400 italic | `Segoe UI` w400 upright | `Segoe UI` w400 italic |
  | w700 italic | `Segoe UI` w400 upright | `Segoe UI` w700 italic |

  With the theme's own roles: `TitleFont` asks for w700 and `SectionFont` and `EmphasizedFont` for
  w600, so on Windows the sample's titles, section headings, button labels and emphasis were all drawn
  regular. That is the defect, and it is what the render comparison in section 8.2 sees. An application
  that wants bold states a family, and naming the default family is how a description states the same
  thing without a name from the caller.

Naming it at construction rather than lazily in `ResolveFace` is the second half of the decision, and
it is what keeps `Font` and `FontKey` honest: a description that kept an empty family and let
realization substitute the default would be a *different key* from the same family named explicitly,
so `new Font(13f)` and `new Font(defaultFamily, 13f)` would hold two realizations and two sets of
measurement caches for the same face. Normalising before the key is built gives the sharing for free.

The cost is stated rather than glossed: **creating a font calls nothing native, except the first one
constructed without a family, which asks the platform for its default family name once and caches the
answer for the process.** `Font`'s remarks say exactly that.

`Derive` is therefore pure arithmetic again - `new Font(_familyName, size, weight, italic)` - and the
special case the note's second draft added ("name the default family only when the style changes") is
gone with the `DefaultFamilyName()` call site that was its only reason. That exception was worth
recording rather than deleting from history: it existed because an unnamed description could not
express "the default family in bold", it was the *fix* for the two sample pages that stopped drawing
bold spans in bold, and the underlying defect it papered over was that a description with no family
silently dropped every style asked of it. Resolving the name once at construction removes both the
symptom and the cause.

### 2.2 `RealizedFont` becomes the heavyweight object

Everything `Font` holds today that is not description moves to `RealizedFont`: the `SKFont`, the
typeface it resolved and the one it owns, the fallback `SKFont`s and their typefaces, `_style`,
`_coversAscii`, and all five caches. The member split:

| Member | Today | After |
| --- | --- | --- |
| `Size` | `Font` | `Font` (data) |
| `Weight` | `Font`, read from the face for loaded faces | `Font` (data, the requested weight) |
| `Italic` | `Font` | `Font` (data) |
| `FamilyName` | `Font`, from the typeface | `Font` (data, the description's name) |
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

The internal members move rather than forward because they are the realization: `RichLabel.cs` and
`TextBoxSpanFeature.cs` read the `SKFont`'s metrics, and both are in the same assembly, so they say
`font.Realize().SkFont.Metrics`. Tests that assert on runs (`FontFallbackTests` uses the internal
`TextRunCount`) call the same accessor.

**As built, one correction to the paragraph above.** `internal DrawText` is not reached by two places
but by nine: `TextRenderer`, `TextBox` twice, `MenuBarView`, `MenuRenderer` twice, `RichLabel` and
`TextBoxMultiline` twice. All nine now say `font.Realize().DrawText(...)`. The alternative - an
internal forwarder on `Font` - would have been a forwarding member for the realization, which is the
one thing this table's rule excludes.

`RealizedFont` is internal. An application draws and measures through `Font`, which is the surface it
already has; exposing the realization would make the registration epoch of section 4.4 part of the
public contract, and nothing in the library needs it. Whether that is right long term is open
question 2.

### 2.3 The key, and why it cannot be a `Font`

`FontManager` resolves a description to a realization, once per description, through a table read
lock-free on the happy path, and creates it under one lock on a miss. Section 8's measurement chose the
table's shape: it is a plain `Dictionary<FontKey, WeakReference<RealizedFont>>` published by a single
volatile write and never mutated after publication, rather than the `ImmutableDictionary` this section
first proposed (open question 3).

`FontKey` is the description as a `readonly struct`: the family name, the size, the weight and the
italic flag, with value equality over exactly those four fields. Family names compare `Ordinal`, which
is the comparison the rest of the library uses for a family name (`RichLabel.ResolveFont` compares a
span's family to `EffectiveFont.FamilyName` with `StringComparison.Ordinal`), and a null, empty or
whitespace family all normalise to the same thing - the platform's default family, named at
construction - so `new Font(null, 13f)`, `new Font("", 13f)` and `new Font("   ", 13f)` are one
description rather than three, and all three are the same description as
`new Font(platformDefaultFamily, 13f)`, which is what lets them share one realization (section 2.1).

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

`FontManager` is a static class holding one table and one epoch counter (section 4.4). It owns
resolution and nothing else: it does not own faces, does not decide what a family means (the registry
of section 4 does), does not size anything and does not know what a control is. A single
`RealizedFont` per description is created on the first `Font` that needs it, and thereafter a
dictionary read.

**As built, `FontManager` and `FontKey` are internal**, not public. Section 2.2's argument for keeping
`RealizedFont` internal applies to both: the epoch and the invalidation rule are what a caller would
need documented as public API if it could see them, and nothing outside the library needs either type.
"Internal" rather than merely undocumented, so the compiler enforces the claim.

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
    string Register(string family, ReadOnlySpan<byte> data);
    string Register(string family, FontFace face);
    string RegisterFromFile(string path, string? family = null);      // 4.7
    bool Unregister(string? family);                                  // 4.4
    IReadOnlyList<RegisteredFontFamily> Families { get; }             // 4.8
}
```

**As built, the overload set is exactly this**, and the optional weight and italic parameters this
section first sketched are gone. Nothing in the tree needed them: a file's style is read from the
file, and an application that wants a second style of a family registers a second face - which is what
section 4.5 already describes as the reason the registry holds a set per family. Adding `int? weight`
would have been surface whose only purpose was to let a caller lie about the file it is registering.
Open question 5 records the decision.

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

**Step 1 is reached with a real name.** A description built without a family names the platform's
default family before it is keyed (section 2.1), so it resolves through the registry like any other
name - which is what lets an application register its own face under the platform's default family and
have every theme font that was never given a family pick it up. Nothing with a blank family reaches
resolution, which is why `Register` refuses one (4.5).

**As built, a resolution also decides the layout settings, and that is load-bearing.** `RealizedFont`
keeps the flag the old `Font` called `_fixedFace`, and it now means "this face came from the
registry". A face from the registry gets `LinearMetrics = true`, `Hinting = None` and
`BaselineSnap = false` - exactly what a face loaded from data got before this change, and the reason a
face registered from a file measures the same on all three platforms. The note listed `_fixedFace` as
moving without saying what sets it; nothing else does, and getting it wrong would move every
measurement the demo and the suite pin.

Shadowing is worth more than it first looks for one reason: the family an application puts in
`ApplicationOptions.FontFamily` is usually the platform's own UI family - the demo application passes
`PreferredFontFamily()` from `AppKitSystemTheme` or `Gtk4SystemTheme` into it
(`src/demo/Kolvir.Demo.App/Program.cs:112`), and `InstallProcessServices` assigns it to
`Theme.FontFamily` (`src/core/Kolvir/Application/Application.cs:746`). An application that registers
that same name gets its face with no theme change at all. Note that the backends only *report* the
preferred family; they do not assign the theme, whatever `FontFamily`'s own summary says.

**Windows is the case where nothing is reported, and it is what did the damage.** The demo's Windows
branch of `PreferredFontFamily()` returns `null`, so `ApplicationOptions.FontFamily` stays blank,
`Theme.FontFamily` is never assigned, and every theme font is a description with no family - which,
before the rule in section 2.1, meant all of them asked Skia for a style against a null family name
and got the regular face back. That is the defect the section 2.1 evidence measures, and naming the
default family at construction fixes it without the demo having to know a family name. Whether the
desktop's chosen UI family should be *reportable* - `ISystemTheme` carries only `IsDarkMode` - is open
question 6.

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
  something new rather than find the old entry. Removing only this family's entries is free in
  *correctness* and not in time: finding them is a scan of the table, and the immutable dictionary
  rebuilds itself on the way out. That is acceptable because invalidation is startup-shaped - once per
  registered or unregistered family, never per frame - and the table holds one entry per description
  in use, which is a handful. The epoch itself is global and blunt because a per-family epoch would
  have to be recomputed per `Font` against a set of changed families.

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

- **`Register` refuses a null, empty or whitespace family name**, with an `ArgumentException` naming
  the parameter. This is not politeness: a description made without a family is normalised to the
  platform's default family *name* before it is keyed (section 2.1), so the blank name is not a key any
  `Font` can ever hold - a face stored under it would be an entry no description can reach, and the
  call would look like it had worked. The call site is the mistake, it is a startup-time call, and a
  throw is the right feedback.
- **Same family, same weight and slant, registered twice: replace.** `Register` is idempotent by
  (family, weight, italic), and the later call wins. The alternative - refuse - makes correctness
  depend on call order, and a second registration from a second code path is not a mistake worth
  failing the process over.
- **Same family, different weight or slant: a second face of that family.** The registry holds a set of
  faces per family name, keyed by `(Weight, Italic)`, and this is what that is for: register a regular,
  a semibold, a bold and an italic file under one name and each lands in its own slot - (400, upright),
  (600, upright), (700, upright) and (400, italic) for the four files - with the style read from each
  file's own metadata rather than supplied by the caller (which is why `Register` has no style
  overrides; open question 5). Resolution then answers a description with the nearest weight,
  preferring a matching slant, so `new Font(family, 13f, 700)` draws the bold file and
  `new Font(family, 14f, bold: true, italic: true)` draws the italic one at the nearest weight beside
  it. It is also why `Theme.FontFamily` plus a registered family can serve `EmphasizedFont` honestly if
  the application registers its semibold file. Today one file is one face and no weight can be selected
  from it; this is a strict improvement.
- **Width is not an axis.** The registry distinguishes faces by weight and slant only, so a family is
  `(Weight, Italic)` and nothing else. A condensed face is registered under its own family name -
  `Register("Brand Condensed", bytes)`, which is how foundries ship it - and the toolkit does not model
  OpenType's third style axis (width; `usWidthClass` 1-9, `SKFontStyleWidth` in Skia). The consequence
  is worth stating plainly because it reads as a bug otherwise: **two faces of one family registered
  with the same weight and slant replace each other, and width does not distinguish them**, so
  registering regular and condensed under a single name leaves the later registration in the (400,
  upright) slot and the earlier one gone. Give a condensed face its own name.
- **The same file registered twice** under the same name parses twice and replaces once. Idempotent
  in effect; the extra parse is the price of not hashing every face at registration, which is a
  measurement to revisit rather than a rule to add now (open question 4).
- **A file whose internal family name differs from the name it is registered under: the registered
  name wins for resolution.** The file's internal name is kept in the registry entry, and reported by
  `RegisteredFontFamily`, for diagnostics. Two reasons: the application's reason to register is that
  the name its code uses is the name it wants, and two builds of the same internal family
  ("Inter Regular", say) must be distinguishable. `Font.FamilyName` reports the registered name, so
  `new Font(f.FamilyName, f.Size, f.Weight, f.Italic)` reproduces the same face - the round trip is
  the property the property is for.

Unregistration takes a family name and removes every weight and slant of that family. **It never
throws.** It returns `bool`: `true` when something was registered under that name, `false` otherwise -
for an unknown name, for one already removed, and for a null, empty or whitespace name, which is one
more name nothing is registered under and therefore cannot remove anything. **It is idempotent** -
the second call returns `false` and does nothing, including no version bump when nothing changed.

That asymmetry between the two members is deliberate, and it is a rule rather than an oversight.
`Register` is a call that has to be right: there is something to store, the name is part of what is
stored, and a throw at startup is useful feedback. `Unregister` is a call that has to be safe: it is
the cleanup half, and cleanup runs in `finally` blocks and error paths where the name may never have
been produced. A `string? family = null; try { family = Register(...); } finally { Unregister(family); }`
is exactly the shape that makes a symmetric rule wrong - if `Register` throws, the cleanup would throw
again and mask the original failure. So the two members answer different questions - "store this" and
"remove this" - and only the first refuses. That is also why `Unregister` takes `string?`: the cleanup
call site should not need a null-forgiving operator to say "and if I never got a name, do nothing".

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

`RegisterFromFile` reads the bytes through an `IFontLoader`, and **as built that loader is pushed into
the registry rather than resolved from it.** The note left this as "implementation detail", but the
detail has to be decided somewhere and the registry is a process-wide static with no container of its
own, so `Application.InstallProcessServices` calls an internal `FontRegistry.UseLoader(...)` with the
container's `IFontLoader` - the one composed from the features the application declared. That is what
makes the example above work: the WOFF provider is in the container, so it is in the loader, so it is
what `RegisterFromFile` unwraps with.

**What a registration with no loader does, which the note did not specify.** With no applications
constructed - a test, or a start-up that registers a web font before it builds the `Application` - the
registry reads through one static, provider-less `FontLoader`, and the built-in path alone is left: it
reads TrueType and OpenType, so a `.ttf` registers with no `Application` at all. A container that
reaches that path is refused by `FontFace`'s constructor, and the note's first draft would have
reported it as "the data is not a font this build can read" - true and useless, because the cause is
the registration order rather than the bytes. So a WOFF or WOFF 2 signature that nothing claimed is
re-thrown with an explanation naming both the cause and both fixes: no `Application` was constructed,
so no provider from `AddWoffFonts()` was installed, only TrueType and OpenType can be read, construct
the `Application` before registering a container, or hand over the OpenType the container unwraps to.
Two conditions keep that honest: the explanation is added only when no loader was installed, and only
for the two WOFF signatures - so a genuinely damaged OpenType file still reports exactly what it is,
and an application that exists but never declared `AddWoffFonts()` gets the loader's own answer rather
than advice about constructing something it already constructed.

**The case only exists where the host refuses the container.** On macOS Skia's own font manager reads
WOFF 2 through CoreText, so the same registration simply succeeds and there is nothing to explain;
Windows reads neither container and Linux reads WOFF 1.0 only, so those two are where the failure
happens. The platform difference is the one section 3 already records rather than a new one, and the
test records both answers instead of asserting one platform's: it registers a container before any
`Application` exists and then requires either that the registration was read (macOS) or that the
failure carried the cause and both fixes. That is also the honest limit of the requirement this
explanation came from - its premise, "a `.woff2` registered too early reports 'this is not a font'", is
true on two of the three platforms.

`Application.TearDown` clears the pushed loader, so a registration after the application is gone is
told the same thing a registration before it existed is.

`UseLoader` had no test and one caller before this pass; it now has both directions covered -
`ARegisteredContainerIsReadByTheApplicationsOwnLoader` registers a `.woff2` after constructing an
`Application` whose `ConfigureServices` called `AddWoffFonts()`, and compares the measured width and
line height against the same face registered from bytes the test decoded itself; and
`AContainerRegisteredBeforeAnyApplicationSaysWhatToDoAboutIt` asserts the message - by signature, for
the reason in the paragraph above - for both `RegisterFromFile` and `Register`.

When `family` is null the registered name is the face's internal family name. A face decompressed from
a WOFF file is stored exactly like a face read from a `.ttf`, because by the time it reaches the
registry it *is* one. Nothing in the registry knows what a container format is.

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

**As built**, `FontDialogOptions`'s remarks carry it: the paragraph about an arbitrary loaded face
became one about a registered family, and it names `IFontRegistry.Families` and the choice to build a
picker, next to the existing note that `PickFont` answers with a `FontSelection` for the same reason.

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

### 5.1 As built

**The API.** `label.Font = InheritedFont.WithSize(14f)` compiles exactly as written.
`FontOverride` is a sealed immutable class with `IEquatable<FontOverride>` over four nullable members
- `FamilyName`, `Size`, `Weight`, `Italic` - and no measuring, drawing or realizing member. It has no
public constructor: an override is written, not built. `InheritedFont` is a static class of four
factories, because the name has to resolve at an assignment with only `using Kolvir;` in scope and
only a type can do that; it is the one public name this note did not propose. The instance
`With*` members chain (`InheritedFont.WithSize(14f).WithItalic(true)`), and a null argument leaves
that member inherited, which is how a chain is narrowed back to the neutral override. `string.Empty`
is the platform's default family and `0f` is a real size - the same values they are on `Font`, and the
reason a null member cannot be spelled on the complete type.

**Class, not struct, and the numbers.** `Unsafe.SizeOf` on the closed union, measured in a harness
that references the type from outside the library:

| shape | union | `new BareControl()` |
| --- | --- | --- |
| before (`Union<Font, InheritedValue>`) | 16 bytes | 600.01 bytes |
| struct override (`Union<Font, StructOverride, InheritedValue>`, same four members) | 56 bytes | not built |
| **class override, as shipped** | **24 bytes** | **616.01 bytes** |

A struct of the same four members is 32 bytes, so a struct-in-union costs 32 more bytes in every
`Control` than the reference does; the shipped shape costs 16, of which 8 are the wider union and 8
are the cache described below. That is the whole cost of layer 2 per control, and it is why the type
is a class: the note's alternative was 32 bytes per control for the same capability. `Unsafe.SizeOf`
on `Control` itself answers 8 in both trees, because it reports the size of the reference - section
8.1 recorded the same thing when it specified the measurement - so the allocation of constructing one
is the number that means anything.

**Value equality is what makes it free to use.** `Control.Font`'s setter compares with
`_font.Equals(value)`, which is `Union.Equals` and therefore `EqualityComparer<T>.Default` on the
payload, so an equal-but-new override assigns nothing and invalidates nothing. Two tests pin it: one
in the core suite (the property still reports the first instance and the appearance-change count does
not move) and one in the Virtual DOM suite (a re-render that builds `InheritedFont.WithSize(18f)`
again patches nothing). Without it, every render pass that rebuilds an inline override would invalidate
the subtree - the exact defect layer 1 removed from `Font`.

**Where the composition happens, and what is cached.** `EffectiveFont` composes, as designed, and
nothing else does: `FontOverride.Compose(Font)` is internal, and a partial description has no path to
`FontManager` because it has no `Realize`. The rule that a partial description never reaches a
realization is therefore a shape, not a convention.

The walk up the chain now stops at the nearest ancestor that *has a case*: a `Font` case contributes
that font, and a `FontOverride` case contributes `ancestor.EffectiveFont`, so overrides compose in
order down the tree and an override answers for its whole subtree. One addition the note did not
specify: **the composition is cached only when its base is cached.** When an ancestor assigned the
font, the composed result is stored beside `_inheritedFont` and dropped by the same
`InvalidateAppearanceTree` - which is why it costs a field and not just the wider union. When the base
is the class default, the override is composed on every read against the live `DefaultFont`, so the
rule the guide states - a class default is read every time, and a theme change needs no rebuild -
survives untouched. Without the cache a control with an override would allocate a `Font` per
`EffectiveFont` read, and `ListView` and `ListBox` read it once per row per paint.

**Every member is tested in isolation**, because the failure mode of a type like this is a member that
exists on both `Font` and `FontOverride` and that `Compose` forgets: one test per member asserts that
an override naming only that member changes that field and leaves the other three inherited, a fifth
sets all four through one chain, and a sixth re-sets a member (`WithSize(20f).WithSize(14f)`) so the
last value wins. A member added to `Font` later and not to the composition fails one of them.

**`ToolStripItem.Font` widened too.** A menu item is a control that inherits, "a bit bigger here" is
the same wish, and leaving the two properties different by accident would be worse than the union
arity. The item composes onto `Owner?.EffectiveFont ?? Theme.DefaultFont` and does **not** cache: its
base is the strip's live effective font, so a cache would need to be told when the strip's font
changed, and a strip is a handful of items. The asymmetry is stated in the property's remarks rather
than hidden.

**The Virtual DOM needed no change, and that was verified rather than assumed.** The generator names
no font type anywhere - `property.Type` comes from the control's own declaration, `Values.Unset`
returns null unless `[VNodePropertyUnsetValue]` supplies a literal, and the pristine value is read
from a constructed control. The emitted node for the widened property is:

```csharp
private static readonly Union<Font, FontOverride, InheritedValue> PristineFont = Pristine.Font;
private Specified<Union<Font, FontOverride, InheritedValue>> _font;
// patch:
if (!_font.Equals(old._font))
    target.Font = _font.IsSpecified ? _font.Value : PristineFont;
```

which is precisely the shape section 3 predicted, with the wider union threaded through it by
`property.Type` alone. The mirror-drift test still passes because the node and control property types
are equal by construction.

**An override that names nothing** is *equivalent but not identical* to `InheritedValue`. It is a
different case of the property - `TryGetValue<FontOverride>` is true where
`TryGetValue<InheritedValue>` would be - so code that inspects the assignment sees a difference and
the reconciler sees a property change; but `Compose` returns the inherited `Font` itself when nothing
it names differs from it, so the effective font is the *same object*, and everything downstream that
compares fonts behaves identically. That is the deliberate answer to the question the design left
open: identical in effect, distinguishable in the property.

**What the design left open, and how it was settled.** Three things section 5 does not say, decided
here so they are not re-litigated:

- *What an override composes onto when nothing is assigned above it, and when the class default is not
  the theme's.* It composes onto the font the control would otherwise draw with - the nearest
  ancestor's assignment, else its own class default - so a `Button` with `WithItalic(true)` and no
  assignment above it is the emphasized font in italic, not the default font. A child of a control
  that carries an override inherits the composed font and therefore no longer falls back to its own
  class default, which is the ordinary rule that an assignment answers for the subtree.
- *Whether the composition should be cached, and against what.* Cached exactly when the base is, as
  described above; the class-default case is composed live. The alternative - always cache - would
  have quietly changed the documented "a class default is read every time" behaviour.
- *Whether a `ToolStripItem` caches too.* It does not, for the reason in its remarks: its base is the
  strip's live effective font.

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

**As built, one addition.** The finalizer takes no lock, because a finalizer that blocks is a
finalization queue that stops. Its compare-and-swap therefore competes with the UI thread's
publications, and a removal that loses that race would leave an entry behind. Every publication - the
creator's and the invalidator's - is a compare-and-swap that rebuilds from whatever it finds and
retries, so no publication can drop a concurrent removal; the residual case is the reverse, a removal
that loses to a publication, and what it leaves is a weak reference to a collected object: harmless,
and replaced the next time that description is realized. That is cheaper than the alternative of
locking on the finalizer thread for a table whose entries are also cleaned up by use.

**As built, a second addition the note got wrong: a build can be overtaken by an invalidation.** A
realization is *resolved* before it is *published*, so an `InvalidateFamily` can run to completion in
between: the scan finds nothing for a key that is not in the table yet, does not publish anything
because it has nothing to remove, and the build then publishes a realization made under the previous
resolution. Every later read finds it, and because the font that took it records the current epoch
against it, the epoch compare never fires again - a stale face for the rest of the process. The note's
"the lock covers creation and nothing else" is what leaves the hole. The fix is two-part. The creator
captures the epoch under the lock before it builds and checks it again before it publishes, and a build
whose epoch moved is thrown away and resolved again rather than published. And the invalidator's scan
takes the same lock, which covers the other half of the window: an invalidation that starts after the
check either waits for the publish and then drops that entry, or is refused by the check before it
publishes. The epoch bump itself stays outside the lock, because the check is only meaningful if an
invalidation can land between the two reads.

That makes the lock ordering one-way and worth writing down: a realization holds the manager's lock
across a registry lookup, and the registry calls `InvalidateFamily` only after releasing its own lock,
so the order is always manager-then-registry and the reverse never happens. `InvalidateFamily` costs a
wait for an in-flight resolution, which is one typeface resolve and happens once per registered family
rather than per frame.

## 7. Diagnostics

`Font.ToString` reports the description and never realizes: the family, the size, the weight and the
italic flag, with a description that has no family at all (only possible on a platform that reports no
default family) spelled as `platform default` (`Font(platform default, 13, w400, upright)`) - which
says what the description means, not which family the platform would pick. It is
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

A fifth number is not in the policy and should be taken with the same pull request, because it is the
one cost the design adds to the path it is meant to speed up: **the epoch compare on the measure
path.** `MeasureWidth` is called in binary searches over a line (`RichLabel.FitPrefix`,
`TextRenderer.BuildTextLines`) and several times per line per pass, so a field read and a branch in
front of every call is added to the hottest loop in the library. It is very likely invisible next to
the dictionary probe and the native measurement behind it, but "very likely invisible" is the kind of
claim this note has been refusing to make elsewhere, so it gets a before-and-after timing of one long
wrapped paragraph rather than a sentence.

No number is invented here. The pull request that implements the change carries the measurements, as
the policy's status line asks of every change.

### 8.1 The numbers, as measured

Both trees were published and run from build roots of equal length
(`...\copilot-worktrees\Kolvir\baseline0000` and `...\copilot-worktrees\Kolvir\verbose-lamp`, both 12
characters), SDK 10.0.403.

**1. Publish size.** `Kolvir.Minimal`, `-c Release -r win-x64 -p:EmbedSkiaSharpNative=false`:

| | baseline | this layer | delta |
| --- | --- | --- | --- |
| win-x64 | 3,886,080 | 3,914,752 | 28,672 |

The baseline reproduces at 3,886,080 on a second publish, and 28,672 is 56 alignment steps, so the
delta is the code rather than the 512-byte boundary. Only win-x64 could be weighed here: the fixture
pins its own `TargetFramework` to the host OS, and the publish for another RID stops at
`error : Cross-OS native compilation is not supported.` A linux-x64 and an osx-arm64 row need a
machine of that OS, which is what the policy already says about the publishing machine.

The dependency question is settled by running both shapes. With
`ImmutableDictionary<FontKey, WeakReference<RealizedFont>>` the image publishes at **3,944,448**;
with the copy-on-write `Dictionary` it publishes at **3,910,144** - 3,914,752 after the
always-named-family decision of section 2.1, which is 4,608 bytes and belongs to `Font` rather than to
the table. So `System.Collections.Immutable`
would have cost 34,304 bytes - 67 alignment steps - and open question 3's fallback is what shipped.

`-p:TrimmerSingleWarn=false` reported no IL2xxx or IL3xxx warning in either tree.

**2. Per-instance allocation.** 100,000 `new Font("Verdana", 13f)` with the native work done outside
the measured region, `GC.GetAllocatedBytesForCurrentThread`:

| | baseline | this layer |
| --- | --- | --- |
| 100,000 fonts | 98,759,864 bytes (987.60 each) | 4,800,000 bytes (48.00 each) |

That is the claim this note asked for: a font that is described and thrown away allocates one small
object - 48 bytes, five sixths of it the `Font` object and its string, against 988 for the same
description with its typeface, `SKFont` and five collections.

**3. `Control` size.** `Unsafe.SizeOf` answers 8 bytes for both `Font` and a trivial `Control`
subclass in both trees, because both are reference types and the method reports the size of the
reference - **so the measurement this note specified does not measure an instance size at all.** The
number that can be compared is the allocation of constructing one, and it is unchanged:

| | baseline | this layer |
| --- | --- | --- |
| `new BareControl()` (`Control` subclass, no children) | 608.0 bytes | 608.0 bytes |

As expected and for the stated reason: `_font` and `_inheritedFont` hold the same `Union` of the same
reference, and no field was added to `Control`.

**4. Startup work.** The five theme fonts at the first touch of `Theme`, then the first read of
geometry:

| | baseline | this layer |
| --- | --- | --- |
| five theme fonts, first touch | 5,592 bytes, 1.912 ms | 488 bytes, 1.092 ms |
| first `Theme.DefaultFont.LineHeight` | 0 bytes, 0.017 ms (already realized) | 1,904 bytes, 0.922 ms |

The win is real but smaller than the paragraph above expected: the baseline's 5,592 bytes are five
descriptions with their typefaces and `SKFont`s, and this layer's 488 are five four-field objects.
The cost moves to the first geometry read, which now realizes one description (1,904 bytes, 0.922 ms)
where the baseline had already paid for all five eagerly. What this numbers is "no Skia call until
text is measured": reading `Size` or `FamilyName` on any theme font allocates nothing and touches
nothing native.

**5. The epoch compare on the measure path.** Nine runs of each tree, the median reported:

| | baseline | this layer |
| --- | --- | --- |
| 200,000 cached `MeasureWidth` | 69.8 ns each (13.078-14.309 ms) | 70.4 ns each (13.646-14.218 ms) |
| 2,000 wrapped layouts, 350 characters at 420 px | 26.869 ms (25.617-28.617) | 26.610 ms (25.615-27.565) |

**There is no result here.** The two distributions overlap completely - the baseline's own nine
samples span 65.4 to 71.5 ns per cached measurement - so the epoch compare is below this method's
resolution, and the honest reading is "not measurable" rather than "+0.6 ns". The wrapped-layout row
came out marginally *faster* after the change, which is the same statement: noise. What the change
adds is one volatile read, one null check and one integer compare ahead of a dictionary probe that
costs about seventy nanoseconds, and that is why.

The whole measurement harness is outside the repository - it compiles against either tree through a
`<ProjectReference>` on `$(KolvirRoot)` so the same source produces both columns - and it is not
committed, because a harness that ships is a harness that rots.

**6. The follow-up passes, re-measured.** The review that followed the first push required three things
this note did not: an explanation on the failure a container registered before any `Application` gets
(section 4.7), a test that drives `RegisterFromFile` through the application's own loader, and a fix
for a build that an invalidation overtakes (section 6). None of them is on a measure or paint path, so
the only number that could move is the publish size, and re-publishing the same fixture the same way
gave **3,910,144 bytes** - identical to the number in the table for that commit, so the review pass
cost below one 512-byte step. The decision in section 2.1 came after it and does move the number:
**3,914,752**, 4,608 bytes more, which is the cached platform-default-family lookup and the branch in
the constructor. The table above is published from the tree that carries both.

### 8.2 Renders

Text layout is what this change touches, so every page of the sample was rendered before and after and
the decoded bitmaps compared.

**Method.** All 73 pages in `DemoUi.Demos`, at 1280x820, light appearance, through
`KolvirDemoApp --render <png> 1280 820 --page <id>`. Each build was rendered twice, into separate
directories, and compared with itself first: two pages - `animated-gif` and `animated-webp` - differ
from run to run in *both* builds, in the same 33x46 box, because they paint an animation frame and the
frame depends on the clock. Those two are excluded from the cross-build claim rather than reported as
regressions. Equal PNG bytes are equal pixels because both builds encode with the same SkiaSharp; a
differing file is decoded and compared pixel by pixel, and the number and bounding box below come from
that comparison.

**Result against the baseline, before the always-named-family decision: 71 of 73 pages were
pixel-identical.** The two that were not:

- **`fonts-from-a-file`**, 5,751 pixels in one 38-pixel band at (398,570)-(1243,608). That band is the
  web-font readout, whose text changed: it used to print the face's internal family name at the size
  the loader was asked for, and now prints the name it was *registered* under - "IBM Plex Sans Web" -
  because that is the name a font is built from. The rest of the page - the label, the text box, the
  rich label, the measured comparison - is unchanged, which is the interesting half: the registered
  face measures exactly as the loaded face did, because a registered face keeps the pinned layout
  settings a loaded one had.
- **`font-dialog`**, 5,804 pixels at (304,266)-(1223,611). One caption changed because the API it names
  changed: "A face loaded with `Font.FromFile` is not a system font" became "A family an application
  registered is not a system font". The paragraph re-wraps, so the content below it moves; nothing
  about any font's measurement changed.

**Two pages the note expected to move and one it did not.** It predicted `FontsFromFilePage` and
`InheritedAppearancePage`. The first moved, for the reason above. `inherited-appearance` did **not**,
and that is correct rather than lucky: the page registers the bundled face under the family name
recorded inside the file, so `FamilyName` echoes exactly what it reported before, and a family with one
regular face serves the page's semibold request with that face - so every pixel is the same, and what
changed is only the value `Weight` reports, which is a number and not a glyph.

**Two pages that moved and then stopped moving**, which is where the note was wrong: `rich-text` and
`rich-label-markup` differed by a text band each, because a bold span in a font with no family stopped
being bold. At that point the fix was the `Derive` exception of section 2.1's second draft. It is no
longer: the family is named at construction, and the next subsection is what that does to the whole
sample.

**3. Every page, after the always-named-family decision.** This is the fix, not churn, and it is
reported as one. All 73 pages differ from the previous push, 3,728 to 27,939 pixels each, 6,326 on
average, 461,822 in total - 0.6% of the 73 frames' pixels. Every difference is text drawn at the weight
that was asked for:

- the two passes of the *same* build are identical on 71 pages (the two animation pages differ from
  themselves by clock, as before), so nothing here is run-to-run noise;
- the `Kolvir` title of every page is w700, the page's own heading is w700 and its section labels are
  w600, so a page with nothing else bold still shows about 4 KB of differing pixels - which is the
  floor of the range;
- the two pages above the floor are the two whose emphasized text participates in *layout*.
  `popups` (27,939) is a page of button rows: button labels draw at the theme's emphasized weight, so
  the labels are genuinely semibold now, the buttons widened with them, and one row re-wrapped - "Tail
  from the top edge" moved to the end of the last row. `wrap-panel` (17,089) is a wrapping panel of tag
  chips, and two chips moved from the second row to the first for the same reason. Both were checked by
  looking at the two frames, not inferred from the counts.
- nothing changed shape, position or colour that is not a glyph's own ink or the re-wrap a wider label
  causes. Those two frames were inspected to confirm it; the test suite covers the rest.

`inherited-appearance` is the page to read if a single page is to be read: it assigns a registered
family to a panel, and the family it registers is the one recorded inside the file, so the *only* thing
that can change is the weight its children ask for - which is what the fix is about.


### 8.3 The override layer, as measured

**1. Publish size.** The same fixture and command as 8.1, and the same build root before and after
(one worktree, so the roots are identical by construction), SDK 10.0.403:

| | before (`4a5b283`) | after | delta |
| --- | --- | --- | --- |
| `Kolvir.Minimal`, win-x64 | 3,914,752 | 3,916,288 | 1,536 |

The baseline reproduces at 3,914,752 and the new image at 3,916,288 on a second publish of each, and
1,536 is three 512-byte alignment steps, so the delta is the code rather than the boundary. It is the
public type, its four members and their comparers, the composition, the extra union arm and the
`Control` field - and it is small because the arm is a reference to a small type rather than a second
font implementation, and an application that never assigns an override never constructs one.

**2. Per-instance cost.** `new BareControl()` allocates **616.01 bytes against 600.01** - 16 bytes,
8 of them the wider union and 8 the cache of that composition. Section 5.1 has the rest of the table,
including the 32 bytes a struct override would have cost instead.

**3. Renders.** All 73 pages, 1280x820, light appearance, by the method in 8.2. Each build was
rendered twice first: the two passes of the same build differ on exactly the two clock-dependent pages
(`animated-gif`, `animated-webp`), so those are excluded from the cross-build claim.

**70 of the 71 pages that can be compared are byte-identical to `4a5b283`.** The seventy-first is
`inherited-appearance`, the page this change deliberately moved, where 85,496 pixels in one box at
(252,214)-(1243,723) are the three override labels, the readout line that names their resolved sizes
and weights, and the re-wrap below them. **No other page moved, which is what an additive change
should look like**: a page that moved without an override in it would be a finding, and there is none.

## 9. Migration

`Font`'s public surface today: four constructors, three static factories, `IDisposable`, and
`Size`/`Weight`/`Italic`/`FamilyName`/`LineHeight`/`BaselineOffset`/`MeasureWidth`/`WithSize`. After
the change the constructors and the four data properties and `MeasureWidth`/`WithSize` stay, and the
factories and `Dispose` go.

**Call sites in the tree before the change**, so the size of the change is not a guess:

| What | Where | Count |
| --- | --- | --- |
| `new Font(...)` outside `Font.cs` itself | `Theme.cs` (five statics plus `ThemeFont`), `RichLabel.cs`, `AppKitSkiaView.cs`, demo `FontsFromFilePage.cs` | 9 |
| `Font.FromFile`/`FromStream`/`FromBytes` in non-documentation code | `FontLoader.cs` (2), demo `Program.cs` (2), `FontsFromFilePage.cs`, `InheritedAppearancePage.cs` (3) | 8 |
| Font construction in tests (`Font.From*` or `TestFont.Create`) | 8 files: 23 `TestFont.Create` plus 27 `Font.From*` (`FontLoadingTests` 15, `InheritedAppearanceTests` 6, `WoffFontLoadingTests` 3, `TestFont` 2, `FontLoaderTests` 1) | 50 |
| `using`/`using var` on a font | `AppearanceUnionTests`, `FontFallbackTests`, `FontLoadingTests`, `FontLoaderTests`, `InheritedAppearanceTests`, `FontDialogTests`, `WoffFontLoadingTests`, `AppKitSkiaView` | 49 |
| `Theme.UseFont` | demo `Program.cs` (2), demo `InheritedAppearancePage.cs` (2), tests `FontLoadingTests` (2), `TestFont.Pin` (1) | 7 |
| `font.DrawText(...)`, which becomes `font.Realize().DrawText(...)` | `TextRenderer`, `TextBox` (2), `MenuBarView`, `MenuRenderer` (2), `RichLabel`, `TextBoxMultiline` (2) | 9 |
| `font.SkFont...`, which becomes `font.Realize().SkFont...` | `RichLabel`, `TextBoxSpanFeature` | 2 |

**Corrections to the original table, after doing the work.** Three of its counts were low and one was
high. There are 50 test construction sites across 8 files, not 48 across 7. There are 49 `using` sites
rather than 33 - the original count missed `FontDialogTests` and the WOFF suite, and both
`FontLoadingTests` and `FontLoaderTests` have more than the table said. `Theme.UseFont` has a seventh
caller, `TestFont.Pin`, which the table does not list. And Table 2.2's "two places read `SkFont`" is
right about `SkFont` but incomplete about the realization: `DrawText` moved too, so nine more call
sites gained a `.Realize()`.

**Three things in this section were wrong in a way that changed the work:**

- **`TestFont.Pin` does not go away.** The table above says `TestFont.cs` "loses `Create` and `Pin`",
  on the assumption that `Theme.FontFamily = TestFont.Family` can be written inline wherever `Pin` was
  used. It cannot: `Pin` is a `using`-scoped assignment with a restore, used by 16 tests, and inlining
  it would put a try/finally into each of them. `Pin` stays, with a body that assigns the registered
  family and restores `null`; only `Create` goes.
- **`FontLoadingTests` could not be migrated line by line.** Eight of its fourteen tests existed to pin
  `Font.FromFile`/`FromStream`/`FromBytes` failures, three more pinned disposal, and two pinned
  `Theme.UseFont`. They were rewritten rather than ported: the loader-failure tests belong to
  `FontLoaderTests` and `FontFace`, the registry's own rules got `FontRegistryTests`, and what is left
  in `FontLoadingTests` is the description, its equality, the pool and the theme.
- **`AFontKeepsNoTieToTheFileItCameFrom` changed meaning.** It now belongs to the registry: register
  from a path, delete the file, measure. The registry owns bytes, so the property survives, but it is
  a claim about `FontRegistry` and it moved there.

**What breaks for an application, and what it does instead:**

- **`Control.Font` gains a case** (layer 2, section 5). `label.Font = someFont` still compiles, and so
  does every read of `EffectiveFont`, because the property's type widened and the cases it had are
  unchanged. What stops compiling is an exhaustive `Switch` or `Match` over `Control.Font` - it has to
  name the `FontOverride` arm - and `label.Font = null`, which used to convert to the `Font` case and
  which the setter then refused at run time; with two reference-typed cases the literal no longer
  converts at all (CS0037, measured). The spelling for "no font here" has always been
  `PropertyValue.Inherited`; the new one for a partial font is `InheritedFont.WithSize(...)`.
  `ToolStripItem.Font` widens with it.
- **`Font` stops being `IDisposable`.** A `using var font = Font.FromBytes(...)` stops compiling
  (CS1674), and `font.Dispose()` stops compiling (CS1061). The fix is deleting them, which is the
  point: nothing is owned and nothing needs releasing. The two tests that exist only because of
  disposal - `ADerivedFontOutlivesTheFontItCameFrom` and `DisposingAFontTwiceDoesNothing` in
  `tests/core/Kolvir.Tests/Ported/FontLoadingTests.cs` - go away, and so does
  `RichLabel.ClearDerivedFonts`'s disposal loop (`_derivedFonts` stays as a lookup cache but holds
  descriptions, which re-realize themselves). `tests/core/Kolvir.Tests/TestSupport/TestFont.cs` loses
  `Create`: it registers the embedded face once and hands back its family name, and the test
  construction sites become `new Font(TestFont.Family, size)`.
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
- **`Font.FamilyName` echoes the description, so a family that does not exist reports the name that
  was asked for.** Today it reports the face that resolved, which for a missing family is the
  platform's default; after the change it reports the name. One consequence is not visible on screen:
  `RichLabel.ResolveFont` (`src/core/Kolvir/Controls/RichLabel.cs:1483`) compares a span's requested
  family to `EffectiveFont.FamilyName` with `Ordinal` to decide whether the span needs a derived font,
  so a span naming a missing family that the label's own font also names now matches and skips the
  derive - the same default face is drawn either way, so what changes is one fewer `Font` allocated and
  cached. The second consequence is not this change at all but the decision in section 2.1: a
  description made without a family now *reports* the platform's default family, because it was named
  at construction, and that is what makes its weight and slant real. **The note first expected the
  opposite** - that `new Font(13f).FamilyName` would become `string.Empty` - and the tree said
  otherwise: `SKTypeface.FromFamilyName(null, weight)` ignores the weight while
  `FromFamilyName("Segoe UI", weight)` honours it, so a description left unnamed silently drops every
  style asked of it. Section 8.2 measures what that looked like on screen and section 2.1 records the
  decision.
- **`FontDialogOptions`'s remarks need one sentence** (4.8): registered families resolve but are not
  listed by the platform dialog.
- **The prose that taught the removed API had to change, which the migration table did not count.**
  `dev-docs/DEVELOPER-GUIDE.md`'s font chapter, its feature table, the WOFF chapter's example and the
  font-dialog paragraph, `dev-docs/WOFF.md`'s two examples and `FEATURE-COST-POLICY.md`'s mention all
  showed `Font.FromFile`/`FromBytes`, `Theme.UseFont` or `fonts.LoadFromPath(path, size)`. The table
  above counts call sites in code, and a design that removes an API has as many documentation sites as
  code sites - the guide is the thing an application author reads first. Each was rewritten to the
  registry-then-name shape, including the order rule the follow-up pass added (register after the
  `Application` exists, not before).

**Compatibility.** No version is pinned in `Directory.Build.props` or `Directory.Build.targets`, and
no compatibility promise is written down anywhere in `dev-docs/`, so this is a clean break rather
than a deprecation: the members go rather than gaining `[Obsolete]` shims. The reason is section 4.5's
name ambiguity - a shim for `Font.FromBytes` would have to invent a family name, so an application
would end up with two names for one face and no way to tell them apart. The migration table above is
the compatibility story: every break has a mechanical replacement of one to three lines, and the two
things that are not mechanical - a disposable font and a reference comparison - are behaviour
removals the note argues are improvements. Layer 2's break is smaller than any row above: one union
case to handle where the property is switched on exhaustively, and a `null` literal that was never
usable on the property anyway.

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
| The same null-name refusal from `Unregister` as from `Register` | Symmetric, and wrong: `Unregister` is the cleanup half, so a throw there lands in a `finally` block and masks the failure that got it there. It returns `false` for a name that cannot be registered under anything (4.5). |
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
   **Resolved: layer 2 built it, with the shape this note proposed.** A consumer arrived -
   `label.Font = InheritedFont.WithSize(14f)` - and section 5.1 is what shipped: `FontOverride` as a
   sealed class with value equality, composed in `EffectiveFont`, never seen by realization,
   `Control.Font` and `ToolStripItem.Font` at `Union<Font, FontOverride, InheritedValue>`, and
   `InheritedFont` as the static class that starts a chain. The union arity every application pays for
   is 8 bytes in a `Control` (16 with the cache of the composition, section 5.1) and 1,536 bytes on
   `Kolvir.Minimal` (section 8.3), and no application that never assigns an override carries the type
   at all.
2. **Should an application be able to reach the realization?** `RealizedFont` is proposed internal,
   with `Font` as the whole surface. What an application cannot do today either - draw with a
   `Font` directly, or read `SKFont.Metrics.Descent` for its own layout - it still cannot do. The
   question is whether that stays right: the reading to start from is yes, because exposing the
   realization makes the epoch and the invalidation rule public, and a caller that reaches an
   object which can be replaced under it needs the whole section 4.4 contract documented as public
   API.
   **Resolved as read: `RealizedFont`, `FontKey` and `FontManager` are all internal.** The same
   argument covers all three; see section 2.5. **Re-checked when layer 2 landed**: overrides add no
   pressure at all, because what an override returns is an ordinary `Font` - the composition is
   internal, and `EffectiveFont` hands back the same kind of description a complete assignment does.
   There is nothing an override would let an application reach that the property did not already give
   it.
3. **`ImmutableDictionary`, or a swapped `Dictionary`?** The agreed shape is
   `ImmutableDictionary<FontKey, WeakReference<RealizedFont>>` read lock-free. It is the first use of
   `System.Collections.Immutable` in the core assembly, and since `Font` is always retained the
   container ships to every application. The reading to start from is to measure it - section 8's
   step 1 attributes the delta - and to fall back to a plain `Dictionary` swapped by `Volatile` under
   a lock if the delta is more than one alignment step, because the read pattern the immutable table
   buys is exactly the copy-on-write pattern a swapped dictionary gives.
   **Resolved: the swapped `Dictionary`.** It cost 34,304 bytes more on win-x64 (section 8.1), 67
   alignment steps, so the fallback was forced rather than chosen. One amendment to the shape above:
   the table is published by a compare-and-swap and mutated under a lock only for creation, never
   `Volatile.Write`n while another writer may be mid-publication, because the finalizer removes an
   entry without a lock and a plain write could drop that removal.
4. **Does re-registering a face need to be cheap?** Registration parses the file once to read its
   internal family name and style, and the bytes are kept. An application that registers the same
   file from two code paths pays that parse twice. The reading to start from is to leave it: parsing
   happens at startup, and a content hash at registration is the natural way to de-duplicate it, which
   is the same decision as content-hash identity and should be taken once rather than twice.
   **Resolved as read: left alone.** No content hash was added, and the measured parse cost is the one
   section 4.4 already accepts.
5. **What is the exact `Register` overload set?** `(family, data)`, `(family, FontFace)`,
   `RegisterFromFile(path, family?)` and the optional weight and italic overrides are what this note
   assumes. Whether the overrides belong at registration at all - rather than always being read from
   the file - is surface that has no caller yet, so it is listed rather than fixed.
   **Resolved: the overrides are gone.** Nothing in the tree needed them; a face's style is read from
   its file, and a second style of a family is a second face. The interface is the three `Register`
   forms and nothing else (section 4.2).
6. **Should the desktop's chosen UI family be reportable?** `ISystemTheme` has one member,
   `bool IsDarkMode`. The demo reports a preferred family from `AppKitSystemTheme` and
   `Gtk4SystemTheme` as *static* helpers, and its Windows branch returns `null` because Win32 has no
   equivalent helper - which is how the theme ended up with no family at all on Windows (section 4.3).
   The reading to start from is that the library does not need it: a description with no family is the
   platform's default family, named, so a theme is correct without being told anything. What a
   `PreferredFontFamily` on `ISystemTheme` would add is the *shell's chosen* family rather than the
   platform's default one - a real difference for an application that wants to match the rest of the
   desktop's chrome, and a new member on a backend interface every backend implements. Not decided
   here; the sample is corrected to stop claiming the seam exists.

