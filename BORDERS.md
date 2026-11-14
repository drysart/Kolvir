# Borders

Chrome as an object instead of three numbers. A control's border today is `BorderWidth`, `BorderColor`
and `CornerRadius`: painted last, ignored by layout, and reimplemented by hand in six controls that
want a state-coloured edge. This replaces them with an abstract `Border` that answers the three
questions a frame has to answer — how much of the control it takes, how much it clips, and what it
paints — so that a border can be a value a control class supplies, an application replaces, or a
third party implements.

Status: **phase 1 implemented** on `feature/borders`. Phase 2 (per-edge and per-corner settings) is
scoped but not designed in detail. Section 8 records what was verified, including the two places
where the new model deliberately differs from the old one.

---

## 1. Where it belongs: core, and why not a package

`Kolvir` itself, beside `Background` and `Thickness`. Every control has the property, so nothing here
can be trimmed away and no package split helps; this is the same accepted seam cost as
`FEATURE-COST-POLICY.md` section 7 describes, not a feature.

The price, stated plainly:

- one reference field per `Control` (8 bytes), where the three members it replaces cost 12;
- three virtual dispatches per paint (`ClipTo`, `Paint`, and the `IsRectangular` read that feeds
  `Opaque`) and two per layout (the insets, once in `Measure` and once in `ContentRect`);
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
    public virtual Thickness Insets(Control owner) => Thickness.Empty;

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

**The owner is passed to `Paint` and to `Insets`, and deliberately not to the rest.** `Paint` needs it
because every border worth having is state-coloured: `TextBox` picks disabled, focused, hovered and
resting colours, and `ListView`, `TabControl`, `ComboBox` and `VirtualList` each pick hover colours.
Without the owner a control either allocates a `Border` per state change or mutates a shared one, and
neither is acceptable.

`Insets` needs it for a reason that only shows up in phase 2, and is worth recording now: a
`GroupBoxBorder` takes its caption band out of the content area, and that band's height is the line
height of **the control's font** — plus, for a border that draws its own text, the control's `Text`.
`Control.Text` and `Control.Font` are both public and virtual, so a border can read them from the
owner with no downcast and no control-specific interface. An inset that depends on the font is
therefore not a constant, and `Insets` without the owner could not express a group box.

`NarrowsClip`, `ClipTo` and `IsRectangular` stay argument-less because they describe the *kind* of
shape rather than its measurements: whether the corners let the parent through, and how to clip to
them, are properties of the border, and `Opaque` asks them on a path where a virtual call with an
argument buys nothing.

**State is a value the control computes, not something a border digs for.** `Control` gains one flags
enumeration and one virtual property:

```csharp
[Flags]
public enum ControlState
{
    None = 0,
    Hovered = 1,
    Focused = 2,
    Disabled = 4,
    Pressed = 8,     // a mouse button is down on the control
    Active = 16,     // a control-specific "on": an open combo box
}

public virtual ControlState State =>
    (Enabled ? ControlState.None : ControlState.Disabled)
    | (IsFocused ? ControlState.Focused : ControlState.None)
    | (IsMouseOver ? ControlState.Hovered : ControlState.None)
    | (IsPressed ? ControlState.Pressed : ControlState.None);
```

Four of those five bits are `Control`'s own properties already — `Enabled`, `IsFocused`, `IsMouseOver`
and `IsPressed` — so `State` is a reformatting of what the base type already exposes, and a border that
reads `owner.State` is not seeing anything an application could not.

`Active` is the one exception, and the reason the enumeration exists at all. **A popup being open is
the only border-affecting state in the library that is not on `Control`**, and it is declared three
times: `ComboBox.IsDropDownOpen`, `SplitButton.IsDropDownOpen` and `DateTimePicker.IsDropDownOpen`, all
public, none of them on the base type. A border reading control properties directly would have to know
all three concrete types and cast to each; with a bit, each control overrides `State` to add `Active`
and one border understands all three. `ToggleControl.CheckState` and `Expander.IsExpanded` are the other
control-specific states, and neither touches a border today — if one does, it is a bit, not a cast.

The bits are the ones the library already uses — `Disabled` in `TextBox`, `ComboBox` and
`VirtualList`, `Active` for `ComboBox`'s open list — and no others. A control whose own state is not on
`Control` overrides `State` to add a bit (`ComboBox`, `SplitButton` and `DateTimePicker` each do this
rather than storing a second border), and every border then understands it without knowing the control.

