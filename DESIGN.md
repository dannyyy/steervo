---
name: "Steervo — Canvas / Tinte"
description: "Bestätigtes visuelles Zielsystem für die Migration der bestehenden Community-Anwendung."
colors:
  accent: "#3e5878"
  accent-hover: "#304862"
  soft: "#e9eef3"
  focus: "#3e5878"
  ink: "#252c32"
  muted: "#657077"
  editor-muted: "#536474"
  canvas: "#f4f4f0"
  white: "#ffffff"
  nav-surface: "#fafbf8"
  line: "#e5e7e5"
  field-border: "#cbd2ce"
  filter-surface: "#f2f4f3"
  neutral-surface: "#f1f3f3"
  group-surface: "#f8f9f7"
  row-hover: "#f6f8f6"
  warning: "#8b641f"
  warning-bg: "#fbf4e5"
  negative: "#a34e44"
  negative-bg: "#fbefed"
  positive: "#39705a"
  positive-bg: "#edf5f0"
  feedback-bg: "#283c3a"
  dialog-scrim: "#25332d55"
typography:
  display:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "36px"
    fontWeight: 590
    lineHeight: 1.12
    letterSpacing: "-0.035em"
  headline:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "30px"
    fontWeight: 560
    lineHeight: 1.2
    letterSpacing: "-0.035em"
  context-title:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "28px"
    fontWeight: 570
    lineHeight: 1.2
    letterSpacing: "-0.035em"
  title:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "22px"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "-0.025em"
  section-title:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "19px"
    fontWeight: 560
    lineHeight: 1.2
    letterSpacing: "-0.025em"
  body:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "13px"
    fontWeight: 550
    lineHeight: 1.5
  field-label:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "12px"
    fontWeight: 550
    lineHeight: 1.5
  metadata:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "11px"
    fontWeight: 400
    lineHeight: 1.5
  value:
    fontFamily: "Geist, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif"
    fontSize: "18px"
    fontWeight: 560
    lineHeight: 1.5
    letterSpacing: "-0.02em"
rounded:
  flat: "0"
  status: "6px"
  control: "8px"
  button: "9px"
  detail: "10px"
  candidate: "11px"
  editor: "12px"
  surface-mobile: "14px"
  surface: "16px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  2xl: "32px"
  3xl: "48px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.white}"
    typography: "{typography.label}"
    rounded: "{rounded.button}"
    padding: "10px 14px"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
    textColor: "{colors.white}"
  button-secondary:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.button}"
    padding: "10px 14px"
  button-secondary-hover:
    backgroundColor: "{colors.soft}"
  button-text:
    backgroundColor: "transparent"
    textColor: "{colors.accent}"
    typography: "{typography.label}"
    rounded: "{rounded.button}"
    padding: "10px 14px"
  button-text-hover:
    backgroundColor: "{colors.soft}"
  icon-button:
    textColor: "{colors.muted}"
    rounded: "{rounded.control}"
    width: "36px"
    height: "36px"
  field:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "10px 12px"
  navigation-active:
    backgroundColor: "{colors.soft}"
    textColor: "{colors.accent}"
    rounded: "{rounded.control}"
    padding: "10px 15px"
  status-positive:
    backgroundColor: "{colors.positive-bg}"
    textColor: "{colors.positive}"
    rounded: "{rounded.status}"
    padding: "4px 7px"
  status-open:
    backgroundColor: "{colors.warning-bg}"
    textColor: "{colors.warning}"
    rounded: "{rounded.status}"
    padding: "4px 7px"
  status-negative:
    backgroundColor: "{colors.negative-bg}"
    textColor: "{colors.negative}"
    rounded: "{rounded.status}"
    padding: "4px 7px"
  status-personal:
    backgroundColor: "{colors.neutral-surface}"
    textColor: "{colors.muted}"
    rounded: "{rounded.status}"
    padding: "4px 7px"
  status-selection:
    backgroundColor: "{colors.soft}"
    textColor: "{colors.accent}"
    rounded: "{rounded.status}"
    padding: "4px 7px"
  record:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    rounded: "{rounded.surface}"
    padding: "30px 34px"
  record-mobile:
    backgroundColor: "{colors.white}"
    rounded: "{rounded.surface-mobile}"
    padding: "19px"
  inline-editor:
    backgroundColor: "{colors.soft}"
    textColor: "{colors.ink}"
    rounded: "{rounded.editor}"
    padding: "24px"
  inline-editor-mobile:
    backgroundColor: "{colors.soft}"
    rounded: "{rounded.editor}"
    padding: "19px 14px"
  side-sheet:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    padding: "32px"
    width: "450px"
  filter-active:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    rounded: "{rounded.status}"
    padding: "5px 9px"
