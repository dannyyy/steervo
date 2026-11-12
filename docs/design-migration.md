# Canvas/Tinte: Migration der bestehenden Anwendung

## Entscheidung und Priorität

**Verbindlich gewählt: Canvas mit Tinte.** Die Hauptentwicklung findet auf `main` statt; das Repository hat keinen `master`-Branch. Der Nutzerauftrag ist: zuerst das bestehende Design und Layout umstellen sowie Abläufe und Dialoge vereinheitlichen und optimieren, danach weitere Features entwickeln.

Diese Datei ist der Umsetzungsplan für den vorhandenen Community-Funktionsumfang. Sie beschreibt keine bereits abgeschlossene Migration. Die Auswahl ist abgeschlossen; die untenstehenden Umsetzungspakete sind offen.

| Grundlage | Zweck |
| --- | --- |
| [PRODUCT.md](../PRODUCT.md) | Fachliche Bedeutung und unveränderliche Produktregeln |
| [DESIGN.md](../DESIGN.md) | Verbindliche Gestaltung und Tokens für Canvas/Tinte |
| [Interaktionsmuster](interaction-patterns.md) | Einheitliches Verhalten von Navigation, Formularen, Dialogen und Rückmeldungen |
| [Prototyp](../prototypes/design/README.md) | Interaktive Referenz; Startseite öffnet Canvas/Tinte |
| [Auswahl](../prototypes/design/selection.json) | Maschinenlesbare Designentscheidung |
| [Design-Erweiterungen](../.impeccable/design.json) | Schatten, Bewegung, Breakpoints und isolierte Komponentenbeispiele |

Die Prototyp-Varianten Studio, Petrol und Pflaume sowie die alten Entwurfsrunden sind Vergleichsmaterial, keine parallelen Produkt-Themes. Der Farb-/Layoutumschalter und der Entwurfsbalken gehören nicht in die Anwendung.

## Ausgangslage und Grenzen

Die funktionale Basis ist `main` mit `3c23be9` (0.2.0). `prototype` ergänzt mit `b610c56` Designwerkzeuge, keine veränderte Anwendung. `prototype-codex` entstand daraus für die Layoutstudie. Die Anwendung nutzt Razor Pages, HTMX und lokale Skripte; ihre Fach- und Berechnungsschichten werden für diese Migration nicht ersetzt.

Der Prototyp demonstriert insbesondere Erfassen und Vergleichen mit Beispieldaten. Er ersetzt weder serverseitige Speicherung noch Rechteprüfung, Offertversionierung, Dokumente oder Berechnungen. Seine JS-Datenhaltung, künstlichen Statuswerte und Demo-Reset-Funktion werden nicht in die Anwendung übernommen. Beispielsweise bleibt die echte Priorität **stark gewünscht** erhalten, obwohl die Layoutstudie verkürzt **Gewünscht** zeigt.

Die drei Fahrzeugfotos illustrieren die Komposition. Ein Fahrzeugbild-Upload ist kein Bestandteil des vorhandenen Produkts und kein Auftrag dieser Migration. Die Fahrzeugansicht wird zunächst vollständig mit realen Stammdaten und einem neutralen Zustand ohne Foto gestaltet. Neue Bildfunktionen werden erst nach Abschluss der Migration gesondert behandelt. Herkunft der Referenzbilder: [Provenienz](../prototypes/design/assets/provenance.json).

## Gemeinsames Zielbild

