# Documentation plan

Status: phase 1 in progress. Branch `drysart-app-docs`, cut from `develop`.

What this is: `docs/` becomes the guide for people building applications **with** Kolvir, and
`dev-docs/` stays the material for people working **on** Kolvir. This document records the audience
split, the page kinds, the tree, the decisions and the phases, so a later phase adds pages rather than
re-deciding structure.

The work is re-cutting and re-writing rather than inventing. Most of the content already exists in
[DEVELOPER-GUIDE.md](DEVELOPER-GUIDE.md), whose "Using it" section is 5,000 lines of tutorial,
reference and rationale interleaved, with platform differences scattered through the prose. What is
missing is a front door, one place for platform differences, and a reference that cannot drift from
the code. That file is not deleted, moved or restructured in this phase: the new guide is built
beside it and content migrates a page at a time.

## 1. The audience split

| Directory | Audience | Holds |
| --- | --- | --- |
| `docs/` | application developers | getting started, concepts, task guides, reference, shipping, troubleshooting, samples |
| `dev-docs/` | contributors | the design and rationale documents, `DEVELOPER-GUIDE.md`, the testing runbook, the feature cost policy |

Nothing in `docs/` is about maintaining Kolvir: no backend implementation notes, no feature cost
policy, no release mechanics beyond what a consumer of the packages needs, no benchmark methodology.
Nothing in `dev-docs/` moves in this phase.

## 2. Page kinds

Four kinds, kept apart on purpose. Mixing them is what made "Using it" unnavigable.

| Kind | May contain | Must not contain |
| --- | --- | --- |
| Tutorial (`getting-started/`) | steps, each ending in something observable: a window, or a PNG from the headless render | tables about things the reader has not met, theory |
| Concept (`concepts/`) | how something behaves and fits together, far enough to predict behaviour | step-by-step procedures, rationale for the design |
| How-to (`guides/`) | a task, the code, what to expect, a link to the demo page that runs it | complete member lists, alternatives that were considered |
| Reference (`reference/`) | complete, lookup-shaped: options, members, capabilities | narrative, tutorials, motivation |

## 3. The tree

```
docs/
  index.md                      what Kolvir is, is-it-for-you, three entry paths
  nav.yml                       the whole site's navigation, one file at the root
  assets/
    screenshots/                rendered by the demo's --render, plus manifest.yml
  getting-started/
    install.md                  package, target frameworks, the backend reference
    first-window.md             one window, one control, run it
    first-layout.md             dock, stack panel, table layout (phase 2)
    run-on-each-platform.md     prerequisites and what running it looks like on each OS
  concepts/
    architecture.md             application, window, UiHost, control tree, services, features
    layout-model.md             sizes, dock, table tracks, measure and arrange (phase 2)
    painting.md                 invalidation, what repaints, damage (phase 2)
    appearance.md               palette, inheritance, light and dark (phase 2)
    input-focus.md              input routing, focus, tab order, key handling (phase 2)
  guides/                       one task per page, each ends with its demo page (phase 2)
    windows.md dialogs.md menus.md text.md lists.md images.md svg.md
    animated-gif.md animated-webp.md woff.md drag-and-drop.md animations.md
    badging.md gpu-acceleration.md virtual-dom.md observables.md
    application-component.md
  reference/
    controls/<control>.md       generated from the XML comments; never committed
    application.md window.md uihost.md options.md environment-variables.md
    platform-matrix.md packages.md
  shipping/                     (phase 3)
    nativeaot.md trimming.md packaging.md windows.md macos.md linux.md
  troubleshooting.md            organised by symptom (phase 3)
  samples.md                    the demo projects, page by page (phase 3)
```

`reference/platform-matrix.md` is the one hand-written place that says which Kolvir features each
operating system supports. Every other page links to it instead of repeating a caveat in prose.

## 4. Decisions

Numbered so a later phase can cite them.

