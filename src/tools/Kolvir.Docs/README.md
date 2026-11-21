# Kolvir.Docs

The documentation tool: it generates the control reference from the library's own XML documentation,
translates `docs/nav.yml` into the site generator's navigation, checks the docs tree, and builds the
guide's HTML.

```bash
dotnet run --project src/tools/Kolvir.Docs -- build
```

That is the whole documented command, and it needs nothing but the .NET 10 SDK: it builds `Kolvir`
for its XML documentation file, generates `docs/reference/controls/*.md`, writes `docs/toc.yml`,
checks the tree, restores the pinned generator from `.config/dotnet-tools.json`, and produces
`docs/_site/index.html` with navigation and a search index.

## Verbs

| Verb | What it does |
| --- | --- |
| `build` | Everything below, in order. The default. |
| `reference` | Build the library and generate `docs/reference/controls`. |
| `site` | Translate `nav.yml`, check the tree, build the HTML. |
| `screenshots` | Render every image `docs/assets/screenshots/manifest.yml` lists. |
| `list` | Print the controls the reference would generate, and where each is demonstrated. |
| `check` | Check the tree only: navigation, ASCII, path case, screenshots, snippets. |

Options: `--repo <path>`, `--configuration <name>` (default `Debug`), `--no-build`,
`--allow-missing-docs`.

## What it writes, and what is committed

`docs/reference/controls/`, `docs/toc.yml` and `docs/_site/` are generated and ignored. Everything
else under `docs/` is hand-written. The reference is not committed because it would be a second copy
of the XML documentation the packages ship, and the copy is the one that goes stale.

## Checks

The build fails, rather than warning, when:

- a public control has no entry in `ControlMap.Entries`, or an entry names a control that no longer
  exists, so a control cannot ship undocumented or leave a page behind;
- an entry names a demo page id that is not in the sample's page table;
- a public member has no XML documentation entry (the page would have a hole; `--allow-missing-docs`
  downgrades this while a member is being documented);
- a page or image the navigation names does not exist, a planned page does exist, or a relative link
  resolves only because the filesystem ignores case;
- a Markdown file is not 7-bit ASCII;
- a fenced C# block on a getting-started or concepts page is not in `../Kolvir.Docs.Snippets` verbatim,
  which is what makes the guide's code compile.

The site generator is run with warnings as errors, so a broken link or a missing image is a failure
there too.

## This is a desktop tool

It reads a built assembly by reflection, starts `dotnet` and the sample as child processes, reads
YAML by name and writes into the working tree. That is what a documentation tool is for and what a
shipped library may not do, so the project sets `IsAotCompatible=false` and is excluded from packing
(`ShipsToConsumers` in `Directory.Build.props`). Nothing here touches a shipped assembly.
