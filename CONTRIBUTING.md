# Beiträge zu Steervo

Danke für das Interesse. Steervo ist in einer frühen Phase — bevor du Arbeit investierst, lies bitte die folgenden Punkte.

## Contributor License Agreement (CLA)

Steervo wird doppelt lizenziert: öffentlich unter der PolyForm Noncommercial License 1.0.0 und zusätzlich unter einer kommerziellen Lizenz. Damit das zweite möglich ist, braucht das Projekt von dir das Recht, deinen Beitrag unter beiden Lizenzen zu verwenden.

**Für jeden Pull Request ist deshalb die Zustimmung zum [CLA](CLA.md) erforderlich.** Das Eigentum an deinem Code bleibt bei dir — du überträgst keine Rechte, sondern erteilst eine Lizenz.

So läuft es ab:

1. Du eröffnest einen Pull Request.
2. Ein Bot kommentiert und verweist auf das CLA, falls du es noch nicht unterzeichnet hast.
3. Du liest [CLA.md](CLA.md) und schreibst als Kommentar genau diesen Satz:

   ```
   I have read the CLA Document and I hereby sign the CLA
   ```

4. Die Statusprüfung wird grün. Deine Unterschrift wird in `signatures/version1/cla.json` festgehalten und gilt auch für alle künftigen Beiträge — du machst das nur einmal.

Wenn du dem CLA nicht zustimmen willst, ist das in Ordnung. Issues, Fehlerberichte und Diskussionsbeiträge sind auch ohne CLA willkommen.

## Bevor du etwas baust

Eröffne zuerst ein Issue und beschreibe, was du vorhast. Das Projekt hat eine klare Richtung, und ein fertiger Pull Request, der nicht hineinpasst, ist für beide Seiten verlorene Zeit.

Besonders willkommen sind:

- Fehlerberichte mit nachvollziehbarer Beschreibung
- Verbesserungen an der Rechenlogik der Gesamtkosten, idealerweise mit Tests
- Korrekturen an Dokumentation und Übersetzungen

## Was nicht in dieses Repository gehört

- Erweiterungspunkte, Plugin-Schnittstellen oder Konfigurationsschalter, die für die gehostete Version vorgesehen sind. Der Kern bleibt in sich geschlossen.
- Anbindungen an Bank- oder Kreditkartenkonten sowie Rechnungsempfang per E-Mail. Das ist eine bewusste Produktentscheidung, nicht eine offene Lücke.
- Eine öffentliche, dokumentierte Programmierschnittstelle. Für den Datenaustausch gibt es Import und Export.

## Tests

Für Änderungen an Berechnungen gilt: ohne Tests kein Merge. Das betrifft ausdrücklich auch negative Fälle — was passiert bei fehlenden Annahmen, unmöglichen Werten oder widersprüchlichen Eingaben.

## Stil

Richte dich am umgebenden Code aus. Kein eigener Formatierungsstil, keine grossflächigen Umformatierungen in einem inhaltlichen Pull Request.
