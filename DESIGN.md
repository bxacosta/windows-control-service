---
version: alpha
name: Windows Control Service
description: >
  A dark control panel for a service that enforces policy on the machine it runs on. The
  interface is meant to look native to Windows rather than to the web: Microsoft's own type,
  neutral instrument greys on near-black, and colour reserved for state. Chrome is neutral --
  a primary button and an active tab say "pressable" and "you are here", which is not a
  meaning a hue can improve. Every hue on screen carries information: blue that something is
  active, green that policy is enforced, amber that it is waiting, red that it was denied,
  violet that access came from somewhere else.

colors:
  # Surfaces, from the page up. Each step is a level of elevation and nothing else.
  ground: "#030303"
  inset: "#070707"
  surface: "#0F0F0F"
  raised: "#161616"
  overlay: "#1F1F1F"
  line: "#272727"
  line-soft: "#1A1A1A"

  # Text.
  text: "#EDEDED"
  text-secondary: "#A6A6A6"
  # Raised from #7D7D7D: as placeholder text on a #161616 field it measured 4.40:1, under AA.
  text-muted: "#858585"

  # Control: neutral on purpose. This is the primary interactive colour.
  primary: "#E5E5E5"
  primary-hover: "#FAFAFA"
  primary-press: "#C9C9C9"
  on-primary: "#0A0A0A"
  # What sits on a filled `denied` button. Near-black with the red left in it, so the button
  # reads as one object rather than as black type dropped on a red field.
  on-denied: "#1A0406"

  # State. These five are the only colours allowed to mean something.
  signal: "#4A9EEB"
  enforced: "#3ECF8E"
  waiting: "#E3A93C"
  denied: "#F0555B"
  remote: "#9B87F5"

  # State at 11-16% over the surface, flattened. Backgrounds for pills and section tints.
  signal-wash: "#182632"
  enforced-wash: "#15281F"
  waiting-wash: "#2B2315"
  denied-wash: "#2C1819"
  remote-wash: "#23202F"

  # Not a surface: the colour every shadow and the modal scrim is mixed from, so that no alpha
  # black is written by hand anywhere. It is darker than `ground` on purpose -- a shadow cast by
  # a card has to be darker than the page the card sits on, or it is not a shadow.
  shadow: "#000000"

typography:
  display:
    fontFamily: "Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 20px
    fontWeight: 600
    letterSpacing: -0.015em
  title:
    fontFamily: "Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 13.5px
    fontWeight: 600
    letterSpacing: -0.005em
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 13.5px
    fontWeight: 400
    lineHeight: 1.45
  meta:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 12px
    fontWeight: 400
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 11px
    fontWeight: 600
    letterSpacing: 0.01em
  mono:
    fontFamily: "Cascadia Mono, Cascadia Code, Consolas, ui-monospace, monospace"
    fontSize: 12px
    letterSpacing: -0.01em

rounded:
  sm: 6px
  md: 8px
  lg: 12px
  full: 999px

spacing:
  1: 4px
  2: 6px
  3: 8px
  4: 12px
  5: 14px
  6: 16px
  7: 20px

