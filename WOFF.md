# Web fonts

A way to load the font a web font kit actually hands out. Skia reads OpenType - a `.ttf` or an `.otf` -
and nothing else; WOFF and WOFF 2 are the two containers a downloaded font arrives in, and this package
is what opens them.

The scope is one question: **can these bytes become an OpenType font?** Not glyph rendering, which Skia
already does, and not a second font engine beside it. That framing is what keeps the feature small
enough to sit in a package instead of in the library.

## 1. The rung: 3

`Kolvir.Feature.WoffFonts`, one package, the four-folder layout of `FEATURE-COST-POLICY.md` section 4,
with `windows/`, `macos/` and `linux/` empty.

Rung 1 and rung 2 would both have worked here, and that is stated plainly rather than argued. A
container unwraps into the bytes `Font.FromBytes` already reads, so a provider registered inside
`Kolvir` would cost an application one line that never runs and, for the great majority of desktop
applications, nothing else. The package is chosen for the second reason section 4 allows: almost no
desktop application loads a web font, so keeping it out of the base deliverable is worth more than the
trimming it would have earned.

The consequence is the usual one - a separate assembly cannot add members to a `Kolvir` type - but it
lands differently here. There is no extension block on `Application`, because there is no second entry
point: the package's whole public surface is one `AddWoffFonts()` on `IServiceCollection` and the two
types that method registers, which are `Kolvir`'s own.

## 2. What the two formats cost

Measured on IBM Plex Sans Regular v6.4.1, the same face at the same version in all three containers:

| | `.ttf` | `.woff` | `.woff2` |
|---|---|---|---|
| size on disk | 200,500 B | 87,508 B | 63,020 B |
| what it is | tables and glyphs | the same tables, each zlib-compressed | one Brotli stream, plus a directory of variable-length entries |
| what has to be undone | nothing | decompression, nothing else | `glyf` and `loca` re-encoded as contour, point, flag and coordinate streams; `hmtx` optionally re-encoded |
| this package | - | 95 lines, no dependency | 1,022 lines, Brotli from the BCL |

WOFF 1.0 is a compression choice and nothing more. It was implemented first, in a day, and it is not
where the cost of this feature lives.

WOFF 2 is. Its payload is not a font with an excised section: `glyf` is replaced by a *transform* that
stores each glyph's points in six substreams - the number of contours, the number of points per
contour, the flags, the point coordinates as triplets, the glyph instructions, and a bitmap of which
points may overlap - and `loca` is not stored at all, because the offsets are exactly what rebuilding
`glyf` produces. Rebuilding means undoing that point by point and writing standard TrueType records
back out, and recomputing every glyph's bounding box, which the file chose not to store because it was
derivable.

The triplet is where the encoder earns its keep and the decoder pays for it. One flag byte and a
variable-length signed delta pair carry a point relative to the previous one, and the width and base of
the delta come from a table of 128 cases in four families - on-axis, both axes in a nibble each, both
axes in a byte each, and two widths of bit-packed pair. Nineteen of those cases exist only because
fonts are mostly on-axis deltas of small magnitude, which is also why a `.woff2` is a third of the
`.ttf`.

## 3. The API

The surface is a seam shared with image loading rather than a container-specific loader, and that is the
design decision this document exists to record.

```csharp
services.AddWoffFonts();                 // a feature package registers a format

var fonts = application.Services.GetRequiredService<IFontLoader>();

using var body = fonts.LoadFromPath("Fonts/Inter-Regular.woff2", 14f);
```

The last line is identical for a `.ttf`, a `.woff` and a `.woff2`. That is the point: an application does
not choose a loader by format, it enables the formats it wants once and then hands bytes to one loader.
`IFontLoader` is the counterpart of `IImageDataFactory`, with the same three entry points and the same
rules, because an application that has learned one of them has learned both.

`IFontFormatProvider` is the whole of what a format package implements:

| Member | What it does |
|---|---|
| `Name` | The word that appears in a failure: `"WOFF"`. |
| `Priority` | Arbitration between two providers that both claim data. Higher wins. |
| `CanLoad(header)` | "Is this mine?", from at most the first 512 bytes. |
| `Decode(data)` | The same font without the wrapper, as OpenType bytes. |

A provider unwraps a container. It does not parse outlines and it does not draw, which is what stops
this feature from growing into a font engine: a format whose payload is *not* OpenType cannot be
supported through this seam, and that is deliberate.

The loader's rules are the image factory's, spelled out in `FontLoader`'s remarks: providers are
consulted before the built-in path; a provider that throws while being asked has answered "not mine"
and the search continues; a provider that claims data and then fails ends the load and is named in the
`FontFormatException`, rather than falling through to a parser that would report something less useful;
a buffer that decodes to something which is not a font counts as that provider's failure, not as "no
font here".

Two smaller decisions follow from the seam. `FontFormatException` derives from `ArgumentException`, so
code written against `Font.FromBytes`'s old contract still catches a bad font after this change. And the
built-in path remains exactly `Font.FromBytes`, so an application that registers no provider gets no new
behaviour and no new dependency.