`SimpleBorder` reads `owner.State` and owns one documented precedence — **disabled > pressed > focused
> hovered > resting** — which is the order `TextBox` and `ComboBox` already implement by hand, extended
by the one bit they do not use yet. Today each control reinvents that order; after this, one place does.

**Insets and clip are separate questions, and may disagree.** A 9-slice border insets without
clipping; a rounded border clips the corners without insetting them; a future shadow border would clip
square but paint rounded. Collapsing them into one number would be wrong for all three.

**`IsRectangular` exists because `Opaque` asks.** `Control.Opaque` currently reports a control as not
opaque when `CornerRadius > 0`, since the corners let the parent show through. That logic moves behind
the border, and a rounded border keeps the answer it has today.

`Border.None` is a singleton, so "no border" allocates nothing and compares by reference. It is the
default value of `Control.Border` and the way an application removes a border a control set for
itself.

## 3. Immutability

A `Border` is immutable by contract: its properties are read-only and change is expressed by assigning
a different instance to `Control.Border`. Mutability was rejected because a border has no way to tell
the controls that use it that it changed, and adding a notification channel means subscription
tokens, unsubscription on reassign and dispose, and every control paying for a mechanism most never
use. This also matches how `Thickness` and `Background` are used, and it is what makes a single shared
`static readonly` instance safe for a control class's own border.

The cost is that a state change assigns rather than mutates, which is why `Control.Border`'s setter
compares insets before it lays out (section 5).

## 4. Layout: insets versus padding

Decided: **the border is outside the padding**, the CSS and WinForms arrangement.

```
ClientRect   the whole control bounds        0,0,w,h
ContentRect  ClientRect − Insets(this) − Padding   what children and content get
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
private Border _border = Border.None;

/// <summary>
/// The frame drawn around this control and taken out of its content area.
/// <see cref="Border.None"/> draws nothing, which is the default.
/// </summary>
public Border Border
{
    get => _border;
    set
    {
        if (ReferenceEquals(_border, value))
        {
            return;
        }

        var relayout = !_border.Insets(this).Equals(value.Insets(this));
        _border = value;
        Invalidate();

        if (relayout)
        {
            PerformLayout();
        }
    }
}
```

That is the whole of it: one field, one property, no class default and no second place for a border to
come from. A control that draws a frame assigns one in its constructor, the way it already assigns its
padding, and a derived class that wants different chrome assigns in *its* constructor, which runs
after the base's and therefore wins — ordinary C# precedence rather than a rule to remember.

The `relayout` comparison is between the *old* border's insets and the *new* one's, both measured
against this control, which is what makes a state swap free when the geometry did not change and
correct when it did (a `GroupBoxBorder` recomputes its caption band from the font, so the same screen
position can want a layout when the font changed). It also means a `Border` must answer `Insets`
honestly for the control it is on — it may be called with the old instance while the new one is being
assigned.

**The declarative part is a convention, not an API.** A control class that draws a frame declares it
once, as a shared instance, and assigns it:

```csharp
public class TextBox : Control
{
    private static readonly Border DefaultBorder = SimpleBorder.Thin;

    public TextBox()
    {
        Border = DefaultBorder;
        Padding = new Thickness(7f, 5f);
    }
}
```

`static readonly` and not a field per instance, both for the allocation and because the instance is
what a state change swaps away from: a `TextBox` holds `_rest`, `_hover` and `_focus` borders and
assigns among them, so the shared instance is only ever a *value*, never something mutated.

`Border.None` still earns its name: it is the field's initial value, so a control that says nothing
draws nothing, and `control.Border = Border.None` is how an application takes away a frame a control
gave itself.

The `relayout` check is what keeps a state-coloured border affordable: swapping `_rest` for `_hover`
invalidates the pixel and skips the layout pass, because both take the same inset. That is also the
recommended pattern for controls that colour their border by state — a few static instances and an
assignment, rather than the hand-rolled painting described next.

## 6. `SimpleBorder`, and migrating today's borders

`SimpleBorder` is the shape of everything that exists now, plus state colours, which is what removes the
hand-written resolution from four paint methods:

```csharp
public sealed class SimpleBorder : Border
{
    public static SimpleBorder Thin { get; }      // 1px, Theme.Border, square corners
    public static SimpleBorder Rounded(float r);  // no stroke: rounds the fill and clips, takes nothing
    public float Width { get; }                   // 0 is a shape without a stroke, not a way to say "none"
    public float CornerRadius { get; }
    public SKColor Color { get; }                 // resting, resolved

    public SimpleBorder(float width, float cornerRadius = 0f);
    public SimpleBorder WithWidth(float width);
    public SimpleBorder WithCornerRadius(float radius);
    public SimpleBorder WithInsets(Thickness insets);          // take more room than the stroke is thick
    public SimpleBorder WithColor(SKColor color);
    public SimpleBorder WithThemeColor(ThemeColor role);
    public SimpleBorder WithHoverColor(ThemeColor role);       // ControlState.Hovered
    public SimpleBorder WithFocusColor(ThemeColor role);       // ControlState.Focused
    public SimpleBorder WithActiveColor(ThemeColor role);      // ControlState.Active
    public SimpleBorder WithDisabledColor(ThemeColor role);    // ControlState.Disabled
    public SimpleBorder WithPressedColor(ThemeColor role);     // ControlState.Pressed

    public override Thickness Insets(Control owner) => _insets ?? new(Width);
    public override bool NarrowsClip => CornerRadius > 0f;
    public override bool IsRectangular => CornerRadius <= 0f;
    public override void ClipTo(SKCanvas canvas, SKRect bounds);   // round rect, antialiased
    public override void Paint(SKCanvas canvas, SKRect bounds, Control owner);
}
```

Colour is a fixed `SKColor` or a `ThemeColor` role, mirroring `Background.FromColor` and
`Background.FromThemeColor`; the role is resolved while painting, so a palette swap recolours live
controls with no rebuild.

`Paint` resolves the state in one documented order — **disabled > pressed > active > focused >
hovered > resting** — which is what the built-in controls already implement by hand, differing only in
that `ComboBox` also treats its open list as focused. That difference is now a bit rather than a
special case: `ComboBox` overrides `State` to add `Active` and gives its frame `WithActiveColor` and
`WithFocusColor` the accent, and its paint method stops choosing colours at all. `VirtualList` sets no
hover colour, so it keeps its pixels; `TabControl`, `Button` and `DateTimePicker` set none, and are
unchanged.

This is what lets `TextBox` hold a `static readonly SimpleBorder _focused = SimpleBorder.Thin
.WithThemeColor(ThemeColor.Border).WithFocusColor(ThemeColor.Accent).WithHoverColor(ThemeColor
.SplitterHover).WithDisabledColor(ThemeColor.DisabledBackground)` and stop overriding
`OnPaintBackground` to draw its own frame.

Painting uses `Painter.DrawBorder` in phase 1 — a stroke on the inside of the bounds, which is exactly
what ships today. Phase 2 needs a path-based implementation (section 10); the call sites do not change.

**Ten controls declare a border, in seventeen assignments, and thirteen `Painter.DrawBorder` calls paint
one.** They are not the same set, and the difference is the interesting half of the migration.

| control | border today | padding today | what it became |
|---|---|---|---|
| `TextBox` | 1px | 8, 6 | inset 1, padding 7, 5; the frame is no longer painted in `OnPaintBackground` |
| `ComboBox` | 1px | 8, 6, 26, 6 | inset 1, padding 7, 5, 25, 5; same, and the chevron measures from the frame's inner edge |
| `DateTimePicker` | 1px | 8, 6 | inset 1, padding 7, 5; it inherits `TextBox`'s frame and adds `Active` while the calendar is up |
| `ToolTip` | 1px | 8, 5, 8, 6 | inset 1, padding 7, 4, 7, 5 |
| `Toast` | 1px | 22, 10, 40, 10 | inset 1, padding 21, 9, 39, 9; its width measurement adds the insets back |
| `ColorPicker` | 1px | 8 | inset 1, padding 7; the spectrum and hue views keep their strokes as inner chrome, because a stroke over a bitmap has nothing to inset |
| `Expander` | 1px | 12, 0, 12, 12 | insets 1, 0, 1, 1 and padding 11, 0, 11, 11: the caption sits on the top edge, so the top takes no room |
| `VirtualList` | 1px | 2 | inset 1, padding 1, and `ScrollPadding` is the two together, so the rows stay two pixels in |
| `TabControl` | 1px | 0 | **shape only, no stroke**: the strip and the pages share the frame's edges, so reserving room would move both. It keeps drawing its frame in the background pass, and a `TabControlBorder` that owns the strip is phase-2 work |
| `GroupBox` | 1px | 12, 0, 12, 12 | shape only, no stroke: it draws its captioned frame itself, as before |
| `Button` | none | 14, 8 | shape only, radius 6, no stroke: the only frame a button draws is the secondary style's, which is chrome of that style |

