# Power-Exec Marktplatz

Dieser Ordner ist die mitgelieferte Quelle des Marktplatzes (Einstellungen › Marktplatz).
Eigene Quellen sind genauso aufgebaut: ein öffentliches GitHub- oder GitLab-Repository (oder ein Ordner darin).
In Power-Exec unter Marktplatz › Quellen › „Quelle hinzufügen …“ die URL eintragen, z. B.
`https://github.com/firma/px-katalog` oder `https://github.com/firma/px-katalog/tree/main/marktplatz`
(GitLab: `…/-/tree/main/ordner`).

## Aufbau

Power-Exec liest die Ordner direkt – ein Index ist nicht nötig. Oben stehen die **Kategorien** (je ein Ordner),
darunter die **Gruppen** (Unterordner) mit **einer Datei je Eintrag**. Beim Herunterladen schlägt Power-Exec den
Gruppenordner als Gruppe vor:

```
Einträge/Links/Google-Sofortsuche.pxentry.json
Einträge/Matrix42/Ticket-aufrufen.pxentry.json
Scripte/System/Get-SystemInfo.ps1
Designs/Ozean.design.json
```

Kategorie-Ordner: `Einträge` (oder `Entries`), `Scripte` (oder `Scripts`), `Designs`. Welche Art eine Datei ist,
bestimmt ihre Endung – die Ordner dienen der Ordnung und erscheinen in Power-Exec unter Marktplatz › Einträge,
Scripte und Designs.

| Datei | Inhalt | Landet in |
|---|---|---|
| `*.pxentry.json` | `{ "Entry": { … eigener Eintrag … }, "Variables": [ … ] }` | Eigene Einträge |
| `*.ps1` | PowerShell-Script (`.SYNOPSIS` = Beschreibung) | Dokumente\Power-Exec\Marktplatz + Eigene Einträge |
| `*.design.json` | Designvorlage (Name, Theme, PanelColor, Opacity, Frost, Backdrop, AccentColor …) | Designvorlagen |

- `Variables` nennt Variablen (`{{$Name}}`), die der Eintrag braucht – Power-Exec öffnet beim Herunterladen den
  Variablen-Dialog, falls es sie noch nicht gibt:
  `{ "Name": "Matrix42Url", "Kind": "Text", "Description": "…", "Example": "https://m42.firma.local" }`.
  `Kind`: `Text` (fester Text), `Secret` (geheim, verschlüsselt gespeichert, z. B. API-Schlüssel), `Environment`
  (Name einer Umgebungsvariable), `PowerShell` (Befehl, dessen Ausgabe der Wert ist) oder `Workspace` (je Arbeitsbereich).
  `Example` ist der vorgeschlagene Wert bzw. Befehl.
- Updates erkennt Power-Exec automatisch, sobald sich eine Datei im Repository ändert.