components:
  # The measurements the layout is built from. Modelled as components because the schema has no
  # place for a bare dimension, and a number that lives only in a stylesheet is a number nobody
  # can check against this document.
  page:
    width: 900px
    padding: "0 20px"

  top-bar:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.text}"
    height: 56px
    padding: "0 20px"

  scrim:
    backgroundColor: "{colors.shadow}"

  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.lg}"

  card-header:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    typography: "{typography.title}"
    height: 48px
    padding: "8px 16px"

  # The tinted band at the top of a card. It is not a header: it carries the section's identity
  # and, on Applications, the state of the policy.
  strip:
    textColor: "{colors.text}"
    typography: "{typography.title}"
    height: 40px
    padding: "0 16px"

  card-footer:
    backgroundColor: "{colors.inset}"
    textColor: "{colors.text-muted}"
    typography: "{typography.meta}"
    padding: "10px 16px"

  row:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    padding: "12px 16px"

  row-hover:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"

  row-detail:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-muted}"
    typography: "{typography.mono}"

  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.md}"
    height: 32px
    padding: "0 12px"

  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
    textColor: "{colors.on-primary}"

  button-primary-press:
    backgroundColor: "{colors.primary-press}"
    textColor: "{colors.on-primary}"

  # A hairline is a 1px surface. Modelled as a component because the schema has no border
  # property, and a token no component references is a token nobody can check.
  divider:
    backgroundColor: "{colors.line-soft}"
    height: 1px

  border:
    backgroundColor: "{colors.line}"
    height: 1px

  button-secondary:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    rounded: "{rounded.md}"
    height: 32px

  button-ghost:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
    rounded: "{rounded.md}"
    height: 32px

  button-danger:
    backgroundColor: "{colors.denied}"
    textColor: "{colors.on-denied}"
    rounded: "{rounded.md}"
    height: 32px

  field:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    rounded: "{rounded.md}"
    height: 34px
    padding: "0 11px"

  field-placeholder:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text-muted}"

  # The narrow field that sits beside one taking the remaining width -- the optional name in the
  # block form. Wide enough for a name, narrow enough that the path keeps the room.
  field-compact:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    rounded: "{rounded.md}"
    height: 34px
    width: 190px

  # A form is rows, so its label is the left half of one. Fixed width, because ragged labels down
  # a column is the thing a form layout exists to prevent.
  form-label:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.body}"
    width: 172px

  # What a field says about itself, at its trailing edge: the counter against the minimum, and
  # Match / No match on a repeated password.
  field-note:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text-muted}"
    typography: "{typography.label}"

  switch-off:
    backgroundColor: "{colors.inset}"
    textColor: "{colors.text-secondary}"
    rounded: "{rounded.full}"
    width: 38px
    height: 22px

  switch-on:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.full}"
    width: 38px
    height: 22px

  # Both thumbs are geometrically 16. The one on a light track is drawn a point larger because a
  # dark shape on a light field reads smaller than the reverse. See Do's and Don'ts.
  switch-thumb:
    backgroundColor: "{colors.text-secondary}"
    rounded: "{rounded.full}"
    size: 16px

  switch-thumb-on:
    backgroundColor: "{colors.on-primary}"
    rounded: "{rounded.full}"
    size: 17px

  tab:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.text-muted}"
    typography: "{typography.meta}"
    height: 44px

  tab-selected:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.text}"

  # The 2px rule under the active tab. A component, not a border, for the same reason `divider`
  # is one: the schema has no border property.
  tab-underline:
    backgroundColor: "{colors.primary}"
    height: 2px

  tab-badge:
    backgroundColor: "{colors.overlay}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    height: 18px

  segmented-track:
    backgroundColor: "{colors.inset}"
    textColor: "{colors.text-muted}"
    rounded: "{rounded.md}"
    padding: "3px"

  segmented-selected:
    backgroundColor: "{colors.overlay}"
    textColor: "{colors.text}"
    rounded: "{rounded.sm}"
    height: 24px

  chip:
    backgroundColor: "{colors.overlay}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.mono}"
    rounded: "{rounded.sm}"
    height: 19px

  pill-signal:
    backgroundColor: "{colors.signal-wash}"
    textColor: "{colors.signal}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    height: 20px

  pill-enforced:
    backgroundColor: "{colors.enforced-wash}"
    textColor: "{colors.enforced}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    height: 20px

  pill-waiting:
    backgroundColor: "{colors.waiting-wash}"
    textColor: "{colors.waiting}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    height: 20px

  pill-denied:
    backgroundColor: "{colors.denied-wash}"
    textColor: "{colors.denied}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    height: 20px

  pill-remote:
    backgroundColor: "{colors.remote-wash}"
    textColor: "{colors.remote}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    height: 20px

  modal:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.lg}"
    width: 540px

  # Tall enough to be worth scrolling, short enough that the dialog never outgrows a laptop.
  modal-list:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    height: 330px

  icon:
    size: 16px

  toast:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    padding: "10px 12px"
    width: 320px
---

## Overview

This interface controls one machine, from that machine. It should read as an instrument panel
for Windows, not as a web application that happens to run on it. Two decisions follow from that
and everything else follows from them.

**The type is Microsoft's.** Segoe UI Variable for text, Cascadia Mono for paths, addresses and
attribute values. Both are already installed on any Windows 11 machine, which is also the only
way this project can have considered typography at all: there is no build step, the assets are
served from `wwwroot`, and a test forbids reaching outside the machine, so a web font would have
to be inlined as base64 or not exist.

