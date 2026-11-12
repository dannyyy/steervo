# Steervo design exploration

Three interactive, local layout proposals for the capture → compare → refine workflow. These are standalone HTML/CSS/JavaScript mock layouts, not a migration of the Razor application.

## Preview

From this worktree:

```sh
python3 -m http.server 5182 --bind 127.0.0.1 --directory prototypes/design
```

Open http://127.0.0.1:5182/ to compare the directions.

- `?concept=workspace`: persistent evaluation navigation and vehicle list; contextual editing panel. Recommended for frequent switching between capturing and comparing.
- `?concept=dossier`: horizontal navigation, focused vehicle record and inline assessment editing. Best for longer research sessions.
- `?concept=matrix`: comparison first, with compact filters and contextual editing. Best for resolving differences in a shortlist.

Each has vehicle details, a two-vehicle comparison, editable assessments, candidate switching, filters, basic vehicle capture, notes and technical-data editing. The same sample state is shared across directions in browser session storage. Reset demo restores the original examples. No network requests or dependencies are needed.

The visible car specifications, offers and amounts are illustrative sample data, not verified model specifications or recommendations. The review shell says this explicitly. Production data is never accessed.

## What the proposals address

The current candidate screen stacks the evaluation heading/navigation, candidate identity, warning, offer link, technical data, assessment forms, classification and notes into separate regions. The comparison repeats selectors and vehicle headings, and its editing expands into nested assessment forms. The layout consumes substantial vertical space before the primary work begins.

All three proposals use one navigation hierarchy, values before editing controls, a consistent edit/save/cancel cycle, and the same field semantics in capture and comparison. Unknown, false and zero stay distinct. Criteria with thresholds can be fulfilled or unfulfilled; subjective 1–5 ratings do not become a total score. Missing a required criterion does not exclude a vehicle.

A preserves context through a persistent vehicle list. B gives one vehicle a coherent dossier and uses inline editing. C makes criteria and differences the primary surface. These are structural alternatives, not colour variations.

## Scope and next decision

Choose the layout direction first, or combine a specific navigation model with a specific editor. Then apply the selected structure to criteria management, full offer versioning/documents, costs, scenarios, sharing, permissions and empty/error states before changing the production templates. Their production behavior remains the reference.

The prototype's criteria and offer pages show information architecture only; they do not implement criterion management, uploads, calculations, authentication or backend persistence. Drafts are discarded on cancel, navigation or reload. In a production implementation, warn before discarding unsaved changes and surface server validation, access restrictions and concurrent-edit conflicts.

## Review basis

Base branch: `prototype` at `b610c56`. Source reviewed: evaluation header, candidate details/comparison, shared layout and CSS, project README. Existing candidate, comparison and cost screenshots from the original checkout were checked against these templates and tokens. No original application files were changed.

The Impeccable context/concept launcher was unavailable because its engine was not installed; existing source and rendered screenshots supplied the context. No permanent product design system is selected by these proposals.

## Verification

Playwright in an isolated Chrome context checked all three directions at 1440 px and 390 px: capture/comparison propagation, unknown versus zero, filtering, cancellation, candidate selection, technical data and notes, adding cars, and session persistence. No JavaScript errors or page-level horizontal overflow were found. Screenshots were visually reviewed; a separate review checked the proposals, including mobile add-auto access and text contrast. The comparison table deliberately scrolls horizontally on small screens.
