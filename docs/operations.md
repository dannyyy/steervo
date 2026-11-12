# Betrieb

Steervo benötigt PostgreSQL 17 oder 18, SMTP und dauerhaften Speicher für Dokumente sowie ASP.NET Data Protection. Die Anwendung verbindet sich online mit dem Server. Der Browser hält keine eigene Datenbank vor.

## Container

`scripts/build-image.sh` stellt die festgelegten NuGet-Pakete wieder her, veröffentlicht die Anwendung mit dem vorhandenen .NET-SDK und baut daraus `steervo:local`. Das Container-Image enthält die Laufzeit und startet als Benutzer 1654. Ein SDK wird dafür nicht auf dem Host installiert. SDK- und Paketversionen stehen in `global.json` und den Projektdateien; Container-Abhängigkeiten sind über Digests festgelegt.

Für einen vollständigen lokalen Containerstart:

```sh
./scripts/init-local.sh
./scripts/build-image.sh
docker compose --profile app up -d
```

Die Anwendung ist unter `http://localhost:5180` erreichbar; der lokale Posteingang unter `http://localhost:51080`. Die Compose-Konfiguration verwendet absichtlich die Entwicklungsumgebung und unverschlüsseltes SMTP zum lokalen Mailserver. Sie ist für lokale Nutzung vorgesehen.

Die lokale Konfiguration erlaubt mit `AllowedHosts='*'` beliebige Hostnamen und IP-Adressen im HTTP-Host-Header. Bei Bedarf kann dieser Wert in `.env` durch eine in Anführungszeichen gesetzte, mit Semikolon getrennte Liste ohne Portnummern ersetzt werden. Die Bindung des Dienstes an eine Netzwerkschnittstelle wird davon unabhängig konfiguriert. `PublicUrl` bleibt die feste Adresse für Einladungslinks und Passkeys; zusätzliche Hostnamen ändern diese Adresse nicht. Im Helm-Chart wird weiterhin der konfigurierte Ingress-Host als erlaubter Host gesetzt.

`docker compose down` erhält die Daten-, Dokument- und Schlüssel-Volumes. `down -v` löscht sie. Die Anwendung führt beim normalen Start keine Schemaänderungen aus; der separate Migrationsprozess muss vorher erfolgreich abgeschlossen sein.

Nach Änderungen am Quellcode das Image mit `./scripts/build-image.sh` neu bauen. Bei unverändertem Datenbankschema genügt danach `docker compose --profile app up -d --no-deps app`, um nur den Anwendungscontainer zu ersetzen. Für ein Upgrade mit Migrationen zunächst sichern, den laufenden Anwendungscontainer stoppen und den Migrationsprozess erfolgreich ausführen, bevor die Anwendung wieder startet. Vorhandene Konten, Evaluationen und Passkeys bleiben in Datenbank und Schlüsselvolume gespeichert.

## Upgrade auf 0.2.0

Version 0.2.0 ergänzt Kandidaten, technische Daten, Offerten mit Dokumenthistorie, Kostenplanung und Rechenszenarien. Die Anwendung und das Helm-Chart verwenden dieselbe Versionsnummer. Das Image muss in der eigenen Registry gebaut und bereitgestellt werden; ein Chart-Tag veröffentlicht kein Image.

Vor dem Upgrade Datenbank und Data-Protection-Schlüssel sowie bereits vorhandene Dokumente gemeinsam sichern. Bei Compose den Anwendungscontainer stoppen, das neue Image bauen und die Migration separat starten:

```sh
docker compose stop app
./scripts/build-image.sh
docker compose --profile app run --rm --no-deps migrate
docker compose --profile app up -d --no-deps app
```

Den letzten Befehl erst nach erfolgreicher Migration ausführen. Das neue Dokument-Volume wird beim Containerstart bereitgestellt. Bestehende Konten, Freigaben, Evaluationen und Kriterien werden erhalten; neue fachliche Daten werden nicht vorbefüllt. Für eine bisherige Helm-Installation zusätzlich den privaten Garage-Bucket und die Dokumentzugangsschlüssel im vorhandenen Runtime-Secret konfigurieren. Nach dem Upgrade Bereitschaftsprüfung, Anmeldung und eine bestehende Evaluation kontrollieren. Für einen Rückweg den gesicherten Anwendungsstand konsistent wiederherstellen; nur das alte Image zu starten setzt das Datenbankschema nicht zurück.

