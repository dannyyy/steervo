# Steervo Community

Steervo schafft einen eigenen Arbeitsbereich für deine Fahrzeugentscheidungen. Version 0.1.0 umfasst Anmeldung, Einladungen, persönliche Evaluationen, geordnete Kriterien und gezielte Freigaben. Die Änderungen sind im [Changelog](CHANGELOG.md) beschrieben.

## Was bereits funktioniert

- Kontrollierte Ersteinrichtung mit Einrichtungsschlüssel und bestätigter E-Mail-Adresse.
- Anmeldung per E-Mail-Einmalcode oder Passkey; Passkeys hinzufügen, umbenennen und entfernen.
- Weitere Konten ausschliesslich durch Einladungen des Hauptbenutzers.
- Private Evaluationen mit Name, Zweck und dauerhafter Speicherung in PostgreSQL.
- Kriterien als Ja/Nein, Messwert mit Schwelle oder Bewertung 1–5; Prioritäten Muss, stark gewünscht und optional. Fehlende Bewertungen werden nicht als Nichterfüllung behandelt.
- Kompakte Kriterienzeilen und eine optionale Gruppenebene. Kriterien und Gruppen per Pfeiltasten oder Drag & Drop sortieren, auch per Touch; Kriterien zwischen Gruppen und nach «Ohne Gruppe» verschieben. Kriterien-IDs, sichtbarer Ausschnitt und offene Bearbeitungsfelder bleiben beim Sortieren erhalten.
- Besitzer-, Bearbeiter- und Leserechte pro Evaluation. Die Installationsverwaltung verschafft keinen Zugriff auf fremde Fälle.
- Schutz vor überschriebenen Änderungen bei gleichzeitiger Bearbeitung, serverseitige Validierung und CSRF-Schutz.
- JSON-Export und Import einer Evaluation einschliesslich Kriterien und Gruppen. Ein Import erzeugt eine neue private Evaluation.
- Deutsche Oberfläche auf Desktop und Mobilgeräten. Es werden keine Geschäftsdaten oder Beispielkosten vorbefüllt.

## Lokal starten

Voraussetzungen: vorhandenes .NET-10-SDK entsprechend `global.json`, Docker mit Compose sowie `openssl`. Der Startweg verändert oder installiert kein SDK.

```sh
./scripts/init-local.sh
docker compose up -d db mail
dotnet restore Steervo.sln
./scripts/run-local.sh
```

Öffne **http://localhost:5180**. Der Einrichtungsschlüssel steht in der lokal erzeugten, ignorierten `.env` unter `Setup__Token`. Gib ihn zusammen mit deinem Namen und deiner E-Mail-Adresse ein. Den Bestätigungscode findest du im lokalen Posteingang unter **http://localhost:51080**. Nach der Bestätigung kann dieselbe Installation nicht nochmals eingerichtet werden.

Danach über **Erste Evaluation anlegen** beziehungsweise **Neue Evaluation** den Dialog öffnen und Name sowie optional einen Zweck erfassen. In der Evaluation Kriterien und bei Bedarf Gruppen hinzufügen. Änderungen werden nach erfolgreichem Speichern auf dem Server bestätigt.

Zum Sortieren den Punktgriff ziehen oder die Pfeile neben einem Eintrag verwenden. Ein fokussierter Griff unterstützt auch die Pfeiltasten ↑ und ↓; Escape bricht das Ziehen ab. Die Einfügemarkierung zeigt das Ziel, und am Bildschirmrand scrollt die Liste während des Ziehens weiter. Über **Bearbeiten** lassen sich Bezeichnung, Priorität, Typ und Gruppenzuordnung ändern.

Weitere Personen lassen sich unter **Einladungen** hinzufügen. Sie melden sich mit ihrer eingeladenen Adresse an und bestätigen ihren eigenen Code. Erst eine separate Freigabe innerhalb einer Evaluation gibt ihnen Zugriff darauf.

`.env`, Datenbank und Data-Protection-Schlüssel bleiben lokal. Zum Stoppen `Ctrl+C` und `docker compose down` verwenden; dabei bleiben die Volumes erhalten. Der vollständig containerisierte Start und die Helm-/Argo-CD-Konfiguration sind in [Betrieb](docs/operations.md) beschrieben.

## Prüfen

```sh
dotnet restore Steervo.sln --locked-mode
dotnet build Steervo.sln --no-restore
./scripts/test.sh
```

