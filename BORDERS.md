# Borders

Chrome as an object instead of three numbers. A control's border today is `BorderWidth`, `BorderColor`
and `CornerRadius`: painted last, ignored by layout, and reimplemented by hand in six controls that
want a state-coloured edge. This replaces them with an abstract `Border` that answers the three
questions a frame has to answer — how much of the control it takes, how much it clips, and what it
paints — so that a border can be a value a control class supplies, an application replaces, or a
third party implements.

Status: phase 1 designed, not implemented. Phase 2 (per-edge and per-corner settings) is scoped but
not designed in detail.

---

## 1. Where it belongs: core, and why not a package

`Kolvir` itself, beside `Background` and `Thickness`. Every control has the property, so nothing here
can be trimmed away and no package split helps; this is the same accepted seam cost as
`FEATURE-COST-POLICY.md` section 7 describes, not a feature.

The price, stated plainly:

- one reference field per `Control` (8 bytes), where the three members it replaces cost 12;
- three virtual dispatches per paint (`ClipTo`, `Paint`, and the `IsRectangular` read that feeds
  `Opaque`) and two per layout (the insets);
- an `SKPath` allocation only when a border actually narrows the clip, never on the plain path.

An abstract **class** rather than a `Background`-style struct, because polymorphism is the whole point:
a dashed border, a 9-slice image border or a group-box frame cannot be a discriminant plus four fields.
Immutability is then a contract rather than a property of the type, which is why section 3 spends
words on it.

## 2. The contract

```csharp
public abstract class Border
{
    /// <summary>How much of the control this border takes away from its content.</summary>
    public virtual Thickness Insets => Thickness.Empty;

    /// <summary>Whether this border clips the control to something narrower than its bounds.</summary>
    public virtual bool NarrowsClip => false;

    /// <summary>Narrows the clip to the border's own shape. Only called when NarrowsClip is true.</summary>
    public virtual void ClipTo(SKCanvas canvas, SKRect bounds) { }

    /// <summary>Whether every pixel inside the bounds is still painted with the control's own
    /// background, i.e. whether this border lets the parent show through.</summary>
    public virtual bool IsRectangular => true;

    /// <summary>Paints the border, inside <paramref name="bounds"/>.</summary>
    public abstract void Paint(SKCanvas canvas, SKRect bounds, Control owner);

    /// <summary>No border: takes nothing, clips nothing, paints nothing.</summary>
    public static Border None { get; } = new NoneBorder();

    private sealed class NoneBorder : Border
    {
        public override void Paint(SKCanvas canvas, SKRect bounds, Control owner) { }
    }
}
```

Three deliberate details:

**`Paint` receives the owner.** Every border worth having here is state-coloured: `TextBox` picks
disabled, focused, hovered and resting colours, and `ListView`, `TabControl`, `ComboBox` and
`VirtualList` each pick hover colours. Without the owner, a control either allocates a `Border` per
state change or mutates a shared one, and neither is acceptable. With it, a border can read
`owner.IsFocused` and decide.

**Insets and clip are separate questions, and may disagree.** A 9-slice border insets without
clipping; a rounded border clips the corners without insetting them; a future shadow border would clip
square but paint rounded. Collapsing them into one number would be wrong for all three.

**`IsRectangular` exists because `Opaque` asks.** `Control.Opaque` currently reports a control as not
opaque when `CornerRadius > 0`, since the corners let the parent show through. That logic moves behind
the border, and a rounded border keeps the answer it has today.

`Border.None` is a singleton, so "no border" allocates nothing and compares by reference. It is what
`Control.DefaultBorder` returns and what an application assigns to remove a border a class supplied.

## 3. Immutability

A `Border` is immutable by contract: its properties are read-only and change is expressed by assigning
a different instance to `Control.Border`. Mutability was rejected because a border has no way to tell
the controls that use it that it changed, and adding a notification channel means subscription
tokens, unsubscription on reassign and dispose, and every control paying for a mechanism most never
use. This also matches how `Thickness` and `Background` are used, and it is what makes a single shared
instance safe for a class default (`SimpleBorder.Thin`, below).

The cost is that a state change assigns rather than mutates, which is why `Control.Border`'s setter
compares insets before it lays out (section 5).

## 4. Layout: insets versus padding

