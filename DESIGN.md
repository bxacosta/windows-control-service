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
  sunken: "#0D0D0D"
  surface: "#0F0F0F"
  raised: "#161616"
  overlay: "#1F1F1F"

  # The top bar over the page it is stuck to: `ground` at 86%, so what scrolls under it is felt
  # rather than seen. It is the only translucent surface here.
  ground-veil: "rgba(3, 3, 3, 0.86)"

  # Their own ramp, not part of the one above. Which step to use: see Colors.
  line-soft: "#1A1A1A"
  line: "#272727"
  line-selected: "#303030"
  line-strong: "#343434"
  line-focus: "#4A4A4A"

  # Neutral light at 9%, the only thing on this palette that brightens a surface instead of
  # darkening it: the halo around a focused field.
  halo: "rgba(255, 255, 255, 0.09)"
  # The same 9% flattened over `ground`, for the one place it sits behind text: the badge on the
  # tab you are already on. Flattened because text on a translucent surface cannot be checked for
  # contrast against it.
  halo-flat: "#1A1A1A"

  # Text.
  text: "#EDEDED"
  text-secondary: "#A6A6A6"
  # Do not darken it: as placeholder text on a #161616 field, #7D7D7D measures 4.40:1, under AA.
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

  # The two paints of the sign-in screen's lattice, and the only greys here that are on neither
  # ramp: they are not a surface and not an edge, they are light on the page. See Layout.
  lattice: "rgba(255, 255, 255, 0.032)"
  lattice-lift: "rgba(255, 255, 255, 0.026)"

typography:
  # The wordmark, and nothing else. Small on purpose: see Typography.
  display:
    fontFamily: "Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 14px
    fontWeight: 600
    letterSpacing: -0.01em
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
  # The name of a row, which has to win over the value under it without becoming a heading.
  body-strong:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 13.5px
    fontWeight: 500
    lineHeight: 1.45

  # What you press. See Typography for why a control is 500.
  control:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 13px
    fontWeight: 500

  # The one control that executes rather than offers, so it carries the weight of a title.
  control-strong:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 13px
    fontWeight: 600

  # Text a person typed, and the label naming it. Never `control`: see Typography.
  input:
    fontFamily: "Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: 13px
    fontWeight: 400

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

  # The same face one step down, for a machine value inside a chip. A chip is 19px tall and the
  # value in it is read at a glance, not compared character by character like a path.
  mono-compact:
    fontFamily: "Cascadia Mono, Cascadia Code, Consolas, ui-monospace, monospace"
    fontSize: 11px
    letterSpacing: -0.01em