Der Testlauf startet dedizierte PostgreSQL-17/18- und SMTP-Container mit eigenen Ports; die lokale Anwendung kann weiterlaufen. Der Testmailserver verwendet SMTP-Port 52025 und Posteingang-Port 52080. Integrationstests erstellen und entfernen eigene Datenbanken. Die Verbindung zu alternativen Testinstanzen kann mit `STEERVO_TEST_POSTGRES17` und `STEERVO_TEST_POSTGRES18` vorgegeben werden; diese Rollen benötigen das Recht, Testdatenbanken anzulegen. Keine Produktivdatenbank verwenden.

Für Browserprüfungen werden Node.js und Playwright benötigt:

```sh
npm ci --ignore-scripts
# Einmalig, falls noch kein passender Testbrowser vorhanden ist:
npx playwright install chromium
./scripts/test.sh --browser
```

Ein bereits installierter Chromium-Browser kann über `PLAYWRIGHT_EXECUTABLE_PATH` verwendet werden. Playwright benutzt isolierte Kontexte und niemals das persönliche Browserprofil. Die Tests prüfen den kompletten Einstieg, Kriterien, Freigaben, direkte unzulässige Requests, Passkey-Anmeldung mit virtuellem Authenticator, Wiederherstellung per E-Mail, Neustarts und die mobile Darstellung. Für die Sortierung werden lange Listen, stabile Scrollposition und Tastaturfokus, Drag & Drop zwischen Gruppen, leere Gruppen, Touch-Eingaben, Abbrechen und erhaltene Bearbeitungsentwürfe geprüft. Bilder und Ergebnisse liegen unter `.artifacts/browser/`. Testcontainer anschliessend mit `docker compose -f compose.test.yaml down` stoppen.

Mit `STEERVO_E2E_HTTPS=1 ./scripts/test.sh --browser` läuft derselbe Ablauf zusätzlich über eine eigene HTTPS-Testdomain. Dieser Modus prüft auch die Passkey-Meldungen bei einer falschen Domain und unverschlüsseltem HTTP. Dafür erzeugt der Test ein kurzlebiges Zertifikat; nur der isolierte Testbrowser akzeptiert es. Es wird kein Zertifikat im System installiert.

Um dieselben Browserprüfungen gegen das tatsächliche Linux-Containerimage auszuführen, zuerst `./scripts/build-image.sh` und dann `STEERVO_E2E_CONTAINER=1 STEERVO_E2E_HTTPS=1 ./scripts/test.sh --browser` starten. Der Test verwendet das vorhandene Image ohne Download, die dedizierten Testdienste sowie ein eigenes temporäres Schlüsselvolume. Der lokale HTTPS-Testproxy wird über die Gateway-Adresse dieses Testnetzwerks freigegeben.

## Aufbau und Umfang

`Steervo.Core` enthält Fachregeln ohne Web- oder Datenbankabhängigkeit. `Steervo.Application` enthält die Evaluationsabläufe und ihre fachlichen Ports. `Steervo.Infrastructure` implementiert PostgreSQL/EF Core, ASP.NET Core Identity und SMTP. `Steervo.Web` liefert Razor Pages, HTMX-Fragmente, WebAuthn-Interaktion und lokalisierte Oberflächentexte. Es gibt keine öffentliche Programmierschnittstelle.

Dieser Stand dient dem Erstellen und gemeinsamen Bearbeiten eines Anforderungsprofils. Die Kriterientypen legen fest, wie Anforderungen beschrieben werden; tatsächliche Fahrzeugbewertungen und Ergebnisberechnungen sind nicht enthalten.

## Lizenz

Steervo steht unter der **PolyForm Noncommercial License 1.0.0** (siehe [LICENSE](LICENSE)).

**Kostenlos erlaubt** ist jede nichtkommerzielle Nutzung — private Nutzung, Selbstbetrieb, Hobbyprojekte, Lernen und Forschen. Ebenfalls erlaubt ist die Nutzung durch gemeinnützige Organisationen, Bildungseinrichtungen und öffentliche Institutionen.

**Eine kommerzielle Lizenz erforderlich** ist für jede kommerzielle Nutzung, insbesondere den entgeltlichen Betrieb von Steervo für Dritte. Anfragen bitte über ein GitHub Issue.

> **Hinweis zur Bezeichnung:** Steervo ist quelloffen (*source available*), aber keine Open-Source-Software im Sinne der Open Source Initiative, da die Lizenz kommerzielle Nutzung einschränkt. Diese Unterscheidung wird hier bewusst eingehalten.

## Beiträge

Beiträge sind willkommen. Weil Steervo doppelt lizenziert wird, ist für jeden Beitrag die Zustimmung zum [Contributor License Agreement](CLA.md) erforderlich — das Eigentum an deinem Code bleibt bei dir. Ablauf und Hinweise in [CONTRIBUTING.md](CONTRIBUTING.md).

## Gehostete Variante

Eine gehostete Variante ist in Vorbereitung.