1. **The sources live in this repository; publication does not.** Documentation is updated in the
   pull request that changes the API, the samples compile in CI, and the reference is generated from
   the code it describes, so a second repository cannot drift. Publication is from this repository
   to GitHub Pages: a push to `develop` builds the site and deploys it as an artifact at
   `https://drysart.github.io/Kolvir/latest/`, with the site root redirecting into `latest/`. A
   deployment replaces the whole site rather than adding to it, so one directory per release tag
   (`/v0.9.0/`) waits until the job can build `latest/` and every tag into a single artifact. The
   `latest/` directory exists from the first deployment so that later change moves nothing.

2. **`docs/` must export to static HTML, so the Markdown stays generator-agnostic.** Plain Markdown,
   relative links only (`../guides/text.md`, never a repository or site URL), images under
   `docs/assets/`, one navigation file at the root, case-correct paths because the CI build runs on
   Linux where a wrong case is a broken link. No front matter: the first `#` heading is the page
   title, so there is no second place for a title to disagree.

3. **The control reference is generated from the XML documentation file.** Every shipped project sets
   `GenerateDocumentationFile`, so the comments IntelliSense reads are the comments the reference is
   built from, and a control cannot ship with a page that contradicts its IntelliSense. One page per
   public control, members grouped, each page cross-linked to its how-to page and to the demo page
   that exercises it. **The generated pages are not committed**: they are written to
   `docs/reference/controls/`, which is ignored, and CI runs the generator, so a generator that
   cannot run is a red build rather than a stale page.

   The reference covers the application-facing surface only: controls, `Application`, `Window`,
   `UiHost`, the options and the services an application calls. Public seam types the application is
   not meant to name - `IApplicationBackend`, `IWindowBackend`, the dialog and timer seams, and
   everything else under `Kolvir.Platform` - get no page even though they are public.

4. **Screenshots live in the repository and are rendered, not captured.**
   `docs/assets/screenshots/` holds PNGs produced by `Kolvir.Demo.App --render`, which paints through
   the headless backend - no window, no display server, the same result on every machine - and
   `docs/assets/screenshots/manifest.yml` records, for each image, the demo page id and the arguments
   that produced it, so the set is regenerable with one command and any page's picture can be
   re-taken from any checkout.

5. **The pipeline is .NET-only.** A local tool manifest at `.config/dotnet-tools.json` pins the site
   generator, so CI needs nothing but the .NET SDK. DocFX 2.81.0 was chosen because it ships a
   `net10.0` tool and therefore runs on the SDK this repository already requires; it turns Markdown
   into HTML with navigation and a search index and needs no code from us. The XML-to-Markdown
   generator is a project in this repository. If DocFX stops producing a usable static export, the
   answer is another *pinned* generator, never a hand-written one, and the trade-off gets reported.

6. **One documented build command.** `dotnet run --project src/tools/Kolvir.Docs -- build` does the
   whole thing: builds `Kolvir` for its XML file, generates the control reference, translates
   `docs/nav.yml` into the generator's navigation format, checks the tree, and runs DocFX.
   `dotnet tool restore` is the only prerequisite and is documented beside it. A shell script is not
   in the loop, because a shell script is not the .NET-only pipeline decision 5 asks for.

7. **`docs/nav.yml` is the single navigation file, in a schema of our own.** An entry is `title`, an
   optional `href`, an optional `items`, and an optional `planned: true`; the tool parses it and emits
   the generator's navigation file (`docs/toc.yml`, ignored) from it. YAML is parsed with a pinned
   `YamlDotNet` reference in the docs tool only: nothing is hand-parsed and nothing is added to a
   shipped package.

   One entry uses `include: controls` instead of `items`, and the tool expands it from the control
   map. That is what keeps the list of controls in one place: the grouped reference cannot list a
   control the map does not have, or miss one it does.

   `planned: true` is what lets the real tree be recorded now, while the guides are written in phase
   2. The tool skips a planned entry with a notice, and fails when a planned entry's file exists (the
   marker was forgotten) or when an unplanned entry's file is missing (a page was dropped). So the
   navigation is one file that cannot lie in either direction, and a group whose pages are all
   unwritten is left out of the built navigation rather than published empty.