---

# Design System: Steervo — Canvas / Tinte

## Overview

**Creative North Star: "Canvas und Tinte"**

Canvas und Tinte bilden das bestätigte Zielsystem von Steervo. Eine ruhige, warme Grundfläche trägt zusammenhängende weisse Arbeitsflächen; der gedämpfte Tintenakzent kennzeichnet Handlungen, Auswahl und Fokus. Fahrzeugdaten, Anforderungen und persönliche Eindrücke bleiben gut unterscheidbar und stehen trotzdem in einem gemeinsamen Arbeitskontext.

Die Gestaltung ist offen, übersichtlich und zurückhaltend. Horizontale Navigation, klare Schriftgrössen, schmale Trennlinien und weiche Flächenschatten schaffen Hierarchie. Wiederkehrende Aufgaben verwenden dieselben Steuerelemente und Rückmeldungen; Farbe unterstützt die Bedeutung, ersetzt aber weder Statuswörter noch Quellen und Einheiten.

**Status und Autorität.** Dieses Dokument beschreibt das bestätigte **Zielsystem**. Seit dem 13. November 2026 sind der gemeinsame Rahmen, Fahrzeugliste, Fahrzeugakte, Bewertung, Fahrzeugvergleich, Evaluationsübersicht, Kriterien, Gruppen, Freigaben und Einstellungen umgesetzt; die übrigen Bereiche folgen dem Migrationsplan. Massgeblich sind die [Auswahl](prototypes/design/selection.json), die Canvas-Regeln und gemeinsam genutzten Komponenten in [round-three.css](prototypes/design/round-three.css) sowie deren Verhalten in [design.js](prototypes/design/design.js). Die Produktionsgrundlage liegt in [tokens.css](src/Steervo.Web/wwwroot/css/tokens.css) und [canvas.css](src/Steervo.Web/wwwroot/css/canvas.css). [site.css](src/Steervo.Web/wwwroot/css/site.css) trägt die weiterhin vorhandenen Fachlayouts. Der gemeinsame [Bewertungseditor](src/Steervo.Web/Pages/Evaluations/Candidates/_AssessmentEditor.cshtml) und [Editoraktionen](src/Steervo.Web/Pages/Shared/_EditorActions.cshtml) setzen die Regeln in Akte und Vergleich um. Auswahl: `dossier` / `ink`; bestätigt am 12. November 2026.

[PRODUCT.md](PRODUCT.md) hält die Produktgrenzen fest. [Designmigration](docs/design-migration.md) ordnet die vollständige Übertragung des vorhandenen Produkts, [Interaktionsmuster](docs/interaction-patterns.md) die verbindlichen Abläufe und Zustände. Neue Funktionen folgen nach der Migration der bestehenden Oberflächen, Layouts, Workflows und Dialoge. Die Dokumentation bleibt innerhalb der öffentlichen Community-Ausgabe und ihrer *source-available*-Einordnung.

**Key Characteristics:**

- Horizontale Navigation und eine zusammenhängende Fahrzeugakte.
- Geist für Überschriften, Texte, Bedienelemente und tabellarische Zahlen.
- Tinte für Interaktion; eigene semantische Farben für offen, erfüllt und nicht erfüllt.
- Bewertungen direkt im Kontext bearbeiten, auch im mobilen Vergleich.
- Weisse Flächen, zurückhaltende Rundungen und geringe, funktionale Tiefe.

## Colors

Tinte ist ein gedämpfter blau-grauer Interaktionsakzent auf warmen, fast neutralen Flächen. Die YAML-Tokens sind die verbindlichen Farbwerte; die Namen unten erklären ihre Anwendung.

### Primary