rounded:
  xs: 5px
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
  # Dimensions, hairlines and gaps are modelled as components because the schema has no place for
  # a bare number, and a number that lives only in a stylesheet is a number nobody can check
  # against this document. Where a comment below would run longer than a line, it points at the
  # section that carries the reasoning instead of repeating it.

  # The measurements the layout is built from.
  page:
    width: 900px
    padding: "0 20px"

  top-bar:
    backgroundColor: "{colors.ground-veil}"
    textColor: "{colors.text}"
    height: 56px
    padding: "0 20px"

  # The product's name. Its mark keeps a hue where nothing else does: see Colors.
  wordmark:
    backgroundColor: "{colors.ground-veil}"
    textColor: "{colors.text}"
    typography: "{typography.display}"

  wordmark-mark:
    backgroundColor: "{colors.ground-veil}"
    textColor: "{colors.signal}"
    size: 18px

  # Anchored near the top rather than centred: see Elevation & Depth.
  scrim:
    backgroundColor: "{colors.shadow}"
    padding: "80px 20px 20px"

  # The sign-in screen. Not a card: see Layout for why it stopped being one.
  gate:
    backgroundColor: "{colors.ground}"
    width: 380px

  # The lattice behind it. The pitch is the layout's own 56px, and the mask is what keeps it from
  # reaching an edge -- a grid that runs to the corners is a background, not a light.
  gate-lattice:
    backgroundColor: "{colors.lattice}"
    height: 1px
    pitch: 56px

  gate-lattice-lift:
    backgroundColor: "{colors.lattice-lift}"

  # Larger than the mark in the bar, and the one place it is. In the bar the product's name is
  # the thing nobody needs to read; here it is the only thing identifying what is asking.
  gate-mark:
    size: 46px

  # The gap above the form and below it, both. The two things it separates -- who is asking, and
  # what is answering -- are further apart than any two rows of a form.
  gate-gap:
    size: 34px

  # The only control on an otherwise empty viewport, so it is taller than a field inside a card
  # and takes the card's radius rather than a button's.
  gate-field:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.input}"
    rounded: "{rounded.lg}"
    height: 44px
    padding: "0 13px"

  # The primary button, shrunk to the square that fits inside that field with 6px of air. Same
  # fill and same shimmer: it is the same control, not a new one.
  gate-submit:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.sm}"
    size: 32px

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

  # What a header says at its trailing edge: a fact about the card that is not its title.
  card-meta:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-muted}"
    typography: "{typography.meta}"

  # The band above the blocked applications, and the only one. A strip is not a header: see
  # Layout. Set in text rather than display, with only the state itself at 600 -- "Policy
  # enforced" is the claim, "2 rules" is the detail, and bolding both says they weigh the same.
  strip:
    textColor: "{colors.text}"
    typography: "{typography.input}"
    height: 40px
    padding: "0 16px"

  card-footer:
    backgroundColor: "{colors.sunken}"
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

  # The name of the row. Heavier than the value under it, lighter than a card title.
  row-title:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    typography: "{typography.body-strong}"

  row-detail:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-muted}"
    typography: "{typography.mono}"

  # An access event is one line of text and a time, where an application is two lines and two
  # controls. Same row, less air.
  row-compact:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    padding: "10px 16px"

  # The question a row asks before a removal. Grey, not red: the wash behind it and the button
  # under it are already red, and a third red is not more certain, only louder.
  row-confirm:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.meta}"

  # The guide icon at the head of a row sits in a fixed slot, so titles line up down a list
  # whether or not their row has one.
  row-icon:
    size: 22px

  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    typography: "{typography.control-strong}"
    rounded: "{rounded.md}"
    height: 32px
    padding: "0 12px"

  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
    textColor: "{colors.on-primary}"

  button-primary-press:
    backgroundColor: "{colors.primary-press}"
    textColor: "{colors.on-primary}"

  # A hairline is a 1px surface.
  divider:
    backgroundColor: "{colors.line-soft}"
    height: 1px

  border:
    backgroundColor: "{colors.line}"
    height: 1px

  button-secondary:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.control}"
    rounded: "{rounded.md}"
    height: 32px

  button-ghost:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.control}"
    rounded: "{rounded.md}"
    height: 32px

  # A ghost button that removes something answers the pointer in the colour of what it does.
  button-ghost-destructive-hover:
    backgroundColor: "{colors.denied-wash}"
    textColor: "{colors.denied}"
    rounded: "{rounded.md}"
    height: 32px

  button-danger:
    backgroundColor: "{colors.denied}"
    textColor: "{colors.on-denied}"
    typography: "{typography.control}"
    rounded: "{rounded.md}"
    height: 32px

  # An action that belongs to one row or one dialog, not to the card. See Components.
  button-small:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.meta}"
    rounded: "{rounded.md}"
    height: 27px
    padding: "0 9px"

  # A button whose whole content is one icon is square, and sized so the icon has air on every
  # side rather than the two the padding of a text button would give it.
  button-icon:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
    rounded: "{rounded.md}"
    height: 30px
    width: 30px

  field:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.input}"
    rounded: "{rounded.md}"
    height: 34px
    padding: "0 11px"

  field-placeholder:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text-muted}"

  # The two edges a field draws on itself. See Components.
  field-border-hover:
    backgroundColor: "{colors.line-strong}"
    height: 1px

  field-border-focus:
    backgroundColor: "{colors.line-focus}"
    height: 1px

  # And the halo outside that edge. A field is the one control that does not take the global
  # focus ring: see Components.
  field-focus-ring:
    backgroundColor: "{colors.halo}"
    size: 3px

  # The narrow field that sits beside one taking the remaining width -- the optional name in the
  # block form. Wide enough for a name, narrow enough that the path keeps the room.
  field-compact:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.input}"
    rounded: "{rounded.md}"
    height: 34px
    width: 190px

  # A form is rows, so its label is the left half of one. Fixed width, because ragged labels down
  # a column is the thing a form layout exists to prevent.
  form-label:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.input}"
    width: 172px

  # A form row is tighter than a list row: a field already carries 34px of its own height, so the
  # padding only has to keep the rows apart.
  form-row:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    padding: "9px 16px"

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

  # Asked for and not answered yet: neither of the two settled colours, so what is on screen
  # reads as the request rather than as the state of the machine.
  switch-pending:
    backgroundColor: "{colors.overlay}"
    textColor: "{colors.text-secondary}"
    rounded: "{rounded.full}"
    width: 38px
    height: 22px

  # Both are geometrically 16. The dark one is drawn a point larger on purpose: see Components.
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
    typography: "{typography.control}"
    height: 44px
    padding: "0 14px"

  tab-selected:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.text}"

  # The 2px rule under the active tab. See Layout for where it sits.
  tab-underline:
    backgroundColor: "{colors.primary}"
    height: 2px

  tab-badge:
    backgroundColor: "{colors.overlay}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.label}"
    rounded: "{rounded.xs}"
    height: 18px
    padding: "0 5px"

  # On the tab you are already on, the badge is lit rather than recessed: it belongs to the
  # selected tab and has to read as part of it.
  tab-badge-selected:
    backgroundColor: "{colors.halo-flat}"
    textColor: "{colors.text}"
    typography: "{typography.label}"
    rounded: "{rounded.xs}"
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
    padding: "0 11px"

  # The selected segment is the one place a border is drawn a step above `line`: it has to lift
  # off a track that is already recessed.
  segmented-border-selected:
    backgroundColor: "{colors.line-selected}"
    height: 1px

  chip:
    backgroundColor: "{colors.overlay}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.mono-compact}"
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

  # Inside a control, where the icon is beside a word rather than standing for one.
  icon-small:
    size: 14px

  # The direction mark in Activity, bigger than the icons around it. See Components.
  icon-caret:
    size: 12px

  # A state dot and the halo around it. The halo is what makes it read as live rather than as a
  # bullet, and it is the same size on the tab rail and in the top bar.
  state-dot:
    size: 6px

  state-dot-halo:
    size: 3px

  pager-page:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-muted}"
    typography: "{typography.meta}"
    rounded: "{rounded.sm}"
    height: 26px
    width: 26px

  # Gaps.
  gap-control:
    size: 7px

  gap-note:
    size: 5px

  gap-stack:
    size: 3px

  gap-segmented:
    size: 2px

  # A toast is as wide as its message needs, between these two. See Components.
  toast:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    padding: "10px 12px"
    width: 380px

  toast-narrow:
    backgroundColor: "{colors.raised}"
    textColor: "{colors.text}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    width: 260px
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