Decided: **the border is outside the padding**, the CSS and WinForms arrangement.

```
ClientRect   the whole control bounds        0,0,w,h
ContentRect  ClientRect − Insets − Padding   what children and content get
```

`Measure` adds **both**, so the border enlarges a control instead of eating its content — which is
what it does today, where `Measure` knows only about padding and a 1px border quietly shrinks every
`TextBox` by two pixels.

Two consequences worth being explicit about:

- **Hit testing does not change.** `HitTest` stays on `ClientRect`, so the border band still belongs
  to the control for mouse input, exactly as a window's frame belongs to the window for
  `WM_NCHITTEST`. The inset describes *content*, not ownership of the pointer.
- **The existing controls shrink their padding by their border** so that phase 1 changes no pixels.
  A control with a 1px border and 8px of padding becomes 1px of inset and 7px of padding: the content
  starts 8 pixels from the edge, as it does now, and the measured size is unchanged. Section 6 lists
  which ones this is arithmetic for and which three need eyes on a render.

`Padding`'s own setter has a bug that this work inherits: it calls `Invalidate()` and not
`PerformLayout()`, so changing padding at runtime repaints without re-arranging the children. The
`Border` setter must do both, and `Padding` should be fixed in the same pass.

## 5. The `Control` side

```csharp
private Border? _border;

/// <summary>
/// The frame drawn around this control and taken out of its content area. Never null:
/// <see cref="Border.None"/> is the absence.
/// </summary>
public Border Border
{
    get => _border ?? DefaultBorder;
    set
    {
        if (ReferenceEquals(_border, value))
        {
            return;
        }

        var relayout = !(_border?.Insets ?? Thickness.Empty).Equals(value.Insets);
        _border = value;
        Invalidate();

        if (relayout)
        {
            PerformLayout();
        }
    }
}

/// <summary>What this control class draws when the application assigns nothing.</summary>
protected virtual Border DefaultBorder => Border.None;
```

`DefaultBorder` is the reason the property is non-nullable. Once a class can have a default border,
`null` cannot mean "none" — it would also mean "nothing assigned yet", which is the same trap
`Background` documents at length with `Inherit` versus `None`. Rather than a third state, the absence
gets a name: `Border.None`. `control.Border = Border.None` is how an application removes a border its
control class supplied.

`DefaultBorder` also replaces seventeen constructor assignments (section 6) with one overridden
property per class:

```csharp
// TextBox
protected override Border DefaultBorder => SimpleBorder.Thin;
```

The `relayout` check is what keeps a state-coloured border affordable: swapping `_rest` for `_hover`
invalidate the pixel and skips the layout pass, because both take the same inset. That is also the
recommended pattern for controls that colour their border by state — three static instances and an
assignment, rather than the hand-rolled painting described next.
## 6. `SimpleBorder`, and migrating today's borders

`SimpleBorder` is the shape of everything that exists now, plus a decision about colour:

```csharp
public sealed class SimpleBorder : Border
{
    public static SimpleBorder Thin { get; }      // 1px, Theme.Border, square corners
    public float Width { get; }                   // 0 means none: use Border.None instead
    public float CornerRadius { get; }
    public SKColor? Color { get; }                // null means Theme.Border
    public ThemeColor? Role { get; }              // a palette role, so a swap recolours live borders

    public SimpleBorder WithWidth(float width);
    public SimpleBorder WithColor(SKColor color);
    public SimpleBorder WithThemeColor(ThemeColor role);
    public SimpleBorder WithCornerRadius(float radius);

    public override Thickness Insets => new(Width);
    public override bool NarrowsClip => CornerRadius > 0f;
    public override bool IsRectangular => CornerRadius <= 0f;
    public override void ClipTo(SKCanvas canvas, SKRect bounds);   // round rect, antialiased
    public override void Paint(SKCanvas canvas, SKRect bounds, Control owner);
}
```

Colour is a fixed `SKColor` or a `ThemeColor` role, mirroring `Background.FromColor` and
`Background.FromThemeColor`; the role is resolved while painting so a palette swap recolours live
controls with no rebuild. That is what lets `TextBox` hold `_focus = SimpleBorder.Thin.WithThemeColor(
ThemeColor.Accent)` and stop overriding `OnPaintBackground` to draw its own frame.

