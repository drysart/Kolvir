# Documentation and row/column styles on VNodes

Two things a generated node could not express, both of them about ordinary settable properties that
the generator could not see or could not describe:

- a `VButton` exposed `Text`, but its generated property carried no documentation, so a consumer read
  `VButton.Text` with no explanation while `Button.Text` had one;
- a `VTableLayoutPanel` could not say what its rows and columns were, because `RowStyles` and
  `ColumnStyles` were get-only - which is most of the reason to use that control.

The first half of this document is the answer to the question that decided the shape of the work:
**can a generator see a control property's documentation at all?** The second half is what shipped.
Section 6 records the measurements, and section 7 the row and column styles.

## 1. The question, and the experiment

Three facts were established before any code was written, and two of them decide everything:

1. `Directory.Build.props` sets `GenerateDocumentationFile` to `$(ShipsToConsumers)`, which is true
   for the library and the feature projects and false for the samples and the tests. `CS1591` is an
   **error** for the projects that ship.
2. A referenced project arrives in the consuming compilation as **metadata**, and a metadata symbol's
   documentation comes from a documentation *provider*. `ISymbol.GetDocumentationCommentXml()`
   returns nothing without one, whatever XML file sits next to the assembly.
3. Documentation read from a **source** symbol is the raw XML with unresolved crefs, exactly as
   written. From a **metadata** symbol it can only be what the XML file holds, where crefs have been
   resolved to documentation IDs (`T:Kolvir.Control`), which are not valid source cref syntax.

So the experiment was: does the compiler attach a provider in a real build? Measured three ways, and
all three agree.

**A real `dotnet build` of `Kolvir.Demo.VirtualDom`.** Temporary instrumentation reported
`Control.Text`'s documentation length from inside the generator, and dumped the resolved reference
list. The compiler is passed `obj/Release/net10.0/ref/Kolvir.dll` - the *reference assembly* - and
`Kolvir.xml` is not written there at all (`bin/Release/net10.0/Kolvir.xml` is, and `obj/.../ref/`
holds the dll alone). Length: 0.

**A package-shaped reference, and documentation generation turned on.** A throwaway project with a
`<Reference>` whose hint path is a directory holding both `Kolvir.dll` and `Kolvir.xml`, with
`<GenerateDocumentationFile>true</GenerateDocumentationFile>`, so the compiler's parse options are
`DocumentationMode.Diagnose`. The reference it names is the one with the XML beside it. Length: 0.
So `DocumentationMode` was not the blocker and the sibling file was not being picked up.

**`MetadataReference.CreateFromFile`, in process.** A console harness compiles a one-line source
against `MetadataReference.CreateFromFile(...\Kolvir.dll)` with `Kolvir.xml` in the same directory,
asks the metadata symbol for `Kolvir.Control.Text`, and gets an empty string. The compiler creates
metadata references without a documentation provider; the host supplies one in an IDE, and no
command-line build does.

**Conclusion: a generator cannot see a control property's documentation in a real build, by any
route.** Emitting nothing would have been a feature that never fires in any application, which is
worse than not having it, so the XML has to be handed to the generator.

## 2. The mechanism: the build hands the files over

`src/features/Kolvir.Feature.VirtualDom/buildTransitive/Kolvir.Feature.VirtualDom.targets` adds each
resolved reference's documentation file to `AdditionalFiles` before `CoreCompile`:

```
<ReferenceCopyLocalPaths>  where the extension is .xml          -> the ordinary case
<ReferencePath>            with a sibling <name>.xml that exists -> a build whose copy-local list is empty
```

A NuGet consumer imports that file automatically, because it is packed into `buildTransitive/`.
This repository's projects use a `ProjectReference`, which imports no build assets at all, so
`Directory.Build.targets` imports the same file explicitly - the arrangement the feature packages
already use for the native-library targets.

The generator indexes the files by **assembly name** (the file name is the assembly name by
convention, which is what NuGet and `ResolveAssemblyReference` both produce), and looks a member up
by `ISymbol.GetDocumentationCommentId()`. Nothing is read until a member in that assembly is asked
for: a build hands in the documentation of every reference - eleven files here, including several
megabytes of Microsoft.CodeAnalysis - and only the two or three that declare a control or an
extension member are ever parsed.

A control declared in the compilation being built needs none of this: its symbol has the
documentation, and that is read directly.

## 3. What is copied, and what is not

