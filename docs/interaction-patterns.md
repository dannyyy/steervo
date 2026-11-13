# Gemeinsame Interaktionsmuster

Diese Regeln gelten für die gesamte bestehende Anwendung. Die Canvas/Tinte-Migration ist abgeschlossen; die Regeln ergänzen [DESIGN.md](../DESIGN.md). Umsetzungsstand und Prüfnachweise stehen im [Migrationsplan](design-migration.md).

## Navigation und Kontext

Evaluation, ausgewähltes Fahrzeug, Offerte beziehungsweise Rechenszenario und Vergleichspartner bleiben beim Wechsel zwischen Erfassen und Vergleichen erhalten. Zurück führt zum sinnvollen Ausgangspunkt; direkt aufrufbare Seiten und Browsernavigation bleiben nutzbar. Der aktive Bereich ist visuell und über `aria-current` erkennbar.

Die globale Navigation führt zu Evaluationen und Konto; der Evaluationskontext führt zu den vorhandenen Fachbereichen. Freigaben, Einstellungen, Kosten und Rechnen dürfen durch die verkürzte Mockup-Navigation nicht verschwinden. Lange Navigation auf kleinen Bildschirmen wird strukturell angepasst, nicht durch unleserlich kleine Schrift eingepasst.

Kostenbearbeitung erhält Fahrzeugbezug, Monats-/Jahresansicht und Seite der Zahlungstabelle. Szenariobearbeitung erhält Vergleichspaar, Stichtag und Zahlungsansicht. Nach Speichern führt der Fokus zur betroffenen Position oder zum Szenariotitel; lokale Annahmen führen zu ihrem Abschnitt zurück. Im mobilen Szenarienvergleich bleibt die zuvor bearbeitete Spalte sichtbar.

## Welche Bearbeitungsform passt zu welcher Aufgabe?

| Aufgabe | Gemeinsames Zielmuster | Grund |
| --- | --- | --- |
| Fahrzeug-/Offertbewertung mit Wert, Grundlage, Notiz | Editor am betroffenen Kriterium | Der bewertete Inhalt bleibt der Bezugspunkt |
| Fahrzeugbewertung im mobilen Vergleich | Editor außerhalb der horizontal scrollenden Tabelle mit Fahrzeug- und Kriteriumsname | Ein vollständiges Formular bleibt innerhalb der Bildschirmbreite |
| Offertbewertung aus dem Vergleich | Link zum Kriterium in der Offertakte; gewähltes Paar und Rückweg zur Spalte bleiben erhalten | Die Bewertung nennt den aktuellen Offertstand und dessen Prüfbedarf |
| Dokument hinzufügen / neue Dateiversion | Dedizierte Formularseite mit demselben Entwurfsschutz | Datei, Versionsbezug und Uploadfehler bleiben zusammen |
| Kurzer Objektname, Gruppe oder kleine Kriterienänderung | Kompakter Editor am Inhalt | Keine Unterbrechung für eine lokale Änderung |
| Neues Fahrzeug / kompakte Stammdaten / längere persönliche Notiz | Gemeinsame seitliche Bearbeitungsfläche, auf Mobilgeräten volle Breite | Der zugrunde liegende Fahrzeugkontext bleibt erhalten |
| Neue Evaluation und andere kompakte Anlageaufgaben | Dasselbe gemeinsame Editorverhalten; Größe passend zum Inhalt | Kein eigener Dialogstil pro Objekt |
| Planungszeitraum, gemeinsame Renditeannahme, Restwert-Stichtag, Kostenanpassung, Szenariokopie | Gezielt geöffneter Inline-Editor mit gemeinsamen Aktionen | Kleine Annahmen bleiben am zugehörigen Ergebnis; keine dauerhaft sichtbare Formularsammlung |
| Vollständige Offerte, neue Offertversion, Kostenposition oder Rechenszenario | Dedizierte, gegliederte Formularseite | Längere zusammenhängende Aufgaben brauchen Raum und stabile Navigation |
| Passkey anlegen / umbenennen | Gezielt geöffneter Inline-Editor im Konto | Name, Gerätebestätigung und Fehler bleiben zusammen |
| Person zur Installation einladen | Gemeinsame seitliche Bearbeitungsfläche | Einladung und Evaluationsfreigabe bleiben getrennte Aufgaben |
| Dauerhaftes Entfernen, Zurücksetzen mit Datenverlust, Entwurf verwerfen | Kompakter Bestätigungsdialog | Objekt und tatsächliche Folgen müssen bewusst bestätigt werden |
| Archivieren / Wiederherstellen | Aktion am Objekt mit klarer Rückmeldung | Reversibles Verhalten von dauerhaftem Löschen unterscheiden |
| Nur lesen, Quellen prüfen, historische Version ansehen | Lesedarstellung beziehungsweise aufklappbare Details | Ein Lesevorgang öffnet kein scheinbar bearbeitbares Formular |

