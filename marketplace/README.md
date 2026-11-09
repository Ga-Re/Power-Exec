# Power-Exec Marktplatz

Dieser Ordner ist die Standardquelle des Marktplatzes in Power-Exec (Einstellungen › Inhalte › Marktplatz).
Eigene Marktplätze sind genauso aufgebaut: ein Ordner (oder Repository) mit einer `index.json`. In Power-Exec unter
Marktplatz › Quellen › „Quelle hinzufügen …“ die Repository-URL eintragen, z. B. `https://github.com/firma/px-katalog`
oder einen Unterordner `https://github.com/firma/px-katalog/tree/main/marktplatz` (GitLab: `…/-/tree/main/ordner`).
Das Repository muss öffentlich lesbar sein. Vor der ersten Installation aus einer neuen Quelle fragt Power-Exec einmal,
ob du ihr vertraust.

## index.json

```json
{
  "Name": "Mein Marktplatz",
  "Items": [
    { "Id": "ticket", "Type": "Package", "Name": "Ticket-Paket", "Description": "…", "Category": "Pakete",
      "Version": "1", "File": "packages/ticket.json",
      "Variables": [ { "Name": "TicketUrl", "Description": "Adresse des Ticketsystems", "Example": "https://tickets.firma.local" } ] }
  ]
}
```

| Type | Datei |
|---|---|
| `Entry`, `Package` | Export eigener Einträge (Rechtsklick › Exportieren, `*.pxentries.json`) |
| `Design` | Designvorlage als JSON (Name, Theme, PanelColor, Opacity, Frost, Backdrop, AccentColor …) |
| `Script` | PowerShell-Script (`.ps1`) |

- `File` ist relativ zur `index.json` (auch `../` ist erlaubt).
- `Version` bei jeder Änderung erhöhen – installierte Elemente zeigen dann „Update verfügbar“.
- `Variables`: Variablen, die beim Installieren abgefragt werden, falls es sie noch nicht gibt (`{{$Name}}` in den Einträgen).