Painting uses `Painter.DrawBorder` in phase 1 — a stroke on the inside of the bounds, which is exactly
what ships today. Phase 2 needs a path-based implementation (section 10); the call sites do not change.

**Ten controls declare a border, in seventeen assignments, and thirteen `Painter.DrawBorder` calls paint
one.** They are not the same set, and the difference is the interesting half of the migration.

| control | border today | padding today | becomes |
|---|---|---|---|
| `TextBox` | 1px | 8, 6 | inset 1, padding 7, 5; frame no longer painted in `OnPaintBackground` |
| `ComboBox` | 1px | 8, 6, 26, 6 | inset 1, padding 7, 5, 25, 5; same |
| `DateTimePicker` | 1px | 8, 6 | inset 1, padding 7, 5; frame no longer painted in `OnPaintBackground` |
| `ToolTip` | 1px | 8, 5, 8, 6 | inset 1, padding 7, 4, 7, 5 |
| `Toast` | 1px | 22, 10, 40, 10 | inset 1, padding 21, 9, 39, 9; frame no longer painted in `OnPaint` |
| `ColorPicker` | 1px | 8 | inset 1, padding 7 |
| `Expander` | 1px | 12, 0, 12, 12 | inset 1, padding 11, 0, 11, 11 — the zero top becomes a pixel of inset, so the header budget shrinks; verify the render |
| `VirtualList` | 1px | 2 | `Inset` (`ListView.cs:643`) already compensates; verify the render |
| `TabControl` | 1px | 0 | computes four content insets from `BorderWidth` by hand (`388-477`); those become `Border.Insets` |
| `GroupBox` | 1px | 12, 0, 12, 12 | **unchanged in phase 1**; it draws its caption in the top border band |

Of the thirteen frame-painting sites, eleven are a control's own outer frame and become a
`DefaultBorder`:

- the six above that already draw their frame by hand — `TextBox`, `ComboBox`, `TabControl`, `Toast`,
  `VirtualList` (in `OnPaintBorder`), `DateTimePicker`;
- five that paint a frame they never declared, which is a latent inconsistency this fixes rather than
  migrates: `Button` draws a hardcoded `Theme.Border` at 1 pixel in `OnPaintBackground` while leaving
  `BorderWidth` at zero, `DateTimePicker` does the same for its field, and the spectrum and hue views
  inside `ColorPicker` each paint `Theme.Border` at 1 pixel in their own `OnPaint` regardless of any
  setting.

The remaining two are `GroupBox`'s, which stay.

**Seven call sites are inner chrome and stay exactly as they are**, which is why `Painter.DrawBorder`
remains public: `CheckBox` and `RadioButton` glyph boxes, `TreeView`'s item frame and item checkbox,
`VirtualList`'s focus rectangle, and `DateTimePicker`'s calendar glyph and day cells. They draw on an
inner rectangle, not on the control's edge, and no `Border` object should be involved.

Four more controls zero a border they never had — `TabPage`, `Overlay`, `Popup`'s `ScaledPopupHost`,
`ColorDialog` — and those assignments simply go, because `Border.None` is already the default.

**The old members are deleted, not shimmed.** A forwarding `BorderWidth` would have to build a
`SimpleBorder` from whatever `Border` is assigned — meaningless when the assignment is a subclass — and
would keep a second way to say one thing alive forever. Nothing is published to nuget.org; the only
feed is the pre-release test feed in `Release`, and this repository's own call sites are the ones being
swept. If that ever stops being true, a shim is one property away.

## 7. Painting: order, clip, and staying inside

`Control.Paint` today, and after:

```
clip to ClientRect (round rect when the border rounds the corners)
OnPaintBackground → PaintSelection → OnPaint → RenderChildren → OnPaintBorder
```

The border pass stays **last**, so it draws over anything a child painted at the edge. That is what
makes a border chrome rather than a background: geometrically a child may occupy the band, visually
the border wins. Controls that want the frame *behind* their content (the six above, whose text or
caret would rather sit above a 1px line) draw it in the background pass instead — the object makes
that a call to `Border.Paint` from two places rather than two implementations of a frame.