„Einheitlich“ bedeutet dieselbe Aufgabenlogik, Beschriftung und Rückmeldung. Es bedeutet nicht, jedes Formular in einen Dialog zu verwandeln. Das vorhandene native `<dialog>` kann die gemeinsame seitliche oder kompakte Darstellung tragen; kein neues UI-Framework ist dafür nötig.

## Formulare und Aktionen

- Ein sichtbares Label pro Feld; Hinweise und Einheit am betroffenen Feld. Platzhalter ersetzen keine Labels.
- Pflicht und optional werden verständlich gekennzeichnet. Unbekannte Werte bleiben leer/offen; kein automatisch gesetztes Nein oder null.
- Der Editor nennt das bearbeitete Objekt. Bei Bewertungen nennt er zusätzlich Kriterium, Anforderung und gegebenenfalls Priorität.
- Eine Hauptaktion pro Aufgabe. Bei Bearbeitung **Speichern** und **Abbrechen**; bei Anlage das Objekt konkret benennen, beispielsweise **Auto hinzufügen**. Keine Mischung aus „OK“, „Übernehmen“ und „Speichern“ für denselben Vorgang.
- Aktionen stehen in allen Editoren in derselben Reihenfolge: Abbrechen vor der hervorgehobenen Abschlussaktion. Die mobile Darstellung erhält beide ohne Überlauf.
- Gleichartige Felder und Fehler verwenden dieselben Teilansichten und CSS-Regeln. Technische Formularnamen und Fachbegriffe werden nicht für eine rein optische Vereinheitlichung umdefiniert.
- Im laufenden Request sind doppelte Absendungen verhindert; eine erkennbare Rückmeldung zeigt, dass gespeichert wird. Erfolg erst nach bestätigter Serverantwort anzeigen.
- Ein gespeicherter Wert aktualisiert alle betroffenen Darstellungen. Die Rückmeldung bleibt knapp; Fehler verschwinden nicht in einem kurzlebigen Toast.

## Öffnen, Schließen und Fokus

1. Ein Editor wird durch eine eindeutig beschriftete Aktion geöffnet. Er erhält einen zugänglichen Titel und passenden Anfangsfokus.
2. Ein modaler Dialog hält den Fokus im Dialog; Hintergrundaktionen sind währenddessen nicht verfügbar. Ein Inline-Editor erhält keine künstliche modale Fokusfalle.
3. **Abbrechen**, Schließen und Escape folgen derselben Regel. Unveränderte Eingaben schließen direkt; ungespeicherte Änderungen werden nicht still verworfen. Der Nutzer kann weiter bearbeiten oder bewusst verwerfen.
4. Ein Klick auf den Hintergrund darf keine ungespeicherten Eingaben verlieren. Keine verschachtelten Bearbeitungsdialoge.
5. Nach Schließen oder erfolgreichem Speichern kehrt der Fokus zum Auslöser beziehungsweise einer sinnvoll benachbarten Stelle zurück. Dieser Bezug muss nach einem HTMX-Austausch wiederherstellbar sein.
6. Es öffnet sich kein zweiter Editor, der den ersten Entwurf unbemerkt ersetzt. Navigation und Fahrzeugwechsel berücksichtigen ungespeicherte Änderungen.

