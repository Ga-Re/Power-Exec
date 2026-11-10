# Power-Exec Marktplatz

Dieser Ordner ist die mitgelieferte Quelle des Marktplatzes (Einstellungen › Inhalte › Marktplatz).
Eigene Quellen sind genauso aufgebaut: ein öffentliches GitHub- oder GitLab-Repository (oder ein Ordner darin).
In Power-Exec unter Marktplatz › Quellen › „Quelle hinzufügen …“ die URL eintragen, z. B.
`https://github.com/firma/px-katalog` oder `https://github.com/firma/px-katalog/tree/main/marktplatz`
(GitLab: `…/-/tree/main/ordner`).

## Aufbau

Power-Exec liest die Ordner direkt – ein Index ist nicht nötig. **Eine Datei je Eintrag**, Ordner gruppieren
(beim Herunterladen wird der Ordnername als Gruppe vorgeschlagen):

```
Links/Google-Sofortsuche.pxentry.json
Matrix42/Ticket-aufrufen.pxentry.json
Scripte/System/Get-SystemInfo.ps1
Designs/Ozean.design.json
```

| Datei | Inhalt | Landet in |
|---|---|---|
| `*.pxentry.json` | `{ "Entry": { … eigener Eintrag … }, "Variables": [ … ] }` | Eigene Einträge |
| `*.ps1` | PowerShell-Script (`.SYNOPSIS` = Beschreibung) | Dokumente\Power-Exec\Marktplatz + Eigene Einträge |
| `*.design.json` | Designvorlage (Name, Theme, PanelColor, Opacity, Frost, Backdrop, AccentColor …) | Designvorlagen |

- `Variables` nennt Variablen (`{{$Name}}`), die der Eintrag braucht – Power-Exec fragt sie beim Herunterladen ab,
  falls es sie noch nicht gibt: `{ "Name": "Matrix42Url", "Description": "…", "Example": "https://m42.firma.local" }`.
- Updates erkennt Power-Exec automatisch, sobald sich eine Datei im Repository ändert.