## Kubernetes und Argo CD

Das Chart unter `deploy/helm/steervo` verwendet eine externe PostgreSQL-Datenbank, ein bestehendes Secret, einen Schlüssel-PVC, internen Garage-Speicher und eine Traefik-`IngressRoute`. Das Beispiel unter `deploy/argocd-application.example.yaml` enthält ausschliesslich Platzhalter. Es wird nichts automatisch auf einen Cluster angewendet.

1. Ein getestetes Image in die eigene Registry übernehmen und seinen Digest konfigurieren.
2. PostgreSQL-Datenbank und Rollen anlegen. Der Migrationsprozess benötigt Schemaänderungsrechte.
3. Das Secret `steervo-runtime` im Ziel-Namespace über den eigenen sicheren Secret-Prozess bereitstellen. Benötigte Schlüssel:
   - `ConnectionStrings__Database`: Npgsql-Verbindungszeichenfolge; in Produktion mit TLS entsprechend der Datenbankkonfiguration.
   - `Setup__Token`: zufälliger Einrichtungsschlüssel mit mindestens 32 Zeichen. Nach erfolgreicher Einrichtung entfernen.
   - `DocumentStorage__AccessKey` und `DocumentStorage__SecretKey`: Schlüssel mit Lese- und Schreibrechten für einen ausschliesslich dieser Installation zugeordneten Garage-Bucket.
   - `Smtp__Username` und `Smtp__Password`, falls der SMTP-Server Authentifizierung verlangt.
4. Internen S3-Endpunkt, Region und bereits angelegten Bucket unter `documentStorage` setzen; die Anwendung legt keinen Bucket an. Öffentliche HTTPS-Adresse, erlaubten Host, SMTP-Absender und tatsächliche Versandlimits in den Chart-Werten setzen. Der konfigurierte Traefik-Zertifikatsresolver muss bereits existieren.
5. Unter `trustedProxies` ausschliesslich die tatsächlichen Proxy-Adressen eintragen. Weitergeleitete Protokollangaben unbekannter Absender werden nicht vertraut. Steervo benötigt hinter Traefik das korrekte HTTPS-Schema für sichere Cookies und WebAuthn.
6. Chart prüfen und über die eigene Argo-CD-Anwendung synchronisieren. Der Schlüssel-PVC wird in Wave −2 erzeugt, der Migrationsjob läuft als Sync-Hook in Wave −1, danach startet die Anwendung. Das bestehende Secret muss vorher verfügbar sein.

```sh
helm lint deploy/helm/steervo
helm template steervo deploy/helm/steervo -f /path/to/installation-values.yaml
```

Das Chart startet bewusst eine Replik mit `Recreate`, passend zum ReadWriteOnce-Schlüsselvolume. Für Änderungen an Topologie, Storage und Proxy-Vertrauen ist die konkrete Installation zu berücksichtigen. Das Migrationskonto und das Laufzeitkonto können bei Bedarf getrennt bereitgestellt werden; vorliegend verwenden beide Prozesse dasselbe bestehende Secret.

## Anmeldung und E-Mail

Die Ersteinrichtung ist nur mit dem konfigurierten Schlüssel und anschliessender E-Mail-Bestätigung möglich. Eine Datenbankrestriktion und eine Transaktionssperre verhindern zwei Hauptkonten. Es gibt keine öffentliche Registrierung. Einladungen gelten sieben Tage und können vor Annahme entzogen werden. Kontoeinladungen und Fallfreigaben sind getrennt.

E-Mail-Codes gelten zehn Minuten, funktionieren einmal und erlauben fünf Prüfversuche. Pro Adresse gelten mindestens 60 Sekunden Abstand und höchstens fünf neue Codes pro Stunde. Zusätzlich begrenzt die Anwendung Anmeldeanfragen je IP-Adresse. Codes werden mit Data Protection geschützt gespeichert und nicht protokolliert. SMTP-Versand reserviert das konfigurierte Stunden- und Tageskontingent transaktional in PostgreSQL. Fehlgeschlagene oder unklare Versandversuche behalten ihre Reservierung, damit ein erneuter Versuch das Anbieterlimit nicht überschreiten kann. Ein ausgeschöpftes Kontingent und Versandfehler erscheinen als ausdrückliche Meldung. Es gibt keine stille Zustellwarteschlange.

