---
name: Applications Ledger
colors:
  primary: "#2A2420"
  secondary: "#786F68"
  tertiary: "#A85A43"
  neutral: "#FAF8F5"
typography:
  h1:
    fontFamily: Iowan Old Style
    fontSize: 4.5rem
  body-md:
    fontFamily: -apple-system
    fontSize: 1rem
  label-caps:
    fontFamily: JetBrains Mono
    fontSize: 0.6875rem
rounded:
  sm: 0px
  md: 0px
spacing:
  sm: 12px
  md: 24px
---

## Overview

Editorial utility for candidates tracking job applications. The interface mixes
broadsheet-style typography with product-grade structure: a paper-toned canvas,
deep ink text, hairline borders, and one restrained clay accent reserved for
primary action and sync progress.

## Colors

The palette is restrained and warm-neutral, with the accent used sparingly.

- **Primary (#2A2420):** Deep ink used for headlines, body copy, and primary button fills.
- **Secondary (#786F68):** Muted editorial grey for metadata, labels, table headers, and helper copy.
- **Tertiary (#A85A43):** Clay accent used for sync progress, focused states, and emphasis moments.
- **Neutral (#FAF8F5):** Warm paper background for the application shell and surrounding canvas.

## Typography

Typography is split by role rather than volume.

- **H1 — Iowan Old Style, 4.5rem:** Used for the page-defining headline and modal titles.
- **Body-md — -apple-system, 1rem:** Used for table rows, descriptive copy, controls, and status text.
- **Label-caps — JetBrains Mono, 0.6875rem:** Used for uppercase metadata, field labels, and table headings with wide tracking.

## Layout

The page is designed to fill the browser rather than sit inside a centered mockup.

- Use a full-width shell with a minimum viewport-height canvas.
- Split the page into a hero/action header and a larger data region beneath it.
- Preserve generous editorial padding at the section level while keeping data areas dense and scan-friendly.
- Let the table expand horizontally with overflow support instead of compressing columns past readability.
- On smaller widths, stack hero content and let controls wrap before reducing spacing.

## Elevation & Depth

Depth is intentionally quiet.

- Use a single soft shadow on the primary shell and modal surfaces.
- Rely on borders before shadows to create separation.
- Use tinted surface fills for sync progress and status states instead of heavy elevation.
- Keep overlays translucent and lightly blurred rather than dark and dramatic.

## Shapes

The system is mostly square and editorial.

- Inputs, selects, pills, buttons, tables, and modal cards use sharp corners.
- Borders are thin and consistent across shells, controls, and table dividers.
- Avoid soft card language, pill-heavy UI, or oversized radii.
- Shape contrast comes from spacing and linework rather than curvature.

## Components

- **Primary actions:** Two header buttons, with the primary action filled in ink and shifting to clay on hover/focus.
- **Sync feedback:** A bordered progress panel with percentage, stage text, and a single clay progress bar.
- **Account modal:** Compact, centered dialog with one dropdown, clear labels, and minimal action rows.
- **Search and table:** Search sits above the table as a utility control; the table uses mono uppercase headers, generous row spacing, hover tinting, and an Action column with quiet inline edit buttons.
- **Status pills:** Lightweight bordered chips with subtle state-specific tinting for Applied, Interviewing, Offered, Company Rejected, and Candidate Rejected.
- **Pagination:** Quiet footer bar with counts and previous/next controls, treated as part of the data surface.
- **Edit flow:** The edit screen is a full-page form in the same editorial shell, with connected-email selection, conditional custom-email entry, bottom-right cancel/submit controls, and a confirmation modal before saving.

## Do's and Don'ts

- **Do** keep the accent color scarce and purposeful.
- **Do** favor serif display type with sans-serif body copy and monospace metadata.
- **Do** use borders, spacing, and type hierarchy before adding decoration.
- **Do** preserve full-page responsive behavior for app-like usage.
- **Don't** add gradients, large rounded cards, or multiple competing accent colors.
- **Don't** turn the layout into a generic dashboard with dense chrome.
- **Don't** replace the warm paper foundation with pure-blue SaaS styling.
- **Don't** use body text as display type or introduce extra ornamental flourishes.
