# Borders

Chrome as an object instead of three numbers. A control's border today is `BorderWidth`, `BorderColor`
and `CornerRadius`: painted last, ignored by layout, and reimplemented by hand in six controls that
want a state-coloured edge. This replaces them with an abstract `Border` that answers the three
questions a frame has to answer — how much of the control it takes, how much it clips, and what it
paints — so that a border can be a value a control class supplies, an application replaces, or a
third party implements.

Status: **both phases implemented**. Phase 1 (`Border` on `Control`, `SimpleBorder`, `ControlState`)
merged as PR #78; phase 2 (a width per edge, a colour per edge, a radius per corner) is on
`feature/borders-phase2`. Section 8 records what was verified, including the places where the new
model deliberately differs from the old one; section 10 records what phase 2 settled and what is
deliberately still not here.

---

## 1. Where it belongs: core, and why not a package

`Kolvir` itself, beside `BackColor` and `Thickness`. Every control has the property, so nothing here
can be trimmed away and no package split helps; this is the same accepted seam cost as
`FEATURE-COST-POLICY.md` section 7 describes, not a feature.

The price, stated plainly:

- one reference field per `Control` (8 bytes), where the three members it replaces cost 12;
- three virtual dispatches per paint (`ClipTo`, `Paint`, and the `IsRectangular` read that feeds
  `Opaque`) and two per layout (the insets, once in `Measure` and once in `ContentRect`);
- an `SKPath` allocation only when a border actually narrows the clip, never on the plain path.

An abstract **class** rather than a value-type discriminant, because polymorphism is the whole point:
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

    /// <summary>The radius of each corner, which the control rounds its own fill by.</summary>
    public virtual CornerRadii Corners => default;

    /// <summary>Whether the frame draws the control's Text in a break in its top edge.</summary>
    public virtual bool ShowsControlText => false;

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

`Insets` needs it for a reason that only shows up in phase 2, and is worth recording now: a frame that
shows its control's caption takes the caption band out of the content area, and that band's height is the
line height of **the control's font** — plus, for a border that draws the text, the control's `Text`.
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
use. This also matches how `Thickness` and `BackColor` are used, and it is what makes a single shared
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
correct when it did (a frame that shows its control's caption recomputes its band from the font, so the
same screen position can want a layout when the font changed). It also means a `Border` must answer
`Insets` honestly for the control it is on — it may be called with the old instance while the new one is
being assigned.

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

**Two holes in that pattern, both found in phase 2 by pointing at a demo card and seeing nothing.**

The first is that `Control` does not repaint when the pointer arrives: `RaiseMouseEnter` deliberately
invalidates nothing, because a label or a table looks the same hovered and not, and repainting one
widens the next frame by its whole bounds. A control that draws its own hover look makes up for it by
overriding `OnMouseEnter` — but a border-coloured one has no override to add it in. So `Border` grew
`VariesByState`, `SimpleBorder` reports it when it has any state role, and `Control.RaiseMouseEnter` /
`RaiseMouseLeave` invalidate for a border that says so. A plain frame still costs nothing per hover.

The second is that hover belongs to the deepest control under the pointer, on every platform, so a card
full of labels is never hovered — only the labels are. A container that paints itself by hover has to
be able to say "the pointer anywhere inside me counts", so `Control.HoverIncludesChildren` makes the
host raise enter and leave along the ancestor chain as well, and `IsMouseOver` (and so
`ControlState.Hovered`) mean the whole control. It is false by default and must stay that way: an
`Expander` and a `TabControl` draw their own hot header and hot tab and clear them on `OnMouseLeave`,
so a pointer moving from the header into the content has to keep raising that leave.

## 6. `SimpleBorder`, and migrating today's borders

`SimpleBorder` is the shape of everything that exists now, plus state colours, which is what removes the
hand-written resolution from four paint methods:

```csharp
public sealed class SimpleBorder : Border
{
    public static SimpleBorder Thin { get; }      // 1px, Theme.Border, square corners
    public static SimpleBorder Rounded(float r);  // no stroke: rounds the fill and clips, takes nothing
    public Thickness Widths { get; }              // one per edge; 0 on both is "no stroke", not "none"
    public override CornerRadii Corners { get; }  // one per corner
    public override Thickness Insets(Control owner) => _insets ?? _widths;

    public SimpleBorder(float width, float cornerRadius = 0f);
    public SimpleBorder(Thickness widths, float cornerRadius = 0f);
    public SimpleBorder(Thickness widths, CornerRadii corners);
    public SimpleBorder WithWidth(float width);
    public SimpleBorder WithWidths(Thickness widths);
    public SimpleBorder WithCornerRadius(float radius);
    public SimpleBorder WithCorners(CornerRadii corners);
    public SimpleBorder WithInsets(Thickness insets);          // take more room than the stroke is thick
    public SimpleBorder WithControlText(bool show = true);     // draw the owner's Text in the top edge
    public SimpleBorder WithColor(Union<SKColor, ThemeColor> color);              // every edge
    public SimpleBorder WithColor(BorderSide side, Union<SKColor, ThemeColor> color);
    public SimpleBorder WithHoverColor(ThemeColor role);       // ControlState.Hovered
    public SimpleBorder WithFocusColor(ThemeColor role);       // ControlState.Focused
    public SimpleBorder WithActiveColor(ThemeColor role);      // ControlState.Active
    public SimpleBorder WithDisabledColor(ThemeColor role);    // ControlState.Disabled
    public SimpleBorder WithPressedColor(ThemeColor role);     // ControlState.Pressed

    public SKColor ColorFor(BorderSide side, ControlState state);
    public override bool NarrowsClip => !Corners.IsNone;
    public override bool IsRectangular => Corners.IsNone;
    public override bool ShowsControlText => _showsControlText;
    public override void ClipTo(SKCanvas canvas, SKRect bounds);   // round rect, antialiased
    public override void Paint(SKCanvas canvas, SKRect bounds, Control owner);
}
```

`CornerRadii` (section 10) is a `readonly struct` of four radii in the order top-left, top-right,
bottom-right, bottom-left, with an implicit conversion from `float` so `SimpleBorder.Thin.WithCornerRadius(6f)`
still reads as one number. `Border.CornerRadius` survives as `Corners.Maximum` for the callers that
genuinely need one number — a focus ring, `Painter.Fill` — and the per-corner overloads of those take
`Corners`.

Colour is a `Union<SKColor, ThemeColor>` - the same pair `Control.BackColor` accepts, without the
inherit arm, because a border has no parent to inherit from. A role is resolved while painting, so
a palette swap recolours live controls with no rebuild.

`Paint` resolves the state in one documented order — **disabled > pressed > active > focused >
hovered > resting** — which is what the built-in controls already implement by hand, differing only in
that `ComboBox` also treats its open list as focused. That difference is now a bit rather than a
special case: `ComboBox` overrides `State` to add `Active` and gives its frame `WithActiveColor` and
`WithFocusColor` the accent, and its paint method stops choosing colours at all. `VirtualList` sets no
hover colour, so it keeps its pixels; `TabControl`, `Button` and `DateTimePicker` set none, and are
unchanged.

This is what lets `TextBox` hold a `static readonly SimpleBorder _focused = SimpleBorder.Thin
.WithColor(ThemeColor.Border).WithFocusColor(ThemeColor.Accent).WithHoverColor(ThemeColor
.SplitterHover).WithDisabledColor(ThemeColor.DisabledBackground)` and stop overriding
`OnPaintBackground` to draw its own frame.

Painting is path-based, as section 10 describes: four edges in one colour are filled as a single
even-odd ring, so the mitres between them cannot show a seam; four edges in different colours are four
mitred pieces. Phase 1 stroked the ring with `Painter.DrawBorder`; the call sites did not change.

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
| `TabControl` | 1px | 0 | **shape only, no stroke**: the strip and the pages share the frame's edges, so reserving room would move both. It still draws its frame in the background pass; unifying it means a border that owns the strip, which is not obviously a win (section 10) |
| `GroupBox` | 1px | 11, 0, 11, 11 | `WithControlText()`: the frame draws the caption in a break in its top edge and takes the band out of the content, so the control is now just a `Panel` with that border |
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

**Phase 2, the same pages, plus one.** No page is byte-identical any more, and none of them should be:
the frame stopped being a stroke and became a filled ring, so the antialiasing of every corner arc
moves by a fraction of a pixel. The pixel comparison is the useful one:

| Pages | Pixels differing | Largest channel delta | Where |
| --- | --- | --- | --- |
| 79 of 82 | 46 – 864 of 800 000 | 8 – 12 | the corner arcs of each frame, and nothing else |
| `wrap-panel` | 752 | 48 | many small frames, so many corners |
| `animated-gif` | 240 | 200 | the page whose two renders of the *same* build differ (see above) |

`dialogs`, `group-boxes` and `split-container` differ on 46 pixels, the least; the mask of the whole
page is corner arcs. Masking the differences rather than counting them is what makes this reviewable:
a one-pixel layout shift would show as a straight line, and there is not one on any page.