**How to read this.** The front matter carries the values; the sections below carry the reasoning,
once each. A comment beside a token says what the token is and, when the reasoning runs longer
than a line, points at the section that has it rather than repeating it.

## Colors

Surfaces are a single neutral ramp with no hue bias, and each step means one level of elevation:
`ground` for the page, `surface` for cards, `raised` for inputs and hovered rows, `overlay` for
chips and the selected segment, `sunken` for a card's own footer, `inset` for anything recessed
deeper than that. Nothing is coloured because it looked better; a surface is picked by how far it
should sit from the page.

`sunken` and `inset` are not interchangeable. A footer is part of its card and sits barely below
it; the track of a segmented control and a switch that is off are holes in the surface and sit
much further down. A footer painted at `inset` reads as a separate black band under the card.

**Hairlines are their own ramp**, and which step to use is decided by how much the edge has to be
noticed: `line-soft` between rows inside a card, `line` for the edge of a card, a chip or a
field, `line-selected` for the segment that is chosen, `line-strong` for a field under the
pointer, `line-focus` for the field the caret is in. A border is not a surface, which is why
these are not picked from the ramp above.

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

Each section's card header carries its own tint, fading out by 48% of the width: `enforced` for
Applications, `signal` for Devices, `remote` for Activity, `waiting` for Settings. **This is the
only place a section is allowed to identify itself by hue.** Anywhere else, a colour is a claim
about the machine.

