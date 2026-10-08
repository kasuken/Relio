# Relio design system

The rules for how Relio looks, reads and behaves. Agents and contributors follow this file for any UI work.

- Tokens: [`tokens.json`](tokens.json) (colours per theme, type, spacing, radius, layout).
- MudBlazor theme: [`Relio.Web/Theme/RelioTheme.cs`](../../Relio.Web/Theme/RelioTheme.cs).
- CSS tokens and Relio-specific classes (thread, monogram, tags, empty state): [`Relio.Web/wwwroot/app.css`](../../Relio.Web/wwwroot/app.css).
- Fonts: [`Relio.Web/wwwroot/fonts/`](../../Relio.Web/wwwroot/fonts/) (SIL Open Font License).

When a token changes, update all three: `tokens.json`, `RelioTheme.cs` and `app.css`.

## Overview

Relio is a private notebook about the people in your life. The design borrows from that notebook: blue-black pen, an ochre pencil for notes, and plum for the moments that were hard. It should feel calm, personal and quiet. It is not a CRM dashboard and not a social feed.

## Principles

- **Your words in the serif, Relio's words in the sans.** Anything the user wrote (an interaction, a note, a difficult moment, a person's name) is set in Alegreya. Every label, button and piece of guidance from Relio is set in Hanken Grotesk. Never swap them.
- **One thread per person.** A person's history is a single vertical thread. Each entry type has its own marker shape and colour, so the type is readable at a glance and without colour (see The thread).
- **Quiet surfaces.** Separate content with space and hairlines (`line`), not with cards and shadows. Only floating layers get `shadow-float`.
- **Calm, never urgent.** No red badges, counters or streaks. Overdue reminders use `warning` and a word, never `error`.
- **Private by default.** Relio loads nothing from third parties: fonts ship with the app in `Relio.Web/wwwroot/fonts/`. Never add Google Fonts, analytics or CDN links to the app.

## Content fundamentals

- Address the user as "you". Relio never speaks as "I" or "we".
- Sentence case everywhere: "Log an interaction", not "Log An Interaction". No all-caps labels.
- Name things by what people do: "Reach out", "Log an interaction", "Add a note", "Record a difficult moment". Use the same verb in the button and in the confirmation: "Save note" → "Note saved".
- Dates are human: "Today", "Yesterday", "12 days ago", then "3 March" and "3 March 2025" for past years. Birthdays read "Turns 40 on Friday".
- Difficult moments are written without judgement: "What happened", "What set it off", "How it was resolved", "What you learned". Never "conflict", "fight" or "incident".
- Empty states invite the next step in one sentence and one button: "No one here yet. Add the first person you want to keep in touch with." → **Add a person**.
- Errors say what happened and how to fix it, without apologising: "Enter a date in the past or today."
- No emoji, no exclamation marks, no superlatives.

## Colour

- Page ground is `paper`; content sits on `surface`. Use `surface-sunken` for filled inputs and row hover.
- `pen` is the only accent. Use it for primary actions, links, the selected nav item and reminders. Put labels on a pen fill in `on-pen`.
- Entry types own their colours: interactions `pen`, notes `pencil` (marker fill `pencil-mark`), difficult moments `plum`. Their `*-soft` tints are backgrounds behind `text` or the matching colour.
- `plum` is not an error colour. Errors are `error`, confirmations `success`, overdue items `warning`, and each always comes with a word or icon.
- Body text is `text`; dates and metadata are `text-muted`. Both pass 4.5:1 on every ground in both themes.
- QR codes (two-factor setup) are always `qr-ink` on `qr-ground`, dark on light in both themes: scanners cannot read an inverted code. They keep a four-module quiet zone, a hairline `line` border and no other decoration.
- Dark theme is the "night desk": the same roles with lighter inks on a blue-black ground. It is a first-class theme. Follow the system setting by default.

## Typography

- Faces: Alegreya (serif, roman and italic) for people and their stories, Hanken Grotesk (sans) for the interface. Both are self-hosted variable fonts, weights 400 to 700.
- Names and pages: `display` for the person's name on their profile, `heading-1` for page titles, `heading-2` for dashboard sections and dialog titles.
- User content: `entry` for everything the user wrote, `entry-reflection` (italic) only for "What you learned".
- Interface: `title` for panel titles and names in lists, `body` for copy, `label` for buttons, tabs and form labels, `meta` for dates and relationship types, `caption` for helper text.
- Keep entry text within `measure` (680px, about 65 characters per line).

## Spacing, radius and layout

