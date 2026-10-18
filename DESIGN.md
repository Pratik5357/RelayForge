---
name: RelayForge
description: Departures. A run is a departures board; a tour demo is a boarding pass.
colors:
  concourse: "#e4e6e3"
  paper: "#ffffff"
  field: "#f4f5f2"
  field-focus: "#fffbea"
  hairline: "#c4c8c2"
  hairline-soft: "#daddd8"
  ink: "#121412"
  muted: "#4e534d"
  label: "#5c615b"
  amber: "#ffc22e"
  error: "#b3261e"
  board: "#121412"
  board-raised: "#1b1e1b"
  board-control: "#262a26"
  board-rule: "#2a2e2a"
  board-text: "#f2f0e8"
  board-muted: "#a9aea6"
  remark-pending: "#a9aea6"
  remark-running: "#ffc22e"
  remark-succeeded: "#5bd687"
  remark-failed: "#ff6e5e"
  remark-partiallyfailed: "#ffa24a"
  remark-deadlettered: "#c9a3ff"
  remark-cancelled: "#a9aea6"
typography:
  display:
    fontFamily: "Atkinson Hyperlegible Next, system-ui, sans-serif"
    fontSize: "clamp(52px, 6.4vw, 92px)"
    fontWeight: 800
    lineHeight: 0.95
    letterSpacing: "-0.04em"
  page:
    fontFamily: "Atkinson Hyperlegible Next"
    fontSize: "72px"
    fontWeight: 800
    letterSpacing: "-0.045em"
  section:
    fontFamily: "Atkinson Hyperlegible Next"
    fontSize: "40-44px"
    fontWeight: 800
    letterSpacing: "-0.035em"
  body:
    fontFamily: "Atkinson Hyperlegible Next"
    fontSize: "16-17px"
    fontWeight: 400
    lineHeight: 1.55
  label:
    fontFamily: "Atkinson Hyperlegible Next"
    fontSize: "12px"
    fontWeight: 700
    letterSpacing: "0.08em"
    textTransform: "uppercase"
  data:
    fontFamily: "Atkinson Hyperlegible Mono, ui-monospace, monospace"
    fontSize: "12-26px"
rounded:
  remark: "4px"
  input: "4px"
  button: "6px"
  board: "12px"
  pass: "16-18px"
motion:
  ease-out: "cubic-bezier(0.23, 1, 0.32, 1)"
  ease-in-out: "cubic-bezier(0.77, 0, 0.175, 1)"
  ease-drawer: "cubic-bezier(0.32, 0.72, 0, 1)"
---

# Design System: RelayForge

## Overview

**North star: "Departures."** A running job is read the way a traveller reads a departures board: rows that move, remarks that change, the one you care about lit. Each tour demo is a boarding pass you tear off to run. Light concourse ground, black board panels, white passes, one hyperlegible family. Amber means "working now".

Built for two audiences at once (see PRODUCT.md): newcomers watching a demo, and engineers using it as a console. No mode switch.

## Colour

- **Concourse** (`#e4e6e3`) is the page ground; **paper** (`#fff`) for passes, forms and lists; **board** (`#121412`) for live data panels and the top bar.
- **Ink** (`#121412`) for text and primary buttons; **muted** for secondary text (≥4.5:1 on concourse and paper).
- **Amber** (`#ffc22e`) is reserved for: the running state, the active-nav underline, the one "go" action per screen, retry countdowns, and the focus halo.
- **Remarks** carry state on the board: pending/cancelled grey, running amber, succeeded green, failed red, partly failed orange, parked violet. Colour is never alone: the remark always spells the state ("Parked", "Partly failed"), the lamp is hollow for pending/cancelled and solid otherwise, cancelled is struck through, and only running blinks.

## Typography

One family: **Atkinson Hyperlegible Next** for everything, **Atkinson Hyperlegible Mono** only for times, counts, tries, ids and codes. Headings are 800 weight with tight negative tracking. Small uppercase tracked labels (`.label`) are the only caps, used for field and column labels.

