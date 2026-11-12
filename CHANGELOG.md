# Änderungen

## 0.2.0 — 2026-11-12

- Gespeicherte Rechenszenarien für Barkauf, Leasing mit Rückgabe oder Übernahme und Kreditfinanzierung, mit festen Offertversionen und eigenständig bestätigten Gesamtpreisen.
- Monatsgenaue Zahlungen, effektiver jährlicher Zinseszins, Kreditrestschuld und Schlussrate sowie Kilometerabrechnung bei Leasingrückgabe.
- Restwertbänder an frei gewählten Stichtagen ohne Interpolation; zeitlich begrenzte Kostenanpassungen und unabhängige Szenariokopien.
- Vergleich zweier Szenarien mit nachvollziehbarer Kostendifferenz und Restwertschwelle; offene Angaben bleiben sichtbar. Monats- und Jahresansicht zeigen dieselben Zahlungsbewegungen.

- Kostenplanung mit frei gewähltem Startmonat und Zeitraum; gemeinsame und fahrzeugspezifische Positionen für Ausgaben und Einnahmen.
- Nachvollziehbare Fälligkeiten für einmalige, monatliche und jährliche Zahlungen sowie inklusive Positionen; offene Beträge bleiben von bekannten Nullbeträgen unterscheidbar.
- Monats- und Kalenderjahresansicht, bekannte Teilsummen, Zeitraumanpassung ohne Datenverlust und unabhängige Kostenkopien beim Kopieren eines Autos.

- Dokumente an Offerten mit eigener Dateihistorie, Notizen und Bezug zum jeweiligen Offertstand; frühere Dateiversionen bleiben abrufbar.
- Uploads und Downloads über die bestehenden Evaluationsrechte, mit Grössenbegrenzung, Integritätsprüfung und dauerhafter Löschwarteschlange.
- Persistenter lokaler Dateispeicher sowie S3-kompatible Garage-Anbindung über Helm und das bestehende Secret.
- Vollständiger ZIP-Export mit Anhängen und Rückimport als unabhängige private Evaluation; JSON-Exporte bleiben kompatibel.

- Mehrere strukturierte Offerten pro Auto mit Anbieter, Verbindlichkeit, Lieferangaben sowie Kauf-, Leasing- und Finanzierungskonditionen.
- Versionierte Offerten und Positionen: Änderungen ergänzen die Historie, frühere Stände sind auch auf Datenbankebene gegen Überschreiben geschützt. Versionsunterschiede und Archivierung bleiben nachvollziehbar.
- Zubehör, Gebühren, Ausstattung und Sonderkonditionen mit offenen Preisen, bekannten Beträgen oder inklusive Leistungen; bei Sonderkonditionen zusätzlich Wirkung, Kostenbereich und Dauer.
- Eine gemeinsame Währung pro Evaluation, beim ersten Angebot gewählt und danach fest.
- Vergleich zweier Offerten desselben Autos und eigene Offertkriterien. Nach einer neuen Version bleiben frühere Einschätzungen sichtbar und werden zur erneuten Prüfung markiert.

- Optionale technische Fahrzeugdaten für Abmessungen, Platz, Gewicht, Leistung sowie Elektro- und Verbrennungsantrieb; getrennte Darstellung im Vergleich.
- Nachvollziehbare Berechnung fehlender Gewichts- und Leistungswerte, ohne eingegebene Ausgangswerte zu ersetzen.

- Kandidaten mit Stammdaten, Notizen, sicheren Quellenlinks, eigener Reihenfolge und unabhängigen Kopien.
- Begründete Einstufung als Favorit, Alternative, beobachtet, zurückgestellt oder ausgeschieden.
- Persistente Kriterienbewertungen mit getrennten Zuständen für unbekannt, nein und null sowie Grundlage und Notiz.
- Direkter Vergleich zweier frei wählbarer Autos mit Bearbeitung der Bewertungen auf Desktop und Mobilgeräten.
- Bewertungen von 1–5 bleiben persönliche Einschätzungen; daraus wird auch bei Muss-Kriterien keine Erfüllung oder Verletzung abgeleitet.
- Bestehende Freigaben gelten auch für Kandidaten und Bewertungen. Gleichzeitige Änderungen werden auf Konflikte geprüft.
- Exportformat Version 6 enthält Kandidaten, technische Daten, Bewertungen, Offerten mit vollständiger Historie und Währung sowie Kostenplanung, Szenarien, Restwerte und Kostenanpassungen; Versionen 1 bis 5 bleiben importierbar. Imports erzeugen weiterhin unabhängige private Evaluationen.
- Datenmigration mit Erhalt vorhandener Evaluationen und Kriterien; erweiterte Fachlogik-, PostgreSQL- und Playwright-Prüfungen.

## 0.1.0 — 2026-11-09

Erster benutzbarer Stand von Steervo Community.

- Ersteinrichtung mit Einrichtungsschlüssel, bestätigte E-Mail-Adressen und Einladungen durch das Hauptkonto.
- Anmeldung mit E-Mail-Einmalcode oder Passkey; Passkeys hinzufügen, umbenennen und entfernen.
- Private, dauerhaft in PostgreSQL gespeicherte Evaluationen mit Besitzer-, Bearbeiter- und Leserechten.
- Kriterien der Typen Ja/Nein, Messwert und Bewertung mit Prioritäten und optionalen Gruppen.
- Kompakte Kriterienansicht; Sortierung mit Pfeilen, Tastatur und Drag & Drop auf Desktop und Touch-Geräten. Gruppenwechsel, stabile Kriterien-IDs sowie erhaltene Scrollposition und Bearbeitungsentwürfe.
- Dialog zum Anlegen einer Evaluation und eine Vektorgrafik für die leere Übersicht.
- JSON-Export und Import als neue private Evaluation, Konflikterkennung bei gleichzeitigen Änderungen und serverseitige Zugriffsprüfung.
- Deutsche Oberfläche mit Ressourcen für sichtbare Texte; englische Code- und Datenbankbezeichner.
- Lokaler Start mit vorhandenem .NET-10-SDK oder Docker Compose, separate Datenbankmigrationen sowie Helm-Konfiguration und Argo-CD-Beispiel.
- Automatisierte Fachlogik- und Datenbanktests mit PostgreSQL 17/18 sowie Playwright-Prüfungen für Desktop, Mobilgeräte, Touch, Passkeys und HTTPS hinter einem Reverse-Proxy.