Of the thirteen frame-painting sites, six are a control's own outer frame and became a declared
`DefaultBorder` — `TextBox`, `ComboBox`, `TabControl`, `Toast`, `VirtualList` and `DateTimePicker`. Five
paint a frame they never declared, and the answer differs by what the frame *is*:
`Button` draws the secondary style's hairline, so its border is the shape without a stroke;
`DateTimePicker`'s field and the two spectrum views inside `ColorPicker` draw a stroke over content that
fills the whole control, so they stay inner chrome. `GroupBox`'s captioned frame stays where it is.

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
the border wins. Two controls that used to draw their own frame in the background pass — `TextBox` and
`ComboBox` — now let the border pass do it, because their content is inset by the frame's own pixel and
so cannot reach it; and one that used to draw a no-op in the border pass because it drew the frame
earlier now draws it there.

**A border paints inside the control's bounds, and phase 1 keeps it that way.** Three places assume
it: the clip at `Control.cs:2019-2027`, the `QuickReject(_bounds)` subtree skip at `2011` whose comment
says nothing inside a control can leave its bounds, and `Invalidate()` marking only `ClientRect` at
`2108`. An external focus ring, a drop shadow or an outward stroke would need all three widened. That
is deliberately punted: nothing in the library draws outside a control today, and building the
widening when something needs it is cheaper than carrying an unused invariant now.

## 8. What is verified

Phase 1 claims to be a refactor, so it is held to that.

**Headless renders, byte for byte.** 82 whole-page PNGs — 60 light pages, 16 dark ones and 6 zoomed
ones — were rendered through the same rasteriser on the same machine before the change and after it,
and compared by SHA-256 digest. **78 of 82 are byte-identical.** The four that are not:

| Page | Difference | Why |
| --- | --- | --- |
| `animated-gif`, `animated-webp` | a 15×17 pixel block | The pages show an animation; two renders of the *same* build differ from each other, so they are not a stable baseline at all |
| `multi-line-text`, `undo-and-redo` | one row of the inner scrollbar's track, x 948–963 | The frame is painted after the content, so it now wins the edge pixel the scrollbar used to paint over. This is exactly what §7 asks for, and it is the only visible consequence of the paint order changing |

Everything else — every control, in both themes, at three zoom levels — came out the same, which is
the evidence that the padding reductions, the insets arithmetic and the clip changes agree.

**The suite.** Ten new tests in `Kolvir.Tests/Core/BorderTests.cs`: a control starts on `Border.None`
and takes no room; a one-pixel frame comes out of the content and back into the measurement, so the
frame makes a control larger rather than shrinking its contents; a stroke-less shape reserves nothing
but narrows the clip and stops the control reporting itself opaque; the state colours are taken in the
documented order and a state colour that was never given falls back to the resting colour; `State`
follows the control's own flags and an override keeps the base's bits; a frame paints its own pixel
inside the bounds and paints over what a child drew; and a frame that takes more room lays out again.
The rest of the suite — 908 tests — is unchanged and green.

**What the migration found.** Four things the design did not anticipate:

- `Panel` arranged its children in `ContentBounds` less `Padding` and knew nothing about the border,
  so every panel with an insetting frame would have put its children a pixel out. It now arranges in
  `ContentBounds` less `Border.Insets(this)`, and clips and invalidates against the same rect.
- `TextBox.ContentPadding`, `VirtualList.ScrollPadding`, `ComboBox`'s chevron and `Toast`'s width
  measurement each worked from `Padding` alone and now add the insets, because every one of them is
  "the space between the frame and the content".
- The five controls that had a `BorderWidth > 0f ? BorderWidth : 1f` fallback painted a pixel at
  width zero. They no longer do: a zero-width border paints nothing, which is what the contract says,
  and the sample's navigation list — which relied on the accident — now declares the frame it was
  actually showing.
- Assigning geometry in a constructor runs layout immediately, and a control whose `OnLayout` reads
  fields that the constructor has not assigned yet will throw. `ColorPicker` did exactly that, and now
  assigns its frame and gutter after its views exist — the order `Border`'s remarks ask for.

## 9. Decisions

Settled in review:

1. **One abstract `Border`, on `Control`**, with paint, clip and insets. No separate container control
   is needed, which is also why the name `Border` is free.
2. **Insets are outside padding** (`ContentRect = ClientRect − Border.Insets(this) − Padding`) and
   `Measure` adds both; the existing controls shrink their padding to keep their pixels.
