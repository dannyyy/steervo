# Steervo · second design exploration

Three interactive local proposals for the capture → compare → refine workflow, on `prototype-codex` based on `prototype` at `b610c56`.

The first round was rejected for feeling dull and dated. This round changes typography, palette, vehicle presentation, and composition. The rejected version is preserved under `round-one/`; it is not the default preview.

## Preview

```sh
python3 -m http.server 5182 --bind 127.0.0.1 --directory prototypes/design
```

Open http://127.0.0.1:5182/.

- **A · Studio** (`?concept=workspace`): cobalt navigation rail; horizontal visual shortlist; vehicle presentation beside the criteria; contextual edit panel.
- **B · Canvas** (`?concept=dossier`): aubergine navigation and an apricot vehicle surface; generous typography; inline assessment editing and contextual notes.
- **C · Focus** (`?concept=matrix`): graphite/lavender comparison surface; vehicle images in the column headers; directly editable values and difference filters.

No design has been selected as the permanent application system. The prototype uses self-hosted Geist, locally stored illustrative photographs, and plain HTML/CSS/JS. The existing Razor application is not modified.

## Interaction and scope

All directions share the same illustrative data within a browser tab. Editing an assessment updates the vehicle and comparison. The prototypes demonstrate selection, filtering, save/cancel, adding cars, notes, technical-data editing, and contextual editing. Unknown, false, and zero stay distinct. Subjective ratings do not produce a total score, and failed required criteria do not automatically exclude a car. Reset clears only this prototype's sample state.

Full criterion management, offer versioning/documents, costs, scenarios, sharing, authentication, concurrency handling, and backend persistence remain outside this layout exploration. Offer and criteria views show how their content joins the information hierarchy. Vehicle photography is a proposed visual affordance: the current application does not implement vehicle-image uploads. Images show illustrative models and are not evidence for the sample vehicle's exact trim or specifications.

Mobile comparisons deliberately scroll horizontally while criterion names and thresholds remain pinned; each vehicle column fits the space beside those labels. Studio and Canvas reflow to single-column records; the add-auto action remains visible. Production work would add unsaved-draft protection and server-side errors/access handling.

## Review and verification

Playwright runs in an isolated Chrome context. Checks cover all three concepts at 1440 px and 390 px: capture/comparison propagation, unknown versus zero, filtering, cancellation, candidate selection, technical data, notes, adding cars, session persistence, JavaScript errors, and page-level overflow. Desktop and mobile captures include the default view, comparison, and open editor. Source and captures receive a separate proposal review. This is not a full production accessibility audit.

The Impeccable launcher remains unavailable locally; its design guidance is applied through direct source/render inspection. Design choice and taste remain with the user.

## Asset origins

These images are used as illustrations in the local, unpublished design study. Model data and prices are synthetic, not sourced from the images or their captions.

- `assets/enyaq.jpg`: [Škoda Storyboard, Enyaq Sportline](https://www.skoda-storyboard.com/en/skoda-model/enyaq-iv-2/enyaq-sportline-iv/), image © Škoda Auto.
- `assets/id7.jpg`: [Bilia, Volkswagen ID.7 Tourer](https://www.bilia.se/bilar/volkswagen/bilmodeller/volkswagen-id7-tourer/), manufacturer/dealer product image.
- `assets/i5.jpg`: [Caradisiac, BMW i5 Touring presentation](https://www.caradisiac.com/presentation-video-bmw-i5-touring-2024-le-premier-break-electrique-premium-du-marche-207105.htm), editorial photograph.
- `assets/geist.woff2`: [Vercel Geist](https://github.com/vercel/geist-font), SIL Open Font License, included in `assets/OFL.txt`.

The exact asset URLs are retained in `assets/provenance.json`. Images should be replaced by user-supplied or appropriately licensed vehicle assets if this direction becomes production work.
