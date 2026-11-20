# Konzept: Eintragsfenster, Presets und Ausführen

Stand: 20.11.2026 · Status: entschieden, nicht umgesetzt

Demo-Screens (klickbar, privat): https://claude.ai/artifact/LjWNobMaPJ6ZgKQTDSG4dF

## Warum

Das heutige Eintragsfenster ist unübersichtlich und verschachtelt:

- Fast alles Wichtige steht im Reiter „Profile“. Der Reiter „Allgemein“ enthält im Wesentlichen nur einen Hinweis, dorthin zu wechseln.
- Das Erben vom ★ Standard ist schwer zu durchschauen: Jedes Feld kann „eigen“ sein, und man sieht nicht, welcher Wert gilt.
- Lange Karten stehen untereinander, man muss viel scrollen.
- Eine URL, ein Snippet und ein PS1-Skript sehen fast gleich aus, obwohl sie ganz andere Einstellungen brauchen.

Beim Ausführen gibt es zu viele Wege: Klick, Aufklapp-Karte im Panel, `RunWindow` und das Menü am Ausführen-Knopf. In der Aufklapp-Karte ist die Parametereingabe eng, und nach dem Start gibt es kaum Rückmeldung.

## Eintragsfenster

- Die Navigation steht links, der Inhalt rechts. Es gibt keine Reiter mehr. Der Kopf zeigt Symbol, Name, Typ, Quelle und den Ausführen- bzw. Öffnen-Knopf.
- Die Abschnitte richten sich nach dem Typ:

| Typ | Abschnitte |
|---|---|
| Skript (PS1/CMD/BAT), Befehl | Allgemein, Parameter, Start, Presets, Verhalten |
| URL, Ordner, Datei | Allgemein, Öffnen mit, Variablen, (Presets) |
| Snippet | Allgemein, Text, Platzhalter, Einfügen, (Presets) |
| Workflow | Allgemein, Schritte, Variablen, Start, Verhalten, (Presets) |

  Klammern bedeuten, dass der Abschnitt erst nach „+ Preset“ erscheint.

- „Allgemein“ ist bei allen Typen gleich und enthält Name, Symbol, Tags, Beschreibung, Notiz und Quelle/Ziel mit „Im Editor öffnen“ und „Im Explorer zeigen“. Die Gruppe wird weiterhin im Panel per Ziehen geändert.
- **Parameter:** eine Tabelle mit Parameter, Wert und „Beim Start“ (Fest / Vorbelegt / Fragen), darunter eine Vorschau des Aufrufs.
- **Snippet:** Der Text steht im Mittelpunkt, die Platzhalter mit Quelle (Fragen / Fest / Automatisch) und eine Vorschau stehen daneben.
- **Workflow:** Die Schritte stehen als Kette, die Details zum gewählten Schritt rechts. Ein Schritt kann ein bestimmtes Preset eines anderen Eintrags nutzen. Workflow-Variablen werden einmal abgefragt und an die Schritte weitergegeben.

## Presets (bisher „Profile“)

- Der Begriff in der Oberfläche ist **Preset**. „Profil“ fällt weg, auch weil es mit PowerShell-Profilen (`-NoProfile`) verwechselbar ist.
- Der ★ Standard ist die Grundlage. Ein Preset speichert **nur seine Abweichungen**: Mit „+ Wert ändern“ kommt ein Feld dazu, mit × wird es entfernt. Alles andere kommt aus dem Standard, und spätere Änderungen am Standard wirken auf alle Presets.
- In der Liste steht pro Preset eine Zusammenfassung der Abweichungen (z. B. „Wo = SRV-T01 · Mode = B“). Beim Aufklappen lassen sich die Abweichungen bearbeiten.
- Presets gibt es für alle Typen. Bei einfachen Typen ändern sie vor allem Variablenwerte (z. B. `{umgebung}` = test statt prod).
- **Migration:** ★ Standard bleibt Standard, jedes bisherige Profil wird automatisch zu einem Preset mit genau seinen „eigenen Werten“. Es gibt keine Nachfrage.

### Code-Ersetzungen im Preset

- Der Einstieg ist im aufgeklappten Preset: „+ Code ersetzen …“ öffnet eine Codeansicht. Man markiert eine Stelle (oder wählt „Ganze Funktion“) und gibt den Ersatz ein.
- Die Codeansicht zeigt Ersetzungen wie einen Diff (− Original, + Ersatz). Umschalten zwischen „Nur Änderungen“ und „Ganzer Code“ ist möglich. Die Vorschau hebt die Stellen ebenfalls hervor.
- **Wiederfinden:** Gespeichert werden der Originaltext und etwas Umgebung, nicht die Zeilennummer. Verschobener Code wird so trotzdem gefunden. Fehlt die Stelle oder ist sie nicht mehr eindeutig, wird das Preset als Problem markiert und startet erst, wenn die Ersetzung neu zugeordnet oder entfernt wurde. Es startet nie stillschweigend mit falschem Code.
- **Ausführung:** Beim Start entsteht eine temporäre Kopie mit den Ersetzungen im Skriptordner, damit relative Pfade, `$PSScriptRoot` und Module weiter stimmen. Nach dem Lauf wird sie gelöscht. Die Signatur gilt dann nicht mehr, und das Startblatt weist darauf hin.
- Offen bei der Umsetzung: Was passiert, wenn der Skriptordner schreibgeschützt ist (Ausweichen auf den Temp-Ordner mit Hinweis)? Wie wird mit Remote-Läufen umgegangen, bei denen die Kopie mit „Skriptordner mitschicken“ übertragen wird?

