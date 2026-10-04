---
name: RelayForge
description: A graphite route card where each step shows the temper colour steel takes as it heats.
colors:
  background: "#151a20"
  rail: "#0f1318"
  surface: "#1c232b"
  surface-raised: "#232b34"
  border: "#303a45"
  border-strong: "#46525f"
  foreground-bone: "#e8e4d9"
  muted-steel: "#9aa5b1"
  accent-ink: "#14181d"
  focus-straw: "#e6c25a"
  pending-fg: "#a9b3be"
  pending-solid: "#7f8a96"
  running-bg: "#2b2611"
  running-fg: "#efcb62"
  running-ring: "#6b5a1c"
  running-solid: "#e6c25a"
  succeeded-bg: "#14264a"
  succeeded-fg: "#8db0ff"
  succeeded-ring: "#2c4f93"
  succeeded-solid: "#5b8def"
  failed-bg: "#3a1518"
  failed-fg: "#ff7a82"
  failed-ring: "#8a2a35"
  failed-solid: "#e5484d"
  partiallyfailed-bg: "#33230f"
  partiallyfailed-fg: "#e0a566"
  partiallyfailed-ring: "#7a5427"
  partiallyfailed-solid: "#cf8f4e"
  cancelled-bg: "#1f262e"
  cancelled-fg: "#93a0ae"
  cancelled-ring: "#3d4854"
  cancelled-solid: "#6c7885"
  deadlettered-bg: "#2a1c3f"
  deadlettered-fg: "#c9a2f5"
  deadlettered-ring: "#5f3f93"
  deadlettered-solid: "#a97ae0"
typography:
  display:
    fontFamily: "Big Shoulders, Archivo, sans-serif"
    fontSize: "3rem"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "0.01em"
  headline:
    fontFamily: "Big Shoulders, Archivo, sans-serif"
    fontSize: "2.25rem"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "0.01em"
  title:
    fontFamily: "Big Shoulders, Archivo, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "0.01em"
  body:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 400
    lineHeight: 1.55
  label:
    fontFamily: "Archivo, system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.5
  data:
    fontFamily: "Geist Mono, ui-monospace, monospace"
    fontSize: "0.75rem"
    fontWeight: 400
    lineHeight: 1.5
rounded:
  hairline: "1px"
  control: "3px"
  container: "4px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "16px"
  lg: "20px"
  xl: "40px"
components:
  button-primary:
    backgroundColor: "{colors.foreground-bone}"
    textColor: "{colors.accent-ink}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  button-primary-hover:
    backgroundColor: "#ffffff"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.foreground-bone}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  button-secondary-hover:
    backgroundColor: "{colors.surface-raised}"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.muted-steel}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.container}"
    padding: "16px"
  input:
    backgroundColor: "{colors.background}"
    textColor: "{colors.foreground-bone}"
    rounded: "{rounded.control}"
    padding: "8px 12px"
  nav-item:
    textColor: "{colors.muted-steel}"
    rounded: "{rounded.control}"
    padding: "8px 12px"
  nav-item-active:
    backgroundColor: "{colors.surface-raised}"
    textColor: "{colors.foreground-bone}"
  step-node:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.container}"
    padding: "12px"
    width: "216px"
  state-badge:
    rounded: "{rounded.control}"
    padding: "2px 8px"
    typography: "{typography.label}"
---

# Design System: RelayForge

## Overview

**Creative North Star: "The Temper Route Card"**

The job is a piece of work travelling a steel-mill route card, and each step's state is the oxide colour steel takes when heated. The shell is dark graphite and stays quiet; all chroma is reserved for state. Bare steel grey (pending) heats to straw gold (running) and settles to temper blue (succeeded). Bronze, cherry and oxide purple carry the failure family.

Density is console-grade: dense tables for engineers, a wide step graph for newcomers, both in one view with no mode switch. Surfaces are flat, bordered, and square-ish (3-4px corners). Colour never works alone: every state also has its own glyph shape and node-border treatment, so the card reads in greyscale.

