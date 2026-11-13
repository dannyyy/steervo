# Steervo contributor context

Read [CLAUDE.md](CLAUDE.md) for the public Community repository boundaries and [CONTRIBUTING.md](CONTRIBUTING.md) for conventions.

## Current priority

The user selected **Canvas / Tinte**. The main development branch is **`main`**; this repository has no `master` branch.

The migration of the **existing** application to Canvas / Tinte is complete (phases 0–5, verified on 13 November 2026). Continue development on `main` using the documented design and shared interactions. New features require their own scope; the prototype remains a visual reference, not the production implementation.

Before UI work, read:

1. [PRODUCT.md](PRODUCT.md) — product facts and invariants.
2. [DESIGN.md](DESIGN.md) — selected visual system and normative tokens.
3. [docs/interaction-patterns.md](docs/interaction-patterns.md) — shared interaction behaviour.
4. [docs/design-migration.md](docs/design-migration.md) — phased scope, source inventory and acceptance checklist.

Preserve the completed migration and record actual verification for subsequent changes. Do not reopen the design selection unless the user changes it.

## Implementation constraints

- Keep Razor Pages / HTMX and preserve server-side authorisation, validation, concurrency checks and import/export compatibility.
- Preserve current domain semantics and calculations. Unknown is not no or zero; subjective ratings are not objective scores; a missed required criterion never automatically excludes a candidate.
- Preserve the full existing navigation and feature set, including areas omitted from the abbreviated prototype.
- The prototype's colour/layout switcher, session storage, sample content and vehicle photos are not production features. An image-upload feature is outside this migration.
- User-facing text belongs in `src/Steervo.Web/Resources/Ui.resx`. Code, comments and identifiers stay English.
- Use Playwright with isolated contexts for browser verification. Test desktop and mobile, focus/scroll/draft preservation and relevant failure/permission states. Keep screenshots and test artifacts out of commits.
- Use the existing test and startup commands in README. Never test against production data.
