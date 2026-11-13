# Canvas/Tinte: Migration der bestehenden Anwendung

## Entscheidung und Priorität

**Verbindlich gewählt: Canvas mit Tinte.** Die Hauptentwicklung findet auf `main` statt; das Repository hat keinen `master`-Branch. Der Nutzerauftrag ist: zuerst das bestehende Design und Layout umstellen sowie Abläufe und Dialoge vereinheitlichen und optimieren, danach weitere Features entwickeln.

Diese Datei dokumentiert die abgeschlossene Migration des vorhandenen Community-Funktionsumfangs. **Alle Phasen 0–5 sind seit dem 13. November 2026 umgesetzt und verifiziert.** Weitere Entwicklung findet auf `main` mit Canvas/Tinte und den gemeinsamen Interaktionsmustern statt. Neue Features werden gesondert priorisiert.

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

- [x] Anwendung und bestehende Tests auf `main` starten; die Befehle stehen im [README](../README.md#prüfen).
- [x] Mit isolierten Testdaten aktuelle Desktop-/Mobilansichten und wichtige Fehler-/Leerzustände aufnehmen. Artefakte lokal unter `.artifacts/browser/` halten.
- [x] Vorhandene Browser-Selektoren, HTMX-Ziele und Fokus-/Scroll-Erhaltung vor Markupänderungen erfassen.
- [x] Das Inventar unten um konkret gefundene Sonderfälle ergänzen; keine erfolgreiche Mockup-Prüfung als Produktionsprüfung werten.

### 1. Gestaltungsgrundlage und durchgehender Kernablauf

**Erstes Umsetzungspaket:** gemeinsames Canvas-Layout und Tinte-Tokens, anschließend eine reale Fahrzeugakte mit Bewertung und Wechsel in den Vergleich.

- [x] Geist lokal mit OFL-Lizenz ausliefern; Farben, Größen, Abstände, Fokus, Zustände und Bewegungen zentral definieren.
- [x] `_Layout.cshtml` und `_EvaluationHeader.cshtml` auf horizontale, responsive Navigation umstellen. Vorhandene Bereiche und rollenabhängige Zugänge erhalten; die nummerierte Darstellung nicht als erzwungene Reihenfolge übernehmen.
- [x] Gemeinsame Razor-Teilansichten beziehungsweise kleine, tatsächlich verwendete UI-Bausteine für Buttons, Felder, Status, Hinweise und Editoraktionen einführen. Keine neue allgemeine Plugin- oder Komponentenplattform bauen.
- [x] Kandidatenliste, Fahrzeugakte und Fahrzeugvergleich als zusammenhängenden Ablauf portieren. Bearbeitung einer Bewertung in beiden Ansichten verwendet denselben Editor und dieselben serverseitigen Regeln.
- [x] Wechsel zwischen Detail und Vergleich erhält die Fahrzeugauswahl. Nach Speichern bleibt auch die bearbeitete zweite Vergleichsspalte auf Mobilgeräten sichtbar.
- [x] Auf allen berührten Seiten das gemeinsame Formular-/Dialogverhalten aus Phase 2 bereits verwenden.

**Abnahme:** Ein echtes Fahrzeug erfassen, Messwert und Quelle ergänzen, in den Vergleich wechseln, dort ändern und in der Akte wiederfinden; auf Desktop und Mobilgerät, mit Tastatur sowie als Besitzer/Bearbeiter/Leser. Unbekannt, Nein und null bleiben korrekt unterscheidbar. Keine bestehende Funktion dieses Ablaufs wird entfernt.

### 2. Evaluationen, Kriterien und gemeinsame Interaktionen

- [x] Evaluationsübersicht, Anlegen, Umbenennen, Import/Export, Freigaben und Einstellungen konsistent gestalten.
- [x] Kriterien und Gruppen anlegen, bearbeiten, verschieben und sortieren; Quellen und Bewertungen erhalten. Tastatur-/Touch-Sortierung, offene Entwürfe und Scrollposition prüfen.
- [x] Fahrzeug anlegen, Stammdaten ändern, kopieren, klassifizieren und entfernen in das festgelegte Editor-/Bestätigungsmuster überführen. Begründung für Zurückstellen und Ausscheiden beibehalten.
- [x] Das in Phase 1 eingeführte gemeinsame Dialogverhalten auf Evaluationen, Kriterien, Gruppen, Freigaben und Einstellungen übertragen: Titel, Anfangsfokus, Escape, Rückkehrfokus, ungespeicherte Eingaben, Validierung und Fehlerrückmeldung.
- [x] Browser-`confirm()` durch das gemeinsame Bestätigungsmuster ersetzen, wo eine irreversible Aktion bestätigt werden muss. Kein Bestätigungsdialog für normales Speichern.

**Abnahme:** Einmal erlernte Interaktionen verhalten sich überall gleich. Abbruch, Validierungsfehler, veraltete Versionsstände, fehlende Berechtigung und Netzfehler verlieren keine bereits eingegebenen Werte.

### 3. Offerten und Dokumente

- [x] Offertenliste, Detail, Bearbeitung, Versionshistorie und Vergleich nach derselben Hierarchie gestalten.
- [x] Aktuelle und historische Versionen deutlich unterscheiden. Ein neuer Stand bleibt eine neue Version; alte Stände bleiben unverändert.
- [x] Offertbewertungen konsistent mit Fahrzeugbewertungen bearbeiten, aber ihren Angebotsbezug und die notwendige erneute Prüfung nach Versionswechsel sichtbar halten.
- [x] Upload, Dateiversionen, Download und Entfernen in die gemeinsame Rückmeldungs- und Dialogstruktur übernehmen.
- [x] Archivieren/Wiederherstellen verständlich von dauerhaftem Entfernen unterscheiden.

**Abnahme:** Zwei Offerten desselben Fahrzeugs vergleichen, eine neue Version mit Dokument ergänzen und alte Versionen weiter lesen. Währungssperre, inklusive/offene Preise, Uploadgrenzen und aktuelle Downloadrechte bleiben wirksam.

### 4. Kosten und Rechenszenarien

- [x] Kostenübersicht, Planungszeitraum, Positionen und Monats-/Jahresansichten vereinheitlichen.
- [x] Lange Kosten- und Szenarioformulare als klar gegliederte Seiten gestalten; keine großen Formulare in enge Dialoge pressen.
- [x] Szenarioauswahl, Preisbestätigung, Annahmen, Ergebnis und Vergleich konsistent zusammenführen.
- [x] Währungen, Einheiten, Zeiträume, Offertversionen und offene Angaben direkt an den betroffenen Werten zeigen.

**Abnahme:** Derselbe Datenbestand liefert unveränderte Berechnungsergebnisse. Fehlende Beträge, Restwerte oder Annahmen bleiben sichtbar offen. Vergleich, Szenariokopien und angepasste Kosten behalten ihre bestehenden Regeln.

### 5. Konto, Einstieg, Systemzustände und Abschluss

- [x] Login, Verifikation, Ersteinrichtung, Konto, Passkeys und Einladungen in die gemeinsame Gestaltung überführen.
- [x] Fehlerseite, Verbindungsfehler, Ladezustände, leere Bereiche, gesperrte Aktionen und Leserechte vollständig gestalten.
- [x] Alte, nicht mehr verwendete Styles und ersetzte Dialogskripte entfernen; Ressourcen, direkte URLs, Zurücknavigation und Fokusziele überprüfen.
- [x] Vollständige Regression und visuelle Prüfung aller Bereiche durchführen und verbleibende Unterschiede bewusst auflösen.

**Abnahme:** Die gesamte bestehende Anwendung folgt Canvas/Tinte und den gemeinsamen Interaktionsmustern. Erst nach diesem Abschluss werden neue Features priorisiert.

## Nachweis Phase 0 und 1 vom 13. November 2026

**Stand des ersten Umsetzungspakets:** Ausgangsprüfung und Kernablauf abgeschlossen. Die echte Razor-Anwendung verwendet den horizontalen Canvas-Rahmen und lokal ausgeliefertes Geist. Fahrzeugliste, Anlage, unabhängige Kopie, Akte, Einordnung, Stammdaten, Bewertungen und Fahrzeugvergleich sind migriert. Der Rahmen erhält alle bisherigen Bereiche und rollenabhängigen Zugänge. Andere Fachbereiche erhalten damit die gemeinsame Basis, sind aber noch nicht vollständig nach den neuen Interaktionsregeln überarbeitet.

Die Akte zeigt Anforderungen als lesbare Zeilen und Notizen auf einer zusammenhängenden Fläche. Technische Daten sind über einen eigenen Tab erreichbar. Dieselbe Teilansicht `_AssessmentEditor.cshtml` bearbeitet Bewertungen in Akte und Vergleich. Im Vergleich folgt der Editor auf die echte HTML-Tabelle, außerhalb des horizontalen Scrollbereichs. Die ausgewählte Fahrzeugkombination bleibt in den Links zwischen beiden Ansichten erhalten. Die Akte funktioniert ohne Fotos.

Anlage, Kopie, Stammdaten und Einordnung verwenden native seitliche Dialoge. Speichern/Abbrechen, Schließen, Escape, Entwurfsschutz, Rückkehrfokus und lokale Fehlerdarstellung teilen sich dieselben Regeln. Zurücksetzen einer Bewertung und Entfernen eines Fahrzeugs verwenden den gemeinsamen Bestätigungsdialog mit sicherem Anfangsfokus. Fahrzeuganlage und Kopie senden per HTMX; eine erfolgreiche Serverantwort navigiert zur neuen Akte. Die vorhandenen Fachoperationen bleiben unverändert.

**Fehler und Zustände:** HTTP 422 erhält auch ungültige Werte und Notizen. HTTP 409 zeigt den aktuellen gespeicherten Stand und bewahrt den lokalen Entwurf, einschließlich während der Prüfung dieses Standes. Erst eine erneute bewusste Speicherung sendet ihn mit der aktuellen Version. Verbindungsfehler lassen die Eingabe offen. Ein HTTP-403-Fehler oder eine durch abgelaufene Anmeldung verursachte Weiterleitung zur Loginseite darf den Entwurf nicht ersetzen; weitere Schreibaktionen werden gesperrt. Fachliche Rechteprüfungen bleiben serverseitig.

### Überprüfte Schnittstellen und Sonderfälle

- `#candidate-workspace`, `data-version`, Anti-Forgery-Felder, Objekt-IDs und die vorhandenen POST-Aktionen bleiben erhalten. HTMX tauscht den Workspace weiterhin mit `outerHTML show:none focus-scroll:false`; gleichzeitige Requests werden synchronisiert.
- Stabile IDs für Bewertung, Öffner und Editor ermöglichen die Fokusrückkehr nach dem Fragmentaustausch. Scrollpositionen von Kandidatenwechsel und Vergleich werden separat vom Seiten-Viewport wiederhergestellt. Auch die zweite Vergleichsspalte bleibt nach Speichern sichtbar.
- Die früher ständig sichtbaren Bewertungsformulare und verschachtelten Vergleichsformulare sind durch gezielt geöffnete Editoren ersetzt. Browsertests öffnen sie über zugängliche Aktionen; ihre fachlichen Assertions für unbekannt/Nein/null, Kopien, Rechte und Berechnungen bleiben bestehen.
- Die alten Nummern in der Evaluationsnavigation und der reine CSS-Selektor des Exportlinks entfallen. Browserprüfungen verwenden die weiterhin sichtbaren Namen der Bereiche und des Exports.
- Die Kriterien-Sortierung und ihre bestehenden IDs bleiben unverändert. Offene Details, Eingaben, Fokus und Scrollposition werden auch nach Konfliktantworten wiederhergestellt.
- Die Gestaltungsgrundlage liegt in `wwwroot/css/tokens.css`, die Canvas-Komponenten in `canvas.css`; `site.css` trägt die weiter vorhandenen Fachlayouts. Die bisherigen Sidebar-Regeln und ersetzte fahrzeugspezifische Layoutregeln wurden entfernt. Die vollständige Bereinigung der übrigen Bereiche gehört zu deren Migration.

### Nachweise

- Ausgangsstand: 100 erfolgreiche Fach-/Integrationstests und bestehender vollständiger Browserlauf vor dem Umbau. Vergleichsbilder liegen lokal unter `.artifacts/browser/before-canvas/`.
- Nach Umsetzung: `dotnet build src/Steervo.Web --no-restore` ohne Warnungen/Fehler; `PLAYWRIGHT_EXECUTABLE_PATH='/Applications/Google Chrome.app/Contents/MacOS/Google Chrome' ./scripts/test.sh --browser` mit 100 erfolgreichen Tests und 47 erfolgreichen Browserprüfungen. Dieser Lauf umfasst die bestehenden Produktbereiche und die neuen Kernabläufe.
- Neue gezielte Regressionen in `tests/browser/canvas.mjs`: Fahrzeugpaar bei Hin-/Rückwechsel, Tastaturfokus, Escape und bewusstes Verwerfen, mobile zweite Spalte, Netzfehler, 422, 409 mit erhaltenem Entwurf, Navigation während der Konfliktprüfung und tatsächlich abgelaufene Anmeldung. Dialoganlage und abgebrochene Löschung werden zusätzlich geprüft.
- Echte Anwendung mit synthetischen Daten bei 1440 und 390 px visuell geprüft, einschließlich Akte, Vergleich, Inline-Editor und Seitenpanel. Strukturprüfungen bei 320, 768 und 1024 px ergeben keinen seitenweiten horizontalen Überlauf. Artefakte bleiben unter `.artifacts/` und werden nicht eingecheckt.

## Phase 2: Evaluationen und gemeinsame Interaktionen

**Abgeschlossen am 13. November 2026.** Die Evaluationsübersicht zeigt die vorhandenen Entscheidungen auf einer gemeinsamen Fläche. Anlage und JSON-/ZIP-Import verwenden dieselbe seitliche Bearbeitungsfläche wie Fahrzeuge. Importfehler lassen die gewählte Datei im ursprünglichen Formular stehen; auch der Dateientwurf wird vor unbeabsichtigtem Verlassen geschützt. Ein erfolgreicher Import erzeugt weiterhin eine unabhängige private Evaluation mit allen bisherigen Daten und Versionen.

Kriterien und Gruppen liegen auf einer zusammenhängenden Arbeitsfläche. Die dauerhaft sichtbare Erfassungsspalte entfällt. Neue Kriterien werden im Seitenpanel erfasst; vorhandene Kriterien und kurze Gruppennamen bearbeiten Inline-Editoren mit denselben Feldern, Fehlern und Speichern-/Abbrechen-Aktionen. Kriterien-IDs, Gruppenzuordnung, Bewertungen und Quellen bleiben erhalten. Gruppenauflösung ordnet ihre Kriterien weiterhin ohne Gruppe ein. Kriterienlöschung und Evaluationslöschung nennen das konkrete Objekt im gemeinsamen Bestätigungsdialog.

Freigaben und Einstellungen haben eigene Ansichten im Evaluationskontext mit aktivem Navigationseintrag. Die Rolle lässt sich am freigegebenen Konto ändern; Lesen, Inhaltsbearbeitung und Besitzeraktionen bleiben serverseitig getrennt. Einstellungen zeigen Name und Zweck zunächst lesbar, danach den lokalen Editor, Export und die klar getrennte Löschaktion. Alte Verweise mit `#sharing` und `#case-settings` führen weiterhin zum richtigen Bereich.

### Technische Übergabe

- `_EvaluationOverview.cshtml` bündelt die Übersicht mit ihren Anlage-/Importdialogen. Erfolgreiche HTMX-Anlage und -Import navigieren per `HX-Redirect`; native POSTs behalten ihre normale Weiterleitung. Ein Importfehler liefert lokalisierten Text mit HTTP 422 und ersetzt den Dateieingabe-Editor nicht.
- `_Workspace.cshtml` behält sein bestehendes HTMX-Ziel und die Versions-/Anti-Forgery-Felder. `_Sharing.cshtml` und `_EvaluationSettings.cshtml` tragen die neuen Abschnitte; `section` erhält den aktiven Bereich auch nach Speichern.
- `_CriterionEditor`, `_CriterionEditorFields`, `_GroupEditor` und `_ShareEditor` verwenden die gemeinsamen Editoraktionen. Serverseitig gerenderte Fehlerantworten enthalten den Entwurf getrennt von den gespeicherten Standardwerten; bewusstes Verwerfen kehrt zu diesen zurück.
- `editors.js` erhält offene Editoren und deren Eingaben beim Sortieren, auch bei HTTP 409. `sorting.js` bleibt für Reihenfolge, Tastatur, Touch und Scrollposition verantwortlich. Die alten parallelen Wiederhersteller greifen nicht mehr auf die gemeinsam verwalteten Formulare zu.
- Nach fehlgeschlagenem Zurücksetzen/Entfernen bleibt auch ein Entwurf im benachbarten Hauptformular erhalten. Beim tatsächlichen Entfernen eines Eintrags kehrt der Fokus zur Abschnittsüberschrift zurück. Bei Rechteentzug bleibt der Entwurf sichtbar und weitere Schreibaktionen werden gesperrt.
- Die ersetzten Karten-, Sidebar-Erfassungs-, Popover- und Dialogregeln wurden aus `site.css` entfernt; ebenso der nicht mehr verwendete alte Dialoghandler aus `site.js`. Noch bestehende Browserbestätigungen in späteren Fachbereichen werden in deren Migrationspaketen ersetzt.

### Verifikation

- Abschlusslauf: `PLAYWRIGHT_EXECUTABLE_PATH='/Applications/Google Chrome.app/Contents/MacOS/Google Chrome' ./scripts/test.sh --browser` mit 100 erfolgreichen Fach-/Integrationstests und 56 erfolgreichen Browserprüfungen. Build ohne Warnungen und Fehler. Protokoll lokal unter `.artifacts/phase2/verification.log`.
- Playwright prüft weiterhin den gesamten bestehenden Funktionsumfang. `tests/browser/evaluations.mjs` ergänzt Netzfehler mit Wiederholung, veraltete Sortierung bei offenem Entwurf, Gruppenkonflikte, fehlgeschlagene Gruppenauflösung, Validierung in Einstellungen/Freigaben, Rollenänderung, tatsächlichen Rechteentzug, bestätigte Löschung und Erhalt der Kriterien beim Auflösen einer Gruppe.
- Die bestehende Sortierprüfung deckt lange Listen, Pfeiltasten, Rückkehrfokus, Drag & Drop zwischen Gruppen, leere Gruppen, Touch, Escape und erhaltene Eingaben ab. Der Import prüft ungültige Dateien, Erhalt der Dateiauswahl und anschliessenden erfolgreichen JSON-/ZIP-Import.
- Echte Oberfläche bei 1440 und 390 px visuell geprüft; Strukturprüfungen zusätzlich bei 320, 768 und 1024 px. Lange Namen, Leser/Bearbeiter/Besitzer, aktive Navigation und alte Abschnittslinks sind berücksichtigt. Lokale Screenshots liegen unter `.artifacts/browser/phase2/` sowie `.artifacts/browser/phase2-*.png`; Logs und Bilder bleiben ausserhalb der Commits.

## Phase 3: Offerten und Dokumente

**Abgeschlossen am 13. November 2026.** Offertenliste und Offertakte verwenden zusammenhängende Canvas-Flächen. Die Akte verbindet Angebotsangaben, Positionen, Änderungen, Einschätzungen, Dokumente und Historie über Abschnittslinks. Aktuelle und historische Versionen bleiben unterscheidbar; alte Stände sind unveränderlich. Archivieren und Wiederherstellen bleiben direkte reversible Aktionen, während dauerhaftes Entfernen eines Dokuments dessen Namen und Folgen im gemeinsamen Bestätigungsdialog zeigt.

Der Vergleich verwendet eine echte Tabelle mit fixierter Kriterienspalte auf Mobilgeräten. Das gewählte Offertenpaar bleibt in Detail, Versionsbearbeitung und Upload erhalten; die Rückkehr zum Vergleich stellt die betroffene Spalte wieder her. Ein Bewertungslink führt zum Kriterium in der Offertakte. Dort teilt die Offertbewertung den Editor mit Fahrzeugbewertungen, einschliesslich Wert, Grundlage, Notiz, Rückkehrfokus und Fehlerverhalten. Nach einem neuen Offertstand bleibt der Hinweis zur erneuten Prüfung sichtbar; die Bestätigung erfolgt ausdrücklich für die aktuelle Version.

Vollständige Offerten und Uploads bleiben eigene Formularseiten mit gemeinsamen Abschlussaktionen, Escape und Entwurfsschutz. Validierung und Verbindungsfehler behalten dynamische Positionen, Reihenfolge, Notizen und ausgewählte Dateien. Bei HTTP 409 bleibt auch der alte Versionswert stehen: Der aktuelle Stand kann in einem zweiten Tab geprüft werden; erst die ausdrücklich beschriftete Wiederholung sendet den Entwurf mit dem geprüften Versionswert. Rechteentzug oder abgelaufene Anmeldung lassen den Entwurf sichtbar und sperren weitere Speicherung.

### Technische Übergabe

- `Shared/AssessmentEditor`, `AssessmentDraft` und `_AssessmentEditor.cshtml` dienen Fahrzeugen und Offerten. Die Fachaktionen, Auswertungen und Berechtigungen bleiben getrennt und unverändert. Offertspezifische Prüfhinweise und Bestätigungsbeschriftungen werden dem gemeinsamen Editor übergeben.
- `Shared/_PageEditorActions.cshtml` ergänzt gemeinsame Abbrechen-/Abschlussaktionen für die vorhandenen langen Seiten. `Edit` und `Upload` liefern bei erfolgreichen HTMX-POSTs `HX-Redirect`; bei HTTP 409/422 liefern sie lokale Fehlermeldung, aktuellen Versionsstand und Prüfziel. Native POSTs behalten den bisherigen Seiten-/Redirect-Ablauf.
- `editors.js` erhält vollständige Seitenformulare statt sie bei Fehlern auszutauschen. Versionsfelder werden unabhängig von Gross-/Kleinschreibung vom Entwurfsvergleich ausgenommen; anfänglich deaktivierte Positionsfelder werden vor der Baseline berücksichtigt. Ausgeblendete Fehler markieren einen unveränderten Entwurf nicht als bearbeitet.
- `First`/`Second` werden auf die bestehende Evaluation und dasselbe Fahrzeug beschränkt. Dateidownloads, historische Versionsbezüge, Uploadlimits, Servervalidierung, Anti-Forgery, Zugriffskontrolle und Speicheroperationen bleiben erhalten.
- Stabile Dokument- und Abschnitts-IDs erhalten geöffnete Historien und bieten Fokusziele nach Löschung. Positionswechsel erhalten den Fokus am Sortierbutton. Die alten parallelen Entwurfswiederhersteller greifen nicht mehr auf gemeinsam verwaltete Offertformulare zu.
- Ersetzte Bewertungsformulare und Grid-Vergleichsregeln wurden aus `site.css` entfernt. Die neuen Flächen und Seiteneditoren liegen in `canvas.css`; Fachfelder und Positionslayouts bleiben bestehende Razor-Bausteine. Keine Datenbank- oder Import-/Exportformatänderung.

### Verifikation

- 100 erfolgreiche Fach-/Integrationstests über den bestehenden Testbefehl; Build ohne Warnungen und Fehler. Der Abschlusslauf `PLAYWRIGHT_EXECUTABLE_PATH='/Applications/Google Chrome.app/Contents/MacOS/Google Chrome' npm run test:e2e` gegen eine eigene temporäre Testdatenbank enthält 64 erfolgreiche Browserprüfungen über alle bestehenden Produktbereiche. Protokolle lokal unter `.artifacts/phase3/verification.log` (Fach-/Integrationstests), `build.log` und `browser-final.log`.
- `tests/browser/offers.mjs` prüft zusätzlich erhaltene Reihenfolge und Notizen bei Netzfehlern, unverändertes Verlassen, Entwurfsschutz, Null/Unbekannt, ungültige Bewertungsgrundlage, Rückkehrfokus, explizite Konfliktwiederholung, unveränderliche Historie, entzogene Bearbeitungsrechte, mobile zweite Vergleichsspalte und Upload-Rückweg. Archivieren und Wiederherstellen erhalten die Historie.
- `tests/browser/documents.mjs` prüft erhaltene Dateien bei Netzfehlern, Validierung, Konflikten und abgelaufener Anmeldung; die ausdrückliche Wiederholung erzeugt eine getrennte Dateiversion mit denselben Bytes. Löschen prüft Objektname, sicheren Anfangsfokus, Escape, Rückkehrfokus und Erhalt der unabhängigen ZIP-Importkopie.
- Die echte Anwendung wurde mit synthetischen Daten bei 1440 und 390 px visuell geprüft. Offertenliste, Detail, Vergleich, Bearbeitung und Upload passen zusätzlich bei 320, 768 und 1024 px ohne seitenweite Überbreite. Lange Offert-, Dokument- und Dateinamen sowie ungetrennte Notizen wurden an denselben fünf Breiten geprüft. Screenshots unter `.artifacts/browser/phase3/`; Bilder, Logs und Testdaten bleiben ausserhalb der Commits.

## Phase 4: Kosten und Rechenszenarien

**Abgeschlossen am 13. November 2026.** Die Kostenplanung zeigt Zeitraum, Fahrzeugbezug, bekannte Teilsumme, Positionen und Zahlungszeitpunkte auf einer gemeinsamen Canvas-Fläche. Fehlende Beträge bleiben sichtbar offen; die bekannte Summe wird weiterhin ausdrücklich als unvollständig gekennzeichnet. Monats- und Jahresansicht verwenden dieselben Zahlungen, die erste Tabellenspalte bleibt beim horizontalen Scrollen sichtbar. Die vollständige Kostenbearbeitung gliedert Zuordnung, Zahlung, Zeitraum und Notizen auf einer eigenen Seite.

Szenarioübersicht und Akte verbinden gemeinsame Rendite, feste Offertversion, bestätigte Annahmen, Ergebnis, Zusammensetzung, Restwert-Stichtage, Kostenanpassungen und Zahlungsplan. Der Vergleich ist eine echte Tabelle mit einheitlichen Zeilen für effektive Kosten, Zahlungsbestandteile, Restwertband und Rechenweg. Auf Mobilgeräten bleibt die erste Spalte sichtbar und die zuvor bearbeitete zweite Szenariospalte wird wiederhergestellt. Ein neuer Offertstand überschreibt keine Annahmen; der Quellenlink folgt der bewusst gewählten Version.

Planungszeitraum, Rendite, Restwerte, Kostenanpassungen und Szenariokopien verwenden gezielt geöffnete Inline-Editoren. Lange Kosten- und Szenarioformulare verwenden die gemeinsamen Seitenaktionen und denselben Entwurfsschutz. Validierung, Netzfehler, Konflikte, Rechteentzug und abgelaufene Anmeldung erhalten die Eingaben. Löschaktionen nennen das Objekt, schützen benachbarte Entwürfe und verlangen bei einem Konflikt eine erneute ausdrückliche Bestätigung. Bereits entfernte Einträge zeigen eine lokale Fehlermeldung und sperren weitere Schreibversuche.

### Technische Übergabe

- `Costs/_PlanEditor` sowie `Scenarios/_SettingsEditor`, `_ResidualEditor`, `_AdjustmentEditor` und `_CopyEditor` nutzen `Shared/_EditorActions`. Vollständige Bearbeitungsseiten nutzen `_PageEditorActions`. Native POSTs behalten ihre Seiten-/Redirect-Antwort; HTMX erhält bei Fehlern lokale JSON-Rückmeldungen und bei Erfolg `HX-Redirect`.
- Kosten behalten Fahrzeugbezug, Zeitraumansicht und Tabellenseite bei; Szenarien behalten Vergleichspaar, Stichtag und Zahlungsansicht. Vergleichspartner werden auf die aktuelle Evaluation beschränkt. Explizite Abschnitts-, Positions- und Titelfokusziele erhalten nach dem Neuladen den Bezug zur Änderung.
- `editors.js` lädt erfolgreiche Antworten auf denselben Pfad und dieselbe Query auch dann neu, wenn sich nur der Sprungpunkt ändert. Sonst würde HTMX lediglich zum Fragment springen und veraltete Werte stehen lassen. `data-operation` verwendet dieselbe lokale Fehlerbehandlung auch für Löschformulare; das Attribut macht sie nicht zu Bearbeitungsdialogen.
- Bei HTTP 409 bleibt der ursprüngliche Versionswert bis zur ausdrücklich beschrifteten Wiederholung erhalten. Löschaktionen verwenden dafür «Stand geprüft – erneut bestätigen» und öffnen erneut den konkreten Dialog. Ein bestätigtes Entfernen umgeht den Schutz anderer offener Entwürfe nicht.
- `ScenarioForm.Fields` ergänzt ausschliesslich Anzeigegruppen und Währungseinheiten. Fachregeln, Rechenalgorithmen, Kostenrhythmen, Restwert-Stichtage ohne Interpolation, Kopien, Autorisierung, Anti-Forgery, Datenbankschema und Import-/Exportformat bleiben unverändert.
- Ersetzte Kosten- und Szenarioregeln wurden aus `site.css` entfernt und in `canvas.css` zusammengeführt. Neue sichtbare Texte liegen in `Ui.resx`. Lange Namen und Notizen umbrechen auch in Zusammensetzung und Vergleichszusammenfassung.

### Verifikation

- 100 erfolgreiche Fach-/Integrationstests über den bestehenden Testbefehl; Build ohne Warnungen und Fehler. Die gezielte Kosten-/Szenariostrecke umfasst 20 erfolgreiche Browserprüfungen. Der Abschlusslauf `PLAYWRIGHT_EXECUTABLE_PATH='/Applications/Google Chrome.app/Contents/MacOS/Google Chrome' npm run test:e2e` gegen eine eigene temporäre Datenbank umfasst 74 erfolgreiche Browserprüfungen über alle bestehenden Produktbereiche. Protokolle lokal unter `.artifacts/phase4/verification.log` (Fach-/Integrationstests), `build.log` und `browser-final.log`.
- `tests/browser/costs.mjs` und `scenarios.mjs` behalten ihre fachlichen Erwartungswerte für Zahlungen, Zinseszins, Restschuld, Restwertschwelle, Null/Unbekannt, Kopien und JSON-/ZIP-Austausch. Erfolgreiche Speicherschritte warten nun auf die tatsächliche Navigation.
- `tests/browser/financial-workflows.mjs` prüft erhaltene Fahrzeug-/Tabellen-/Vergleichskontexte, mobilen Spaltenwechsel, Fokus, bewusstes Verwerfen, 422, Netzfehler, 409 mit ausdrücklicher Wiederholung, wiederholtes Speichern auf derselben Seite, entzogene Rechte, abgelaufene Anmeldung, Kostenanpassungen, Kopien und benannte Löschungen einschliesslich zwischenzeitlich entfernter Einträge.
- Echte Oberfläche mit synthetischen Daten bei 1440 und 390 px visuell geprüft. Struktur- und Langtextprüfungen zusätzlich bei 320, 768 und 1024 px; Vergleich und Zahlungspläne scrollen nur in ihren bezeichneten Bereichen. Vollständige und offene Ergebnisse, Formularseiten, Inline-Editoren, Auswahlzustände und Validierungsfehler sind berücksichtigt. Artefakte unter `.artifacts/browser/phase4/` bleiben ausserhalb der Commits.

## Phase 5: Konto, Einstieg und Abschluss

**Abgeschlossen am 13. November 2026.** Ersteinrichtung und Anmeldung verwenden eine zusammenhängende Arbeitsfläche; Verifikation, Konto, Passkeys, Einladungen und Fehlerseiten folgen derselben Typografie, Farbgebung und Hierarchie. Passkeys werden in gezielt geöffneten Inline-Editoren verwaltet, Einladungen in der gemeinsamen seitlichen Bearbeitungsfläche. Leere, gesperrte, ausstehende und fehlgeschlagene Zustände sind ausdrücklich dargestellt.

### Technische Übergabe

- Authentifizierungsformulare erhalten ihre Eingaben bei lokalen Fehlern und Netzproblemen. HTMX und native Antworten bleiben verfügbar; lokale Rücksprungziele führen nach erneuter Anmeldung zurück zum Konto.
- `passkeys.js` wird nur auf Anmeldung und Konto geladen. Geräteabbruch und fehlgeschlagene Requests behalten den Namen; laufende Anfragen verhindern doppeltes Absenden und Schliessen. Eine erfolgreiche Anlage lädt die aktuelle Liste auch bei gleichem Seitenpfad mit neuem Fragment und stellt den Fokus auf den neuen Eintrag.
- Benannte Passkey-Löschung, Umbenennung und Einladungen verwenden dieselben Editoraktionen, Entwurfsschutz- und Fokusregeln wie die Fachbereiche. Bestehende Autorisierung, WebAuthn-Prüfung, zeitliche Grenzen und Identity-Konfliktprüfung bleiben erhalten.
- Vollständige Browser-Fehlerseiten zeigen 403, 404, 429 und allgemeine Fehler mit einem sinnvollen Rückweg. Der ursprüngliche HTTP-Status bleibt bestehen; die interne Fehlerdarstellung wird nicht erneut vom globalen Anfragelimit blockiert. Schreibantworten, HTMX-Fragmente und Health-Checks behalten ihr bisheriges Format.
- Überflüssige Auth-/Karten-/Kontolayouts, die alte Tacho-Illustration, Farb-Aliase und der letzte Browser-`confirm()`-Handler wurden entfernt. `site.js` trägt Felder, Verbindungs- und Ladehinweise; `editors.js` trägt weiterhin die gemeinsamen Interaktionen.
- Die abschliessende Sichtprüfung hat auch die Gruppenüberschriften in Fahrzeug-, Offert- und Szenarienvergleichen vereinheitlicht: Sie bleiben beim horizontalen Scrollen auf Mobilgeräten sichtbar.

### Verifikation und Abschluss

- **100 Fach-/Integrationstests erfolgreich**, Web-Build ohne Warnungen und Fehler. Vorhandener Prüfablauf aus dem README; lokales Protokoll `.artifacts/phase5/verification.log`.
- **84 Browserprüfungen erfolgreich** im vollständigen HTTPS-Lauf mit eigener Testdatenbank und isolierten Playwright-Kontexten. Einschliesslich Einrichtung, E-Mail-/Passkey-Anmeldung, aller Fachabläufe, Rollen, konkurrierender Änderungen, Entwurfsschutz, Dateiinhalt/Versionen, JSON-/ZIP-Austausch sowie Anwendungs- und Datenbankneustart. Der letzte Browserlauf ist in `.artifacts/phase5/https-browser.log` und `.artifacts/browser/results.json` festgehalten.
- `tests/browser/accounts.mjs` ergänzt ungültige Einrichtung und Codes, Offline-/Anfragelimitfehler, doppeltes Absenden, Einladung und Entzug, Passkey-Abbruch, Umbenennen/Entfernen, sichere Bestätigung und tatsächlichen Sitzungsverlust. Ein echtes überschrittenes Serverlimit liefert die gestaltete 429-Seite. Falsche Passkey-Domain und unsicheres HTTP sind zusätzlich im HTTPS-Lauf geprüft.
- Eine gesonderte lokale Prüfung mit den Schlüsseln der eigenen Testinstanz bestätigt eine tatsächliche 16 Minuten alte Sitzung: Passkey-Anlage serverseitig gesperrt, erneute E-Mail-Anmeldung mit Rückkehr ins Konto. Native Login-Validierung ohne JavaScript behält die E-Mail-Adresse. Dafür wurde kein Anwendungscode und kein produktiver Zugang verändert.
- **105 Seiten-/Breitenprüfungen** über 21 Ansichten bei 320, 390, 768, 1024 und 1440 px erfolgreich. Desktop-/Mobilaufnahmen aller Bereiche geprüft; keine seitenweite horizontale Überbreite oder JavaScript-Fehler. Gruppenbezeichnungen bleiben auch in der zweiten Vergleichsspalte sichtbar. Screenshots und Prüfergebnisse liegen lokal unter `.artifacts/browser/phase5/`; die zusätzlichen Kontozustände unter `.artifacts/browser/account-*.png`.
- Alle Migrationseinträge und Abschlusskriterien sind erfüllt. Bewusst beibehalten: lange Formulare als eigene Seiten, horizontal scrollende Vergleichs-/Zahlungstabellen innerhalb ihrer Bereiche sowie native Datei- und Geräteauswahl. Die Designauswahl wird nicht erneut geöffnet; neue Funktionen sind nicht Bestandteil dieses Abschlusses.


## Inventar der vorhandenen Oberflächen

| Bereich | Bestehende Einstiegspunkte / Dateien | Migrationsschwerpunkt |
| --- | --- | --- |
| App-Rahmen | `Pages/Shared/_Layout.cshtml`, `wwwroot/css/site.css` | Navigation, Typografie, Tokens, Footer, globale Hinweise |
| Evaluation / Navigation | `Pages/Index.cshtml`, `Pages/_EvaluationOverview.cshtml`, `Pages/Evaluations/_EvaluationHeader.cshtml` | Kontext, Übersicht, Anlegen, Import, Export, erreichbare Bereiche |
| Kriterien / Gruppen / Freigaben | `Pages/Evaluations/Details.cshtml`, `_Workspace.cshtml`, `_CriterionFields.cshtml`, `_Sharing.cshtml`, `_EvaluationSettings.cshtml` | Bearbeitung, Reihenfolge, Rollen, Einstellungen |
| Kandidaten | `Pages/Evaluations/Candidates/Index.cshtml`, `Details.cshtml`, `_CandidateList.cshtml`, `_CandidateDetails.cshtml` | Auswahl, Akte, Anlage, Klassifikation, Kopie |
| Bewertungen / Vergleich | `Candidates/_AssessmentRow.cshtml`, `Compare.cshtml`, `_Comparison.cshtml` | Gemeinsamer Editor, Spaltenkontext, Unterschiede |
| Technische Daten | `Candidates/_Specifications.cshtml`, `_SpecificationsFields.cshtml`, `_SpecificationsComparison.cshtml` | Faktengruppen, Einheiten, Kennzeichnung berechneter Angaben |
| Offerten | `Pages/Evaluations/Offers/` | Neue Version, Details, Vergleich, Historie, Bewertungen |
| Dokumente | `Offers/_OfferDocuments.cshtml`, `Upload.cshtml`, `Pages/Evaluations/Document.cshtml` | Upload, Versionen, Rechte und Rückmeldungen |
| Kosten | `Pages/Evaluations/Costs/Index.cshtml`, `Edit.cshtml` | Übersicht, vollständiges Formular, Zahlungszeitpunkte |
| Rechnen | `Pages/Evaluations/Scenarios/` | Annahmen, Ergebnis, Vergleich, Offertbindung |
| Einstieg / Konto | `Pages/Auth/`, `Pages/Account/` | Einheitliche Felder, Verifikation, Passkeys, Rollen |
| Fehler / Status | `Pages/Error.cshtml`, Layout, HTMX-Hinweise | Verständliche Wiederherstellung und zugängliche Meldungen |
| Interaktionsskripte | `wwwroot/js/site.js`, `editors.js`, `passkeys.js`, `workspace.js`, `sorting.js`, `specifications.js`, `offers.js` | HTMX-Lebenszyklus, Fokus, Entwürfe, Sortierung, Bestätigungen |

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

- [x] Alle vorhandenen Bereiche sind umgestellt und erreichbar; keine fachliche Regression.
- [x] Einheitliche Navigation, Formulare, Status, Aktionen und Dialoge auf Desktop und Mobilgeräten.
- [x] Keine seitenweite horizontale Überbreite; Vergleichstabellen dürfen innerhalb eines klar bezeichneten Bereichs scrollen, wobei die Kriterien erkennbar bleiben.
- [x] Lesbarer Kontrast, sichtbarer Fokus, passende Labels, erreichbare Aktionen und reduzierte Bewegung.
- [x] Bestehende Tests erfolgreich und relevante neue UI-Risiken sinnvoll geprüft.
- [x] Dokumentation entspricht der tatsächlichen Umsetzung; offene Punkte und bewusste Abweichungen sind benannt.
- [x] Abschluss der Design-/UX-Migration festgehalten, bevor weitere Features begonnen werden.

## Git-Übergabe

Der Hauptbranch heisst `main`. Die ausgewählte Referenz und diese Dokumentation werden als eigener nachvollziehbarer Commit übernommen. Der breite Designwerkzeug-Commit `b610c56` ist dafür keine Voraussetzung. Die Prototyp-Branches und der andere Design-Worktree bleiben als Referenzen erhalten.

Die Implementierung beginnt aus dem aktualisierten `main`; kurze Umsetzungsbranches dürfen jeweils ein Paket abbilden und nach Prüfung wieder in `main` einfliessen. Keine erneute Auswahlrunde für Studio oder alternative Farben starten, solange der Nutzer Canvas/Tinte nicht ändert. Kein Release oder Deployment wird durch diese Dokumentationsübergabe ausgelöst.
