# Steervo Community

Steervo schafft einen eigenen Arbeitsbereich für deine Fahrzeugentscheidungen. Der aktuelle Stand umfasst Anmeldung, Einladungen, persönliche Evaluationen, geordnete Kriterien, Fahrzeugkandidaten, Bewertungen, Vergleiche, versionierte Offerten mit Dokumenten, Kostenplanung, Rechenszenarien und gezielte Freigaben. Aktueller Stand: **0.2.0**. Die Änderungen sind im [Changelog](CHANGELOG.md) beschrieben.

## Aktueller Entwicklungsschwerpunkt

Die gewählte Designrichtung ist **Canvas mit Tinte**. Auf dem Hauptbranch `main` wird zuerst die bestehende Anwendung auf dieses Layout umgestellt; Abläufe, Formulare und Dialoge werden dabei vereinheitlicht und optimiert. Neue Features folgen nach Abschluss dieser Migration. Der gemeinsame Seitenrahmen, Fahrzeugakte, Bewertungen und Fahrzeugvergleich sowie Evaluationsübersicht, Kriterien, Gruppen, Freigaben und Einstellungen sind bereits umgestellt. Die übrigen Bereiche folgen schrittweise.

Verbindliche Grundlagen: [Designsystem](DESIGN.md), [Interaktionsmuster](docs/interaction-patterns.md), [Migrationsplan mit Abnahmekriterien](docs/design-migration.md) und [Produktregeln](PRODUCT.md). Der [interaktive Prototyp](prototypes/design/README.md) dient als Referenz. Als Nächstes folgt Phase 3 des Migrationsplans: Offerten und Dokumente.

## Was bereits funktioniert

- Kontrollierte Ersteinrichtung mit Einrichtungsschlüssel und bestätigter E-Mail-Adresse.
- Anmeldung per E-Mail-Einmalcode oder Passkey; Passkeys hinzufügen, umbenennen und entfernen.
- Weitere Konten ausschliesslich durch Einladungen des Hauptbenutzers.
- Private Evaluationen mit Name, Zweck und dauerhafter Speicherung in PostgreSQL.
- Kriterien als Ja/Nein, Messwert mit Schwelle oder Bewertung 1–5; Prioritäten Muss, stark gewünscht und optional. Fehlende Bewertungen werden nicht als Nichterfüllung behandelt.
- Kompakte Kriterienzeilen und eine optionale Gruppenebene. Kriterien und Gruppen per Pfeiltasten oder Drag & Drop sortieren, auch per Touch; Kriterien zwischen Gruppen und nach «Ohne Gruppe» verschieben. Kriterien-IDs, sichtbarer Ausschnitt und offene Bearbeitungsfelder bleiben beim Sortieren erhalten.
- Autos mit optionalen Stammdaten, Notizen und Quellen erfassen, in eigener Reihenfolge anordnen und unabhängig kopieren. Einstufungen wie Favorit, zurückgestellt oder ausgeschieden halten deine Auswahl fest; Zurückstellen und Ausscheiden verlangen eine Begründung.
- Die Ausstattung pro Auto anhand der eigenen Fahrzeugkriterien prüfen. Unbekannt, nein und null bleiben unterscheidbar; Grundlage und Notiz dokumentieren eine Einschätzung. Muss-Verletzungen führen nie automatisch zum Ausschluss.
- Zwei Autos direkt vergleichen und ihre Bewertungen im Vergleich bearbeiten. Bewertungen von 1–5 bleiben persönliche Einschätzungen, auch bei Muss-Kriterien; sie erzeugen weder Erfüllungsschwellen noch eine Gesamtnote.
- Mehrere Offerten pro Auto mit frei erfassten Anbietern, Kauf-/Leasing-/Finanzierungsangaben, Zubehör, Gebühren und Sonderkonditionen. Ergänzungen erzeugen neue Versionen; ältere Stände und Änderungen bleiben lesbar.
- Eine beim ersten Angebot gewählte Währung pro Evaluation. Offertkriterien werden je Angebot bewertet; nach einer neuen Version bleibt die Einschätzung erhalten und wird zur erneuten Prüfung markiert.
- Dokumente mit unveränderlichen Dateiversionen und Bezug zum Offertstand. Downloads prüfen die aktuellen Freigaben.
- Gespeicherte Rechenszenarien für Barkauf, Leasing mit Rückgabe oder Übernahme und Kreditfinanzierung. Feste Offertversionen, bestätigte Preise, Restwertbänder und ein Vergleich zum gemeinsamen Stichtag.
- Zusätzliche Kostenpositionen für alle Autos oder je Auto, mit frei gewähltem Zeitraum, einmaligen, monatlichen, jährlichen und inklusiven Positionen sowie Einnahmen. Monats- und Jahresansicht zeigen dieselben Zahlungen.
- Besitzer-, Bearbeiter- und Leserechte pro Evaluation. Die Installationsverwaltung verschafft keinen Zugriff auf fremde Fälle.
- Schutz vor überschriebenen Änderungen bei gleichzeitiger Bearbeitung, serverseitige Validierung und CSRF-Schutz.
- JSON-Export ohne Anhänge oder ZIP-Export mit sämtlichen Dateiversionen und Import einer Evaluation einschliesslich Kriterien, Gruppen, Kandidaten, Bewertungen, vollständiger Offertversionen, Kostenplanung und Szenarien. Ältere Exporte der Versionen 1 bis 5 können weiterhin importiert werden. Ein Import erzeugt eine neue private Evaluation.
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

