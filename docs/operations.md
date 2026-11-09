# Betrieb

Steervo benötigt PostgreSQL 17 oder 18, SMTP und ein dauerhaftes Verzeichnis für ASP.NET Data Protection. Die Anwendung verbindet sich online mit dem Server. Der Browser hält keine eigene Datenbank vor.

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

`docker compose down` erhält die Daten- und Schlüssel-Volumes. `down -v` löscht sie. Die Anwendung führt beim normalen Start keine Schemaänderungen aus; der separate Migrationsprozess muss vorher erfolgreich abgeschlossen sein.

Nach Änderungen am Quellcode das Image mit `./scripts/build-image.sh` neu bauen. Bei unverändertem Datenbankschema genügt danach `docker compose --profile app up -d --no-deps app`, um nur den Anwendungscontainer zu ersetzen. Für ein Upgrade mit Migrationen zunächst sichern, den laufenden Anwendungscontainer stoppen und den Migrationsprozess erfolgreich ausführen, bevor die Anwendung wieder startet. Vorhandene Konten, Evaluationen und Passkeys bleiben in Datenbank und Schlüsselvolume gespeichert.

## Kubernetes und Argo CD

Das Chart unter `deploy/helm/steervo` verwendet eine externe PostgreSQL-Datenbank, ein bestehendes Secret, einen Schlüssel-PVC und eine Traefik-`IngressRoute`. Das Beispiel unter `deploy/argocd-application.example.yaml` enthält ausschliesslich Platzhalter. Es wird nichts automatisch auf einen Cluster angewendet.

1. Ein getestetes Image in die eigene Registry übernehmen und seinen Digest konfigurieren.
2. PostgreSQL-Datenbank und Rollen anlegen. Der Migrationsprozess benötigt Schemaänderungsrechte.
3. Das Secret `steervo-runtime` im Ziel-Namespace über den eigenen sicheren Secret-Prozess bereitstellen. Benötigte Schlüssel:
   - `ConnectionStrings__Database`: Npgsql-Verbindungszeichenfolge; in Produktion mit TLS entsprechend der Datenbankkonfiguration.
   - `Setup__Token`: zufälliger Einrichtungsschlüssel mit mindestens 32 Zeichen. Nach erfolgreicher Einrichtung entfernen.
   - `Smtp__Username` und `Smtp__Password`, falls der SMTP-Server Authentifizierung verlangt.
4. Öffentliche HTTPS-Adresse, erlaubten Host, SMTP-Absender und tatsächliche Versandlimits in den Chart-Werten setzen. Der konfigurierte Traefik-Zertifikatsresolver muss bereits existieren.
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
- Das gesamte Data-Protection-Schlüsselverzeichnis; im Chart ist dies der Schlüssel-PVC.
- Die zur Installation gehörenden Secrets, öffentliche Adresse und Chart-Werte über den eigenen sicheren Sicherungsweg.

Der Schlüssel-PVC enthält kryptografisches Schlüsselmaterial. Zugriff und Sicherungen müssen entsprechend beschränkt und im Storage beziehungsweise im Sicherungsziel verschlüsselt werden. Die Anwendung verschlüsselt dieses Verzeichnis nicht nochmals mit einem separat verwalteten Zertifikat.

Eine Wiederherstellungsprobe gehört in eine isolierte Umgebung: PostgreSQL-Dump einspielen, passendes Schlüsselverzeichnis und Konfiguration wiederherstellen, Migrationsstand prüfen und dann E-Mail-Anmeldung, Passkeys an der passenden Domain, private/geteilte Evaluationen sowie Kriterienreihenfolge kontrollieren. Ein blosser erfolgreicher Datenbank-Restore ersetzt diese Funktionsprüfung nicht. Dieser Stand speichert noch keine Dokumentobjekte.

`/health/live` prüft den laufenden Prozess, `/health/ready` die Datenbankverbindung und den vollständigen Migrationsstand. Vor einem Upgrade eine Sicherung erstellen und Migrationen prüfen; ein altes Image ist kein automatischer Datenbank-Rollback.