`DocumentationCleaner` reduces a member's documentation to something a **property** can carry. This
is the part that could introduce a build warning, and in a consumer a warning can be an error, so it
is deliberately conservative:

| In the source member's documentation | In the generated property |
| --- | --- |
| `<summary>`, `<remarks>`, `<value>`, `<example>` | copied verbatim, including their nested markup |
| `<see cref="...">`, `<seealso cref="...">` | `<c>name</c>` - the name, not the link |
| `<see langword="null"/>` | `<c>null</c>` |
| `<paramref name="x"/>`, `<typeparamref name="x"/>` | `<c>x</c>` |
| `<param>`, `<typeparam>`, `<returns>`, `<inheritdoc>`, `<include>` | dropped |
| any other top-level element (`<filterpriority>` and friends) | dropped |

Text that is kept is copied as it was written, so entities (`&lt;`, `&amp;`) and markup are exactly
as documented and no escaping decision is made anywhere.

**Why crefs become text rather than source crefs.** Three reasons, in order of weight:

- a documentation ID (`P:Kolvir.Control.Text`) is not source cref syntax at all, so it cannot be
  copied - it has to be *converted*;
- converting means resolving the ID back to a symbol and writing a fully qualified source cref, and
  that cref still has to resolve in the *consumer's* compilation. A member the consumer cannot see
  from the generated file's context is `CS1574` - in somebody else's build, where a warning may be an
  error. A generated file that warns in a strict consumer is worse than one that links nothing;
- a source cref written by the control's own author is relative to *that* file's namespace and usings,
  so it is no safer.

The text is what a reader needs; the link is not worth a warning in a build that is not ours.

The test for this compiles the demo-shaped case with `DocumentationMode.Diagnose` and asserts the
generated tree raises **no** compiler diagnostic at all - not just no errors - while the test's own
deliberately undocumented public classes still report `CS1591` where they should, which proves the
analysis was on.

## 4. What a consumer sees

- A **package consumer** gets the XML from `lib/<tfm>/`, because the target adds every reference's
  documentation file. This is the case the feature exists for.
- A **project reference inside this repository** gets it too: `ReferenceCopyLocalPaths` carries the
  referenced project's XML even though the compiler's own reference is the ref assembly.
- A **control declared in the application** gets it from its own source, but only if the application
  parses doc comments - which is `GenerateDocumentationFile`, and is what makes the compiler keep them
  at all. Measured: `DocumentationMode.None` returns nothing for a source property, `Parse` and
  `Diagnose` return the text. An application that does not generate documentation has thrown its own
  doc comments away before any generator could read them, so there is nothing left to carry.
- A control from a reference whose assembly ships no XML - hand-built, or a third-party package that
  strips documentation - gets nothing: the node property is emitted without a comment and the build
  says nothing about it. That is deliberate rather than a bug. There is no documentation to carry, so
  the alternative is a warning for every property of every undocumented reference, which is a message
  about somebody else's packaging that an application cannot act on.

### Measured through a real package, not inferred

The chain was proven once through project references and then again through actual packages: `Kolvir`,
`Kolvir.Feature.Observables`, `Kolvir.Feature.VirtualDom` and `Kolvir.Backend.Headless` packed into a
local folder feed, and a console application **outside this repository** that references them only by
`<PackageReference>`, restores from that feed, declares `[VirtualDomAll]`, and generates no
documentation of its own. The build succeeded with no warnings and the generated node carried the
control's summary:

```csharp
/// <summary>Text shown by the control. Simple controls use it directly.</summary>
public string Text
```

The target handed the compiler **114** documentation files. Four are the Kolvir packages' own
(`Kolvir.xml`, `Kolvir.Feature.Observables.xml`, `Kolvir.Feature.VirtualDom.xml`,
`Kolvir.Backend.Headless.xml`), and those are the only ones the generator ever reads, because only
they declare a control or an extension member. The remaining 110 belong to references the application
did not write: 107 from the .NET reference pack, `SkiaSharp.xml`, and two `Microsoft.Extensions.*`
files. The rule is "the documentation file beside each resolved reference", so they are handed over;
indexing 114 file names is the entire cost, and content is read only for an assembly whose member is
actually asked for.

Two things that run taught, worth knowing before believing a local package test:

- NuGet caches by **id and version**, so packing over an id/version that is already installed leaves
  the old package resolved and the scratch application generates nodes from a stale `Kolvir.dll`
  without saying so. A local package test has to pack a version that has never been restored.