Zum Sortieren den Punktgriff ziehen oder die Pfeile neben einem Eintrag verwenden. Ein fokussierter Griff unterstützt auch die Pfeiltasten ↑ und ↓; Escape bricht das Ziehen ab. Die Einfügemarkierung zeigt das Ziel, und am Bildschirmrand scrollt die Liste während des Ziehens weiter. Die Bearbeitungsaktion am jeweiligen Eintrag öffnet Bezeichnung, Priorität, Typ und Gruppenzuordnung. Offene Eingaben bleiben auch beim Sortieren erhalten. **Freigaben** und **Evaluation bearbeiten** öffnen eigene Ansichten im selben Evaluationskontext.

Unter **Autos** Kandidaten erfassen und über **Ausstattung prüfen** Werte, Quellen und Notizen hinterlegen. Die optionalen **Technischen Daten** erfassen Abmessungen, Sitzplätze, Stauraum, Gewichte, Leistung, Verbrauch und Lade- bzw. Tankangaben. Fehlende Gewichts- und Leistungswerte werden aus den vorhandenen Angaben berechnet und gekennzeichnet. **Vergleichen** stellt zwei wählbare Autos gegenüber und trennt technische Daten von Kriterienbewertungen. Weitere Kandidaten bleiben über die Auswahlfelder erreichbar. Kriterien mit dem Bezug Offerte werden beim jeweiligen Angebot geprüft.

Unter **Offerten** oder über **Offerten für dieses Auto** ein Angebot erfassen. Beim ersten Angebot die gemeinsame Währung der Evaluation wählen. Eine Anfrage darf noch unvollständig sein; unbekannte Angaben bleiben sichtbar offen. **Neue Version erfassen** übernimmt den aktuellen Stand zur Ergänzung. Frühere Versionen bleiben unverändert in der Historie. **Offerten vergleichen** stellt zwei Anbieter desselben Autos gegenüber. Archivierte Offerten lassen sich wieder einblenden und aktivieren.

In einer Offerte unter **Dokumente** eine Datei hinzufügen. **Neue Dateiversion** ergänzt die Dateihistorie; der Download einer früheren Version bleibt möglich. Dateien sind auf 25 MiB, eine Evaluation auf insgesamt 200 MiB einschliesslich ihrer Historie begrenzt. Der Export enthält vorhandene Anhänge automatisch als ZIP.

Unter **Kosten** den Startmonat und die Planungsdauer festlegen. Nach der ersten Offerte steht die gemeinsame Währung fest. Kostenpositionen für alle Autos oder ein einzelnes Auto erfassen; leere Beträge bleiben offen. Die Aufstellung summiert diese zusätzlichen Zahlungen. Unter **Rechnen** kommen Kauf-/Leasingzahlungen, Restwert, Kreditrestschuld und entgangene Rendite hinzu.