- **Tinte** (`accent`): primäre Handlungen, aktive Navigation und aktive Detailsichten.
- **Dunkle Tinte** (`accent-hover`): Hoverzustand der primären Handlung.
- **Helle Tinte** (`soft`): aktive Bereiche, Auswahl, Textauswahl und Inline-Editor.
- **Fokus-Tinte** (`focus`): sichtbare Tastaturkontur. Der eigene Rollenname bleibt bestehen, obwohl der Wert dem Akzent entspricht.

### Neutral

- **Texttinte** (`ink`): Überschriften, Werte, Beschriftungen und normale Texte.
- **Sekundärtext** (`muted`): ergänzende Angaben auf den neutralen Oberflächen.
- **Editor-Sekundärtext** (`editor-muted`): korrigierte sekundäre Schrift auf der getönten Inline-Editorfläche. Im Prototyp ist dies eine lokale Überschreibung von `--muted`; gemessener Kontrast für Tinte: 5.22:1.
- **Canvas** (`canvas`), **Arbeitsfläche** (`white`) und **Navigation** (`nav-surface`): Hintergrund, Hauptinhalt und Kopfbereich.
- **Trennlinie** (`line`) und **Feldkontur** (`field-border`): Inhaltsgliederung und erkennbare Eingabefelder.
- **Filterfläche** (`filter-surface`), **neutrale Statusfläche** (`neutral-surface`), **Gruppenfläche** (`group-surface`) und **Zeilen-Hover** (`row-hover`): zurückhaltende lokale Gliederung.
- **Rückmeldung** (`feedback-bg`) und **Dialogabdunklung** (`dialog-scrim`): Bestätigungshinweis und Sperrebene hinter einem modalen Seitenpanel.

### Semantic states

| Bedeutung | Text / Fläche | Anwendung |
| --- | --- | --- |
| Erfüllt | `positive` / `positive-bg` | Erfasste objektive Anforderung erfüllt. |
| Noch offen | `warning` / `warning-bg` | Angabe fehlt; keine negative Bewertung. |
| Nicht erfüllt | `negative` / `negative-bg` | Erfasste objektive Anforderung verfehlt. |
| Persönlich | `muted` / `neutral-surface` | Subjektive Bewertung, etwa 1–5. |
| In Auswahl / Favorit | `accent` / `soft` | Einordnung des Fahrzeugs, unabhängig von der Erfüllung. |

**The Bedeutung vor Farbe Rule.** Jeder fachliche Zustand hat eine lesbare Bezeichnung. Unbekannt, Nein und null bleiben unterscheidbar; persönliche Bewertungen erhalten keine objektive Erfüllungsfarbe.

Die Statusfarben sind fachliche Rollen und keine alternativen Markenpaletten. Die achtstufigen Farbverläufe im Sidecar sind aus diesen Farben abgeleitete Vorschauen, keine zusätzlichen freigegebenen Farbwerte. Angaben wie „Zurückgestellt“, „Ausgeschieden“, Prüfbedarf nach einer Offertversion oder Formularfehler gehören weiterhin zum Produkt; ihre genaue visuelle Ausprägung muss während der Migration mit den vorhandenen Fachzuständen abgeglichen werden. Der Prototyp belegt dafür noch kein vollständiges Komponentenset.

## Typography

**Display Font / Body Font:** selbst gehostetes Geist mit System-Sans-Fallbacks aus dem Frontmatter. **Label/Mono Font:** ebenfalls Geist; Zahlen verwenden `font-variant-numeric: tabular-nums`.

Geist hält Überschriften, Daten und Bedienelemente in einem gemeinsamen Schriftbild. Überschriften sind leicht verdichtet; ihre variable Gewichtung schafft Hierarchie ohne eine zweite Schriftfamilie. Der Prototyp lädt die variable Schrift mit `font-weight: 100 900`, `font-display: swap` und deaktivierter synthetischer Schriftbildung.

### Hierarchy