- Horizontale Navigation und ein konsistenter Evaluationskontext. Die vorhandenen Bereiche bleiben erreichbar: Kriterien, Autos, Fahrzeugvergleich, Offerten, Kosten, Rechnen, Freigaben und Einstellungen. Das Mockup zeigt nur einen Ausschnitt davon.
- Fahrzeugdetails als zusammenhängende Akte: kompakter Einstieg mit Identität und Stammdaten, danach Anforderungen und Notizen. Aufgaben erhalten eine erkennbare Hauptaktion; Nebenaktionen erscheinen am betroffenen Inhalt.
- Dieselben Werte, Zustände und Bearbeitungsmuster beim Erfassen und Vergleichen. Ein Wechsel erhält Evaluation, ausgewählte Fahrzeuge und die sinnvolle Rückkehrposition.
- Einheitliche Felder, Fehlermeldungen, Speichern/Abbrechen-Aktionen, Seitentitel und Navigation. Keine verschiedenen Editorformen für dieselbe Aufgabe.
- Funktionen inhaltlich zusammenführen, ohne Fachobjekte zusammenzulegen: technische Fakten sind keine Bewertungen; Fahrzeug- und Offertbewertungen haben unterschiedliche Bezüge; Kostenplanung und Rechenszenarien haben unterschiedliche Aufgaben.
- Desktop und mobile Weboberfläche werden pro Paket gemeinsam umgesetzt. Kleine Bildschirme sind keine nachträgliche CSS-Reparatur.

## Reihenfolge und Arbeitspakete

Jedes Paket umfasst Quellcode, notwendige Tests und eine überprüfte echte Oberfläche. Nur eine Phase mit nachgewiesenen Abnahmekriterien wird abgehakt. Während der Übergangsphase darf die Migration schrittweise erfolgen; bei Abschluss darf kein alter Bereich vergessen bleiben.

### 0. Ausgangsstand sichern