Passkeys sind an die konfigurierte öffentliche Adresse gebunden. Für lokale Tests ist `localhost` möglich, im Betrieb ist HTTPS erforderlich. Ein aktueller WebAuthn-fähiger Browser ist notwendig. Passkey-Verwaltung verlangt eine höchstens 15 Minuten alte Anmeldung. E-Mail-Anmeldung bleibt zur Wiederherstellung verfügbar. Eine geänderte öffentliche Domain kann die erneute Einrichtung von Passkeys erforderlich machen.

Bei einer eigenen Domain muss `PublicUrl` die tatsächlich im Browser verwendete HTTPS-Adresse enthalten, einschliesslich eines abweichenden Ports. Ein zusätzlich erlaubter Host oder `AllowedHosts='*'` erweitert die Passkey-Domain nicht. Nach einer Änderung der lokalen `.env` den Anwendungscontainer mit `docker compose --profile app up -d --no-deps app` neu erzeugen. Die Oberfläche erklärt abweichende Adressen, fehlendes HTTPS, fehlende Browserunterstützung und abgebrochene Passkey-Dialoge getrennt.

Für einen lokalen HTTPS-Reverse-Proxy die tatsächliche Absenderadresse des Proxys in `.env` unter `TrustedProxies__0` eintragen; weitere Adressen erhalten die Indizes `1`, `2` usw. Bei Docker kann dies die Gateway-Adresse des verwendeten Container-Netzwerks sein. Entscheidend ist die Adresse, die die Anwendung tatsächlich sieht. Der Proxy muss `X-Forwarded-Proto: https` setzen. Die Vertrauensliste ist unabhängig von `AllowedHosts`.

Bei einer fehlgeschlagenen Passkey-Registrierung die Anwendungslogs mit `docker compose logs --since 10m app` prüfen. Abgelehnte Registrierungen protokollieren den Prüfgrund beziehungsweise Identity-Fehlercodes, ohne den Credential-Inhalt zu protokollieren. Zusätzlich sind die im Browser verwendete Adresse, `PublicUrl`, das weitergeleitete HTTPS-Schema und die Browsermeldung relevant. Die E-Mail-Anmeldung bleibt verfügbar.

## Sicherung und Wiederherstellung

Zusammen sichern:

- PostgreSQL inklusive Identity, Einladungen, Freigaben und Migrationshistorie.
- Sämtliche Dokumentobjekte des zugehörigen Buckets beziehungsweise das lokale Dokumentverzeichnis oder Docker-Volume, einschliesslich früherer Dateiversionen.
- Das gesamte Data-Protection-Schlüsselverzeichnis; im Chart ist dies der Schlüssel-PVC.
- Die zur Installation gehörenden Secrets, öffentliche Adresse und Chart-Werte über den eigenen sicheren Sicherungsweg.

Der Schlüssel-PVC enthält kryptografisches Schlüsselmaterial. Zugriff und Sicherungen müssen entsprechend beschränkt und im Storage beziehungsweise im Sicherungsziel verschlüsselt werden. Die Anwendung verschlüsselt dieses Verzeichnis nicht nochmals mit einem separat verwalteten Zertifikat.

Eine Wiederherstellungsprobe gehört in eine isolierte Umgebung: PostgreSQL-Dump einspielen, passendes Schlüsselverzeichnis und Konfiguration wiederherstellen, Migrationsstand prüfen und dann E-Mail-Anmeldung, Passkeys an der passenden Domain, private/geteilte Evaluationen, Kriterienreihenfolge, Kandidaten, Bewertungen, technische Daten, Offertversionen, Positionen, die festgelegte Währung sowie Planungszeitraum, Kostenfälligkeiten, Szenarien und ihre Restwert-Stichtage kontrollieren. Danach auch aktuelle und frühere Dateiversionen herunterladen und ihren Inhalt prüfen. Ein blosser erfolgreicher Datenbank-Restore ersetzt diese Funktionsprüfung nicht. Datenbank und Objektspeicher müssen denselben Sicherungsstand abbilden; während einer gemeinsamen Sicherung Schreibzugriffe und die Dateibereinigung stoppen. Eine neuere Datenbank mit älteren Dokumentobjekten kann auf nicht gesicherte Dateien verweisen.