## Components

- **Top bar** — board-black, wordmark + three-slat mark with the amber lamp, nav with an amber underline that slides between tabs (`layoutId`), live local clock. Anchored during page transitions (`view-transition-name: site-header`). On phones the nav is one scrolling row under the logo.
- **Remark** (`components/Remark.tsx`) — a small black tag with a lamp and the state in words; flips in like a board flap when the state changes.
- **Board** — black panel, 12px corners, uppercase column labels, mono figures. Rows re-rank with a layout spring (working rows rise, finished rows settle) rather than jumping.
- **Boarding pass** (`components/BoardingPass.tsx`) — white pass with a dashed tear line and two notches (`components/Perforation.tsx`); the stub holds the tour number, scenario code and "Run it". The tear line turns horizontal when the stub wraps under the pass (container query).
- **Timeline** (`components/RunTimeline.tsx`) — one lane per step on the run's own clock: solid bars for tries that worked, hatched for tries that failed, dotted lines for the wait before a retry. Built from tries the page observed (`lib/attempts.ts`); tries that finished before the page opened are drawn as one labelled span ("tries 1–3").
- **Buttons** — primary (ink), secondary (1.5px ink outline), go (amber, one per screen), board (on black). 6px corners, `scale(0.97)` on press.
- **Inputs** — field-grey with a bottom rule; focus turns the field pale amber with a 3px ink rule.
- **Overlays** (`components/Overlay.tsx`) — native `<dialog>` with `showModal()` (focus trap, Escape, inert page). Centred dialog for confirmations (cancel a run); right-edge sheet for step details.
- **Toasts** (`components/Toaster.tsx`) — bottom-right stack, newest in front, older ones step back; pause on hover and while the tab is hidden.

## Motion

Library: **Motion** (`motion/react`) for layout, presence and gesture-style motion; CSS for predetermined loops; React `<ViewTransition>` for route changes. Tokens live in `lib/motion.ts` and as CSS variables.

| Moment | Purpose | Curve / time |
|---|---|---|
| Page transition | Old page leaves fast with a 2px blur, new one arrives from the side you're heading | out 150ms, in 260ms ease-out; nav-forward / nav-back types |
| Nav underline | Shows where you are by moving, not recolouring | 300ms ease-in-out |
| Page entrance | Content rises in a short stagger; board panels drop in with a clip reveal | 420ms ease-out, ≤200ms stagger; 560ms clip |
| Overview headline | The one authored entrance | 720ms clip reveal |
| Board rerank | Rows slide to their new rank | spring, 0.45s, no bounce |
| Remark flip | New state turns in like a flap | 240ms ease-out |
| Running lamp | Only working steps blink | 1.1s steps(1) loop (CSS) |
| Cancel dialog | Protects an irreversible action | in 250ms scale .96→1, out 150ms |
| Step sheet | Comes from its edge, leaves the same way | in 500ms / out 300ms drawer curve |
| Toasts | Rise from the corner and stack | 400ms `ease` |
| Timeline tooltip | Names a try without a click, grows from its bar | 125ms, 250ms hover delay |
| Start run / Run it | Label morphs to show progress | 200ms ease-out + 2px blur |

Rules: exits are faster than entrances; nothing scales from 0; hover motion only on `(hover: hover) and (pointer: fine)`; no animation on keyboard-driven or high-frequency actions (nav clicks themselves, typing). `prefers-reduced-motion` keeps opacity and colour changes and drops movement: rows jump to place, remarks crossfade, page transitions fade, the lamp stops blinking.

## Do's and don'ts

- **Do** spell every state in words on its remark; colour is a second signal.
- **Do** keep amber for "working", focus and the single go action.
- **Do** use mono only for numbers, times and ids.
- **Don't** add a second accent colour or use state colours as decoration.
- **Don't** add animations that block interaction or replay on every visit beyond the Overview headline.