**Colour means state, and nothing else means colour.** The primary button, the active tab and
the focus ring are neutral, because "pressable" and "you are here" are not meanings a hue can
sharpen. Spending a colour on chrome puts it in competition with the five that carry
information. On a near-black ground, near-white is the strongest thing available, and it belongs
to whatever executes an action.

The interface is dense on purpose. It has no explanatory paragraphs, no lead-ins under headings,
and no captions describing what a control does. Where a caveat genuinely has to be shown -- that
there is no password recovery -- it is one line of `meta` in a card footer, not prose.

## Colors

Surfaces are a single neutral ramp with no hue bias, and each step means one level of elevation:
`ground` for the page, `surface` for cards, `raised` for inputs and hovered rows, `overlay` for
chips and the selected segment, `inset` for anything recessed. Nothing is coloured because it
looked better; a surface is picked by how far it should sit from the page.

`primary` is the neutral control colour. It fills the primary button with `on-primary` on top,
draws the focus ring, underlines the active tab, and fills a switch that is on.

The five state colours are the only ones that mean anything:

| Token | Means | Where it appears |
|---|---|---|
| `signal` | this section has something active | the dot on a tab, that section's header tint, the USB `Blocked` pill |
| `enforced` | the policy is in force | the policy strip, the `Enforced` pill, an inbound connection |
| `waiting` | rules exist but are not applied | the `Not enforced` pill |
| `denied` | a write was refused, or is about to be undone | error toasts, the danger button, a row awaiting confirmation |
| `remote` | access arrived over RDP | the `RDP` pill, the Activity header tint |

Each section header carries its own tint at 10-11%, fading out by 48% of the width: green for
Applications, `signal` for Devices, `remote` for Activity, `waiting` for Settings. **This is the
only place a section is allowed to identify itself by hue.** Anywhere else, a colour is a claim
about the machine.

## Typography

Six roles, and no size outside them. `display` for the wordmark, `title` for card headings,
`body` for row titles and running text, `meta` for anything secondary, `label` for pills and
uppercase eyebrows, `mono` for every value that came from the machine -- executable paths, IP
addresses, match attributes, version strings.

The `mono` rule is not decoration. A path is a machine value that a person has to compare
character by character, and a proportional face makes `l`, `I` and `1` the same shape.

Every place digits line up in a column -- counts, durations, pagination, the character counter --
sets `font-variant-numeric: tabular-nums`, so numbers do not shift as they change.

## Layout

One column, 900px maximum, centred. A sticky top bar with the wordmark, the service health and
sign-out; a tab rail beneath it; cards below.

**The interface is built from rows.** Something on the left, its control on the right, a hairline
between. Lists are rows, and so are forms -- a label on the left, the field taking whatever is
left. This is why the password form fills its card: a field that stops two thirds of the way
across leaves a hole nothing explains.

**Every card header is the same height.** 48px minimum with 8px padding, so the padding gives way
instead of the header growing. The tallest thing a header ever holds is the segmented control at
32px, and the row is sized for that. A header that is taller than its neighbours because of what
happens to be inside it is a layout accident.

Card footers carry the summary on the left and the action on the right: the pager in Activity,
the caveat and `Change password` in Settings.

**One breakpoint, at 720px.** Above it the block form is three columns -- path, name, button.
Below it they stack, the button goes full width, and a form row puts its label above its field
instead of beside it. Nothing else reflows: a column that is already one column does not need a
second layout.

**The sign-in and first-run screens are one card, 380px wide, centred in the viewport**, with no
top bar and no tabs: there is nothing to navigate to until there is a session. It is the same
card, the same field and the same primary button as everything else, which is the point -- a
sign-in screen that looks like a different product is a sign-in screen that looks like a
phishing page.

## Elevation & Depth

Depth is real here, not ornamental, and it runs in both directions.

Cards sit **above** the page: `surface` on `ground`, a `line` border, and a soft shadow. The gap
between `#030303` and `#0F0F0F` is what makes a card read as an object rather than as a region.

Recessed controls sit **below** their surface: the segmented track and a switch that is off both
use `inset` with `inset 0 1px 2px rgba(0,0,0,.6)`. A control that is not doing anything sits
below the surface it is on, and rises when it is. A switch that is on drops the inset shadow;
the selected segment gains a border and a drop shadow.