- `dotnet pack` writes `exclude="Build,Analyzers"` onto a `ProjectReference`-derived dependency, so an
  application that references a feature package gets that feature's own `buildTransitive/` assets but
  not those of `Kolvir` itself. This target is in the feature package, which is why it runs; anything
  that only lives in `Kolvir`'s own build assets reaches a consumer only when `Kolvir` is a direct
  reference. That is a packaging question about the dependency attribute, not about this feature.

## 5. Rung

**Rung 1, by construction.** Documentation is compile-time text: no member is emitted for it, nothing
runs, and no IL exists to retain. `Kolvir.Minimal` published as NativeAOT is byte-identical with and
without the change (section 6).

The one thing that is *not* free is the build: generated source grows by 45 percent on a manifest that
sweeps 36 controls, and a compile that writes more source costs more time. That is measured below and
is the price of the feature.

## 6. Measurements

The virtual DOM half is measured with the shipped generator driven through `CSharpGeneratorDriver`
over this repository's `Kolvir.Demo.VirtualDom` compilation - six source files, 185 references, 36
node classes, about 3,400 node properties - with one comment added to one file per edit, median of 15
edits after a cold run, reusing one driver so the incremental model is exercised as an IDE build does.
Both roots are worktrees of equal path length (`C:\kolvircost\a` this branch, `C:\kolvircost\b`
`develop` at `d809819`).

| | develop (`d809819`) | this branch |
| --- | --- | --- |
| generated source | 2,148,952 chars | **3,116,352 chars** (+45%) |
| cold generator run | 698-731 ms | 727-756 ms |
| per edit, median of 15 | 23.5-38.7 ms (five runs: 38.7, 31.9, 29.2, 36.5, 23.5) | 36.0-43.9 ms (six runs: 39.9, 43.3, 41.3, 38.5, 43.9, 36.0) |
| per edit, fastest observed | 21.9-23.4 ms | 27.3-29.1 ms |
| `dotnet build -t:Rebuild --no-dependencies` of the sample | 3,924-4,035 ms | 4,622-5,178 ms |

The ranges are run-to-run spread on one machine, not confidence intervals; the fastest observed
per-edit number is the least noisy comparison and says the documentation feature costs about **5-6 ms
per edit** on 36 node classes, which is the writing of one megabyte of extra source plus a dictionary
lookup per property. The cold-compile row says the same thing from the other end: about a second more
for the sample, most of it the 45 percent larger file.

Measured separately, the two collection properties (`RowStyles`, `ColumnStyles` on
`VTableLayoutPanel`) add **1,977 characters** of generated source and nothing detectable in time:

| | develop (`d809819`) | collection properties only (`dec0ebc`) |
| --- | --- | --- |
| generated source | 2,148,952 chars | 2,150,929 chars |
| per edit, median of 15 | 23.5-38.7 ms | 23.4-27.0 ms (three runs: 26.1, 27.0, 23.4) |
| per edit, fastest observed | 21.9-23.4 ms | 21.7-23.6 ms |

**Image size.** `Kolvir.Minimal` published with `-c Release -r win-x64
-p:EmbedSkiaSharpNative=false -p:TrimmerSingleWarn=false` from the two equal-length roots, and
reported at 512-byte resolution:

| | win-x64 |
| --- | --- |
| `develop` (`d809819`) | 3,885,056 bytes |
| this branch | 3,885,056 bytes |

Identical to the byte, which is what a compile-time feature should be. Neither publish reported an
IL2xxx or IL3xxx warning.

## 7. Row and column styles on a node

`TableLayoutPanel.RowStyles` and `ColumnStyles` were get-only, so the generator skipped them: a
`VTableLayoutPanel` could not describe its grid, which is the main reason to use the control.

### 7.1 The control has to earn its own collection

A node assigns properties; it does not call methods. So the control's collections became ordinary
settable properties, and everything followable from that followed without any generator work:

```csharp
new VTableLayoutPanel {
    RowStyles = [ new RowStyle(SizeType.Absolute, 24f), new RowStyle(SizeType.Percent, 100f) ],
    ColumnStyles = [ new ColumnStyle(SizeType.Absolute, 160f), new ColumnStyle(SizeType.Percent, 100f) ],
}
```

That is the whole of the generator side: one field, one assignment in `ApplyTo`, one comparison in
`PatchFrom`. **No collection concept was added to the generator** - the members go down the ordinary
settable-property path, and `CollectionPropertyTests` asserts exactly that, including the
`IList<RowStyle>` type surviving to the emitted member.

