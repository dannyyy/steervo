# Änderungen

## Unveröffentlicht

- Favicon aus dem Steervo-Bildzeichen in Canvas/Tinte: SVG, ICO mit mehreren Grössen und Touch-Icon, auf allen Seiten eingebunden. Build und Auslieferung über Playwright geprüft; Vorschau bei 16, 32 und 48 px auf hellen und dunklen Hintergründen.

- Evaluationsübersicht: Hover über die gesamte Zeilenbreite mit passenden äusseren Rundungen und Innenabstand; sichtbarer Tastaturfokus und mehr Platz für mobile Titel. Am 13. November 2026 mit Playwright für eine und mehrere Evaluationen bei 320–1440 px sowie per Tastatur und Touch geprüft.

- Ja/Nein-Kriterien bieten zusätzlich «Optional» für separat erhältliche Ausstattung. Fahrzeug- und Offertbewertungen zeigen «Optional erhältlich» als eigenen Status, auch bei Muss-Kriterien weder erfüllt noch verfehlt. «Unbekannt» bleibt getrennt; Bezugsquelle und Details können in der Notiz stehen.
- Der Status bleibt beim Kopieren, Speichern sowie im JSON-/ZIP-Austausch erhalten. Exportformat Version 7; bestehende Dateien der Versionen 1 bis 6 bleiben importierbar. Die Datenbankmigration erhält bestehende Ja-/Nein-Werte und fehlende Angaben. [Fachregeln und Prüfnachweis](docs/optional-equipment.md).

- Canvas/Tinte-Migration abgeschlossen: auch Anmeldung, Ersteinrichtung, Verifikation, Konto, Passkeys, Einladungen und Systemzustände verwenden die gemeinsame Gestaltung und Bedienung.
- Konto- und Authentifizierungsformulare behalten Eingaben bei Fehlern; Passkeys verwenden gezielte Editoren und benannte Löschbestätigungen. Erneute Anmeldung führt sicher zum Konto zurück, erfolgreiche Änderungen zur aktualisierten Liste mit Rückkehrfokus.
- Einheitliche lokale Verbindungs- und Ladehinweise sowie vollständige Fehlerseiten mit erhaltenem HTTP-Status. Mobile Vergleichsüberschriften bleiben beim horizontalen Scrollen sichtbar; alte Layoutregeln und Dialogskripte sind bereinigt.

- Vierter Teil der Canvas/Tinte-Migration: zusammenhängende Kostenplanung und Szenarioakten, gegliederte Formularseiten und zeilenweiser Szenarienvergleich mit fixierter erster Spalte. Fahrzeugbezug, Stichtag, Vergleichspaar und Zahlungsansicht bleiben bei der Bearbeitung erhalten.
- Planungszeitraum, Rendite, Restwerte, Kostenanpassungen und Szenariokopien verwenden gemeinsame Inline-Editoren mit Entwurfsschutz, lokalen Fehlern und ausdrücklicher Konfliktwiederholung. Offertversionen bleiben fest gebunden; ein Quellenwechsel ersetzt keine Rechenannahmen.
- Benannte Löschbestätigungen schützen auch benachbarte Entwürfe. Wiederholtes Speichern auf derselben Seite lädt aktuelle Werte und stellt den Fokus wieder her; inzwischen entfernte Einträge beenden weitere Schreibversuche. Berechnungen und Austauschformat bleiben unverändert.

- Dritter Teil der Canvas/Tinte-Migration: gemeinsame Offertenliste, zusammenhängende Offertakte, lesbare Versionshistorie und Offertenvergleich mit fixierter Kriterienspalte auf Mobilgeräten. Vergleichsauswahl bleibt beim Wechsel in Bearbeitung und Upload erhalten.
- Fahrzeug- und Offertbewertungen verwenden denselben Editor. Neue Offertstände kennzeichnen weiterhin frühere Einschätzungen zur erneuten Prüfung.
- Gegliederte Offertformulare und Dokumentuploads verwenden den gemeinsamen Entwurfsschutz und lokale Fehler. Versionskonflikte erhalten alle Eingaben und die gewählte Datei; erneutes Senden erfolgt ausdrücklich nach Prüfung. Dokumentlöschung nennt das Objekt im gemeinsamen Bestätigungsdialog.

- Zweiter Teil der Canvas/Tinte-Migration: gemeinsame Evaluationsübersicht, Kriterien und Gruppen auf einer Arbeitsfläche sowie eigene Ansichten für Freigaben und Einstellungen. Einheitliche Anlage-, Bearbeitungs- und Bestätigungsdialoge einschliesslich Import und Löschen.
- Sortieren erhält offene Kriterienentwürfe auch bei Konflikten. Fehlgeschlagene Importe behalten die gewählte Datei; Rechteentzug verhindert weitere Speicherung, ohne den Entwurf zu verlieren.

- Erster Teil der Canvas/Tinte-Migration: horizontaler Seitenrahmen, Fahrzeugliste, zusammenhängende Fahrzeugakte und Fahrzeugvergleich mit lokal ausgelieferter Geist-Schrift.
- Gemeinsame Bewertung im Kontext, seitliche Formulare für Anlage, Kopie, Stammdaten und Einordnung sowie einheitliche Bestätigungen für Zurücksetzen und Entfernen.
- Erhaltene Fahrzeugauswahl, mobile Vergleichsspalten und Tastaturfokus; geschützte Entwürfe bei Abbruch, Validierungs- und Verbindungsfehlern, gleichzeitigen Änderungen und abgelaufener Anmeldung. Die übrigen Bereiche wurden in den folgenden Paketen umgestellt.

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