Unter **Rechnen** die gemeinsame jährliche Anlagerendite festlegen und ein **Szenario anlegen**. Dazu eine Offerte wählen, den Gesamtpreis bestätigen und die übernommenen Raten prüfen. Fehlende Beträge bleiben offen. Bei Eigentumsvarianten einen Restwert genau für den gewünschten Stichtag erfassen. Befristete Vergünstigungen über **Kosten für dieses Szenario anpassen** abbilden. **Szenarien vergleichen** stellt zwei frei gewählte Varianten mit derselben Rendite und zum selben Stichtag gegenüber.

Weitere Personen lassen sich unter **Einladungen** hinzufügen. Sie melden sich mit ihrer eingeladenen Adresse an und bestätigen ihren eigenen Code. Erst eine separate Freigabe innerhalb einer Evaluation gibt ihnen Zugriff darauf.

`.env`, Datenbank, Dokumentdateien und Data-Protection-Schlüssel bleiben lokal. Der SDK-Start verwendet `.data/documents`, der Containerstart ein eigenes Dokument-Volume. Zum Stoppen `Ctrl+C` und `docker compose down` verwenden; dabei bleiben die Volumes erhalten. Der vollständig containerisierte Start und die Helm-/Argo-CD-Konfiguration sind in [Betrieb](docs/operations.md) beschrieben.

## Prüfen

```sh
dotnet restore Steervo.sln --locked-mode
dotnet build Steervo.sln --no-restore
./scripts/test.sh
```

Der Testlauf startet dedizierte PostgreSQL-17/18-, SMTP- und S3-Testcontainer mit eigenen Ports; die lokale Anwendung kann weiterlaufen. Der S3-Testserver verwendet Port 55439 mit einem ausschliesslich für Tests bestimmten Bucket. Der Testmailserver verwendet SMTP-Port 52025 und Posteingang-Port 52080. Integrationstests erstellen und entfernen eigene Datenbanken. Die Verbindung zu alternativen Testinstanzen kann mit `STEERVO_TEST_POSTGRES17` und `STEERVO_TEST_POSTGRES18` vorgegeben werden; diese Rollen benötigen das Recht, Testdatenbanken anzulegen. Keine Produktivdatenbank verwenden.

Für Browserprüfungen werden Node.js und Playwright benötigt:

```sh
npm ci --ignore-scripts
# Einmalig, falls noch kein passender Testbrowser vorhanden ist:
npx playwright install chromium
./scripts/test.sh --browser
```

Das Testskript verwendet vorhandene Node-Abhängigkeiten und installiert sie nicht erneut. Ein bereits installierter Chromium-Browser kann über `PLAYWRIGHT_EXECUTABLE_PATH` verwendet werden. Playwright benutzt isolierte Kontexte und niemals das persönliche Browserprofil. Die Tests prüfen den kompletten Einstieg, Kriterien, Freigaben, direkte unzulässige Requests, Passkey-Anmeldung mit virtuellem Authenticator, Wiederherstellung per E-Mail, Neustarts und die mobile Darstellung. Szenariotests prüfen Zahlungszeitpunkte, Zinseszins, Kreditrestschuld, Restwertbänder, Kilometerabrechnung, Offertbezüge und unabhängige Kopien. Kostentests prüfen Fälligkeiten, Teiljahre, offene Beträge, Zeiträume, Kopien und Zugriffsrechte. Dokumenttests prüfen Dateiinhalte, Versionsbezüge, Rechteentzug, Neustarts, ZIP-Rückimport und Löschung. Für Offerten werden unvollständige Angaben, inklusive/offene Preise, Versionen, Unterschiede, erneute Prüfung von Bewertungen, Währungssperre, Archivierung, Rechte, gleichzeitige Änderungen und Import/Export geprüft. Für die Sortierung werden lange Listen, stabile Scrollposition und Tastaturfokus, Drag & Drop zwischen Gruppen, leere Gruppen, Touch-Eingaben, Abbrechen und erhaltene Bearbeitungsentwürfe geprüft. Bilder und Ergebnisse liegen unter `.artifacts/browser/`. Testcontainer anschliessend mit `docker compose -f compose.test.yaml down` stoppen.