A new demo page, `borders`, shows the feature itself: an accent left edge on a four-pixel frame, a card
rounded at the top with square feet, a frame with a different colour on its top and bottom edges, an
eight-pixel band across the top, a frame that answers the pointer through `WithHoverColor`, and one that
draws its control's text. It renders in both themes and is part of the baseline, which is therefore 84
pages: 61 light, 17 dark and 6 zoomed.

**The caption, re-rendered.** Moving `GroupBox` onto the border moved nothing: every page but
`group-boxes` and `borders` came out byte-identical. On `group-boxes` the difference is the frame
outline - 5 418 pixels in light, compared against the whole outline of three boxes, largest channel
delta 18 - because the old stroke sat on a half-pixel offset and left two half-covered pixels where the
ring leaves one. Content positions, captions and the disabled caption's colour are unchanged, which the
mask shows by marking the outline and nothing else.

**The suite.** `../tests/core/Kolvir.Tests/Core/BorderTests.cs` covers the contract: a control starts on `Border.None`
and takes no room; a one-pixel frame comes out of the content and back into the measurement, so the
frame makes a control larger rather than shrinking its contents; a stroke-less shape reserves nothing
but narrows the clip and stops the control reporting itself opaque; the state colours are taken in the
documented order and a state colour that was never given falls back to the resting colour; `State`
follows the control's own flags and an override keeps the base's bits; a frame paints its own pixel
inside the bounds and paints over what a child drew; and a frame that takes more room lays out again.

Phase 2 added ten more: an edge takes its own resting colour and a state colour still covers every
edge; a width per side reserves exactly that much and the content lands inside it; an asymmetric frame
paints the pixels it reserved and no others; a mitred corner has no gap in it; the four radii are held
one by one and `CornerRadius` reports the largest; one square corner is still a corner; a radius too
large for the control is given up rather than wrapped past it; and every `With...` returns a new border,
leaving the shared `Thin` untouched.

Then five for the two hover holes: a state-coloured border repaints when the pointer arrives and a
plain one does not (both assert `UiHost.IsUpdatePending` across a full frame, so the first would fail on
a missing invalidation rather than on a stale pixel); a pointer over a caption lights the card's frame;
and `HoverIncludesChildren` is off by default, reaches every ancestor that asked for it, and still
leaves an ancestor that did not.

And three for the caption: a frame that shows the text reserves its band at the top and the content
starts below it; the top edge is cut away behind the caption and painted where the caption does not
reach, while the other three edges stay where they were; and a frame with no text is a plain frame, with
the band appearing when the text does. The rest of the suite — 936 tests — is unchanged and green.

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
8. **`GroupBox` is left alone as an API**, but not as an implementation. The caption became
   `SimpleBorder.WithControlText()` rather than a `GroupBoxBorder` type, so a group box is a `Panel`
   with a flag on its frame, and `GroupBox` itself is now four lines of constructor.
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

Settled in phase 2:

14. **Per-edge widths are a `Thickness`, per-corner radii are a `CornerRadii`.** Both are already how the
    library spells four numbers, and neither needs a type of its own beyond the radii.
15. **State colours stay uniform across the edges; the resting colour is per edge.** A frame whose edges
    differ only while hovered is not a shape anything in this library needs, and per-edge state colours
    would be four arrays instead of five fields.
16. **The filled ring replaces the stroke for a control's own frame.** Four differently coloured edges
    and four different radii cannot be one stroked rectangle, and a ring needs no mitre arithmetic in
    the common case. The cost is a fraction of a pixel of antialiasing on every corner arc (section 8).
17. **A dashed frame is not a `SimpleBorder`.** A dash belongs to a stroke; a frame with a width per
    edge has to be filled, so a dashed border is a `Border` of its own that strokes a path.
18. **A border that follows the state says so**, through `Border.VariesByState`, and `Control`
    repaints on hover for it rather than every control repainting on every hover.
19. **Container hover is opt-in** through `Control.HoverIncludesChildren`, because the platform rule
    (the deepest control is the hovered one) is what transient chrome - a hot tab, a hot expander
    header - already relies on to clear itself.

## 10. Phase 2: per edge, per corner

Landed. The phase-1 shape held: `Insets` was already a `Thickness` so layout needed no change, and
painting became path-based inside `SimpleBorder` alone.

- **`CornerRadii`** — a `readonly struct` of the four radii in the order top-left, top-right,
  bottom-right, bottom-left, with `(float)` and `(tl, tr, br, bl)` constructors, `None`, `IsNone`,
  `IsUniform`, `Maximum`, `+`, `-`, `*` and an implicit conversion from `float`. `Border.CornerRadius`
  became `Corners.Maximum` rather than disappearing, so the callers that genuinely need one number - a
  focus ring, `Painter.Fill`, `Image`'s placeholder - did not move.