| Rolle | Verwendung |
| --- | --- |
| `display` | Fahrzeugname in der Canvas-Einleitung; mobil reduziert. |
| `headline` | Hauptüberschrift im Vergleich. |
| `context-title` | Evaluationstitel oberhalb der Fahrzeugakte; auf anderen Ansichten kompakter. |
| `title` | Überschrift des Editors. |
| `section-title` | Arbeitsabschnitt mit zugehöriger Werkzeugleiste. |
| `body` | Grundschrift und Eingabewerte; lange Notizen erhalten mehr Zeilenabstand. |
| `label` | Standardbutton und vergleichbare Aktionsbeschriftungen. |
| `field-label` | Sichtbare Feldbeschriftungen. |
| `metadata` | Quellen, ergänzende Hinweise und kleinere Beschriftungen. |
| `value` | Bewertungswert in einer bearbeitbaren Zeile. |

Das sind beobachtete Rollen, keine mathematisch abgeleitete Skala. Die effektiv verwendete Kriteriumsüberschrift ist auf dem Desktop (14px), darunter stehen Hinweise (11px); der Bewertungswert ist (18px). Auf kleinen Bildschirmen werden diese Rollen zu (12px), (10px) und (16px). Notizen im Seitenbereich verwenden (13px) mit grosszügiger Zeilenhöhe; längere Notizen sind auf ungefähr (65ch) begrenzt. Eingabefelder verwenden mobil (16px).

**The Ein Schriftbild Rule.** Geist trägt die gesamte Oberfläche. Zahlen werden über tabellarische Ziffern ausgerichtet, nicht durch eine zusätzliche Monospace-Schrift.

**Assets.** Die vorhandene [Geist-Datei](prototypes/design/assets/geist.woff2) und ihre [SIL Open Font License 1.1](prototypes/design/assets/OFL.txt) bilden die Schriftgrundlage. Herkunft und Lizenz stehen in [provenance.json](prototypes/design/assets/provenance.json). Bei der Übernahme in die Produktionsassets bleibt die Lizenz erhalten.

## Layout

### Canvas composition

Der Kopf enthält Marke, horizontale Evaluationsnavigation und persönlichen Bereich. Danach folgen Evaluationskontext und horizontaler Kandidatenwechsel. Die Fahrzeugakte liegt auf einer gemeinsamen weissen Fläche: zuerst Fahrzeugname, Fakten und Handlungen; danach Detailnavigation, Anforderungen beziehungsweise Detailinhalt und persönliche Notizen.

Die äussere Canvas-Breite ist auf (1300px) begrenzt und zentriert. Der Inhaltsbereich hat auf grossen Bildschirmen seitlich (32px) Abstand. Die Navigation ist mindestens (83px) hoch und hat seitlich (42px) Innenabstand. Die Akte übernimmt `components.record`.

Die Einleitung verwendet zwei Spalten im Verhältnis (.95fr / 1.05fr) mit (48px) Zwischenraum. Darunter teilen sich Hauptinhalt und Notizen den Raum: flexible Hauptspalte, Seitenbereich (245px), Abstand (54px). Diese Zweiteilung betrifft die Fahrzeugakte; sie ist keine Pflicht für jede Produktseite. Die Spacing-Tokens bilden den wiederkehrenden Rhythmus. Zusätzliche, beobachtete Komponentenabstände bleiben bei ihrer jeweiligen Komponente.

Die Bildfläche der Studie ist illustrativ. Es gibt keinen neuen Auftrag für Fahrzeugbild-Upload oder eine bildabhängige Produktionsansicht. Titel, Fakten, Aktionen und Bewertungen müssen auch ohne Bild eine vollständige Einleitung bilden. Die vorhandenen Bilder sind nur für den Entwurf dokumentiert; [Provenienz](prototypes/design/assets/provenance.json) ist keine Produktionsfreigabe.

### Responsive behaviour

Alle folgenden Breiten sind CSS-Mediengrenzen aus der ausgewählten Implementierung, keine Geräteklassifikation. Die Sidecar-Erweiterung führt sie maschinenlesbar.