**A border paints inside the control's bounds, and phase 1 keeps it that way.** Three places assume
it: the clip at `Control.cs:2019-2027`, the `QuickReject(_bounds)` subtree skip at `2011` whose comment
says nothing inside a control can leave its bounds, and `Invalidate()` marking only `ClientRect` at
`2108`. An external focus ring, a drop shadow or an outward stroke would need all three widened. That
is deliberately punted: nothing in the library draws outside a control today, and building the
widening when something needs it is cheaper than carrying an unused invariant now.

## 8. What is verified

Phase 1 claims to be a refactor, so it is held to that:

- **The pixel-invariant suite** (`Kolvir.Tests/Core/PixelInvariantTests.cs`): a bordered control's
  fill, its border colour at a sampled pixel, and the focus ring still painted.
- **Headless renders before and after.** `--render` produces whole-page PNGs through the same
  rasteriser on the same machine, so the fonts page, the controls page and the menus page are rendered
  before the change and compared byte for byte after it. This is the check that catches an inset
  applied twice or a padding rounded the wrong way. (The suite has no committed golden images, on
  purpose — see that file's remarks — so this comparison is a manual step, run locally and quoted in
  the pull request.)
- **Geometry, not pixels**, for the layout half: a bordered control measures two pixels wider and
  taller than the same control with `Border.None`, `ContentRect` is inset by exactly `Insets + Padding`,
  and `Border.None` changes nothing at all.
- **The new unit tests**: `Control.Border` defaults to `DefaultBorder`; assigning `Border.None`
  removes a class default; the setter repaints without re-arranging when the insets are unchanged and
  does both when they differ; a rounded border narrows the clip and reports not opaque; a square one
  does neither.
- Full suite green, and the demo's pages still draw.

## 9. Decisions

Settled in review:

1. **One abstract `Border`, on `Control`**, with paint, clip and insets. No separate container control
   is needed, which is also why the name `Border` is free.
2. **Insets are outside padding** (`ContentRect = ClientRect − Insets − Padding`) and `Measure` adds
   both; the existing controls shrink their padding to keep their pixels.
3. **Immutable borders**, reassigned rather than mutated.
4. **`Border.None`, not a nullable property.** A class default needs "unset" and "none" to be
   different states.
5. **`Paint` takes the owning control**, so state colours cost nothing per frame.
6. **Borders paint inside the bounds.** External focus rings are a later, cross-cutting change.
7. **`GroupBox` is left alone.** A `GroupBoxBorder` may later delete the control entirely, since a
   group box is a panel with a distinctive frame.
8. **The old members are deleted** rather than kept as shims.

Chosen while writing this down, and worth disagreeing with now rather than later:

9. **`DefaultBorder`**, mirroring `DefaultBackground`, so chrome is declared per class.
10. **The setter compares insets** before running a layout pass.
11. **`Padding`'s setter is fixed** to lay out as well as repaint.

## 10. Phase 2: per edge, per corner

Deferred, but the phase-1 shape is chosen to make it additive:

- `SimpleBorder` gains per-side widths and colours and per-corner radii. `Insets` is already a
  `Thickness`, so layout needs no change.
- Painting becomes path-based — the bounds minus the insets as one path, filled — because four
  differently coloured edges and four different radii cannot be a single stroked rectangle. That is a
  rewrite of `SimpleBorder.Paint` alone.
- `GroupBoxBorder` becomes possible, and `GroupBox` becomes a `Panel` with one.
- Dashed edges are a `SKDashPathEffect` once the paint is path-based, so they are nearly free at that
  point and expensive now.

## 11. Risks

- **The migration's silent failure is a one-pixel shift somewhere nobody renders.** Nine of the ten
  controls are arithmetic; `TabControl`, `VirtualList`, `Expander` and `GroupBox` involve hand-placed
  insets and need a render each, not a subtraction.
- **`Opaque` is load-bearing for repaint cost.** A border that wrongly claims to be rectangular stops
  a control from repainting what is behind its rounded corners, which shows up as a smear rather than
  an exception. The rounded case is covered by the pixel suite; a third-party border is on its honour.
- **`DefaultBorder` plus subclassing is a sharp edge.** A control class that sets `Border` in its
  constructor and a derived class that overrides `DefaultBorder` disagree silently, and the constructor
  wins. The docs must say that a class declares chrome by overriding, never by assigning.
- **Deleting the old members breaks every consumer.** Acceptable while the only feed is a pre-release
  test feed; it stops being acceptable the day something is published.