Three properties of the control are what make the ordinary path behave:

- **The getter's list lays the table out when it changes.** `RowStyles` returns a view over the
  table's own list that runs one layout pass on every mutation that changes something - `Add`,
  `Insert`, `Remove`, `RemoveAt`, `Clear`, an indexer assignment - and none when nothing changed
  (`Remove` of an absent style, `Clear` of an empty list, an indexer assignment of an equal style).
  Before this, `ColumnStyles = { ... }` in an object initialiser added the styles without a layout
  pass, which left a table whose list said one thing and whose arrangement said another, with nothing
  reported anywhere. `AddRow` and `AddColumn` go through the same view, so one call is still one pass.
  `SuspendLayout`/`ResumeLayout` remain the way to populate many rows: sixteen additions inside one
  suspend is one pass, which the tests count rather than assume.
- **The setter compares by value and does nothing when the sequences agree.** A render builds a fresh
  sequence every time, so the node's `Specified<T>` comparison (which is a reference comparison) always
  calls the setter - and the setter is where the value comparison lives. When the new sequence says
  what the table already says, nothing is touched: the styles already on the table stay, so a render
  that produces the same rows again does not rebuild the table. When it differs, the contents are
  replaced and exactly one layout pass runs.
- **Styles are immutable and compare by value.** `RowOrColumnStyle.SizeType` and `Value` are `init`-only
  and the type implements `IEquatable<RowOrColumnStyle>` with `==`/`!=`, including the concrete type in
  equality so a `RowStyle` never equals a `ColumnStyle`. Immutability is what makes that equality
  sound - a hash cannot disagree with an `Equals` computed before a mutation, because there is no
  mutation - and it deletes the one case the collection view could not see: `panel.RowStyles[0]
  .SizeType = ...` no longer compiles, so the only way to change a size is to replace the element,
  which goes through the indexer setter and notifies. The same immutable instance may now appear in two
  tables, which was hazardous before and is harmless after, because there is nothing to notify.

This is a **source-breaking change** for a caller that mutated a style after constructing it. Nothing
in this repository did - verified by building, and by searching the tree for assignment to those two
members - and it is worth taking now rather than later precisely because the alternative is an owner
back-reference from a style to a table, which would be wrong the first time a style is shared or
reused after a `Clear`.

The two properties a node gains are, as a consequence, **documented** (section 1's feature applies to
them like any other settable property), which is a nice end-to-end proof that both halves work.

### 7.2 What the tests assert

Core (`TableStyleTests`, `RowOrColumnStyleTests`):

- adding through the live list arranges the table - asserted as a child's bounds, not as a flag;
- replacing a style by index arranges it;
- an equal sequence causes no layout pass and leaves the **same style instances** in the list;
- a different value, a reordering, a shortening, an addition and an empty sequence are each applied,
  and an empty sequence lays the table out to its implicit tracks;
- `Clear` lays out again, and clearing an empty list does not;
- a `Remove` that finds nothing is not a pass, and one that finds something is;
- bulk population inside `SuspendLayout`/`ResumeLayout` is exactly one pass for sixteen additions;
- `null` throws, self-assignment is a no-op, the assigned list is copied, and a list captured from the
  getter still writes to the current rows after a setter call;
- two styles built differently with the same values are equal and hash alike, a changed kind or value
  makes them unequal, and a row never equals a column.

Layout passes are counted through a `TableLayoutPanel` subclass that increments a counter in
`OnLayout`, because "exactly one" and "none" cannot be told apart from the arrangement that results.

Demo (`--check`): the applied counts and the first row's and column's kind and value; a changed row
height that moves the cells; a reordering; a shortened sequence; an empty one; the cycle back to the
declared rows; and the same style instance after a re-render that produced an equal sequence.

### 7.3 Rung

**Rung 1 for the table change.** The view is one object per collection, allocated when the table is
constructed, and the comparison is a loop over the list the table already holds. `Kolvir.Minimal`,
published from equal-length roots, is **3,885,056 bytes before and after** - identical to the byte, and
`Kolvir.Minimal` does not construct a table at all, so that is the honest shape: an application that
does not name `TableLayoutPanel` does not carry it, and one that does already carries the collection.

**Rung 3 for the generator half, and inside the package rung 1.** These are ordinary generated
properties on a node class that only exists where an application registers the control: no new
mechanism, no registry, no startup work. The measured cost is the 1,977 characters above.