The tint is not the same percentage for all four, because the four hues do not weigh the same at
equal alpha: `signal` 11%, `enforced` and `remote` 10%, `waiting` 9%. They are matched by eye, not
by number, and **equalising them is not a fix.** Amber at 11% turns the header into a warning
about nothing.

One exception, and it is deliberate: **the shield in the wordmark is `signal`**. It identifies the
product rather than a state, which is the one thing on this palette that is not a claim about the
machine. Everywhere else, blue means something is active.

## Typography

Six sizes, and nothing between them: 14, 13.5, 13, 12, 11 and the same 11 in mono. `display` for
the wordmark, `title` for card headings, `body` and `body-strong` for rows and running text,
`control` for anything you press or type into, `meta` for anything secondary, `label` for pills
and uppercase eyebrows, `mono` for every value that came from the machine -- executable paths, IP
addresses, match attributes, version strings.

**The wordmark is 14px, not the largest thing on screen.** At 20px it dominates the top bar and
makes the tab rail under it read as a footnote. The name of the product is the one thing on this
page nobody needs to read: what matters is the sections and what they say about the machine. The
hierarchy runs the other way round on purpose.

**500 is the weight of a control.** A tab, a button, a segment and the name of a row are set in
it: heavy enough to hold their own beside the content they sit next to, and short of the 600 that
says "this is a heading". What a person typed is never 500 -- a field's own contents are `input`,
at 400, because weight on them would be the interface emphasising something it did not write.

The `mono` rule is not decoration. A path is a machine value that a person has to compare
character by character, and a proportional face makes `l`, `I` and `1` the same shape.

Every place digits line up in a column -- counts, durations, pagination, the character counter --
sets `font-variant-numeric: tabular-nums`, so numbers do not shift as they change.

**Every time is relative, and carries the exact one in its title.** "5 h ago" answers the question
actually being asked -- was that just now -- and the timestamp the service recorded answers "when
exactly". A recorded value must never be only paraphrased, so it is a hover away rather than
absent. Never both on the line: two clocks for one fact.

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

**A header and a strip are different things, and a card has one or the other.** The header carries
the card's title, its tint, and whatever control belongs to the whole card -- a segmented filter, a
button. It has no icon: the title is the identification, and an icon beside it only repeats the
tab that is already lit. The strip carries a *state* of the machine, has an icon coloured by that
state, and appears exactly once, above the blocked applications, for the policy. Stacking both is
96px of chrome before the first row and two headings for one card.

**The rule under the tabs is as wide as the content, not as wide as the window.** It ends where
the cards begin -- inset by the page padding, not merely clipped to the page width, or it
overhangs every card by that padding on both sides. The top bar is the opposite: its edge runs the
full width, because it is stuck to the window and not to the column.

The 2px underline of the active tab is **centred on that rule**, not resting above it. Two lines
stacked read as a thick edge under one tab; one line drawn through the other reads as that tab
claiming the edge, which is what it means.

Card footers carry the summary on the left and the action on the right: the pager in Activity,
the caveat and `Change password` in Settings.

**One breakpoint, at 720px.** Above it the block form is three columns -- path, name, button.
Below it they stack, the button goes full width, and a form row puts its label above its field
instead of beside it. Nothing else reflows: a column that is already one column does not need a
second layout.

**The sign-in and first-run screens are a centred column 380px wide**, with no top bar and no
tabs: there is nothing to navigate to until there is a session.

They were one card, and a card was three pieces of chrome for one password: a header reading
"Sign in" above a button reading "Sign in", a label naming the only field on screen, and a footer
whose whole job was to hold that button. The screen is the composition now -- the mark, the
product's name, the machine, the field, and one line about the service. The field and the button
are still the ones used everywhere else, which is the part that mattered: a sign-in screen that
looks like a different product is a sign-in screen that looks like a phishing page. What
identifies the machine sits above the form rather than inside it, because the same screen asks
for a new password on first run and for the existing one afterwards, and neither of those two
forms owns it.

