# Beiträge zu Steervo

Danke für das Interesse. Steervo ist in einer frühen Phase — bevor du Arbeit investierst, lies bitte die folgenden Punkte.

## Contributor License Agreement (CLA)

Steervo wird doppelt lizenziert: öffentlich unter der PolyForm Noncommercial License 1.0.0 und zusätzlich unter einer kommerziellen Lizenz. Damit das funktioniert, müssen alle Rechte an beigetragenem Code beim Projekt liegen.

**Für jeden Pull Request ist deshalb die Zustimmung zu einem CLA erforderlich.** Der Vorgang wird automatisiert am Pull Request abgefragt, sobald das CLA eingerichtet ist. Bis dahin werden keine Code-Beiträge gemerged — bitte eröffne stattdessen ein Issue.

Wenn du damit nicht einverstanden bist, ist das in Ordnung. Dann sind Issues, Fehlerberichte und Diskussionsbeiträge trotzdem willkommen.

## Bevor du etwas baust

Eröffne zuerst ein Issue und beschreibe, was du vorhast. Das Projekt hat eine klare Richtung, und ein fertiger Pull Request, der nicht hineinpasst, ist für beide Seiten verlorene Zeit.

Besonders willkommen sind:

- Fehlerberichte mit nachvollziehbarer Beschreibung
- Verbesserungen an der Rechenlogik der Gesamtkosten, idealerweise mit Tests
- Korrekturen an Dokumentation und Übersetzungen

## Was nicht in dieses Repository gehört

- Erweiterungspunkte, Plugin-Schnittstellen oder Konfigurationsschalter, die für die gehostete Version vorgesehen sind. Der Kern bleibt in sich geschlossen.
- Anbindungen an Bank- oder Kreditkartenkonten sowie Rechnungsempfang per E-Mail. Das ist eine bewusste Produktentscheidung, nicht eine offene Lücke.
- Eine öffentliche, dokumentierte Programmierschnittstelle. Für den Datenaustausch gibt es Import und Export.

## Tests

Für Änderungen an Berechnungen gilt: ohne Tests kein Merge. Das betrifft ausdrücklich auch negative Fälle — was passiert bei fehlenden Annahmen, unmöglichen Werten oder widersprüchlichen Eingaben.

## Stil

Richte dich am umgebenden Code aus. Kein eigener Formatierungsstil, keine grossflächigen Umformatierungen in einem inhaltlichen Pull Request.