`/health/live` prüft den laufenden Prozess, `/health/ready` die Datenbankverbindung und den vollständigen Migrationsstand. Vor einem Upgrade eine Sicherung erstellen und Migrationen prüfen; ein altes Image ist kein automatischer Datenbank-Rollback.

## Datenaustausch

Ohne Dokumente wird ein JSON-Export erzeugt. Bei vorhandenen Dateien liefert derselbe Export einen ZIP-Container mit `evaluation.json`, `documents.json` und `files/` für sämtliche Dateiversionen. `documents.json` verwendet das Format `steervo-documents`, Version 1; es enthält Offertbezüge, Dateinamen, Versionsnummern, Notizen, Zeitpunkte, Längen und SHA-256-Prüfsummen. Interne Speicheradressen und Objektschlüssel werden nicht exportiert.

JSON-Exporte der Version 6 enthalten Anforderungsprofil, Gruppen, Kandidaten mit optionalen technischen Daten, deren Einstufungen und Kriterienbewertungen mit Quellenangaben und Notizen sowie Offerten mit allen Versionen, Positionen, Archivstatus, Offertbewertungen, gemeinsamer Währung, Planungszeitraum, Kostenpositionen, Szenarien, Restwert-Stichtage und Kostenanpassungen. Konten und Freigaben werden nicht exportiert. Ein Import legt eine neue private Evaluation mit eigenen IDs an und ordnet alle Bewertungsbezüge neu zu; es entsteht keine Verbindung zur ursprünglichen Evaluation.

Exporte der Versionen 1 bis 5 bleiben importierbar. Version 1 enthält noch keine Kandidaten, Version 2 noch keine technischen Daten und Version 3 noch keine Offerten; Version 4 enthält noch keine Kostenplanung, Version 5 noch keine Szenarien. Die JSON-Importgrenze beträgt 5 MB; ZIP-Archive sind auf 250 MiB begrenzt, mit höchstens 200 MiB Dokumentinhalt. Ein ZIP-Import prüft sämtliche Bezüge und Prüfsummen, speichert unabhängige Dateiobjekte und legt erst danach die Evaluation an. Es wird keine teilweise importierte Evaluation gespeichert. Daneben gelten 100 Gruppen, 1'000 Kriterien und 100 Kandidaten. Zusätzlich gelten höchstens 1'000 Offerten, 1'000 Versionen je Offerte, insgesamt 5'000 Versionen und 500 Positionen je Version sowie 1'000 Kostenpositionen je Evaluation. Unbekannte Formatversionen, ungültige Werte und Verweise auf fehlende Kandidaten oder Kriterien werden abgelehnt. Ein JSON-Export ersetzt keine vollständige Sicherung der Installation.

Technische Daten bleiben von Kriterienbewertungen getrennt. Alle Felder sind optional; leere Werte bedeuten unbekannt, eine erfasste 0 bleibt erhalten, wo sie sinnvoll ist (beispielsweise Frunk oder DC-Ladeleistung). Bei Gewichten reichen zwei der drei Werte Leergewicht, zulässiges Gesamtgewicht und Zuladung. Bei Leistung reicht kW oder metrische PS (1 PS = 0,73549875 kW). Berechnete Werte werden angezeigt, gespeichert und exportiert werden die eingegebenen Ausgangswerte. Widersprüchliche Angaben werden abgelehnt; bei gleichzeitig angegebenen kW und PS wird eine Rundungsabweichung bis 0,5 kW toleriert.

Abmessungen werden in mm, Gewichte in kg, Volumen in Litern und Beschleunigung in Sekunden erfasst. Breite ohne/mit Spiegeln und nutzbare/gesamte Batteriekapazität sind getrennte Felder. Elektrischer Verbrauch hat die Einheit kWh/100 km, Kraftstoffverbrauch l/100 km. AC- und DC-Ladeleistung sind getrennt; die Ladezeit bezieht sich auf 20–80 %. Hybridfahrzeuge können beide Antriebsbereiche nutzen. Reichweite und Verbrauch werden als Angaben übernommen, nicht voneinander abgeleitet.


