# Übersetzungen / Translations

Power-Exec zeigt seine Oberfläche auf Deutsch oder Englisch (Einstellungen › Allgemein › Sprache; „Wie Windows“ = Deutsch bei deutschem Windows, sonst Englisch).

| Datei | Sprache |
|---|---|
| `de.json` | Deutsch |
| `en.json` | English |

**Aufbau:** Jede Datei ist ein JSON-Objekt `"Schlüssel": "Text"`. Der Schlüssel ist der deutsche Originaltext aus dem Quelltext
(`L.T("…")`, `L.F("…")` in C#, `{l:T '…'}` in XAML). Platzhalter wie `{0}`, `{1:HH:mm}` und `{{$Name}}` müssen in der Übersetzung erhalten bleiben.

- Text ändern: nur den Wert rechts anpassen, der Schlüssel bleibt.
- Fehlt ein Schlüssel in einer Datei, zeigt Power-Exec den Schlüssel (deutschen Originaltext).
- Der Test `LocalizationTests` prüft, dass jeder Text in beiden Dateien steht und die Platzhalter passen.

Die Dateien werden beim Bauen in das Programm eingebettet – Änderungen wirken mit dem nächsten Release.
