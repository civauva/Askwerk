# ui-design-system.md — the visual language

Askwerk must be simple, effective and beautiful. **Stupid simple** (owner rule, 2026-10-14): every screen, also for powerful features like themes and templates, stays as simple as what is built so far. Presets and good defaults first, details behind "More", one primary action per screen. Beautiful here means calm, consistent, and fast: one type family, a restrained palette, an 8-point grid, generous whitespace, motion that explains state, nothing on screen that is not needed now.

**Brand: Werkstatt** (approved in AWK-3; board in `design/AWK-3-brand/w2-werkstatt.html`). Engineered, exact, a little industrial: surveys as instruments. Sharp corners, visible structure, monospaced figures, one signal colour that marks only what matters. Wireframes stay grayscale; hi-fi mockups and the product use the tokens below.

## Process rule
Wireframes first (grayscale, real DE/EN copy, all states). Hi-fi only when Dražan asks for it, and then exactly in these tokens. Implementation must match the approved visual.

## Tokens (`packages/ui/tokens`)
- Spacing: 4, 8, 12, 16, 24, 32, 48, 64. Nothing in between.
- Type: the IBM Plex family (OFL), self-hosted from `packages/ui`, never loaded from a font CDN. **Plex Sans Condensed** 600/700 for headlines and survey questions, **Plex Sans** 400/500/600 for reading and UI, **Plex Mono** 400/500 for numbers, codes, variable names and labels (labels in uppercase with 0.06em tracking). Sizes 12, 13, 14 (body), 16, 20, 24, 32; the runner adds 17 (body on phone) and 18 (body on desktop). Line height 1.5 body, 1.2 headings.
- Radius: 2 (controls, chips), 4 (cards, dialogs), full only for toggles. Nothing rounder.
- Colour (light / dark, tuned separately, never inverted):
  - Paper `#f6f5f2` / `#141311`, surface `#ffffff` / `#1c1a18`, ink `#1a1917` / `#f2efea`, graphite `#4a4640` / `#c9c3ba`, muted `#6e6962` / `#9c958b`, line `#e3dfd8` / `#2f2b27`, strong line `#c8c2b8` / `#48423b`.
  - **Signal** `#d2440f` / `#ff7a3d` (soft `#fbe6da` / `#3a2014`, ink on soft `#8f2c06` / `#ffb48f`): only for the one thing that matters on a screen — the primary action, the selected item, live state, progress. 12-step ramp in the token file.
  - Steel `#3e4a56` / `#9fb0c0` for links and information; success `#2e7d4f` / `#6cc28f`, warning `#a86a12` / `#e3a948`, error `#b42318` / `#f2877b`.
  - Defined once as CSS variables for light and dark; components never use raw colours. Organisations may replace logo and signal colour for their surveys (theme); contrast is checked on save.
- Elevation: borders over shadows; one soft shadow only for floating layers (menus, popovers). A faint 24 px grid can be shown behind work surfaces (designer canvas); it is on by default (owner decision, 2026-10-14) and can be switched off on the canvas.
- Signature detail: the **measuring rule** — progress bars and rating scales read like a ruler with ticks; question numbers and counts in mono (`Q7`, `07 / 12`).
- Logo: square tile with an angular question mark of straight strokes, and the wordmark "ask**werk**" in Plex Sans Condensed with "werk" in signal. The tile alone is the favicon (64/32/16) and app icon; reversed on signal for dark-on-orange uses.
- Headers: logo, breadcrumb and status align on one text baseline, never on box centres; the tile centres on the wordmark; a 1 px rule separates logo and breadcrumb.
- Voice: short, factual, specific; say what happened and what to do next; numbers instead of adjectives; no exclamation marks, no filler, no emoji. "Fertig. Ihre 12 Antworten sind gespeichert." / "Nicht gespeichert. Askwerk versucht es in 8 s erneut."
- Motion: 120 ms for hover/press, 200 ms for enter/exit, 300 ms for layout changes; ease-out; CSS transitions and the View Transitions API only; disabled under `prefers-reduced-motion`.

## Layout rules per app
- **Designer**: three regions only — left outline (pages and questions), centre canvas (the survey as the respondent sees it, editable inline), right inspector (settings for the selected thing). Top bar: survey name, language switch, preview toggle, Publish. No dashboards inside the editor. Command palette (Ctrl/Cmd-K) for everything.
- **Runner**: one column, max 640 px wide, large type (18 px body on desktop, 17 on phone), one question in focus in the one-at-a-time layout or a clean form in the classic layout, progress visible, keyboard-first, respects the survey theme tokens. First paint under 1 s on a mid-range phone.
- **Analytics** (inside designer): one measure per card, calm charts, one accent colour for the thing that matters, our own SVG charts with a table view for every chart, tables that read well, filters as chips. No decorative gradients, no 3D, no chart junk.
- **Themes and templates** (owner requirement, AWK-17): real templates with themes, images and different layouts, and every survey look renders on **web**, and optionally **email** and **PDF**. Looks are data (tokens, presets, images), never web-only CSS: each renderer (runner CSS, inline-styled table HTML for email, paged print for PDF) maps the same data. Every wireframe that touches presentation says what happens in email and PDF.
- **Settings and configuration**: every setting on a page with a sentence explaining it, sensible default shown, validation inline, save is automatic or one clear button. Domains, email, branding, auth and integrations are each one screen.

## Components
- Every Radix primitive is wrapped once in `packages/ui`; apps import wrappers only.
- Each component has: all states (default, hover, focus-visible, active, disabled, loading, error), both themes, both languages, shown on the `/dev/states` page, and an axe check.
- Empty states are designed, not left blank: one sentence, one primary action.
- **One standard look for all common controls** (owner requirement, 2026-10-14, AWK-19): text inputs, selects and combo boxes (with search), date, time and date-time pickers, checkboxes, radio groups, switches, dialogs, menus, tabs and data grids exist once in `packages/ui` and look and behave the same in every app and screen. No screen styles its own select, picker or table. Native date/time inputs are not used directly; the shared pickers handle locale, time zone and keyboard entry.
- **Data grids** (`DataGrid` in `packages/ui`, AWK-19, W13 approved): every list uses it. Sortable headers (sorted on the server), numbers, durations and dates right-aligned in mono, 40 px rows, skeleton rows, empty and error states, a card per row on phones with one Sort menu. Paging by default (25/50/100, page numbers); the response list loads more while scrolling and keeps only the rows near the screen in the page. Row selection only where a screen acts on several rows (Participants). The API offers offset paging and, for responses, a cursor.
- Icons: Lucide (ISC), the SVGs we use copied as data into `packages/ui/src/icons/lucide.ts` (licence beside it); row actions use `RowTools` (W42b B). One icon set only; our own icons follow its style.

## Quality gates
- Lighthouse ≥ 95 (performance, accessibility, best practices) on the runner; axe: zero violations on every app.
- WCAG 2.2 AA: contrast, focus order, visible focus, labels, error messages, target size.
- Measure: time to first published survey under 5 minutes for a new author; respondent completion rate tracked per layout.

- Standard controls (AWK-19, W20): use Dialog, ConfirmDialog, Menu, Tabs, Switch, Checkbox, RadioGroup, Field and Notice from packages/ui; never hand-build these again.