| Grenze | Canvas-Verhalten |
| --- | --- |
| Bis 1200px | Akten-Innenabstand (28px); Notizenspalte (220px), Spaltenabstand (35px); Fahrzeugname (33px). Kandidatenwechsel wird kompakter. |
| Bis 980px | Äussere Inhaltsabstände (24px), Akten-Innenabstand (26px); Notizen unter dem Hauptinhalt zunächst in zwei Spalten. Fahrzeugname (31px). |
| Bis 700px | Navigation in eigener zweiter Kopfzeile; Inhaltsabstände (14px), Akten-Innenabstand (19px). Einleitung, Anforderungen und Notizen stehen untereinander. Fahrzeugname (29px); Formularfelder (16px), Feldgruppen einspaltig. |
| Bis 360px | Navigationsicons entfallen; die lesbaren Navigationstexte bleiben. |

Die Notizen folgen auf kleinen Bildschirmen dem Hauptinhalt. Kandidatenwechsel und Detailnavigation dürfen innerhalb ihrer eigenen Regionen horizontal scrollen; die Seite selbst bleibt innerhalb des Viewports. Die Bewertungszeile zeigt mobil keine separate Statusplakette, sondern den Status in lesbarem Text am Wert. Ein offener Wert bleibt ausdrücklich „Noch offen“.

### Comparison and editing

Der Vergleich verwendet eine echte Tabelle mit Spalten- und Zeilenüberschriften. Auf dem Desktop beansprucht die erste Spalte (29%). Bis (700px) bleibt sie mit (134px) links fixiert, während die Vergleichsregion horizontal scrollt; die Tabellenbreite folgt `calc(200% - 134px)`. Ein sichtbarer Wischhinweis und die per Tastatur fokussierbare, benannte Scrollregion machen dieses Verhalten auffindbar.

**Der Inline-Editor folgt im Vergleich auf die Scrollregion.** Er steht nicht in einer Tabellenzelle und nicht innerhalb der horizontal scrollenden Fläche. Seine Überschrift nennt das Kriterium, der Kontext das ausgewählte Fahrzeug. Dadurch bleibt die Eingabe mobil innerhalb des sichtbaren Bereichs. In der Fahrzeugakte steht der Editor unmittelbar nach der ausgewählten Bewertungszeile.

## Elevation & Depth

Das System kombiniert Tonflächen, feine Trennlinien und leichte Schatten. Die gemeinsame Akte und der Vergleich erhalten eine geringe, weiche Anhebung. Ein ausgewählter Kandidat und ein aktiver Filter heben sich nur leicht ab. Ein modales Seitenpanel hat einen deutlich stärkeren Schatten und eine abdunkelnde Ebene; die Bestätigung erscheint darüber.

Die exakten Schattenwerte liegen in `.impeccable/design.json` unter `extensions.shadows`: `surface`, `candidate-selected`, `filter-selected`, `side-sheet` und `feedback`. Der Standard-Schatten entspricht dem vorhandenen CSS-Token `--shadow`. Diese Erweiterung ist nötig, weil das Frontmatter keine Schatteneigenschaften erlaubt.

**The Zusammenhängende Fläche Rule.** Eine Fahrzeugakte bleibt eine gemeinsame Arbeitsfläche. Trennlinien und Abstand gliedern ihre Inhalte; zusätzliche Schatten gehören nur an tatsächlich getrennte Ebenen.

Bewegung bleibt lokal. Der Inline-Editor wird nur bei `prefers-reduced-motion: no-preference` kurz sichtbar aufgeblendet beziehungsweise über den Ausschnitt geöffnet (240ms); der Inhalt ist auch ohne Animation von Anfang an verfügbar. Buttons wechseln ihre Hintergrundfarbe kurz (150ms). Bei reduzierter Bewegung entfallen Animationen, Übergänge und weiches Scrollen. Die Sidecar-Motionwerte dokumentieren die vorhandenen Abläufe; sie definieren keine zusätzliche Seitenanimation.

## Shapes

Weich gerundete Rechtecke prägen die Oberfläche. Die Rollen `rounded.surface` und `rounded.surface-mobile` gehören zur gemeinsamen Akte, `rounded.editor` zur lokalen Bearbeitung. Buttons, Felder, Kandidatenauswahl und Statusplaketten haben abgestufte kleinere Radien. Das Seitenpanel ist auf dem Desktop nur links gerundet, mobil rechteckig und bildschirmbreit. Avatare sind kreisförmig.