- [ ] Anwendung und bestehende Tests auf `main` starten; die Befehle stehen im [README](../README.md#prüfen).
- [ ] Mit isolierten Testdaten aktuelle Desktop-/Mobilansichten und wichtige Fehler-/Leerzustände aufnehmen. Artefakte lokal unter `.artifacts/browser/` halten.
- [ ] Vorhandene Browser-Selektoren, HTMX-Ziele und Fokus-/Scroll-Erhaltung vor Markupänderungen erfassen.
- [ ] Das Inventar unten um konkret gefundene Sonderfälle ergänzen; keine erfolgreiche Mockup-Prüfung als Produktionsprüfung werten.

### 1. Gestaltungsgrundlage und durchgehender Kernablauf

**Erstes Umsetzungspaket:** gemeinsames Canvas-Layout und Tinte-Tokens, anschließend eine reale Fahrzeugakte mit Bewertung und Wechsel in den Vergleich.

- [ ] Geist lokal mit OFL-Lizenz ausliefern; Farben, Größen, Abstände, Fokus, Zustände und Bewegungen zentral definieren.
- [ ] `_Layout.cshtml` und `_EvaluationHeader.cshtml` auf horizontale, responsive Navigation umstellen. Vorhandene Bereiche und rollenabhängige Zugänge erhalten; die nummerierte Darstellung nicht als erzwungene Reihenfolge übernehmen.
- [ ] Gemeinsame Razor-Teilansichten beziehungsweise kleine, tatsächlich verwendete UI-Bausteine für Buttons, Felder, Status, Hinweise und Editoraktionen einführen. Keine neue allgemeine Plugin- oder Komponentenplattform bauen.
- [ ] Kandidatenliste, Fahrzeugakte und Fahrzeugvergleich als zusammenhängenden Ablauf portieren. Bearbeitung einer Bewertung in beiden Ansichten verwendet denselben Editor und dieselben serverseitigen Regeln.
- [ ] Wechsel zwischen Detail und Vergleich erhält die Fahrzeugauswahl. Nach Speichern bleibt auch die bearbeitete zweite Vergleichsspalte auf Mobilgeräten sichtbar.
- [ ] Auf allen berührten Seiten das gemeinsame Formular-/Dialogverhalten aus Phase 2 bereits verwenden.

**Abnahme:** Ein echtes Fahrzeug erfassen, Messwert und Quelle ergänzen, in den Vergleich wechseln, dort ändern und in der Akte wiederfinden; auf Desktop und Mobilgerät, mit Tastatur sowie als Besitzer/Bearbeiter/Leser. Unbekannt, Nein und null bleiben korrekt unterscheidbar. Keine bestehende Funktion dieses Ablaufs wird entfernt.

### 2. Evaluationen, Kriterien und gemeinsame Interaktionen

- [ ] Evaluationsübersicht, Anlegen, Umbenennen, Import/Export, Freigaben und Einstellungen konsistent gestalten.
- [ ] Kriterien und Gruppen anlegen, bearbeiten, verschieben und sortieren; Quellen und Bewertungen erhalten. Tastatur-/Touch-Sortierung, offene Entwürfe und Scrollposition prüfen.
- [ ] Fahrzeug anlegen, Stammdaten ändern, kopieren, klassifizieren und entfernen in das festgelegte Editor-/Bestätigungsmuster überführen. Begründung für Zurückstellen und Ausscheiden beibehalten.
- [ ] Gemeinsames Dialogverhalten implementieren: Titel, Anfangsfokus, Escape, Rückkehrfokus, ungespeicherte Eingaben, Validierung und Fehlerrückmeldung.
- [ ] Browser-`confirm()` durch das gemeinsame Bestätigungsmuster ersetzen, wo eine irreversible Aktion bestätigt werden muss. Kein Bestätigungsdialog für normales Speichern.

**Abnahme:** Einmal erlernte Interaktionen verhalten sich überall gleich. Abbruch, Validierungsfehler, veraltete Versionsstände, fehlende Berechtigung und Netzfehler verlieren keine bereits eingegebenen Werte.

### 3. Offerten und Dokumente

- [ ] Offertenliste, Detail, Bearbeitung, Versionshistorie und Vergleich nach derselben Hierarchie gestalten.
- [ ] Aktuelle und historische Versionen deutlich unterscheiden. Ein neuer Stand bleibt eine neue Version; alte Stände bleiben unverändert.
- [ ] Offertbewertungen konsistent mit Fahrzeugbewertungen bearbeiten, aber ihren Angebotsbezug und die notwendige erneute Prüfung nach Versionswechsel sichtbar halten.
- [ ] Upload, Dateiversionen, Download und Entfernen in die gemeinsame Rückmeldungs- und Dialogstruktur übernehmen.
- [ ] Archivieren/Wiederherstellen verständlich von dauerhaftem Entfernen unterscheiden.

**Abnahme:** Zwei Offerten desselben Fahrzeugs vergleichen, eine neue Version mit Dokument ergänzen und alte Versionen weiter lesen. Währungssperre, inklusive/offene Preise, Uploadgrenzen und aktuelle Downloadrechte bleiben wirksam.

### 4. Kosten und Rechenszenarien

- [ ] Kostenübersicht, Planungszeitraum, Positionen und Monats-/Jahresansichten vereinheitlichen.
- [ ] Lange Kosten- und Szenarioformulare als klar gegliederte Seiten gestalten; keine großen Formulare in enge Dialoge pressen.
- [ ] Szenarioauswahl, Preisbestätigung, Annahmen, Ergebnis und Vergleich konsistent zusammenführen.
- [ ] Währungen, Einheiten, Zeiträume, Offertversionen und offene Angaben direkt an den betroffenen Werten zeigen.

**Abnahme:** Derselbe Datenbestand liefert unveränderte Berechnungsergebnisse. Fehlende Beträge, Restwerte oder Annahmen bleiben sichtbar offen. Vergleich, Szenariokopien und angepasste Kosten behalten ihre bestehenden Regeln.

### 5. Konto, Einstieg, Systemzustände und Abschluss

- [ ] Login, Verifikation, Ersteinrichtung, Konto, Passkeys und Einladungen in die gemeinsame Gestaltung überführen.
- [ ] Fehlerseite, Verbindungsfehler, Ladezustände, leere Bereiche, gesperrte Aktionen und Leserechte vollständig gestalten.
- [ ] Alte, nicht mehr verwendete Styles und ersetzte Dialogskripte entfernen; Ressourcen, direkte URLs, Zurücknavigation und Fokusziele überprüfen.
- [ ] Vollständige Regression und visuelle Prüfung aller Bereiche durchführen und verbleibende Unterschiede bewusst auflösen.

**Abnahme:** Die gesamte bestehende Anwendung folgt Canvas/Tinte und den gemeinsamen Interaktionsmustern. Erst nach diesem Abschluss werden neue Features priorisiert.

## Inventar der vorhandenen Oberflächen

| Bereich | Bestehende Einstiegspunkte / Dateien | Migrationsschwerpunkt |
| --- | --- | --- |
| App-Rahmen | `Pages/Shared/_Layout.cshtml`, `wwwroot/css/site.css` | Navigation, Typografie, Tokens, Footer, globale Hinweise |
| Evaluation / Navigation | `Pages/Index.cshtml`, `Pages/Evaluations/_EvaluationHeader.cshtml` | Kontext, Übersicht, Anlegen, Import, Export, erreichbare Bereiche |
| Kriterien / Gruppen / Freigaben | `Pages/Evaluations/Details.cshtml`, `_Workspace.cshtml`, `_CriterionFields.cshtml` | Bearbeitung, Reihenfolge, Rollen, Einstellungen |
| Kandidaten | `Pages/Evaluations/Candidates/Index.cshtml`, `Details.cshtml`, `_CandidateList.cshtml`, `_CandidateDetails.cshtml` | Auswahl, Akte, Anlage, Klassifikation, Kopie |
| Bewertungen / Vergleich | `Candidates/_AssessmentRow.cshtml`, `Compare.cshtml`, `_Comparison.cshtml` | Gemeinsamer Editor, Spaltenkontext, Unterschiede |
| Technische Daten | `Candidates/_Specifications.cshtml`, `_SpecificationsFields.cshtml`, `_SpecificationsComparison.cshtml` | Faktengruppen, Einheiten, Kennzeichnung berechneter Angaben |
| Offerten | `Pages/Evaluations/Offers/` | Neue Version, Details, Vergleich, Historie, Bewertungen |
| Dokumente | `Offers/_OfferDocuments.cshtml`, `Upload.cshtml`, `Pages/Evaluations/Document.cshtml` | Upload, Versionen, Rechte und Rückmeldungen |
| Kosten | `Pages/Evaluations/Costs/Index.cshtml`, `Edit.cshtml` | Übersicht, vollständiges Formular, Zahlungszeitpunkte |
| Rechnen | `Pages/Evaluations/Scenarios/` | Annahmen, Ergebnis, Vergleich, Offertbindung |
| Einstieg / Konto | `Pages/Auth/`, `Pages/Account/` | Einheitliche Felder, Verifikation, Passkeys, Rollen |
| Fehler / Status | `Pages/Error.cshtml`, Layout, HTMX-Hinweise | Verständliche Wiederherstellung und zugängliche Meldungen |
| Interaktionsskripte | `wwwroot/js/site.js`, `workspace.js`, `sorting.js`, `specifications.js`, `offers.js` | HTMX-Lebenszyklus, Fokus, Entwürfe, Sortierung, Bestätigungen |

Dateipfade ohne Präfix beziehen sich auf `src/Steervo.Web/`. Sie beschreiben interne Implementierung, keine öffentliche API.

## Technische Leitplanken

- Fachobjekte, Berechnungsergebnisse, gespeicherte Daten, Berechtigungen und Import-/Exportkompatibilität bleiben erhalten. UI-Refactoring rechtfertigt keine Datenbankmigration ohne fachlichen Bedarf.
- HTMX-Fragmente erhalten stabile IDs und dieselben Sicherheits-/Validierungsregeln wie volle Seiten. `409` und `422`, Versionsfelder, Anti-Forgery-Tokens, Entwürfe und Fokus sind Teil der Migration.
- Autorisierung bleibt serverseitig; ausgeblendete Bedienelemente sind kein Zugriffsschutz.
- Neue sichtbare Texte in `Ui.resx`; keine zufälligen Emoji-/Unicode-Symbole als Icons. Bestehende konsistente SVG-Symbole beziehungsweise gemeinsam gepflegte Icons verwenden.
- Styles schrittweise in eine gemeinsame Basis überführen, statt den Mockup-CSS-Override-Stapel auf Produktionsseiten zu kopieren.
- Keine echten Benutzer-, Angebots- oder Dokumentdaten in Screenshots, Commits oder Beispielinhalten verwenden.
- Keine externe Bild-, Schrift- oder Skriptabhängigkeit für die gewählte Oberfläche einführen. Neue öffentliche Inhalte beachten [CLAUDE.md](../CLAUDE.md).

## Prüfen und Abschlusskriterien

Bestehende Befehle, keine neue Testplattform:

```sh
dotnet restore Steervo.sln --locked-mode
dotnet build Steervo.sln --no-restore
./scripts/test.sh
./scripts/test.sh --browser
```

Die Voraussetzungen und Containergrenzen stehen im [README](../README.md#prüfen). Relevante vorhandene Tests liegen unter `tests/Steervo.Tests/` und `tests/browser/`. Fachtests bleiben maßgeblich; UI-Selektoren dürfen angepasst werden, Assertions zur Fachlogik nicht stillschweigend abgeschwächt werden.

Für jedes Paket: tatsächliche Razor-Oberfläche mit Playwright bei Desktop- und Mobilbreite prüfen (mindestens 1440 und 390 px; 320, 768 und 1024 px als Strukturgrenzen), nicht nur den Mockup. Bei geänderten Formen außerdem leere Daten, lange Namen, Validierung, Abbruch, Rollen, Netzfehler und konkurrierende Änderungen. Tastaturbedienung, Fokuswiederherstellung, Scrollposition und Entwurfserhalt sind explizite Abnahmepunkte.

Am Ende müssen alle folgenden Punkte nachgewiesen sein:

- [ ] Alle vorhandenen Bereiche sind umgestellt und erreichbar; keine fachliche Regression.
- [ ] Einheitliche Navigation, Formulare, Status, Aktionen und Dialoge auf Desktop und Mobilgeräten.
- [ ] Keine seitenweite horizontale Überbreite; Vergleichstabellen dürfen innerhalb eines klar bezeichneten Bereichs scrollen, wobei die Kriterien erkennbar bleiben.
- [ ] Lesbarer Kontrast, sichtbarer Fokus, passende Labels, erreichbare Aktionen und reduzierte Bewegung.
- [ ] Bestehende Tests erfolgreich und relevante neue UI-Risiken sinnvoll geprüft.
- [ ] Dokumentation entspricht der tatsächlichen Umsetzung; offene Punkte und bewusste Abweichungen sind benannt.
- [ ] Abschluss der Design-/UX-Migration festgehalten, bevor weitere Features begonnen werden.

## Git-Übergabe

Der Hauptbranch heisst `main`. Die ausgewählte Referenz und diese Dokumentation werden als eigener nachvollziehbarer Commit übernommen. Der breite Designwerkzeug-Commit `b610c56` ist dafür keine Voraussetzung. Die Prototyp-Branches und der andere Design-Worktree bleiben als Referenzen erhalten.

Die Implementierung beginnt aus dem aktualisierten `main`; kurze Umsetzungsbranches dürfen jeweils ein Paket abbilden und nach Prüfung wieder in `main` einfliessen. Keine erneute Auswahlrunde für Studio oder alternative Farben starten, solange der Nutzer Canvas/Tinte nicht ändert. Kein Release oder Deployment wird durch diese Dokumentationsübergabe ausgelöst.