## Offerten und Versionshistorie

Offerten gehören zu einem Kandidaten und übernehmen die Freigaben seiner Evaluation. Leser sehen auch frühere Versionen; Bearbeiter dürfen Angebote erfassen, neue Versionen speichern, archivieren und bewerten. Eine archivierte Offerte muss vor weiteren Änderungen wieder aktiviert werden. Die Installation legt keine Geschäftsdaten oder Währung vorab fest.

Die erste Offerte legt das Währungskürzel für die gesamte Evaluation fest. Weitere Angebote verwenden dieselbe Währung. Vorhandene Beträge werden weder automatisch umgerechnet noch durch ein anderes Kürzel umgedeutet. Beträge und Konditionen dürfen offen bleiben; 0 bedeutet einen bekannten Wert. Bei Positionen sind ein bekannter Betrag, inklusive Leistungen und offene Preise unterschiedliche Zustände. Monatsraten, Zinsen und Restpreise werden als Angaben erfasst, ohne daraus eine Gesamtkostenrechnung abzuleiten.

Jede gespeicherte Ergänzung erzeugt einen vollständigen neuen Stand mit Zeitpunkt, optionaler Änderungsnotiz und eigenen Positionen. Frühere Versionen werden nicht aktualisiert. Datenbank-Trigger verhindern Updates an gespeicherten Versionen und Positionen. Das bewusste Löschen des übergeordneten Kandidaten beziehungsweise der Evaluation entfernt auch deren Offerten und Historie. Für unveränderte Vorschläge ohne Änderungsnotiz wird keine zusätzliche Version angelegt.

Offertbewertungen halten fest, zu welcher Version sie geprüft wurden. Nach einer neuen Version bleiben Wert, Quelle und Notiz sichtbar, zählen aber bis zur erneuten Bestätigung als ungeprüft. Die Versionshistorie selbst wird dadurch nicht verändert. Kandidatenkopien übernehmen technische Daten und Fahrzeugbewertungen, aber keine Offerten. Evaluationsexporte enthalten dagegen die gesamte Offertgeschichte; ein Import vergibt neue IDs und übernimmt keine Freigaben.


## Dokumente und Speicher

Dokumente gehören zu einer Offerte und übernehmen die Rechte der Evaluation: Leser dürfen alle Dateiversionen herunterladen, Bearbeiter dürfen neue Dokumente und Versionen hinzufügen oder ein Dokument samt Historie löschen. Archivierte Offerten bleiben lesbar und müssen für Änderungen zuerst aktiviert werden. Neue Dateiversionen verweisen auf den aktuellen Offertstand; historische Offertansichten zeigen nur Dateiversionen, die diesem oder einem früheren Stand zugeordnet sind. Die Dateiversionen einer Offerte werden beim Kopieren eines Autos nicht mitkopiert.

Eine Datei darf höchstens 25 MiB gross sein. Pro Evaluation sind insgesamt 200 MiB und 1'000 Dateiversionen erlaubt, höchstens 100 Versionen pro Dokument. Leere Dateien werden abgelehnt. Dateien liegen ausserhalb des Webroots und werden ausschliesslich nach aktueller Zugriffsprüfung durch die Anwendung geladen. Downloads verwenden `application/octet-stream`, einen Anhang-Dateinamen und `nosniff`; es gibt keine HTML-Ausführung oder eingebettete Vorschau. SHA-256 und Länge werden vor der Auslieferung geprüft. Es findet keine Virenprüfung statt.

Der SDK-Start verwendet `DocumentStorage__Provider=FileSystem` und `.data/documents`; der Containerstart `/var/steervo/documents` im Volume `documents`. Ein Wechsel zwischen SDK- und Containerbetrieb übernimmt Dateien nicht automatisch. Das gesamte Verzeichnis muss bei gestoppter Anwendung mit unveränderten Objektnamen übertragen werden. Der Benutzer 1654 benötigt im Container Schreibrechte.