- Spacing steps from `space-1` (4px) to `space-12` (48px). MudBlazor's spacing unit is 4px, so `pa-4` = `space-4`.
- Radii by role: `radius-sm` tags, `radius-md` buttons, inputs and menus, `radius-lg` dialogs and the person header, `radius-round` avatars and markers only. Don't give everything the same radius.
- Desktop: a `nav-width` drawer on the left and content up to `content-max`. The person profile is one reading column of `measure` width, left-aligned.
- Phone: the drawer becomes a temporary drawer, gutters are `space-4`, and the primary action ("Log an interaction") is a full-width button at the bottom of the person page.
- Everything is left-aligned. Center only empty states.

## The thread

The person timeline is the signature element of Relio and the one place the design is bold.

- A 2px vertical line in `line-strong` connects the entries, newest first.
- Interaction: a filled `pen` circle. Note: a `pencil-mark` square outlined in `pencil`. Difficult moment: a hollow `plum` diamond.
- The marker shape carries the meaning, so the thread is readable without colour.
- Each entry shows its date in `meta`, its type in `label`, and the user's text in `entry`. A difficult moment adds its status tag (Open, Resolved, Recurring) and its reflection on `plum-soft`.

## Elevation, motion and states

- No shadows on page content. `shadow-float` only for dialogs, menus, popovers and snackbars.
- Motion only answers an action: a new entry slides into the thread (160ms, ease-out); dialogs fade and scale from 98%. Nothing animates on load. Respect `prefers-reduced-motion`.
- Focus: a 2px solid `focus` ring with a 2px offset in the page colour, on every interactive element.
- Hover: rows take `surface-sunken`; buttons darken 8%. Disabled controls use `text-muted` and keep their border.

## Iconography

- Use MudBlazor's Material icons, Outlined set (`Icons.Material.Outlined.*`), at 20px in the interface and 18px inside buttons.
- Fixed meanings: People `Group`, Interaction `Forum`, Note `StickyNote2`, Difficult moment `Thunderstorm`, Reminder `NotificationsNone`, Birthday `Cake`, Archive `Inventory2`, Search `Search`.
- Icons sit next to a label. An icon on its own needs a tooltip and an `aria-label`.

## Logo

- The logo is the two-figure mark and the "Relio" wordmark. The source artwork and its crops are in `assets/brand/relio/` (`crop-manifest.json` records every crop). Never redraw, recolour or retype it.
- In the app, always use `Components/Shared/RelioLogo.razor`: the mark is an image in its own fixed colours (the one exception to "colours come from tokens"), and the wordmark is a mask filled with `text`, so it follows light, dark and forced-colours modes. Set its size with `--rl-logo-height` (28px in the app bar, 32px in the marketing header, 36px on account pages); on phones the app bar shows the mark alone.
- The favicon, the touch icons and the web files are the mark alone, derived by `assets/brand/generate-web-assets.py` (run it after the artwork changes). The social preview places the full logo through `docs/marketing/generate-preview.py`.
- Give the logo clear space of at least half its height, and don't set it below 16px.

## MudBlazor mapping

Build the `MudTheme` from these tokens; never hard-code colours in components.

| MudTheme | Light | Dark |
|---|---|---|
| `Primary` / `PrimaryContrastText` | `pen` / `on-pen` | `pen` / `on-pen` |
| `Secondary` | `plum` | `plum` |
| `Tertiary` | `pencil` | `pencil` |
| `Background` / `Surface` | `paper` / `surface` | `paper` / `surface` |
| `AppbarBackground` / `AppbarText` | `paper` / `text` | `paper` / `text` |
| `DrawerBackground` / `DrawerText` | `paper` / `text` | `paper` / `text` |
| `TextPrimary` / `TextSecondary` | `text` / `text-muted` | `text` / `text-muted` |
| `LinesDefault` / `LinesInputs` / `Divider` | `line` / `line-strong` / `line` | same |
| `ActionDefault` / `TextDisabled` | `text-muted` / `text-muted` | same |
| `Success` / `Warning` / `Error` / `Info` | `success` / `warning` / `error` / `pen` | same |
| `LayoutProperties.DefaultBorderRadius` | `radius-md` (8px) | |
| `LayoutProperties.DrawerWidthLeft` | `nav-width` (248px) | |
| `Typography.Default` | Hanken Grotesk, `body` | |
| `Typography.H1`–`H3` | Alegreya, `display`, `heading-1`, `heading-2` | |

Set `Elevation="0"` on app bar, drawer, papers and cards; only `MudDialog`, `MudMenu` and `MudPopover` keep their elevation.
