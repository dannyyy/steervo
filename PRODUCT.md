# Steervo Community

<!-- impeccable:product-schema 1 -->

## Platform

web

## Product Purpose

Steervo schafft einen eigenen Arbeitsbereich für Fahrzeugentscheidungen. Nutzer erfassen ihre Anforderungen, recherchieren Kandidaten, halten Einschätzungen fest und vergleichen Fahrzeuge sowie Offerten und Kosten. Die Entscheidung bleibt beim Nutzer; das Produkt erzeugt keinen automatischen Gewinner.

## Users

Menschen, die eine Fahrzeugentscheidung vorbereiten, sowie ausdrücklich eingeladene Mitwirkende. Evaluationen haben Besitzer-, Bearbeiter- und Leserechte. Weitergehende Zielgruppensegmente sind nicht festgelegt.

## Operating Context

Deutsche Weboberfläche für Desktop und Mobilgeräte. Der bestätigte Schwerpunkt ist der Wechsel zwischen Erfassen und Vergleichen. Anforderungen, Kandidaten, Quellen, Notizen, Offertversionen, Dokumente und Kosten gehören zum selben Evaluationskontext. Die Community-Anwendung wird selbst betrieben.

## Capabilities and Constraints

Der vorhandene Funktionsumfang ist im [README](README.md#was-bereits-funktioniert) beschrieben. Die Umgestaltung muss ihn erhalten:

- Anmeldung mit E-Mail-Einmalcode oder Passkey, kontrollierte Ersteinrichtung, Einladungen und Freigaben.
- Kriterien mit optionalen Gruppen, Reihenfolge, Prioritäten Muss / stark gewünscht / optional und Bezug auf Fahrzeug oder Offerte.
- Ja/Nein mit zusätzlicher Angabe «Optional» für separat erhältliche Ausstattung, Messwerte mit Schwelle und persönliche Bewertungen von 1–5. Unbekannt, Nein und null sind verschiedene Zustände. Persönliche Bewertungen haben keine objektive Erfüllungsschwelle und ergeben keine Gesamtnote. «Optional erhältlich» ist ein eigener Zustand: bekannt, aber noch nicht enthalten; auch bei Muss-Kriterien weder erfüllt noch verfehlt. Die Angabe ist unabhängig von der Kriterienpriorität «optional». Ein verfehltes Muss schliesst ein Fahrzeug nicht automatisch aus.
- Kandidaten, technische Daten, Quellen und Notizen; unabhängige Kopien; begründetes Zurückstellen und Ausscheiden. Technische Fakten und Kriterienbewertungen bleiben getrennt.
- Mehrere Offerten pro Fahrzeug; unveränderliche ältere Offert- und Dokumentversionen. Offertbewertungen bleiben nach einer neuen Version erhalten und werden zur erneuten Prüfung markiert. Währung pro Evaluation.
- Kostenplanung und gespeicherte Rechenszenarien mit expliziten Annahmen, festen Offertbezügen und offen bleibenden Ergebnissen bei fehlenden Angaben. Keine Länder- oder Steuerautomatik.
- Import/Export einschliesslich bestehender Versionen und Anhänge; ein Import erzeugt eine neue private Evaluation.
- Serverseitige Validierung, CSRF-Schutz, aktuelle Zugriffsprüfung und Schutz vor überschriebenen gleichzeitigen Änderungen. Installationsverwaltung gewährt keinen Zugriff auf fremde Evaluationen.

ASP.NET Core Razor Pages mit HTMX auf .NET 10, PostgreSQL/EF Core und separaten Fach- und Berechnungsschichten sind der vorhandene Stack. Die Designmigration ist kein Frameworkwechsel. Sichtbare Texte gehören in `src/Steervo.Web/Resources/Ui.resx`; Code und Bezeichner bleiben Englisch.

## Brand Commitments

Der Nutzer hat **Canvas mit Tinte** gewählt. Die verbindliche visuelle Grundlage steht in [DESIGN.md](DESIGN.md). Die genannten Referenzprodukte sind Inspiration für Konsistenz und Hierarchie, keine Vorlagen zum Kopieren.

Die Community-Ausgabe ist quelloffen (*source available*), nicht OSI-Open-Source. Öffentliche Dokumentation darf keine Inhalte einer gehosteten Variante, internen Strategie oder privaten Anforderungen übernehmen; siehe [CLAUDE.md](CLAUDE.md).

## Evidence on Hand

- Die vorhandene Anwendung und ihr README belegen die Fachfunktionen.
- [Ausgewählter Prototyp](prototypes/design/README.md) und [Auswahl](prototypes/design/selection.json): Canvas / Tinte.
- [Referenzanalyse und Prüfbericht](prototypes/design/README.md#verification) dokumentieren die Gestaltung und Prototyp-Prüfungen. Diese ersetzen keine Tests der echten Anwendung.
- Fahrzeugbilder, Preise und Bewertungen im Prototyp sind illustrativ. Es gibt bisher keinen Fahrzeugbild-Upload; daraus entsteht kein neuer Featureauftrag. Die Produktionsoberfläche muss auch ohne Bilder vollständig funktionieren.

## Product Principles

1. Fakten erfassen und vergleichen, ohne den Arbeitskontext zu verlieren.
2. Gleichartige Aufgaben verwenden dieselben Bedienelemente und Rückmeldungen.
3. Ungewissheit, persönliche Einschätzung und bestätigte Angaben sichtbar unterscheiden.
4. Bestehende Daten, Rechte und Fachregeln bei der Umgestaltung erhalten.
5. Zuerst das vorhandene Produkt konsistent gestalten; neue Features folgen nach Abschluss der Design- und UX-Migration.
