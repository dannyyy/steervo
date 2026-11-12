# Steervo — Canvas / Tinte

Origin: worktree `steervo-prototype-codex`, branch `prototype-codex`, based on `prototype` (`b610c56`). The user selected **Canvas with Tinte**. This reference is retained on `main`; the ASP.NET application has not been migrated. The authoritative handoff is [DESIGN.md](../../DESIGN.md), [interaction patterns](../../docs/interaction-patterns.md), and the [migration plan](../../docs/design-migration.md).

## Run

```sh
python3 -m http.server 5182 --bind 127.0.0.1 --directory prototypes/design
```

Open http://127.0.0.1:5182/ for the selected Canvas/Tinte preview. The alternatives gallery is `?view=designs`. Studio is `?concept=workspace`; Canvas is `?concept=dossier`; append `&view=compare` for comparison. The old three-direction round remains under `round-two/`, and the first round under `round-one/`.

## Round 3 brief and reference analysis

The user liked the feel of Studio and Canvas, but found the compositions insufficiently coherent and still rather boring. They supplied Yuvomi, Wealthfolio and Libretto as references, explicitly asking not to copy them. The anchor workflow remains moving between capturing and comparing candidates. The subsequent user choice was “Canvas und Tinte”; this is now the selected direction.

Inspected on 2026-11-12:

- [Yuvomi](https://yuvomi.cloud/): actual [dashboard](https://yuvomi.cloud/screenshots/dashboard-light-web.png), [calendar](https://yuvomi.cloud/screenshots/calendar-light-web.png), and [meal planner](https://yuvomi.cloud/screenshots/meals-light-web.png) screenshots. Warm neutral ground, soft white surfaces, repeated navigation and controls, and functional accents make different modules feel related.
- [Wealthfolio](https://wealthfolio.app/): actual [investments](https://assets.wealthfolio.app/images/landing/hero/investment-tracking.png), [net worth](https://assets.wealthfolio.app/images/landing/hero/net-worth.png), and [spending](https://assets.wealthfolio.app/images/landing/hero/spending.png) screenshots. The chart occupies the main space directly; quiet navigation, repeated segmented controls and consistent subordinate surfaces support the content.
- [Libretto demo](https://demo.libretto.ch/): overview, [vehicle list](https://demo.libretto.ch/fahrzeuge), [vehicle detail](https://demo.libretto.ch/fahrzeug/q4etron) and [comparison](https://demo.libretto.ch/vergleich), inspected with Playwright. Its serif headings, mono figures, warm surfaces and restrained green controls form a deliberate, recurring visual vocabulary. No demo records were changed. Docker was unnecessary because the public demo and supplied product screenshots were accessible.

The inference for Steervo is a common visual and interaction grammar, not a replica of a reference's brand, typography or layout. No reference product code, screenshots or assets are included in the prototype.

## Proposal contract

Mode: Operate. **Selected: Canvas / Tinte.** Studio remains a comparison reference:

- **Studio:** a persistent, quiet navigation and one white working surface. Vehicle, facts and personal impression sit on the left; editable requirements sit on the right. On mobile the criteria precede the personal impression.
- **Canvas:** horizontal navigation and a vehicle introduction followed by criteria and personal notes on a shared surface. More spacious and longer than Studio.
- Both use Geist, the same spacing rhythm, soft elevation, controls and evaluation statuses. Primary editing opens inline; vehicle metadata and longer notes use the same side sheet.
- **Signature interaction:** selecting a requirement reveals its editor in context, saving updates both capture and comparison, Escape/cancel restores the opener. In comparison the editor follows the table, outside the horizontally scrolling region, retaining the selected vehicle and criterion in its heading. This keeps the mobile form within the viewport.
- **Motion:** only the inline editing region has a short reveal; content starts visible and reduced motion is supported.
- **Colour exploration:** Tinte, Petrol and Pflaume change the interaction accent and its pale surfaces, while warning, failure and subjective statuses keep their meanings. This is a proposal control, outside the product UI, not a product feature. Tinte is selected; other accents remain available only for comparison in the prototype.

## Prototype behaviour and limits

Editing criteria, vehicle metadata, technical data and notes works. Adding a mock candidate works. Comparison candidate selection and filters work. Changes persist only in this browser tab's session storage (`steervo-design-demo-v3`); the selected accent uses a separate key. Demo reset restores example cars and assessments.

Unknown remains different from zero and no. Subjective ratings remain subjective, and missing a required threshold never excludes a car automatically. Statuses have text equivalents on mobile.

Offert details and criteria management show information architecture; full document management, versioning, financial modelling, sharing, authentication and production persistence are not implemented here. Those existing product capabilities are not removed. Vehicle images are a proposed presentation feature, not a claim that the current app supports uploads. Photographs are illustrative; specs, prices and assessments are synthetic. See `assets/provenance.json` and the font's OFL licence.

## Verification

Playwright/Chrome at 1440×1000 and 390×844 exercised capture-to-comparison updates, zero versus unknown, filters, cancellation, candidate selection, technical data, notes, adding candidates and session persistence. No page errors or page-level horizontal overflow at those sizes. The comparison scrolls horizontally on mobile with criterion labels pinned. Further targeted checks cover the second candidate, palette persistence/contrast, and 320/768/1024/1280 px widths.

The design skill launcher/detector is unavailable in this environment; review uses source, computed browser checks and screenshots. Canvas / Tinte is the approved design direction. The production migration is still outstanding.

Final independent review: the one material finding (secondary text contrast in editors and the review footer) was corrected and scored resolved, disposition `ship` at that fix scope. Measured editor contrasts: Tinte 5.22:1, Petrol 5.10:1, Pflaume 5.21:1; footer 4.89:1. See `review-report.json`. The user subsequently selected Canvas / Tinte.

## Selected direction

User choice: “Canvas und Tinte”. Default layout: `dossier` (Canvas), default accent: `ink` (Tinte). Opening the root preview uses this combination even if an alternative colour was explored earlier. A direct `?concept=dossier&palette=ink` link also selects it. The design keeps horizontal navigation, a vehicle introduction, criteria and notes on one shared surface, inline assessment editing and the same controls in comparison.

Selected colour tokens: accent `#3e5878`, hover `#304862`, pale accent `#e9eef3`, ink `#252c32`, secondary text `#657077`, canvas `#f4f4f0`, surface `#ffffff`, separator `#e5e7e5`. Secondary editor text is `#536474`; success, unknown and failure keep their existing semantic colours. Font: self-hosted Geist.