**The screen is the one place with a decoration, and it is almost invisible**: hairlines at
`lattice`, on the same 56px pitch the layout is built from, put out by a radial mask before they
reach any edge, over a neutral lift at `lattice-lift`. It is drawn on the container's own
`::before` and `::after` so that masking the light cannot take the content with it. Nothing about
it carries a hue: a colour here would be the only one on this interface claiming nothing.

**The sign-in form's submit sits inside the field.** One field needs no label naming it and no
footer to hold its button, and an arrow at the end of the only input on screen is the shortest
way to say "press this when you are done". The label is still in the markup for a screen reader;
the first-run form, which has two fields, keeps its labels visible and its button full width.

## Elevation & Depth

Depth is real here, not ornamental, and it runs in both directions.

Cards sit **above** the page: `surface` on `ground`, a `line` border, and a soft shadow. The gap
between `#030303` and `#0F0F0F` is what makes a card read as an object rather than as a region.

Recessed controls sit **below** their surface: the segmented track and a switch that is off both
use `inset` with `inset 0 1px 2px rgba(0,0,0,.6)`. A control that is not doing anything sits
below the surface it is on, and rises when it is. A switch that is on drops the inset shadow;
the selected segment gains a border and a drop shadow.

Modals use a heavier shadow and a blurred scrim, and rise 8px on open over 160ms. They are
anchored 80px from the top of the viewport rather than centred in it: a dialog floating in the
middle of an empty page has nothing to sit against, and the list inside it grows downward.

The top bar is translucent -- `ground` at 86% with a 12px backdrop blur -- so the page is felt
moving under it. It is the only translucent surface here, and the only one whose colour is not
flat.

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

The one exception is the shimmer, which is not a confirmation but a state: it runs for as long as
the thing it is on is working. On a control it is a band of that control's own `currentColor`
sweeping across it; on a line of text it is the paint of the letters themselves. **It never
occupies layout**, which is the point -- a spinner has to come from somewhere, and wherever it
comes from the control changes width when it is pressed and again when it finishes. Under reduced
motion it is switched off rather than sped up: a sweep run once in a millisecond freezes mid-pass.

## Shapes

`xs` for the count on a tab; `sm` for chips and the selected segment; `md` for buttons, fields and
toasts; `lg` for cards and modals; `full` for pills and switches. Nothing else. A radius that is
not on this list is a mistake, not a variation.

`xs` exists because the badge is 18px tall: at `sm` the corners eat most of an edge that short,
and the badge reads as a lozenge rather than as a square with its corners taken off.

## Components

**Buttons** come in four weights and the weight is the meaning: `primary` filled neutral for the
one action that executes; `secondary` with a border for anything else; `ghost` for actions that
should not compete; `danger` filled red, which appears only inside a confirmation.

**And in two sizes.** 32px is the size of an action that belongs to a card -- `Block`,
`Change password`. 27px is the size of an action that belongs to one row or one dialog: `Use` in
the process list, `Cancel` and `Remove` in a confirmation, `Sign out` on the session row,
`Running processes` in the header. A row is 44px tall; a 32px button in it leaves 6px of air above
and below and reads as the row's main event rather than as its aside.

**Destructive actions never fire on the first click, and never open a dialog.** The row itself
becomes the confirmation: it takes a `denied` wash, the secondary detail gives way to the
question, and the actions become `Cancel` and `Remove`, with focus moving to `Remove`. Opening
one closes any other. The name of what is about to change stays exactly where it already was,
which no dialog can manage.

**A switch that has been asked but not answered shows it.** Applying a policy takes seconds, and
for those seconds what the user asked for and what the machine is doing are not the same thing.
The thumb moves on the click -- a control that waits for the round trip looks dead -- and the
track drops to `switch-pending`, which is neither of its two settled colours. No spinner: the
thumb is already saying what was asked, and a spinner over it is a second thing to read for one
fact.