## 4. What is refused

A font **collection** - `.ttc`, or a `ttcf` flavor inside either container - is refused by name rather
than unwrapped into something that is not a font. Neither container's collection layout is implemented,
and a partial implementation would produce a file that fails later and elsewhere.

Damaged data is `InvalidDataException` from the provider, which the loader re-throws as
`FontFormatException` naming WOFF. Truncation is tested at every 97th byte of both fixtures - some
fifteen hundred cut files, one per length step - and each one must fail as a format failure rather than
crash, index out of range, or allocate a buffer the header claimed.

## 5. Layout

```
Kolvir/Typography/                    the seam, in the main package
    IFontFormatProvider.cs            the format side: Name, Priority, CanLoad, Decode
    IFontLoader.cs                    the application side: path, stream, bytes
    FontLoader.cs                     the composition, the ordering rules and the fallback
    FontFormatException.cs            what a load throws, with the provider's name

Kolvir.Feature.WoffFonts/
    common/                           all of it; no platform folder has code
        WoffFontFormatProvider.cs     one provider for both versions
        WoffServiceCollectionExtensions.cs   AddWoffFonts()
        WoffFormat.cs                 the WOFF 1.0 unwrap
        Woff2Format.cs                the WOFF 2 header, directory, Brotli and table dispatch
        Woff2Glyf.cs                  the glyf transform, the triplet table, the loca rebuild
        Woff2Hmtx.cs                  the hmtx transform
        SfntBuilder.cs                assembling tables back into a font, checksums included
    windows/ macos/ linux/            empty
```

One provider serves both versions because both are named WOFF and both produce an sfnt: the header's
flavor decides which unwrap runs, and an application that registers WOFF gets whatever it registered.

`SfntBuilder` exists because both formats need the same ending: tables, a sorted directory, a checksum
per table with the `head` adjustment field zeroed, and `head.checkSumAdjustment` set to `0xB1B0AFBA`
minus the file's checksum. Skia does not check any of it; a font checker does, and a font that fails a
checker is a font someone will file a bug about.

## 6. Testing

`Kolvir.Feature.WoffFonts.Tests` ships a real face in all three containers and treats the `.ttf` as the
answer:

- every printable ASCII glyph is rendered from the decoded font and from the `.ttf`, and the pixels
  are compared. This is the test that matters, because the reconstruction is allowed to differ byte for
  byte - the format permits several spellings of one outline - but not to draw differently.
- the table directories are compared table for table: the same set, and `glyf`, `loca` and `head`
  allowed to differ for the reason above. `loca`'s entry count and `hmtx`'s length are checked exactly,
  and `glyf` may grow by no more than 5%.
- `IFontLoader` reads all three containers through one call, a loader with no providers refuses a
  `.woff` (proving the format is not built in), and a damaged file reports the provider's name.
- the `hmtx` transform has six unit tests of its own, because no encoder in the wild emits it - the flag
  reads as absent in both fixtures - and a transform exercised only by synthetic streams deserves to be
  said out loud rather than implied.

`Kolvir.Tests/Core/FontLoaderTests.cs` covers the seam itself with a fake provider: priority,
later-registration-wins, declining, commitment after claiming, unreadable output, and an empty registry.

## 7. Decisions

- **One loader, not a WOFF loader.** Asked for, and right for the reason images already proved: the
  call site should not know what a file is called.
- **Both versions.** WOFF 2 is 1,022 lines to WOFF 1.0's 95, and a kit that offers only `.woff2` would
  have been supported by WOFF 1.0 and nothing else.
- **No third-party decoder.** Nothing free exists that returns an sfnt - the portable font libraries
  either own the whole pipeline (SixLabors, under a split licence) or return outlines rather than a
  font. Brotli is in the BCL, so the only dependency a `.woff2` needs is the one that is already there.
  `files/woff-findings.md` records the survey.
- **`FontFormatException` derives from `ArgumentException`.** A breaking change to a thrown type is
  worse than a slightly odd base class.
- **Recalculate the checksums.** Being right costs arithmetic.

## 8. Risks

- **The glyph reconstruction is the complex half.** 587 lines of bit-level parsing with a 128-case
  table, and it is the code most likely to be wrong on a font nobody has tried. The mitigation is the
  pixel comparison against a real face and the truncation sweep; the risk that remains is a
  hand-hinted or extreme-outline font hitting a case IBM Plex does not.
- **Two fixtures is not a corpus.** IBM Plex Sans Regular exercises simple glyphs, composites and
  instructions, but not a font with thousands of glyphs, colour layers, or a variable axis.
- **One face, one version.** A font kit can hand out a *subset* whose tables are laid out differently.
  Nothing in the parser assumes IBM Plex's layout, but nothing here proves that.
- **`.ttc` is unsupported**, and a font downloaded as a collection will fail by name. That is a
  deliberate refusal rather than a gap nobody noticed.