Datenzeilen bleiben flach und durch schmale Linien getrennt. Keine einzelne Bewertungszeile wird zu einer zusätzlichen schwebenden Karte. SVG-Linienicons verwenden `currentColor`, abgerundete Linienenden und Strichstärke (1.6); die Standardgrösse ist (19px), in Buttons (17px). Dekorative Icons sind für assistive Technik verborgen, reine Iconaktionen erhalten einen zugänglichen Namen.

## Components

Die Frontmatter-Komponenten beschreiben die unterstützten Token-Eigenschaften. Die [Sidecar-Datei](.impeccable/design.json) ergänzt acht eigenständig darstellbare HTML/CSS-Beispiele. Diese illustrieren die bestätigten Stile und Zustände; sie sind keine zweite Implementierung der Produktlogik.

### Buttons

Kompakte, klar beschriftete Handlungen mit leicht gerundeter Form. Primäre Buttons verwenden Tinte mit weissem Text, sekundäre eine weisse Fläche mit Trennlinienkontur, Textbuttons transparente Flächen mit Tintenbeschriftung. Innenabstand und Typografie stehen im Frontmatter; die Mindesthöhe ist (42px), der Abstand zwischen Icon und Text (9px). In der Fahrzeug-Einleitung sind die Handlungen etwas breiter gepolstert.

Hover verwendet die dokumentierte dunkle beziehungsweise helle Tinte. Der Fokus ist eine durchgängige (3px) Kontur mit (3px) Abstand. Deaktivierte Buttons sind mit Deckkraft (.45) erkennbar und nicht ausführbar. Im Prototyp gibt es keinen eigenen gedrückten Transformations- oder Ladeeffekt; solche Zustände werden bei der Migration funktional ergänzt, ohne neue visuelle Varianten zu behaupten.

Iconaktionen haben eine klar abgegrenzte Fläche (`icon-button`) und sichtbaren Hover/Fokus. Der kompakte Plusbutton im mobilen Kandidatenwechsel behält seinen zugänglichen Namen.

### Navigation and selection

Die Hauptnavigation verwendet horizontale beschriftete Aktionen mit getönter aktiver Fläche. Der aktive Eintrag ist auch semantisch als aktuell gekennzeichnet. Die aktive Detailnavigation wird durch eine Tintenlinie (2px) und stärkere Beschriftung sichtbar; sie kann horizontal scrollen. Die Migration verwendet für echte Seitenwechsel Links und für lokale Aktionen Buttons, jeweils mit passender Aktiv- beziehungsweise Auswahlsemantik.

Der Kandidatenwechsel ist eine horizontale Reihe; der aktive Kandidat liegt auf einer weissen, leicht angehobenen Fläche. Sein Name bleibt sichtbar. Bilder sind für die Auswahl nicht erforderlich. Kriterienfilter bilden eine kompakte gemeinsame Gruppe: aktive weisse Teilfläche, alle Zustände über `aria-pressed`. Die Filterlogik und vorhandenen Ansichten bleiben erhalten.

### Status chips and priorities

Statusplaketten sind nicht interaktiv. Sie verwenden kleine Rundungen, kurze Wörter und die semantischen Farbpaarungen. „Erfüllt“ kann zusätzlich ein Häkchen tragen. „Persönlich“ bleibt neutral; „Favorit“ gehört zur Auswahlrolle. Prioritäten stehen als Text beim Kriterium und bleiben von Erfüllung und Fahrzeugstatus getrennt. Die verbindlichen Fachbegriffe und Prioritätsstufen kommen aus dem Produkt, nicht aus verkürzten Demobezeichnungen.

### Shared surface and assessment rows

Die Akte fasst Einleitung und Detailinhalt zusammen. Bewertungszeilen ordnen Kriterium mit Anforderung, Wert mit Quelle, Status und Bearbeitungshinweis. Die gesamte bearbeitbare Zeile ist ein Button mit einem zugänglichen Namen. Die Mindesthöhe in Canvas ist auf dem Desktop (83px), mobil (91px). Der mobile Status wird als Text direkt beim Wert gezeigt.