8. **The generator reads the XML file and the compiled library.** The tool references `Kolvir` as a
   project, so it reads the same assembly and the same XML file the packages ship, resolves member
   kinds, signatures and inheritance by reflection over the built types, and gets the prose from
   `Kolvir.xml` beside it. Reflection is confined to this desktop-only tool; nothing it does reaches a
   shipped assembly. A table in the tool maps each public control to its how-to page and to its demo
   page id, and the build **fails when a public control has no entry**, so a new control fails the
   docs build until it is both documented and demonstrated.

9. **The generated reference carries behaviour, not argument.** `<summary>`, `<param>`, `<returns>`
   and `<exception>` are always carried through. `<remarks>` is **not** carried by default: many of
   them argue for a design, which is contributor material. A control's remarks are included only when
   the map says so for that control, and a control whose comments are mostly rationale gets a short
   behavioural paragraph written in the map instead. A remark that explains behaviour in backend terms
   is summarised in platform terms or left out; it is never copied through.

10. **Editorial rule: describe behaviour, do not justify it.** This applies to every page under
    `docs/`, generated or hand-written.

    - Write only what an application developer can act on. The test for every sentence: would a
      developer do anything differently having read it? If not, cut it.
    - No defending design decisions. No "we chose", no "the reason is", no "this exists because", no
      "deliberately", no "which is what makes it safe", no rationale paragraphs, no measured-cost
      narratives, no "this used to", no alternatives that were considered.
    - `concepts/` is the only place allowed to explain *how* something works, and only as far as a
      reader needs to predict behaviour. How it works, never why it was built that way.
    - Facts that change a decision stay, as facts without the study around them: package size, AOT and
      trimming requirements, minimum OS versions, what throws and when, what is not supported.
    - Existing prose is rewritten, not migrated. A paragraph of `DEVELOPER-GUIDE.md` that explains a
      flip chain, a DWM attribute or a deferral survives, at most, as the one sentence an application
      needs: "changing acceleration rebuilds the window, so `Window.Handle` changes - re-read it if you
      cached it".

11. **Capabilities, not platforms.** The reader builds one application and runs it on three systems.
    Platform-specific detail belongs in `docs/` only where a Kolvir application behaves differently and
    the reader has to know.

    - Say "`TopMost = true` has no effect on Linux", and stop. Do not say which backend or OS API is
      involved, do not explain why the platform behaves that way, and never write "the backend ..." in
      an application-facing page: if a sentence needs to name the layer under the application, it is
      internal and belongs in `dev-docs/`.
    - Name no backend (Win32, AppKit, GTK4), no OS API (`WM_MOVE`, `DWMWA_*`, `NSWindow`, `Gdk`), and
      no history of a library version.
    - Where Kolvir exposes the answer at run time, document the call rather than the table:
      `Window.ChromeCapabilities`, `BadgeCapabilities`, `IGpuAccelerationService`. "Ask the window what
      it supports" is better documentation than "remember which platform supports what".
    - Two places want platform material in full, because the developer must do something: the
      `getting-started/run-on-each-platform.md` page and `shipping/*`, which hold prerequisites,
      target frameworks, publish RIDs, bundle layout, and the signing, manifest and plist requirements.
      That is instruction, not internals.

12. **`reference/platform-matrix.md` is the single place platform differences are listed**, and it is
    a plain capabilities table: the three operating systems as columns, Kolvir features as rows
    ("Keep-above", "App badging", "Chromeless windows", "Library-drawn menus", "GPU acceleration"),
    each cell yes, no, or a short qualification. Nothing else in `docs/` repeats a caveat in prose;
    pages link to the matrix. Rows are verified in `src/` and against the demo pages, because the
    guide's prose is a claim and the code is the answer.

13. **Sample code is real and compiles.** Code in `getting-started/`, `concepts/` and any hand-written
    `reference/` page is copied from demo pages or from the snippet project
    `src/tools/Kolvir.Docs.Snippets`, which references `Kolvir` and builds with the docs tool. The
    build fails when a fenced `csharp` block in those pages is not present verbatim in the snippet
    project, so a page cannot ship code that does not compile.

14. **The repository's Markdown rules apply.** Every file is 7-bit ASCII (`-`, `...`, `->`, `x`), no
    tabs, fenced code with a language, tables for capabilities and option lists. The docs tool enforces
    the ASCII rule and the path-case rule as part of the build.

