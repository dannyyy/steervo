# Optional erhältliche Ausstattung

Umgesetzt und geprüft am 13. November 2026, nach Abschluss der Canvas/Tinte-Migration.

## Fachliche Regel

Ja/Nein-Kriterien haben neben Ja und Nein die Angabe **Optional**. Unbekannt bleibt eine eigene Auswahl. Optional bedeutet: separat erhältlich, aber noch nicht enthalten, beispielsweise beim Hersteller oder einem Drittanbieter. Grundlage und Notiz halten Herkunft und Bedingungen fest.

Der Vergleich zeigt **Optional erhältlich** als eigenen Zustand. Das gilt auch für Muss-Kriterien: weder erfüllt noch verfehlt und kein automatischer Ausschluss. Die Angabe ist unabhängig von der Kriterienpriorität «optional». Es werden keine Preise oder Kosten automatisch ergänzt.

Fahrzeugakte, Fahrzeugvergleich, Offertakte und Offertvergleich verwenden denselben Status. Optional erhältliche Muss-Ausstattung wird in Fahrzeugakte und Übersicht separat von verfehlten Anforderungen genannt. Nach einer neuen Offertversion bleibt die frühere Angabe erhalten und benötigt wie bisher eine erneute Prüfung.

## Speicherung und Austausch

- `OptionalAvailable` ergänzt Fahrzeug- und Offertbewertungen; bestehende Ja-/Nein-Werte bleiben unverändert. Der zusätzliche Wert darf nur bei Ja/Nein-Kriterien und nicht gleichzeitig mit einem anderen Bewertungswert gesetzt sein.
- Kopien übernehmen die Angabe unabhängig vom Original. Zurücksetzen entfernt sie zusammen mit Grundlage und Notiz.
- Exportformat Version 7 erhält die Angabe in JSON und ZIP. Versionen 1 bis 6 bleiben importierbar. Details zu Feldern und Migration stehen unter [Betrieb](operations.md#datenaustausch).

## Verifikation

- 109 Fach- und Integrationstests erfolgreich, mit separaten Datenbanken auf PostgreSQL 17 und 18. Die neuen Tests prüfen die Zustände, ungültige Kombinationen, Kopien, Kriterienänderungen, Offertversionen, Erhalt bestehender Werte bei der Migration und JSON-/ZIP-Rückimporte.
- Web-Build ohne Warnungen oder Fehler; EF Core bestätigt, dass Modell und Migration übereinstimmen.
- 87 Browserprüfungen erfolgreich über `npm run test:e2e`, mit eigener temporärer Datenbank und isolierten Playwright-Kontexten. Die zusätzlichen Prüfungen decken Optional in Fahrzeugen und Offerten, Leserrechte, ungültige Requests, Validierungs-/Netzfehler, Konflikte, Entwurfserhalt, Rückkehrfokus und mobile Vergleichsposition ab.
- Fahrzeugübersicht und Vergleich bei 320, 390, 768, 1024 und 1440 px ohne seitenweite Überbreite; Offertvergleich bei 390 und 1440 px. Desktop-Editor und mobile Vergleichsansicht anhand der Screenshots geprüft.
- Lokale Protokolle: `.artifacts/optional-equipment-verification.log` (109 Tests und Build), `.artifacts/optional-equipment-browser.log` (abgeschlossener Browserlauf). Screenshots unter `.artifacts/browser/optional-equipment-*.png`; diese Artefakte werden nicht eingecheckt.
