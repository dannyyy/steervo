# Code-Stil und Lesbarkeit

Code in diesem Repository wird für Menschen geschrieben. Wer eine Datei zum ersten Mal öffnet, soll den Ablauf von oben nach unten lesen können, ohne Zeilen entwirren zu müssen. Kompaktheit ist kein Ziel.

## Formatierung ist automatisch

| Dateien | Werkzeug | Konfiguration |
|---|---|---|
| C# (`*.cs`) und Projektdateien | [CSharpier](https://csharpier.com) | `.csharpierrc.yaml`, `.csharpierignore` |
| JavaScript (`*.js`, `*.mjs`), CSS, HTML, JSON, YAML | [Prettier](https://prettier.io) | `.prettierrc.json`, `.prettierignore` |
| Razor (`*.cshtml`), Shell | kein Formatierer; es gelten die Regeln unten | `.editorconfig` |

```sh
npm ci --ignore-scripts       # einmalig, installiert Prettier
./scripts/format.sh           # formatiert alles
./scripts/format.sh --check   # prüft nur, Exit-Code 1 bei Abweichungen
```

Beide Werkzeuge sind exakt gepinnt (`.config/dotnet-tools.json`, `package.json`) und verwenden ihre Standardregeln; nur die Zeilenbreite ist für beide auf 100 Zeichen gesetzt. Vor jedem Commit `./scripts/format.sh` ausführen.

Ausgenommen sind generierte und fremde Dateien: EF-Core-Migrationen, Lockdateien, die eingebundene HTMX-Bibliothek und die Helm-Vorlagen.

## Lesbarkeit — in jeder Sprache

1. **Eine Anweisung pro Zeile.** Keine mit `;` verketteten Anweisungen, keine Mehrfachdeklarationen (`var a = 1, b = 2`), kein `if (x) y();` und kein `foreach (...) if (...) ...` auf einer Zeile.
2. **Immer geschweifte Klammern** bei `if`, `else`, `for`, `foreach`, `while` und `using`-Blöcken, auch bei einer einzigen Anweisung.
3. **Leerzeilen gliedern.** Zwischen Membern steht eine Leerzeile. In einer Methode trennen Leerzeilen die gedanklichen Schritte: prüfen, laden, ändern, speichern, antworten.
4. **Namen sagen, was etwas ist.** Ein-Buchstaben-Namen nur in kurzen, offensichtlichen Lambdas (`offers.Where(o => o.IsActive)`) und Zählschleifen. `el`, `t`, `e`, `x`, `tmp` sind keine Namen.
5. **Bedingungen benennen.** Ein Ausdruck mit mehr als zwei verknüpften Teilbedingungen erhält eine sprechende Variable oder eine kleine Methode.
6. **Keine verschachtelten ternären Ausdrücke.** Ein einzelner `?:` ist in Ordnung; ab zwei Stufen `if`/`else`, `switch` oder eine benannte Methode.
7. **Eine Methode, eine Aufgabe.** Tut eine Methode mehrere Dinge nacheinander oder passt sie nicht mehr auf einen Bildschirm, wird sie in benannte Schritte zerlegt. Die aufrufende Methode liest sich danach wie eine Inhaltsangabe.
8. **Früh zurückkehren** statt tief verschachteln.
9. **Kommentare erklären das Warum**: Fachregel, Grund für eine Reihenfolge, bewusste Ausnahme. Code und Kommentare sind englisch. Kein auskommentierter Code.
10. **Keine magischen Werte.** Zahlen und wiederholte Zeichenketten mit Bedeutung erhalten eine benannte Konstante.

## C#

- Ein öffentlicher Typ pro Datei; kleine zusammengehörige Records und Enums dürfen in einer Modelldatei stehen.
- Reihenfolge in einer Klasse: Konstanten, Felder, Konstruktor, Properties, öffentliche Methoden, private Hilfsmethoden. Eine Hilfsmethode steht nach ihrer ersten Verwendung.
- Ausdruckskörper (`=>`) nur, wenn der Ausdruck auf eine Zeile passt; sonst ein Block mit `return`.
- Objektinitialisierer: eine Property pro Zeile, sobald es mehr als zwei sind.
- LINQ-Ketten: ein Aufruf pro Zeile ab drei Aufrufen; lange Ketten in benannte Zwischenergebnisse teilen.
- Tests zeigen Vorbereitung, Ausführung und Prüfung als durch Leerzeilen getrennte Blöcke. Der Testname beschreibt das erwartete Verhalten.

## JavaScript

- `const` vor `let`, nie `var`; eine Deklaration pro Anweisung.
- Benannte `function`-Deklarationen für alles mit eigenem Namen, Pfeilfunktionen für Callbacks.
- DOM-Aufbau in kleinen Funktionen, die nach dem benannt sind, was sie darstellen.
- Der Zustand eines Skripts steht gesammelt und kommentiert am Anfang.
- Längere Dateien erhalten Abschnittskommentare.

## CSS

- Eine Deklaration pro Zeile, eine Leerzeile zwischen Regeln (übernimmt Prettier).
- Zusammengehörige Regeln stehen unter einem Abschnittskommentar. Die Reihenfolge der Regeln bestimmt die Kaskade und wird beim Aufräumen nicht verändert.

## Razor

- Ein Element pro Zeile, Kindelemente vier Leerzeichen eingerückt. Bei mehr als drei Attributen oder über 120 Zeichen steht ein Attribut pro Zeile.
- `@{ ... }`, `@if` und `@foreach` mehrzeilig mit eigenen Klammerzeilen.
- Logik gehört ins Seitenmodell. In der Ansicht stehen höchstens einfache Bedingungen; alles andere wird eine Property oder Methode des Modells.
- Leerraum zwischen Inline-Elementen ist im Browser sichtbar. Beim Umbrechen darf kein Leerzeichen entstehen oder verschwinden, wo Text und Inline-Elemente aneinanderstossen.

## Shell

- `set -euo pipefail` am Anfang, eine Anweisung pro Zeile, `if`/`then`/`fi` mehrzeilig, zwei Leerzeichen Einzug.
- Lange Befehle mit `\` umbrechen; wiederholte Befehlsteile in eine Funktion oder Variable.

## Aufräumen ändert kein Verhalten

Unverändert bleiben beim reinen Aufräumen: Namen, die serialisiert, gespeichert oder per Routing aufgelöst werden (Export- und JSON-Modelle, Entitäten, Razor-Handler, Ressourcenschlüssel, CSS-Klassen, `id`-, `data-` und `hx-`Attribute), die Reihenfolge von Seiteneffekten und die Reihenfolge von CSS-Regeln. Aufräumen und inhaltliche Änderung gehören in getrennte Commits.