Für destruktive Bestätigungen: Objektname und Folge im Dialog nennen, sichere Aktion zuerst fokussieren, Aktion konkret benennen (z. B. **Fahrzeug entfernen**). Nur tatsächlichen Datenverlust bestätigen; normales Speichern bekommt keine zweite Bestätigungsstufe.

## Fehler, Konflikte und Wiederherstellung

| Zustand | Erwartetes Verhalten |
| --- | --- |
| Feldvalidierung / HTTP 422 | Eingaben erhalten, Editor geöffnet lassen, Fehler am Feld und bei Bedarf als Zusammenfassung; Fokus zum ersten relevanten Fehler |
| Gleichzeitige Änderung / HTTP 409 | Lokalen Entwurf erhalten, Konflikt verständlich benennen und erneutes Laden/Prüfen anbieten; kein stilles Überschreiben und keine Erfolgsmeldung |
| Netz- oder Serverfehler | Entwurf behalten, Wiederholen ermöglichen, Fehler sichtbar und zugänglich anzeigen; Erfolg nicht vorwegnehmen |
| Fehlende/entzogene Berechtigung | Serverentscheidung respektieren, Bearbeitung beenden beziehungsweise verhindern und Situation erklären; kein Weiterarbeiten mit veralteten Rechten |
| Erfolgreiche Speicherung | Aktuellen Stand anzeigen, kurze Statusmeldung, Fokus und Kontext erhalten |
| Leerer Bereich | Erklären, was fehlt, und eine passende nächste Aktion anbieten, sofern die Rolle sie erlaubt |
| Keine Treffer im Filter | Filterbezug zeigen und Rückkehr zu allen Einträgen anbieten |
| Nur Leserecht / alte Version | Lesedarstellung und Grund erkennbar; keine scheinbar bedienbaren Schreibaktionen |

Bei langen Offert-, Kosten- und Szenarioformularen sowie Uploads ersetzen Fehler das Formular nicht. Ein Versionskonflikt zeigt einen Link zum aktuellen Stand in einem zweiten Tab; **Stand geprüft – erneut speichern** sendet den erhaltenen Entwurf ausdrücklich mit dem geprüften Versionsstand. Weitere gleichzeitige Änderungen lösen erneut einen Konflikt aus. Gewählte Dateien bleiben bei HTMX-Fehlern im ursprünglichen Eingabefeld; bei einem nativen POST ohne JavaScript fordert die Seite bei Fehlern die erneute Dateiauswahl an.

Löschkonflikte verwenden **Stand geprüft – erneut bestätigen** und öffnen danach erneut die konkrete Bestätigung. Auch eine bereits bestätigte Löschung darf einen benachbarten Entwurf nicht still verwerfen. Ist ein Eintrag inzwischen entfernt, bleibt die Seite erhalten, meldet dessen fehlende Verfügbarkeit und sperrt weitere Schreibversuche.

`role="status"` beziehungsweise `aria-live` für knappe Rückmeldungen, `role="alert"` für relevante Fehler verwenden. Meldungen nicht mehrfach durch einen Fragmentaustausch ankündigen. Ausreichender Kontrast und Text ergänzen die Farben.

## Vergleiche, Status und Datentreue

- Pro Vergleichsspalte Identität und Variante erkennbar halten. Ein horizontaler Wechsel darf Kriterienbezeichnungen nicht verschwinden lassen.
- Beim Bearbeiten der zweiten Spalte bleibt das richtige Fahrzeug ausdrücklich genannt; nach Speichern kehrt die Darstellung zu dieser Spalte zurück.
- Unbekannt, nicht erfüllt, erfüllt und persönlich sind verschiedene Zustände. Farbe allein reicht nicht zur Unterscheidung.
- Technische Angaben bleiben Fakten, subjektive Bewertungen bleiben subjektiv. Keine unbestellte Gesamtnote oder automatische Ausschlusslogik ergänzen.
- Original-/abgeleitete Angaben, Offertversion, Währung, Einheit und Zeitraum bleiben dort sichtbar, wo sie die Interpretation beeinflussen.
- Rechenszenarien binden sich an eine bestimmte Offertversion. Ein Wechsel des Quellenlinks oder Versionsfeldes ersetzt keine bestätigten Annahmen. Gemeinsame Kosten, Rendite und Zeitraum wirken weiterhin aktuell auf alle Rechnungen. Restwerte gelten nur am erfassten Stichtag, ohne Interpolation.
- Fehlende Informationen werden als offen gezeigt. Der Demo-Prototyp setzt weder echte Kosten voraus noch liefert er Standardwerte für Benutzer.