15. **The pre-1.0 caveat appears once.** Kolvir is pre-1.0 and the API is settling, so `install.md`
    and `run-on-each-platform.md` say so where a reader chooses a version; a guide repeats it only
    where one API is known to be in motion.

16. **CI verifies on a pull request and publishes on a push.** `.github/workflows/docs.yml` runs on
    pull requests that touch `docs/`, the docs tool, `src/core/Kolvir/`, the tool manifest or the
    demo's page table, building the library, the snippets, the reference and the site on
    `ubuntu-latest` - which is also the case-sensitivity check - and uploading the HTML as a workflow
    artifact. A push to `develop` then deploys that same artifact to GitHub Pages. A pull request
    never publishes. Superseded pull request builds cancel; publications queue instead, because a
    cancelled deployment leaves the site on whichever version happened to finish.

## 5. The pipeline

```
.config/dotnet-tools.json          docfx 2.81.0 (a net10.0 tool), the only pinned tool
src/tools/Kolvir.Docs/             console app; `build` runs everything, the parts have their own verbs
  Reference/                       XML + assembly -> Markdown, one page per control
  ControlMap.cs                    control -> how-to page, demo page id, remarks choice
  Navigation/                      nav.yml -> generator navigation, planned-entry checks
  Validation/                      ASCII, path case, screenshots, snippets
  Site/                            invokes the pinned generator
src/tools/Kolvir.Docs.Snippets/    compiles the code the getting-started pages show
docs/docfx.json                    hand-written generator configuration
docs/nav.yml                       hand-written navigation
docs/toc.yml                       generated, ignored
docs/reference/controls/*.md       generated, ignored
docs/_site/                        built HTML, ignored
```

Both tool projects set `ShipsToConsumers=false` in `Directory.Build.props`, the switch the demo
projects already use, so neither is packed and neither is held to `CS1591`; both are added to
`Kolvir.slnx` under `/src/tools/`. The docs tool targets `net10.0` and is desktop-only: it reads a
built assembly by reflection and shells out to `dotnet` and the demo, which is exactly what a
documentation tool may do and what a shipped library may not.

Two behaviours of the pinned generator are worth writing down, because both were found by trying them
rather than from its documentation:

- the search index is produced only when `build.postProcessors` names `ExtractSearchIndex`, and the
  table of contents only when the generated `toc.yml` is part of `build.content`; the sidebar then
  needs `_disableToc: false` in `globalMetadata`, because the template's default is a page with no
  navigation at all;
- a broken relative link or a missing image is a warning, so the build passes `--warningsAsErrors` to
  turn them into failures.

One option is set for the opposite reason: `_disableBreadcrumb: true`. The template derives the trail
from the single root navigation, and for a generated control page it produces a trail that names the
wrong parent. The sidebar already says where a page sits.

## 6. Migration map

Nothing is deleted or moved out of `DEVELOPER-GUIDE.md` in this phase; the map is the order the
phases take.

| `DEVELOPER-GUIDE.md` | Goes to |
| --- | --- |
| intro and repository layout | stays in `dev-docs/`; the layout paragraph becomes `concepts/architecture.md` |
| Design | `concepts/*`, one page per concept, rewritten |
| Using it: focus, visibility, handlers | `concepts/input-focus.md` (phase 2) |
| Using it: sizes, panels, scrolling, zoom | `concepts/layout-model.md`, `guides/` (phase 2) |
| Using it: list box through tab controls | `guides/lists.md`, `guides/dialogs.md`, `guides/text.md`, `reference/controls/` (generated) |
| Using it: invalidation, frame timing | `concepts/painting.md` (phase 2) |
| Using it: text box through menus | `guides/text.md`, `guides/menus.md` (phase 2) |
| Using it: borders, appearance, fonts, opacity | `concepts/appearance.md`, `guides/woff.md` (phase 2) |
| Using it: window topics, chromeless windows, badging | `guides/windows.md`, `guides/badging.md`, matrix rows |
| Using it: drag and drop, animations | `guides/drag-and-drop.md`, `guides/animations.md` |
| Using it: images, SVG, GIF, WebP | `guides/images.md`, `guides/svg.md`, `guides/animated-gif.md`, `guides/animated-webp.md` |
| Using it: virtual DOM, observables | `guides/virtual-dom.md`, `guides/observables.md` |
| Hardware acceleration (Windows), (macOS), (Linux) | `guides/gpu-acceleration.md` plus the acceleration rows of the matrix; the measurement recipes stay in `dev-docs/` and `README.md` |
| Writing a backend | stays in `dev-docs/` |
| NativeAOT, Running the application | `shipping/nativeaot.md`, `shipping/trimming.md`, `shipping/<platform>.md`; the host recipes stay in `TESTING-RUNBOOK.md` |
| Packages and releases | `reference/packages.md` for the consumer half, `dev-docs/` for the release half |
| Contributing, tests, the ASCII rule | stays in `dev-docs/` |