Mit `STEERVO_E2E_HTTPS=1 ./scripts/test.sh --browser` läuft derselbe Ablauf zusätzlich über eine eigene HTTPS-Testdomain. Dieser Modus prüft auch die Passkey-Meldungen bei einer falschen Domain und unverschlüsseltem HTTP. Dafür erzeugt der Test ein kurzlebiges Zertifikat; nur der isolierte Testbrowser akzeptiert es. Es wird kein Zertifikat im System installiert.

Um dieselben Browserprüfungen gegen das tatsächliche Linux-Containerimage auszuführen, zuerst `./scripts/build-image.sh` und dann `STEERVO_E2E_CONTAINER=1 STEERVO_E2E_HTTPS=1 ./scripts/test.sh --browser` starten. Der Test verwendet das vorhandene Image ohne Download, die dedizierten Testdienste sowie eigene temporäre Schlüssel- und Dokument-Volumes. Der lokale HTTPS-Testproxy wird über die Gateway-Adresse dieses Testnetzwerks freigegeben.

## Aufbau und Umfang

`Steervo.Core` enthält Fachregeln ohne Web- oder Datenbankabhängigkeit. `Steervo.Calculations` berechnet Zahlungsfälligkeiten, Szenariokosten, Zinseszins und Vermögensstände ohne Web- oder Datenbankabhängigkeit. `Steervo.Application` enthält die Evaluationsabläufe und ihre fachlichen Ports. `Steervo.Infrastructure` implementiert PostgreSQL/EF Core, ASP.NET Core Identity, SMTP sowie lokalen und S3-kompatiblen Dateispeicher. `Steervo.Web` liefert Razor Pages, HTMX-Fragmente, WebAuthn-Interaktion und lokalisierte Oberflächentexte. Es gibt keine öffentliche Programmierschnittstelle.

Dieser Stand unterstützt Anforderungsprofile, Fahrzeugkandidaten, die gemeinsame Bewertung ihrer Ausstattung sowie strukturierte, versionierte Offerten. Dokumente werden versioniert gespeichert und als autorisierte Downloads bereitgestellt. Die zusätzliche Kostenplanung erfasst Zahlungspositionen. Rechenszenarien ergänzen Beschaffung, Rendite, Restwert, Kreditrestschuld und ausdrücklich gewählte Kostenanpassungen. Ergebnisse bleiben bei relevanten Lücken offen.

Kandidaten und Bewertungen übernehmen die Zugriffsrechte ihrer Evaluation. Eine Kandidatenkopie erhält eigene IDs und Bewertungen; fahrzeugspezifische Kostenpositionen erhalten ebenfalls eigene IDs. Offerten werden nicht mitkopiert. Änderungen an der Kopie ändern das Original nicht. Ändert sich eine Messwertschwelle, wird die Erfüllung anhand des gespeicherten Messwerts neu bestimmt. Typ, Bezug oder Einheit lassen sich bei bestehenden Bewertungen erst nach deren Zurücksetzen ändern. Gruppenwechsel und Sortierung erhalten alle Bewertungen.

## Lizenz

Steervo steht unter der **PolyForm Noncommercial License 1.0.0** (siehe [LICENSE](LICENSE)).

**Kostenlos erlaubt** ist jede nichtkommerzielle Nutzung — private Nutzung, Selbstbetrieb, Hobbyprojekte, Lernen und Forschen. Ebenfalls erlaubt ist die Nutzung durch gemeinnützige Organisationen, Bildungseinrichtungen und öffentliche Institutionen.

**Eine kommerzielle Lizenz erforderlich** ist für jede kommerzielle Nutzung, insbesondere den entgeltlichen Betrieb von Steervo für Dritte. Anfragen bitte über ein GitHub Issue.

> **Hinweis zur Bezeichnung:** Steervo ist quelloffen (*source available*), aber keine Open-Source-Software im Sinne der Open Source Initiative, da die Lizenz kommerzielle Nutzung einschränkt. Diese Unterscheidung wird hier bewusst eingehalten.

## Beiträge

Beiträge sind willkommen. Weil Steervo doppelt lizenziert wird, ist für jeden Beitrag die Zustimmung zum [Contributor License Agreement](CLA.md) erforderlich — das Eigentum an deinem Code bleibt bei dir. Ablauf und Hinweise in [CONTRIBUTING.md](CONTRIBUTING.md).

## Gehostete Variante

Eine gehostete Variante ist in Vorbereitung.
