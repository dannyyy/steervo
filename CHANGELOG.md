# Änderungen

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