**Key Characteristics:**
- Graphite shell, bone ink, chroma only where state is meaningful.
- Seven states, each with a colour family (bg / fg / ring / solid) and a distinct shape cue.
- Condensed display voice (Big Shoulders) over a plain grotesque (Archivo), mono for counts and tickets.
- Flat tonal layering; no shadows.
- One authored motion: the running heat bar.

## Colors

A graphite-blue neutral ladder carrying a seven-state oxide ramp; the neutral shell has no accent hue of its own.

### Primary
- **Bone** (#e8e4d9): body ink, and the fill of the primary button (ink on it is #14181d). The only "brand" colour; it is neutral.

### Secondary
- **Straw Focus** (#e6c25a): focus ring, caret, and text selection. Shares the running-state hue, so focus and "working" read as related heat.

### Tertiary (state ramp)
Each state defines bg (tint), fg (text), ring (1px edge) and solid (bars, edges, marks).
- **Bare Steel** (pending, #7f8a96 solid): waiting.
- **Straw** (running, #e6c25a solid): working.
- **Temper Blue** (succeeded, #5b8def solid): done; also colours completed dependency edges.
- **Cherry** (failed, #e5484d solid): failed.
- **Bronze** (partially failed, #cf8f4e solid): some steps failed.
- **Oxide Purple** (dead-lettered "parked", #a97ae0 solid): out of retries, parked.
- **Cool Slate** (cancelled, #6c7885 solid): stopped by the user.

### Neutral
- **Graphite** (#151a20): page background. **Rail** (#0f1318): the darker nav rail.
- **Plate** (#1c232b): cards, tables, nodes. **Raised Plate** (#232b34): table heads, active nav, hover.
- **Seam** (#303a45) and **Strong Seam** (#46525f): borders; strong for inputs, pending nodes, idle edges.
- **Muted Steel** (#9aa5b1): secondary text and labels.

### Named Rules
**The Heat-Is-State Rule.** Chroma outside the neutral ladder means a step or run state, and nothing else. Do not use state colours as decoration.
**The Never-Colour-Alone Rule.** Every state pairs its colour with its glyph and, on nodes, its border style (dashed pending, 2px failed, 4px double parked, struck-through cancelled name).

## Typography

**Display Font:** Big Shoulders (with Archivo, sans-serif)
**Body Font:** Archivo (with system-ui, sans-serif)
**Label/Mono Font:** Geist Mono (counts, tickets, attempt tallies)

**Character:** A tall stamped-plate display voice over a neutral working grotesque; mono makes numbers scan like route-card tickets.

### Hierarchy
- **Display** (700, 3rem, 1.05): the Overview hero h1 only.
- **Headline** (700, 2.25rem, 1.05): page h1 on Runs, New run, Run detail, Glossary.
- **Title** (700, 1.5rem, 1.05): section h2s.
- **Body** (400, 0.9375rem, 1.55): all running text; `text-wrap: pretty` on paragraphs.
- **Label** (600, 0.75rem to 0.875rem): table heads, field labels, graph stage headers, badges. Sentence case, no tracking.
- **Data** (mono, 0.6875rem to 0.75rem, tabular numerals): ticket numbers, "n/m tries".

### Named Rules
**The Tabular Rule.** Tables and counts use tabular numerals (`.tnum`, table default).

## Layout

Two columns from the md breakpoint: a fixed 14rem left rail (sticky, full height) and a fluid main capped at max-w-6xl with 40px side and top padding (20px / 32px below md). Below md the rail collapses to a top bar with a horizontally scrolling nav. Overview stacks the step graph above the scenario list because three 216px stages need full width. The step graph sits on a column grid: 216px nodes, 72px column gaps, 14px row gaps, a 34px stage-header row, each stage on a Graphite band. On narrow screens the graph scrolls sideways with a hint. Spacing follows Tailwind's 4px step with 12/16px inside components, 8-10px between table cells vertically (py-2.5).

## Elevation & Depth

Flat. Depth is tonal: Graphite page, Plate surfaces, Raised Plate for heads and hover, always edged with a 1px Seam border. Badges use an inset 1px ring (the only box-shadow, and it is a border substitute, not elevation). No drop shadows.

### Named Rules
**The Flat-Plate Rule.** Hierarchy comes from tone and border, never from shadow.

## Shapes

Machined and square: 3px on controls (buttons, inputs, badges, nav items), 4px on containers (cards, tables, callouts, nodes), 1px on heat bars and the brand strip. Focus ring is a 2px straw outline with 2px offset. Node borders encode state (dashed, solid, 2px, 4px double).

## Components

### Buttons
- **Shape:** 3px radius; semibold; 150ms colour transition; disabled at 50% opacity.
- **Primary:** Bone fill, ink text, 8px 16px (md) or 6px 12px (sm); hover goes white; 1px press nudge.
- **Secondary:** Plate fill, 1px Strong Seam border, hover Raised Plate and Muted border.
- **Ghost:** Muted text, hover Plate fill and bone text.

### Cards / Containers
- 4px radius, Plate, 1px Seam border, 16px padding, no shadow.

### Inputs / Fields
- Graphite fill (inset against Plate), 1px Strong Seam border, 3px radius, 8px 12px padding. Hover brightens border to Muted; focus swaps to Straw border plus the straw outline. Labels are 12px semibold Muted above the field.

### Navigation
- Left rail on Rail colour, 1px Seam divider. Items 14px medium, Muted; active is Raised Plate with bone text and `aria-current`. Brand is the wordmark over a five-segment temper strip (grey, straw, bronze, purple, blue). A one-line colour legend sits at the rail foot.

### Tables
- Plate body, Raised Plate head, Seam row dividers, 16px by 10px cells, 12px semibold Muted heads, horizontal scroll wrapper.

### State Badge (signature)
- 3px radius, state bg tint, state fg text, inset 1px ring, 12px glyph plus label ("Partly failed", "Parked" for the two renamed states). Glyphs: dashed circle (pending), half-filled pulsing circle (running), filled check disc (succeeded), crossed diamond (failed), left-half disc (partly failed), ringed dot (parked), slashed circle (cancelled).

### Step Node and Heat Bar (signature)
- A 216px Plate node with state-ring border, a 6px heat bar on top (empty outline pending; straw sweep 1.6s running; solid state colour when settled), name plus mono ticket (stage.row), badge plus mono tries. Retry countdown is straw text; error text is cherry, clamped to two lines. Edges are bezier curves: Temper Blue when the upstream step succeeded, otherwise Strong Seam, dashed while the downstream step waits.

### Callout
- 4px radius, 1px border, 16px padding; tones info (Plate), teach (running tint), error (failed tint).

## Do's and Don'ts

### Do:
- **Do** express any new state with bg / fg / ring / solid tokens plus a glyph and a border or text cue.
- **Do** keep surfaces flat: Graphite, Plate, Raised Plate, 1px Seam.
- **Do** use Big Shoulders for h1 and h2 only; Archivo for everything else; mono for numbers.
- **Do** honour `prefers-reduced-motion` for any looping animation (heat-sweep, heat-pulse are disabled).
- **Do** keep the straw focus ring (2px, 2px offset) visible on every interactive element.

### Don't:
- **Don't** use the state colours for decoration or branding outside the temper strip.
- **Don't** convey state by colour alone.
- **Don't** add drop shadows or radii above 4px.
- **Don't** introduce a light theme or a second accent colour.
- **Don't** animate anything other than a running step.

## Known drift (not canonized)

The build carries these; they are not rules for new surfaces. `globals.css` declares `--font-display: var(--font-display)` in `@theme inline`, a self-reference that leaves the Tailwind `font-display` utility unresolved (headings still work through the direct h1/h2 rule). The glossary rail legend and Pending text colours are not independently verified for contrast. Two small text sizes (11px and 13px) are one-offs and are not part of the ramp.