3. **Immutable borders**, reassigned rather than mutated.
4. **`Border.None`, not a nullable property**: it is the field's initial value and the way an
   application removes a frame.
5. **`Paint` and `Insets` take the owning control**, so state colours cost nothing per frame and a
   caption band can measure a font. `NarrowsClip`, `ClipTo` and `IsRectangular` do not, because they
   describe the kind of shape rather than its measurements.
6. **`Control.State` is a flags value the control computes.** Four of its five bits are `Control`'s own
   properties — `Enabled`, `IsFocused`, `IsMouseOver`, `IsPressed` — and the fifth, `Active`, exists
   because a popup being open is the only border-affecting state not on `Control`, declared separately
   on `ComboBox`, `SplitButton` and `DateTimePicker`. Each of those overrides `State`; a border reading
   properties directly would have to know and cast to all three. Three border properties on `Control`
   were also considered and rejected — the triple is short by `Disabled` and by the open list, and the
   extra two would have needed the null-or-sentinel state this design just removed.
7. **Borders paint inside the bounds.** External focus rings are a later, cross-cutting change.
8. **`GroupBox` is left alone.** A `GroupBoxBorder` may later delete the control entirely, since a
   group box is a panel with a distinctive frame.
9. **The old members are deleted** rather than kept as shims.

Settled after the first draft, which had a `protected virtual Border? DefaultBorder` and then a
`Border` subclass that deferred to it:

10. **No class-default property.** A border comes from the field, which a constructor assigns, and from
    nothing else. The draft's `DefaultBorder` could be silently outvoted by a base class's constructor
    assignment, and the sentinel that was proposed to fix that would have needed the owning control
    passed around the whole contract, only to resolve back to `_border ?? DefaultBorder`. Removing the
    second source of truth removes the disagreement.
11. **The declarative fact is a convention**: a control class declares its frame as a
    `private static readonly Border DefaultBorder` and assigns it in its constructor.

Chosen while writing this down, and worth disagreeing with now rather than later:

12. **The setter compares insets** before running a layout pass.
13. **`Padding`'s setter is left alone.** It repaints and does not lay out, which was already the
    case, and this change does not need it: the controls compensate their padding at construction, so
    the content lands where it did. Making it lay out would be a fix in its own right, and it is not a
    safe one — `Panel`'s constructor assigns `Padding` before it has created the scroll helper its
    `OnLayout` uses, so a layout pass there throws. `Font`'s setter already routes through
    `UpdateLayoutAfterAppearanceChange`, which is what a caption band measured from the font needs.

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
  insets and need a render each, not a subtraction. The 82-page comparison in section 8 is what
  closed this, and it only worked because the render is byte-stable on this machine.
- **`Opaque` is load-bearing for repaint cost.** A border that wrongly claims to be rectangular stops
  a control from repainting what is behind its rounded corners, which shows up as a smear rather than
  an exception. The rounded case is covered by the pixel suite; a third-party border is on its honour.
- **The declarative convention is unenforced.** Nothing stops a control class from assigning a fresh
  `new SimpleBorder(...)` per instance instead of a `static readonly` one, or from not assigning at
  all and drawing its frame by hand as four controls do today. The first allocates a border per
  control for no reason; the second reintroduces exactly the undeclared frame this change removes. The
  compiler cannot see either, so the sweep in section 6 has to cover all ten in one pass and review has
  to keep watching.
- **`ControlState` is a magnet.** Every state anyone ever wants to express will want a bit, and by the
  tenth one every border implementation is testing bits it does not care about. The two guards that
  make this cheap: four of the five bits are `Control`'s own properties, so most of the enumeration is a
  reformatting of what already exists; and a bit is added when a control in this library needs one —
  `Active` for the three popup owners — not when one might.
- **`Insets` can read transient state, and nothing stops it.** Passing the control means a border can
  look at `IsMouseOver` and make its inset depend on the pointer, which would turn every hover into a
  layout pass. `SimpleBorder` does not, and the rule — geometry may depend on the control's
  configuration, never on its pointer or focus — is a convention the doc states and review enforces.
- **A caption band costs a measurement per layout pass.** `GroupBoxBorder` measures its font to answer
  `Insets`, and `Insets` is asked from `Measure`, from `ContentRect` and from the `Border` setter. That
  is a text measurement per layout, not per paint, and it is the price of a border that knows what its
  control says.
- **Deleting the old members breaks every consumer.** Acceptable while the only feed is a pre-release
  test feed; it stops being acceptable the day something is published.