Für Garage `DocumentStorage__Provider=S3`, `DocumentStorage__Endpoint`, `DocumentStorage__Region`, `DocumentStorage__Bucket` sowie die beiden Zugangsschlüssel konfigurieren. Das Helm-Chart setzt Provider, Endpunkt, Region und Bucket; Schlüssel kommen ausschliesslich aus dem vorhandenen Runtime-Secret. Beispielwerte enthalten keine Zugangsdaten. Die Anbindung verwendet signierte S3-Anfragen mit Pfadadressierung, passend zur [Garage-Konfiguration](https://garagehq.deuxfleurs.fr/documentation/connect/apps/). Der Bucket muss vorab existieren und privat bleiben. Interne HTTP-Verbindungen sind möglich; bei HTTPS bleibt die Zertifikatsprüfung aktiv. Für jede Installation einen eigenen Bucket verwenden und Datenbank und Speicher gemeinsam umstellen.

Uploads werden vor dem Schreiben des Objekts als ausstehende Bereinigung registriert. Erst das atomare Speichern der Dateiversion übernimmt das Objekt dauerhaft. Unterbrochene oder abgelehnte Uploads werden nach einer Stunde zur Bereinigung freigegeben. Beim Löschen eines Dokuments, Autos oder einer Evaluation legt ein Datenbank-Trigger für alle betroffenen Dateien Löschaufträge an. Diese bleiben auch bei einem Speicherausfall erhalten. Bis zur physischen Löschung ist ein Download über Steervo bereits gesperrt.

Beim Start und nach Änderungen verarbeitet die Anwendung bis zu 20 fällige Löschaufträge mit einem Zeitbudget von drei Sekunden. Für grössere Rückstände kann derselbe Container mit `--cleanup-documents` und bis zu fünf Minuten Laufzeit je Aufruf gestartet werden, beispielsweise lokal:

```sh
docker compose --profile app run --rm --no-deps app --cleanup-documents
```

Wenn fällige Löschungen wegen eines Fehlers verbleiben, endet der Aufruf mit Exitcode 1. Der Prozess benötigt dieselbe Datenbank und denselben Dokumentenspeicher wie die Anwendung. Mehrere Prozesse koordinieren sich über Datenbanksperren. Bei einem Speicherausfall bleiben Aufträge für einen erneuten Lauf erhalten; ausstehende Aufträge stehen in `PendingDocumentDeletion`. Es läuft kein periodischer Hintergrunddienst. Bei längerer Inaktivität die Bereinigung betrieblich ausführen oder die Anwendung neu starten. Bei einer Wiederherstellung zuerst den konsistenten Dokument- und Datenbankstand bereitstellen, bevor irgendein Anwendungs- oder Bereinigungsprozess startet.

Uploads und ZIP-Importe verwenden begrenzte temporäre Dateien. Im Container liegt dieser Arbeitsbereich unter `/tmp`; dafür und für die Multipart-Puffer genügend Platz vorsehen (mindestens 1 GiB für einen maximalen Import, zusätzlich je gleichzeitiger Operation). Der Speicher muss wie die übrigen Anwendungsdaten geschützt sein. Das Löschen eines Dokumentes aus dem aktiven Speicher entfernt keine historischen Sicherungskopien; dafür gilt die eigene Aufbewahrung der Backups.


## Zusätzliche Kosten planen

Die Kostenplanung verwendet einen vom Nutzer gewählten Startmonat und 1 bis 1'200 Monate. Ohne festgelegten Zeitraum werden keine Positionen angelegt. Die bei der ersten Offerte gewählte Evaluationswährung gilt unverändert auch für die Kostenplanung. Es gibt keine vorbefüllten Beträge, Kostenpositionen oder Planungszeiträume.

Eine Position gilt entweder für alle Autos der Evaluation oder nur für einen Kandidaten. Beim Anzeigen eines Kandidaten werden die gemeinsamen Positionen genau einmal hinzugerechnet. Verschiedene Kandidaten werden nicht zu einer Gesamtsumme addiert. Eine Kandidatenkopie bekommt unabhängige Kopien ihrer eigenen Kostenpositionen; gemeinsame Positionen bleiben gemeinsam. Beim Entfernen eines Kandidaten werden nur dessen eigene Positionen gelöscht.

Der erste Monat ist relativ zum Planungsbeginn angegeben. Monatspositionen fallen in jedem aktiven Monat an. Jahrespositionen werden in voller Höhe im ersten Fälligkeitsmonat und danach alle zwölf Monate gebucht; Teiljahre werden nicht proportional gekürzt. Einmalpositionen fallen nur im angegebenen Monat an. Der optionale letzte Monat ist einschliesslich; ohne Endmonat gilt das Ende des gewählten Planungshorizonts. Inklusive Positionen bleiben dokumentiert und tragen keinen Zahlungsbetrag. Eine Einnahme wird gesondert ausgewiesen und von den Ausgaben abgezogen.

Beträge dürfen offen bleiben. Eine explizite 0 bleibt bekannt; ein leerer fälliger Betrag führt zu einer sichtbar unvollständigen Teilsumme. Positionen ohne Fälligkeit im ausgewählten Zeitraum bleiben gespeichert und werden entsprechend markiert. Ein Wechsel des Startmonats verschiebt alle relativen Fälligkeiten gemeinsam. Die Jahresansicht gruppiert dieselben Zahlungen nach Kalenderjahren; die Monatsansicht verteilt Jahresbeträge nicht erneut. Der ausgewiesene Monatsdurchschnitt ist die bekannte Summe geteilt durch die Planungsmonate, keine tatsächliche Monatsrate.

Die Ansicht **Kosten** enthält ausschliesslich die zusätzlich erfassten Positionen. Die Verknüpfung mit Kaufpreis, Leasing-/Kreditraten, Restwert, entgangener Rendite und ausdrücklich gewählten Vergünstigungen erfolgt unter **Rechnen**. Insbesondere wird ein in einer Offerte dokumentiertes Servicepaket nicht stillschweigend mit einer Kostenposition verrechnet. Die Aufstellung wird deshalb nicht als Gesamtkostenrechnung oder als Empfehlung für ein Auto bezeichnet.

Beträge verwenden dezimale Arithmetik mit höchstens zwei Nachkommastellen; angezeigt wird in der gemeinsamen Währung. Leser können die Planung und ihre Zusammensetzung sehen, Bearbeiter dürfen Zeitraum und Positionen ändern. Änderungen verwenden dieselbe Versionsprüfung wie die übrige Evaluation. Der JSON- und ZIP-Export enthält die Eingaben; Ergebnisse werden daraus neu berechnet. Ein Import vergibt neue Positions- und Kandidaten-IDs und übernimmt keine Freigaben.


## Rechenszenarien

Ein Szenario gehört zu einer Offerte und verwendet eine ausdrücklich gewählte, unveränderliche Offertversion. Eine neuere Version wird angezeigt, aber nicht automatisch übernommen. Der Wechsel des Offertbezugs innerhalb der Historie verändert keine eigenen Rechenannahmen. Der Gesamtpreis muss separat bestätigt werden; Zubehör, Gebühren und Zugaben werden nicht automatisch addiert. Zusätzliche Einmalgebühren dürfen nur Beträge enthalten, die nicht bereits im Gesamtpreis oder in anderen Zahlungen stecken.

Die gemeinsame Renditeannahme ist ein effektiver Jahreszins. Die monatliche Verzinsung entspricht dessen zwölfter Wurzel; innerhalb der Vergleichsrechnung werden Zwischenstände nicht auf zwei Nachkommastellen gerundet. Zahlungen am Monatsanfang verlieren die Rendite dieses Monats, Zahlungen am Monatsende erst die der folgenden Monate. Ausgaben und Einnahmen der Kostenplanung fallen am Monatsanfang an. Jahreskosten bleiben vollständig im tatsächlichen Fälligkeitsmonat. Steuern auf Kapitalerträge und Inflation werden nicht berücksichtigt.

Barkauf zahlt den bestätigten Preis zu Beginn. Leasing zahlt Anzahlung und Gebühren zu Beginn sowie Raten am Anfang jedes Vertragsmonats. Rückgabe ist genau am Ende der Vertragslaufzeit berechenbar; dann wird eine Mehr- oder Minderkilometerabrechnung mit der angenommenen Jahresfahrleistung vorgenommen. Nur der in dieser Richtung benötigte Kilometerpreis muss bekannt sein. Bei Übernahme wird der Übernahmepreis am Ende der Laufzeit bezahlt; danach bleibt das Auto im Eigentum. Für vorzeitige Vertragsbeendigung oder weitere Nutzung nach Rückgabe werden keine Ersatzkonditionen angenommen.

Bei Kreditfinanzierung ist der eigene Einsatz der Gesamtpreis abzüglich Kreditbetrag. Der monatliche Kreditzins ist der nominale Jahreszins geteilt durch zwölf; Kreditzinsen werden monatlich auf zwei Nachkommastellen gerundet. Reguläre Raten fallen am Monatsende an. Eine Schlussrate kommt zusätzlich nach der letzten regulären Rate hinzu. Die Eingaben müssen einen Tilgungsplan ergeben, der am Vertragsende genau auf null endet. Ein widersprüchlicher Plan wird als unvollständig angezeigt, nicht mit einer erfundenen Ausgleichszahlung korrigiert. Vor Vertragsende wird die berechnete Restschuld ausgewiesen.

Die effektiven Kosten am Stichtag sind alle bis dahin fälligen Nettozahlungen zuzüglich ihrer entgangenen Rendite und der verbleibenden Kreditschuld, abzüglich des Fahrzeugwerts. Eigentumsvarianten benötigen dafür einen Restwert genau am gewählten Stichtag. Pessimistischer, erwarteter und optimistischer Wert sind getrennte Eingaben; zwischen Stichtagen wird nicht interpoliert. Bei Rückgabe ist der Fahrzeugwert null. Das Restwertband muss zur angenommenen Fahrleistung passen; eine automatische Abwertung aufgrund zusätzlicher Kilometer findet nicht statt.

Für die ergänzende Kontodarstellung starten alle Szenarien mit demselben Referenzkapital: dem höchsten eingetragenen Gesamtpreis der Evaluation, sonst null. Dieser Rechenbetrag beeinflusst weder effektive Kosten noch die Kostendifferenz und ist kein Budget. Rechnerisch negative Kontostände stellen keine tatsächliche Kreditlinie dar. Der Zweiervergleich verwendet den erwarteten Restwert und denselben Stichtag. Die Restwertschwelle verändert ausschliesslich den Fahrzeugwert des linken Szenarios und lässt alle übrigen Annahmen fest.

Gemeinsame und fahrzeugspezifische Kosten werden aktuell eingerechnet. Eine Szenarioanpassung ersetzt den Betrag einer Position für einen ausdrücklich festgelegten Zeitraum; 0 kann eine Gratisleistung abbilden. Frequenz und Zahlungsrichtung bleiben erhalten. Ein leerer Ersatzbetrag bleibt unbekannt. Ausserhalb des Zeitraums gilt wieder der ursprüngliche Betrag. Änderungen an globalen Kosten, Zeitraum und Rendite wirken auf bestehende Szenarien. Die Prüfung der Zusatzkosten und Vergünstigungen wird je Szenario ausdrücklich bestätigt. Ein Rechenszenario ist kein unveränderlicher Entscheidungsstand.

Leser können Szenarien, Vergleiche und historische Offertbezüge sehen. Bearbeiter können Annahmen, Restwerte und Anpassungen ändern. Die Versionsprüfung schützt vor unbeabsichtigtem Überschreiben. Szenariokopien besitzen unabhängige Restwerte und Anpassungen; gemeinsame Kosten bleiben gemeinsame Eingaben. Beim Kopieren eines Autos werden keine Offerten oder Szenarien übernommen. Das Löschen einer Kostenposition entfernt ihre Szenarioanpassungen; das Löschen eines Autos entfernt auch seine Offerten und Szenarien.

JSON- und ZIP-Exporte enthalten die Ausgangswerte; Ergebnisse werden nach Import neu berechnet. Alle Szenario-, Offert- und Kostenbezüge werden auf unabhängige IDs umgestellt. Die Grenzen betragen 200 Szenarien je Evaluation und 100 Restwert-Stichtage je Szenario. Planung und Vertragslaufzeiten sind auf 1 bis 1'200 Monate begrenzt. Die Rendite kann zwischen −99 und 100 Prozent liegen; Kombinationen ausserhalb des darstellbaren Zahlenbereichs liefern eine sichtbare Rechengrenze statt eines Ergebnisses.