## Ausführen

### Im Panel

- Ein **Klick** startet ★ Standard.
- **Rechtsklick** oder **Pfeil ▾** an der Kachel öffnet das Preset-Menü. Es enthält:
  - die Presets, jedes mit seinen Abweichungen als Chips
  - die Tasten 1–9 zum direkten Start eines Presets
  - „Mit Startblatt …“ (Umschalt+Klick)
  - „Zuletzt: …“, um mit denselben Werten zu wiederholen
  - „Bearbeiten …“ und „Als Favorit“
- Kacheln mit Presets zeigen „▾ n“.

### Startblatt

- Es klappt seitlich am Panel auf, wie die Vorschau, und das Panel bleibt sichtbar. Es **ersetzt `RunWindow` und die Aufklapp-Karte** unten im Panel.
- Inhalt: Preset-Wahl mit Chips samt Abweichungen, Parameter, „Ausführen als“, „Wo“ und eine Vorschau des Aufrufs. Enter startet, Esc schließt.
- Wann es erscheint, stellt man pro Eintrag im Abschnitt Verhalten ein (Immer / Nur wenn nötig / Nie). „Nur wenn nötig“ greift bei Abfragen, „Beim Start wählen“ oder einem anderen Benutzer. Mit Umschalt+Klick erscheint es immer.
- Nach dem Start zeigt es Status (läuft / fertig / Fehler), Laufzeit, Exit-Code und die letzten Ausgabezeilen. Es lässt sich jederzeit schließen, der Lauf geht dann in der Lauf-Leiste weiter.

### Rückmeldung

- **Lauf-Leiste im Panel:** unten im Panel, zeigt laufende und letzte Starts mit Status. Ein Klick öffnet das Startblatt bzw. das Ergebnis.
- **Ergebnisfenster:**
  - Kopf mit Zusammenfassung (z. B. „2 ok · 1 Fehler“)
  - eine Tabelle je Rechner mit Status, Dauer, Exit-Code und letzter Zeile
  - Suche in der Ausgabe, Kopieren, Export als CSV
  - die verwendeten Werte
- **Aktionen** im Startblatt und im Ergebnisfenster: „Erneut mit gleichen Werten“, „Nur Fehlgeschlagene“, „Werte ändern …“ und „Als Preset speichern …“.

## Umsetzung in Etappen

Jede Etappe ist ein eigener PR mit Versionssprung, Release Notes, Übersetzungen und Selbsttest-Screenshots.

1. **Datenmodell und Migration:** Profile werden zu Presets, die nur Abweichungen speichern, und bekommen ein Feld für Code-Ersetzungen. Dazu kommen die Migration alter Konfigurationen und Tests für Laden, Speichern, Erben und Migration.
2. **Eintragsfenster:** neuer Rahmen mit Navigation, Abschnitte je Typ, der Abschnitt Presets. Die Reiter Allgemein/Profile fallen weg. Dazu Selbsttest-Screenshots je Typ.
3. **Preset-Menü im Panel:** Rechtsklick und Pfeil, Chips mit Abweichungen, Tasten 1–9, „Zuletzt“ und die Anzeige „▾ n“.
4. **Startblatt und Läufe:** Das Startblatt ersetzt `RunWindow` und die Aufklapp-Karte. Dazu kommen ein Speicher für Läufe, die Lauf-Leiste, das überarbeitete Ergebnisfenster und die Wiederholen-Aktionen.
5. **Code-Ersetzung:** Codeansicht mit Markieren und Ersetzen, Wiederfinden am Text mit Problemstatus, die temporäre Kopie beim Start und der Signaturhinweis.

Die Etappen 1 und 2 hängen zusammen. Die Etappen 3 und 4 sind danach unabhängig, Etappe 5 baut auf 1 und 2 auf.

## Noch offen (nicht blockierend)

- Tastatursteuerung im Eintragsfenster (z. B. Strg+1…5 für die Abschnitte)
- Presets teilen oder exportieren, etwa über den Team-Katalog
- Optionen speziell für Ordner und Dateien unter „Öffnen mit“
- Weitergabe von Workflow-Variablen an Presets einzelner Schritte