Technische Daten sind beschriftete Faktenlisten ohne Erfüllungswertung. Notizen sind lesbarer Fliesstext. Offerten verwenden dieselbe Schrift-, Linien- und Aktionssprache. Ihre Akte verbindet Angebot, Einschätzungen, Dokumente und Versionshistorie über Abschnittslinks auf einer gemeinsamen Fläche. Historische Versionen bleiben lesbar und unveränderlich; frühere Einschätzungen tragen nach einem Versionswechsel weiterhin den Prüfhinweis. Vollständige Offerten und Uploads verwenden gegliederte Formularseiten mit gemeinsamen Abschlussaktionen. Leere Zustände erklären den fehlenden Inhalt und bieten eine passende bestehende Handlung.

### Fields and inline editor

Felder haben sichtbare Labels, weissen Grund, eine eigene Kontur und mindestens (43px) Höhe. Hinweise stehen neben dem jeweiligen Feldkontext. Textareas sind vertikal vergrösserbar und mindestens (75px) hoch. Auf breiten Ansichten stehen Wert und Quelle nebeneinander; mobil untereinander. Platzhalter ersetzen kein Label.

Der Inline-Editor verwendet helle Tinte mit lokal korrigiertem Sekundärtext. Kriterium, Fahrzeug, Priorität und Anforderung bleiben sichtbar. Der erste Eingabewert erhält Fokus. Abbrechen, Schliessen und Escape folgen den gemeinsamen Interaktionsregeln: unveränderte Eingaben schliessen direkt; ungespeicherte Änderungen werden nicht still verworfen. Nach dem Schliessen kehrt der Fokus zum Auslöser zurück. Speichern aktualisiert die Darstellung im Erfassen und im Vergleich erst nach erfolgreicher Speicherung. Der Prototyp zeigt diese Kopplung mit Session Storage; die Anwendung muss ihre vorhandene serverseitige Persistenz und Konfliktbehandlung behalten.

Unbekannt wird als eigener Zustand angeboten. Ein leeres Zahlenfeld wird nicht zu null umgedeutet; Ja und Nein bleiben von einer fehlenden Angabe unterscheidbar. Pflichtfelder, Validierungsfehler, Speichern im Gang und Konflikte sind mit den bestehenden Formularen und [Interaktionsmustern](docs/interaction-patterns.md) abzugleichen; die Studie zeigt diese Zustände nicht vollständig.

### Side sheet, feedback and accessibility

Fahrzeugstammdaten, technische Daten und längere Notizen verwenden dasselbe Seitenpanel. Es ist auf dem Desktop (450px) breit, auf die Viewportbreite begrenzt und vertikal scrollbar. Mobil füllt es die Breite, mit (27px 21px) Innenabstand. Das Panel ist ein benannter modaler Dialog, hält den Tastaturfokus innerhalb, sperrt den Hintergrundscroll und führt den Fokus beim Schliessen zurück. Die Oberfläche enthält sichtbare Schliessen-, Abbrechen- und Speicherhandlungen.

Die kurze Speicherbestätigung ist ein `role="status"`-Hinweis, auf Desktop unten zentriert und auf Mobilbreite begrenzt. Der Prototyp zeigt sie (3200ms). Fehler, Konflikte oder notwendige Handlungen dürfen nicht in einer flüchtigen Bestätigung verschwinden; dafür gelten die vorhandenen Produktzustände und Interaktionsregeln.