## HTMX und Sortierung

Die vorhandene Kombination aus vollständigen Razor-Seiten und Teilansichten bleibt maßgeblich. IDs, Anti-Forgery-Tokens, Versionsfelder und Rollenprüfung dürfen beim Extrahieren gemeinsamer UI-Bausteine nicht verloren gehen.

Erfolgreiche vollständige Formularaktionen dürfen per `HX-Redirect` navigieren. Führt die Antwort auf denselben Pfad mit derselben Query und nur einem anderen Sprungziel, muss der gespeicherte Stand trotzdem neu geladen werden. Stabile, explizite Fokusziele erhalten dabei den Bezug zum bearbeiteten Inhalt.

Nach einem Fragmentaustausch werden nur die notwendigen Interaktionen initialisiert; Handler nicht mehrfach registrieren. Fokus, Scrollposition, offene Editorzustände und ungespeicherte Felder gehören zum Zustand des Arbeitsbereichs. Sortieren ändert die Reihenfolge, nicht IDs oder Bewertungen. Tastatur, Touch, Abbruch und Bewegung zwischen Gruppen bleiben unterstützt.

## Abnahme eines gemeinsamen Musters

Eine einmalige Komponentendemonstration genügt nicht. Das Muster muss mindestens in Fahrzeugakte und Vergleich beziehungsweise bei zwei unterschiedlichen Anlage-/Bearbeitungsaufgaben funktionieren. Mit Playwright die tatsächliche Anwendung prüfen: Maus, Tastatur, Mobilgerät, leere und lange Inhalte, Abbruch, Speichern, Fehler, Konflikt, Rolle und Rückkehrfokus. Bestehende Berechtigungs- und Fachtests bleiben wirksam.

## Einstieg, Konto und Systemzustände

Anmeldung, Ersteinrichtung und Verifikation verwenden dieselben Felder, Fokusregeln und lokalen Fehlerhinweise. Fehler behalten E-Mail-Adresse, Namen und Code auf der aktuellen Seite. Ein laufender Request erhält eine sichtbare Statusmeldung und verhindert doppeltes Absenden. Native Formularantworten bleiben als Rückfall möglich.

Passkey-Anlage und Umbenennung öffnen einen lokalen Editor. Abbruch durch das Gerät und Netzfehler erhalten den Namen. Während der Gerätebestätigung lässt sich der Editor nicht schliessen. Nach Speichern wird die aktuelle Kontoseite geladen und der betroffene Eintrag fokussiert; nach Entfernen die Passkey-Überschrift. Die Löschbestätigung benennt den Passkey und beginnt auf Abbrechen. Die vorhandene 15-Minuten-Regel bleibt serverseitig; eine erneute Anmeldung führt zurück zum Konto. Rücksprungziele müssen lokal sein.

Einladungen zeigen ausstehend, angenommen und abgelaufen/entzogen ausdrücklich. Sie gewähren Zugang zur Installation; eine Evaluation braucht weiterhin eine eigene Freigabe. Nach Senden oder Entziehen führt der Fokus zur Liste zurück.

Vollständige Fehlerseiten unterscheiden fehlende Berechtigung, nicht verfügbare Seiten, zu viele Anfragen und allgemeine Fehler. HTTP-Statuscodes bleiben erhalten. Schreibantworten und HTMX-Fragmente behalten lokale Fehler; eine Loginseite ersetzt keinen offenen Entwurf. Offline-Hinweise lassen sich schliessen und verschwinden bei wiederhergestellter Verbindung. Datei- und Passkey-Gerätedialoge bleiben native Browser-/Betriebssystemoberflächen.