## 7. Phases

**Phase 1. The pipeline first**, so that every page written afterwards is validated by the build from
the day it exists:

1. this document;
2. the pipeline: tool manifest, `src/tools/Kolvir.Docs`, `src/tools/Kolvir.Docs.Snippets`,
   `docs/docfx.json`, `docs/nav.yml` with the whole tree, and a generated page for every public
   control, with the site built end to end;
3. the front door: `index.md`, `getting-started/install.md`, `getting-started/first-window.md`,
   `getting-started/run-on-each-platform.md`, `concepts/architecture.md`,
   `reference/platform-matrix.md`;
4. one screenshot through the manifest: rendered, committed, recorded, referenced;
5. `.github/workflows/docs.yml`.

**Phase 2.** The remaining `concepts/` pages and all of `guides/`, one at a time, each with its demo
page, its screenshot and its matrix link.

**Phase 3.** `shipping/`, `troubleshooting.md`, `samples.md`, and the hand-written reference pages for
`Application`, `Window`, `UiHost`, options and environment variables.

**Phase 4.** A directory per release tag beside `latest/`, once there are releases to publish. The
deploy job then builds `latest/` and every tag into one artifact, because a deployment replaces the
site rather than adding to it. A custom domain, if one is ever wanted, is the same job plus a
`CNAME` file in the artifact.

## 8. Acceptance for phase 1

- `dotnet tool restore`, then `dotnet run --project src/tools/Kolvir.Docs -- build`, produces
  `docs/_site/index.html` with working navigation and a search index, from a clean clone, with no
  other prerequisite.
- Every public control has a generated page, and no such page is committed.
- `docs/nav.yml` lists the whole tree: the pages that exist build, and the planned ones are reported
  by name rather than silently missing.
- The screenshot `index.md` shows exists, is listed in `docs/assets/screenshots/manifest.yml`, and is
  reproducible from the recorded page id and arguments.
- The docs workflow is green on a pull request and uploads the HTML.
- `dev-docs/DEVELOPER-GUIDE.md` is unchanged.
- No page under `docs/` argues for a design, narrates a measurement, names a backend or an OS API, or
  explains why something is the way it is outside `concepts/` (decisions 10 and 11).

## 9. Facts this plan was built on

- The demo's page table (`DemoUi.Demos`) has 69 rows. `README.md` and `DEVELOPER-GUIDE.md` say
  "sixty-three"; both are stale, and both belong to files other phases own, so `docs/` uses the
  verified number and the two stale counts are left alone for now.
- `Kolvir.Demo.App --render <png> [width height] [--page <id>] [--light|--dark]` exists, is handled
  before any toolkit initialises, and paints through the headless backend, so it runs with no display.
- DocFX 2.81.0 ships `tools/net10.0/any/`, which matches the SDK this repository requires
  (`10.0.403` here). A Markdown-only site with navigation, search and nested pages was built with it
  before any prose was written.
- `Window.ChromeCapabilities` (`Borderless`, `CaptionButtons`, `WindowColors`) is the library's own
  answer to "which parts of application-owned chrome this system supports", and every feature that
  varies reports its own capability type. The matrix is built from those, not from prose.
- `IWindowBackend.TopMost` stores the value and does nothing else on Linux; `ChromeCapabilities`
  reports `WindowColors` as unavailable below Windows build 22000; hardware acceleration defaults to
  on except on macOS. The matrix says those three things in platform terms and nothing more.