Die sichtbare Fokusregel gilt für Buttons, Links, Formularelemente und den Vergleichs-Scrollbereich. Sprunglink, sinnvolle Überschriften, echte Tabellenköpfe, sichtbare Labels, Tastaturbedienung und Statuswörter gehören zur Übertragung. Die Prototyp-Prüfung aus dem [README](prototypes/design/README.md#verification) belegt nur die Studie, nicht die Barrierefreiheit oder Funktion einer fertig migrierten Anwendung.

### Prototype to production reference

| Bestätigtes Muster | Prototypquelle | Bestehender Produktionsbezug |
| --- | --- | --- |
| Canvas-Kopf und Kontext | `topNav()`, `context()` und Canvas-CSS | [Layout](src/Steervo.Web/Pages/Shared/_Layout.cshtml), [Evaluationskopf](src/Steervo.Web/Pages/Evaluations/_EvaluationHeader.cshtml), [Workspace](src/Steervo.Web/Pages/Evaluations/_Workspace.cshtml) |
| Kandidatenwechsel und Fahrzeugakte | `candidateRibbon()`, `record()` | [Kandidatenliste](src/Steervo.Web/Pages/Evaluations/Candidates/_CandidateList.cshtml), [Fahrzeugdetails](src/Steervo.Web/Pages/Evaluations/Candidates/_CandidateDetails.cshtml) |
| Bewertung und Vergleich | `assessmentRow()`, `comparison()`, `editorForm()` | [Bewertungszeile](src/Steervo.Web/Pages/Evaluations/Candidates/_AssessmentRow.cshtml), [Vergleich](src/Steervo.Web/Pages/Evaluations/Candidates/_Comparison.cshtml) |
| Fakten und wiederkehrende Formulare | `specs()`, `otherEditor()`, Feld-CSS | [Technische Daten](src/Steervo.Web/Pages/Evaluations/Candidates/_Specifications.cshtml), [Fahrzeugfelder](src/Steervo.Web/Pages/Evaluations/Candidates/_CandidateFields.cshtml) |
| Offerten, Dokumente, Versionen | `offers()` als unvollständige Layoutreferenz | [Offertdetails](src/Steervo.Web/Pages/Evaluations/Offers/_OfferDetails.cshtml), [Dokumente](src/Steervo.Web/Pages/Evaluations/Offers/_OfferDocuments.cshtml) |
| Gemeinsame Gestaltung und Verhalten | gemeinsame Regeln in `round-three.css`; Editor- und Fokusverhalten in `design.js` | [Produktions-CSS](src/Steervo.Web/wwwroot/css/site.css), bestehende Razor-Formulare und HTMX-Verhalten; vollständige Zuordnung in der [Designmigration](docs/design-migration.md) |

Diese Tabelle ist eine Einstiegshilfe, keine vollständige Migrationsliste. Die Studie enthält unter anderem Anmeldung, Freigaben, Kosten, Szenarien und Bestätigungsdialoge nicht vollständig. Alle vorhandenen Funktionen werden in denselben visuellen Rahmen übertragen. Die in der Studie enthaltenen Galerie-, Layoutwechsel- und Palettenbedienungen gehören nicht zur Anwendung.

## Do's and Don'ts

### Do:

- **Do** Canvas mit Tinte als visuelle Grundlage für die bestehende Anwendung verwenden und den vollständigen vorhandenen Funktionsumfang erhalten.
- **Do** dieselben Buttons, Felder, Statusbegriffe und Rückmeldungen beim Erfassen und Vergleichen verwenden.
- **Do** Quellen, Einheiten, persönliche Einschätzungen und offene Angaben sichtbar voneinander unterscheiden.
- **Do** den Fokus mit einer 3px-Kontur und 3px Abstand zeigen, beim Schliessen zum Auslöser zurückführen und reduzierte Bewegung respektieren.
- **Do** den Vergleich auf kleinen Bildschirmen innerhalb seiner Region scrollen lassen und den Editor ausserhalb dieser Region halten.
- **Do** sekundären Text im getönten Inline-Editor mit dem korrigierten Token editor-muted darstellen.
- **Do** die Fahrzeugakte ohne Bilder vollständig benutzbar halten und den bestehenden Umfang vor neuen Funktionen migrieren.

### Don't:

- **Don't** Studio, Galeriebedienung, alternative Paletten oder frühere Entwurfsrunden in das gewählte Zielsystem übernehmen.
- **Don't** Farben allein als Status verwenden, unbekannte Angaben als null darstellen oder persönliche Einschätzungen als objektiv erfüllt bewerten.
- **Don't** aus einem verfehlten Muss einen automatischen Ausschluss oder aus Einzelbewertungen eine Gesamtnote ableiten.
- **Don't** die korrigierte Editor-Textfarbe durch den allgemeinen muted-Token ersetzen.
- **Don't** Beispielbilder, Demowerte oder Session-Storage-Verhalten als Produktionsanforderungen behandeln.
- **Don't** unverifizierte Fehler-, Lade- oder Dialogzustände als bereits gestaltete und getestete Produktionskomponenten ausgeben.
