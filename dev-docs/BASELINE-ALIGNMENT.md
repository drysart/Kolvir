# Baseline alignment

A container that stretches its children across an axis puts their boxes in a row; it does not put
their *text* in a row. Two labels at 12 and 21 pixels both start at the top of their boxes, so their
first lines sit at different heights, and a field beside them looks off by however much its own
inner padding puts its text somewhere else. Lining the boxes up is the only thing the layout can do
by itself, because the box is the only thing it knows about.

A control can be asked about its text. `Control.TryGetBaseline(size, out baseline)` answers where
the first line of its first paragraph would sit if it were arranged at `size`, measured from the top
of its border and excluding the margin, and it answers *false* when it has no text to line up - an
empty label does not drag a row around. `Label`, `RichLabel`, `TextBox` (single line),
`ToggleControl` and `Button` answer it. Each of them takes the number from
`TextRenderer.GetBlockBaseline`, which is the same expression `TextRenderer.Draw` paints with, so the
alignment is with the glyphs rather than with a second piece of arithmetic that happens to agree
today.

Two containers offer to use it:

| Container | Switch | Applies to |
| --- | --- | --- |
| `StackPanel` | `AlignBaselines` | a horizontal run |
| `TableLayoutPanel` | `RowStyle.AlignBaselines` | one row |

## One rule, two containers

The rule is `BaselineRow`, an internal struct in `src/core/Kolvir/Layout/BaselineRow.cs`. Given the
boxes in a row that declare a baseline:

- the shared baseline is the lowest of them (`Include(top, baseline)` per participant, then
  `Reference`);
- every participant moves down by `Reference - baseline` (`ShiftFor`), and nothing moves up,
  because the shared one is the lowest;
- the row must reach from the shared baseline down by each participant's own descent -
  `Reference - baseline + height + below` (`Reserve`) - and never below the height the row's own box
  started with (`NeededHeight`).

It is two passes on purpose. `Reference` is only known once every participant has answered, and
`Reserve` is evaluated as `Reference - baseline + height + below` in that order: that is the
expression the rule was written as inside `StackPanel` before it was extracted, and regrouping it
would be a different number in the last bit, which is a row that measures a hair short of what it
arranges. The extraction moved no pixel - the page-by-page render comparison against the commit
before it is the evidence, not the unit tests.

The two callers fill the same four numbers differently, and that is the whole of the difference
between them:

| | `top` | `height` | `below` | `box` |
| --- | --- | --- | --- | --- |
| `StackPanel` | margin plus the child's own `VerticalAlignment` inside the run's row box | its slot height, capped by the panel | its bottom margin | the run's row box: its tallest child and margins |
| `TableLayoutPanel` | 0 - a cell is already at the top of its cell | the height it will be arranged at | 0 - the table never applies a margin to a cell's box | 0 - the row's height is what is being computed |

`below` being zero there is not a hole a margin can fall through. The table counts a cell's margin
when it sizes a track and never again: the box is placed by the baseline and the row grows until the
tallest box bottom is inside it, so an unapplied margin has no geometry to push against and nothing
to overflow into. If margins ever become positional in a cell, that call site has to pass
`margin.Bottom` as well and the row will grow by it.

## What the stack does with it

`StackPanel.AlignBaselines` turns on only for `StackOrientation.Horizontal`; a vertical stack ignores
it. A child that declares a baseline is not stretched across the stack - it keeps the height it
measured and is placed by its own `VerticalAlignment` in the run's row box - because a stretched
child's baseline would be a function of the panel's height and the panel's height a function of its
children. `AlignedTop` and `BaselineCrossOf` answer where a child would have been placed and how tall
its slot is; `PlanBaselines` collects them into a `BaselineRow`, which is the only place the rule
now lives. A child that reports no baseline is arranged exactly as it always was, in the box the run
grew to. The row's grown height is reported through `Measure`, so an auto-sized stack is measured
tall enough for the row it then arranges; the arrange pass is two walks for the same reason the
helper is two passes.

## What the table does with it

`RowStyle.AlignBaselines` is on the row style rather than on the shared `RowOrColumnStyle` base
because a baseline is a y coordinate: a column cannot have one. `RowStyle` is sealed and overrides
`Equals`/`GetHashCode` to include the flag - the table decides that an assigned sequence of styles
"says what it already says" by value, so a flag left out of the comparison would be a switch the
table silently dropped.

The table is simpler than the stack in one respect: it has no `VerticalAlignment` at all. A cell is
placed at the top-left of its cell at its own size, and only `DockStyle.Fill`, `Right` and `Bottom`
move it, so there is no stretch to fight and a participant is always at the top of its cell.

A cell takes part in its row's baseline when:

- it spans one row (`RowSpan == 1`), because a cell across several rows has no single row baseline;
- its row style flags it;
- its dock is not `DockStyle.Fill` or `DockStyle.Bottom`.

Those two are excluded because their box is not their own: `Fill`'s height is the row's, so its
baseline would be a function of the height the row is being measured for, and `Bottom`'s top is
measured from the bottom of the cell, which is the same circle. Every other dock value takes part,
including `DockStyle.Top` and `DockStyle.Left` - this table does not act on either, so it places
them exactly as it places `DockStyle.None`, and a cell that is placed at the top of its cell at its
own height belongs to the row whatever unused dock value it carries.

Measure and arrange agree by construction. `ComputeTrackSizes` grows the row after the columns are
final (so a cell is asked about its baseline at the width it is about to be given) and before the
percent rows are distributed (so a row grown here is room they no longer share, exactly as the
ordinary growth is). `GrowRowsForBaselines` asks the same cells the same question the arrange pass
will ask and grows the row to `NeededHeight + CellPadding.Vertical` through the existing
`GrowSpannedTracks`, which takes the max of the ordinary growth and this one. Arrange is two walks:
`PlaceInCell` - the cell placement the table always made, extracted so both paths place a cell the
same way - places every cell that does not take part, and the ones that do are held in a reused
array until their row's reference is known.

## The decisions

1. **AutoSize rows grow.** The row is measured tall enough for the row it arranges, through the same
   max-with-the-ordinary-growth rule. The test shape is the stack's: the arranged cells fit inside
   what the row reported.
2. **Fixed rows do not.** An `Absolute` or `Percent` row's height is what the style says, so the
   shift is applied and a participant can be pushed past the bottom of the row. A cell taller than a
   fixed row already overflows it, so growing the row for this one thing would be the surprising
   half of the pair.
3. **Spanned cells take no part.** `RowSpan > 1` is measured and placed exactly as before.
4. **Docked cells take part unless they are `Fill` or `Bottom`.** See above: those two are circular
   for an AutoSize row because the row decides their box; every other dock value leaves the box to
   the cell's content and takes part.
5. **One participant is a no-op.** The shared baseline is that cell's own, so the shift is zero and
   the needed height is its own height - the row the ordinary growth already produced.
6. **Rows resolve independently.** Each row accumulates its own reference; nothing is shared between
   rows.

## Where it is exercised

`Kolvir.Tests`' `BaselineAlignmentTests` covers the stack and `TableBaselineAlignmentTests` the table:
the shared baseline at two sizes, the measure/arrange agreement, spanned and filling cells, a
single-cell row, two flagged rows, a fixed row that overflows, and the unflagged arrangement that has
to stay exactly as it was. `TableStyleTests` and `RowOrColumnStyleTests` cover the style comparison.

The demo's `baseline-align` page shows a live horizontal run with the switch on and off, and below it
the same demonstration for two table rows - one flagged and one not - so a single render shows both
states.