- **`BorderSide`** — `Top`, `Right`, `Bottom`, `Left`, the index order the per-edge colour array uses.
- **`SimpleBorder`** — `Widths` is a `Thickness`, `Corners` is a `CornerRadii`, and each edge has its
  own resting colour (`WithColor(BorderSide, Union<SKColor, ThemeColor>)`).
  State colours stay *uniform across the edges*: nothing in this library needs a frame whose edges
  differ only while hovered, and a per-edge state colour is four arrays instead of five fields.
  `WithControlText()` adds the caption (section 10).
- **`RoundedRect`** — one internal helper holding the geometry the four renderers share (the outline
  path, the corner ovals, the arc angles, the clamp that stops two arcs overlapping on a small
  control), so a border's ring, a control's fill, a stroked frame and a focus ring all round the same
  way. `Painter` gained `CornerRadii` overloads of `Fill`, `DrawBorder` and `DrawFocusRing`, and the
  control-bound call sites in `Control`, `Button`, `ComboBox`, `TextBox`, `VirtualList`, `Image`,
  `Overlay`, `TabControl`, `DateTimePicker`, `Toast`, `ToggleControl` and `TrackBar` now pass
  `Border.Corners` instead of `Border.CornerRadius`.

**How a frame with four widths is painted.** Four edges in one colour are a single even-odd ring -
outer outline, inner outline - so the corners cannot show a seam. Four edges in different colours are
four mitred pieces: the ring is cut at the 45° midpoint of each corner, and each piece runs along the
outer outline, round the outer arc to the diagonal, across to the inner outline and back. The inner
radius at a corner is the outer radius less the thicker of the two edges meeting there, clamped against
the inner rectangle, so the inner outline stays parallel to the outer one.

That replaced the stroked `Painter.DrawBorder` for a control's own frame, which moves the antialiasing
of every corner arc by a fraction of a pixel. Section 8 records what that cost.

**Deliberately not here.** A dashed frame is a `Border` of its own that strokes a path: a dash belongs
to a stroke, and a frame with a width per edge has to be filled to keep its mitres, so the two cannot be
one object. That is the one piece of the original plan still missing.

`TabControl` is the one control still drawing its own frame, and after doing `GroupBox` the case for
unifying it looks weak rather than obvious. What a tab control draws is not a frame: it is a frame plus a
strip of tabs plus a rule across the bottom of the strip, with the selected tab's bottom edge broken out
of that rule, and the strip's height depends on the tabs, which the control owns. A `TabControlBorder`
would have to ask the control for the strip height, and the control would still draw the tabs - so the
drawing code would move without the control learning less about its own chrome.

**The caption, and why it is a flag rather than a type.** `GroupBox` drew its own frame: a caption band,
a break cut out of the top edge, and the content pushed below it by a `ContentBounds` override. All of
that is a property of a frame rather than of a container, so it became `SimpleBorder.WithControlText()`:
the border answers `Insets` with the band (so `ContentRect`, `Measure`, the clip and a panel's children
all move together), drops the top edge by half the band, cuts the edge away behind the text, and draws
`owner.Text` in the gap. `GroupBox` is now a `Panel` whose constructor assigns that border and an
11-pixel gutter, with no overrides at all.

A separate `GroupBoxBorder` type was the alternative, and it would have meant either a second copy of the
per-edge, per-corner and state-colour machinery or an unsealed `SimpleBorder` with a virtual paint hook -
both more code than one field and one `if`, for a shape that is a `SimpleBorder` in everything else.

The visible cost is that the group box's frame became *crisper*: it used to stroke a half-pixel offset,
which left a two-pixel antialiased smear, and a filled ring at a whole pixel is a single pixel of
`Theme.Border`. Section 8's masks show it as an outline difference on the three group boxes in the demo
and nothing else.

**A caption needs neither a `MeasureContent` override nor a `ContentBounds` one.** `GroupBox` had both,
and the first thing to check was that a border answering `Insets` covers them: it does, because
`Control.Measure` adds the insets and `Panel.FramedContentBounds` deflates by them, so `Measure` grew the
band and the children moved down with no code in the control at all.

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
- **A caption band costs a measurement per layout pass.** A frame that shows the control's text measures
  it to answer `Insets`, and `Insets` is asked from `Measure`, from `ContentRect` and from the `Border`
  setter. That is a text measurement per layout, not per paint, and it is the price of a border that
  knows what its control says.
- **Deleting the old members breaks every consumer.** Acceptable while the only feed is a pre-release
  test feed; it stops being acceptable the day something is published.