**Switches** are 38×22 with a 16px thumb. Off is a recess with a grey thumb; on is a `primary`
track with a dark thumb, and the two ends of the neutral ramp are the widest gap this palette
has. The thumb of a switch that is on is scaled to 17px: both are geometrically 16, but a dark
shape on a light field reads smaller than a light shape on a dark one. **This is an optical
correction and it must not be "fixed" by making the numbers equal.**

**Pills** carry no leading dot. The colour already says what the dot would.

**Fields** show their state inside themselves where it is useful -- a character counter against
the minimum, `Match` / `No match` on a repeated password -- in `label` size at the trailing edge,
with a 14px icon when what it says is a complaint. Validation while typing, not after submitting.

A field is the one control that does not take the global focus ring. It answers focus with its own
border at `line-focus`, a 3px `halo` outside it, and a drop to `surface` inside: a 2px ring at 2px
offset around a 34px field draws a box bigger than the field it is pointing at, and on the search
inside a dialog it swallows the control whole. Under the pointer the border goes to `line-strong`
and no further -- a hover that draws a light frame is a hover pretending to be a focus.

**Toasts** stack at the bottom right, dismiss themselves after 4.5s and can be dismissed by hand.
They never occupy layout: a notice that pushes the page down moves the control the user was
about to click. A toast is as wide as its message needs between 260px and 380px, rather than a
fixed width that leaves a short notice half empty and wraps a long one four times.

**Focus** is a 2px `primary` ring at 2px offset, drawn only for `:focus-visible`, so a mouse
click never leaves a ring behind. It is the same ring on every control except a field, which
answers focus in its own border as described above. Nothing removes an outline without putting one
back.

**The mark is drawn, not stroked.** It is the one graphic here that paints itself from its own
gradient instead of taking `currentColor` like every icon, and it is the same drawing as the
favicon. If one of the two changes, both are wrong.

It appears twice -- 18px in the top bar, 46px on the sign-in screen -- and it is one symbol in
the sprite, used twice. Two copies of it in the markup would be a third and fourth drawing to
keep in step with the favicon.

**A disabled control drops to 40% opacity and stops taking pointer events**, and that is the
whole treatment: no separate grey, no change of shape. The one thing that must not happen is a
disabled control that still looks pressable, because the pager spends most of its life with one
of its two ends disabled.

**An empty list says what is not there, in `meta`, on `text-muted`, in the space the rows would
have taken.** It never borrows the layout of a row, and it never apologises: "No applications are
blocked" is the whole message.

**Icons** are Lucide, at 16px with a 1.7 stroke, inlined as a single SVG sprite, and 14px when
they sit beside a word inside a control. No icon font, no CDN, nothing fetched at run time.

The two carets in Activity are the exception: filled rather than stroked, 12px, pointing right for
a connection and left for a disconnection. A direction reads better as a shape than as an arrow,
and a filled shape needs the extra 4px because it has no interior to be read by.

**An icon names what is inside, not what the thing is called.** Activity is a `clock`, not a pulse
line -- it is a list of sessions that already happened, and a pulse says live monitoring. Settings
is `sliders`, not a gear: a gear is the most generic icon there is and says only "options".
Applications is a `grid` of four squares. A card header carries no icon at all.

## Do's and Don'ts

A summary of the rules above, written as the mistake someone will actually make.

**Do** pick a surface by how far it should sit from the page, and a hairline by how much its edge
has to be noticed.
**Don't** invent a grey. There are six surfaces and five hairlines; if none of them fits, the
layout is wrong, not the palette.

**Do** let colour make a claim about the machine.
**Don't** colour a control because it is important. Weight and position do that.

**Do** show a machine value in `mono`.
**Don't** show it in prose, and don't paraphrase it.

**Do** put a genuinely necessary caveat in a card footer as `meta`.
**Don't** write a paragraph explaining what a control does, why a design decision was taken, or
what will happen if the user clicks. If the interface needs a paragraph, the interface is wrong.

**Do** keep every card header at 48px.
**Don't** let its contents decide its height.

**Do** give a card one heading: a header with its tint, or the policy strip.
**Don't** stack a strip and a header on the same card.

**Do** confirm a destructive action in the row that owns it.
**Don't** open a dialog, and don't use `window.confirm`.

**Do** keep the optical correction on the switch thumb.
**Don't** normalise it away.