Modals use a heavier shadow and a blurred scrim, and rise 8px on open over 160ms.

Every shadow is mixed from `shadow`, so no alpha black is written by hand:

| Where | Value |
|---|---|
| Card | `0 1px 2px shadow/50%, 0 8px 24px shadow/35%` |
| Modal | `0 24px 64px shadow/65%` |
| Scrim | `shadow/60%` with a 2px backdrop blur |
| Recessed control | `inset 0 1px 2px shadow/60%` |
| Selected segment | `0 1px 2px shadow/45%` |
| Toast | `0 8px 24px shadow/50%` |

**Motion is short and only ever confirms an action.** 120ms for anything that changes on hover or
press, 160ms for a modal or a toast arriving. Nothing animates on the way out except a toast,
which fades. Everything here is inside `prefers-reduced-motion`, and under it only the opacity
changes remain.

## Shapes

`sm` for chips, badges and the selected segment; `md` for buttons, fields and toasts; `lg` for
cards and modals; `full` for pills and switches. Nothing else. A radius that is not on this list
is a mistake, not a variation.

## Components

**Buttons** come in four weights and the weight is the meaning: `primary` filled neutral for the
one action that executes; `secondary` with a border for anything else; `ghost` for actions that
should not compete; `danger` filled red, which appears only inside a confirmation.

**Destructive actions never fire on the first click, and never open a dialog.** The row itself
becomes the confirmation: it takes a `denied` wash, the secondary detail gives way to the
question, and the actions become `Cancel` and `Remove`, with focus moving to `Remove`. Opening
one closes any other. The name of what is about to change stays exactly where it already was,
which no dialog can manage.

**Switches** are 38×22 with a 16px thumb. Off is a recess with a grey thumb; on is a `primary`
track with a dark thumb, and the two ends of the neutral ramp are the widest gap this palette
has. The thumb of a switch that is on is scaled to 17px: both are geometrically 16, but a dark
shape on a light field reads smaller than a light shape on a dark one. **This is an optical
correction and it must not be "fixed" by making the numbers equal.**

**Pills** carry no leading dot. The colour already says what the dot would.

**Fields** show their state inside themselves where it is useful -- a character counter against
the minimum, `Match` / `No match` on a repeated password -- in `label` size at the trailing edge.
Validation while typing, not after submitting.

**Toasts** stack at the bottom right, dismiss themselves after 4.5s and can be dismissed by hand.
They never occupy layout: a notice that pushes the page down moves the control the user was
about to click.

**Focus** is a 2px `primary` ring at 2px offset, drawn only for `:focus-visible`, so a mouse
click never leaves a ring behind. It is the same ring on every control, including rows that can
be reached by keyboard. Nothing removes an outline without putting one back.

**A disabled control drops to 40% opacity and stops taking pointer events**, and that is the
whole treatment: no separate grey, no change of shape. The one thing that must not happen is a
disabled control that still looks pressable, because the pager spends most of its life with one
of its two ends disabled.

**An empty list says what is not there, in `meta`, on `text-muted`, in the space the rows would
have taken.** It never borrows the layout of a row, and it never apologises: "No applications are
blocked" is the whole message.

**Icons** are Lucide, at 16px with a 1.7 stroke, inlined as a single SVG sprite. The two carets
in Activity are the exception: solid triangles pointing right for a connection and left for a
disconnection, 8px on the base and 10px tall, which is a direction and reads better as a shape
than as an arrow. No icon font,
no CDN, nothing fetched at run time.

## Do's and Don'ts

**Do** pick a surface by how far it should sit from the page.
**Don't** invent a grey. If none of the six fits, the layout is wrong, not the palette.

**Do** let colour make a claim about the machine.
**Don't** colour a control because it is important. Weight and position do that.

**Do** show a machine value in `mono`.
**Don't** show it in prose, and don't paraphrase it.

**Do** put a genuinely necessary caveat in a card footer as `meta`.
**Don't** write a paragraph explaining what a control does, why a design decision was taken, or
what will happen if the user clicks. If the interface needs a paragraph, the interface is wrong.

**Do** keep every card header at 48px.
**Don't** let its contents decide its height.

**Do** confirm a destructive action in the row that owns it.
**Don't** open a dialog, and don't use `window.confirm`.

**Do** keep the optical correction on the switch thumb.
**Don't** normalise it away.
