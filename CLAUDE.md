# Steervo — öffentliches Repository

Dies ist das **öffentliche** Repository von Steervo (Community). Lizenz: PolyForm Noncommercial 1.0.0.

Alles in diesem Repository ist für jeden sichtbar. Jede Datei, jeder Commit, jede Commit-Nachricht.

## Harte Regel: keine Inhalte der gehosteten Version

In diesem Repository werden nicht dokumentiert, erwähnt, vorbereitet oder angedeutet:

- Funktionsumfang, Roadmap oder Implementierung der gehosteten Version
- Datenquellen und deren Aufbereitung
- Preise, Geschäftsmodell, Strategie
- Interne Projektdokumente und Anforderungen

Erlaubt ist genau ein neutraler Hinweis im README und im Footer der Anwendung, dass eine gehostete Variante existiert. Mehr nicht.

Ebenfalls nicht erlaubt, auch nicht „für später vorbereitet":

- Erweiterungspunkte, Plugin-Registries, Provider-Interfaces
- Feature-Flags oder Konfigurationsschalter für nicht enthaltene Funktionen
- Toter Code, leere Implementierungen oder Kommentare, die auf nicht enthaltene Funktionen verweisen

Die Community-Anwendung ist für ihren Funktionsumfang vollständig und in sich geschlossen.

## Keine öffentliche API

Dieses Repository stellt keine Programmierschnittstelle für Dritte bereit: keine dokumentierten Endpunkte, keine API-Keys, kein MCP-Server, keine Kompatibilitätsversprechen auf internen Routen. Für Datenaustausch gibt es Import und Export in einem offenen Format.

## Begriffe

Steervo ist **nicht** „Open Source", sondern quelloffen / *source available*. Die Lizenz schliesst kommerzielle Nutzung aus und erfüllt die OSI-Definition deshalb nicht. Diese Unterscheidung in README, Dokumentation, Code-Kommentaren und Oberfläche konsequent einhalten.

## Länderunabhängigkeit

Der Kern enthält kein Länderwissen. Steuern und Abgaben sind manuell erfasste Kostenpositionen, keine Rechenregeln. Keine kantonalen, bundesstaatlichen oder nationalen Tabellen in dieses Repository.

## Interne Dokumente

Strategie, Anforderungen und Projektvision liegen ausserhalb dieses Repositories und werden nicht hierher kopiert. Wird öffentliche Dokumentation gebraucht, wird sie hier neu geschrieben — nicht aus internen Dokumenten redigiert.

## Aktueller Entwicklungsauftrag

Die gewählte Designrichtung ist **Canvas / Tinte**. Zuerst die bestehende Community-Oberfläche auf `main` migrieren und Abläufe, Formulare und Dialoge vereinheitlichen; danach neue Features. [AGENTS.md](AGENTS.md) verlinkt die verbindliche Gestaltung, die Interaktionsregeln und den schrittweisen [Migrationsplan](docs/design-migration.md). Der Prototyp ist eine Referenz, keine bereits migrierte Anwendung.
