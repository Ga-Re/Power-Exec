# Translations / Übersetzungen

Power-Exec is written in **English** – the English text in the source code is both the default UI text and the key
for every translation. Each other language is one file in this folder, named after its two-letter code:

| File | Language |
|---|---|
| `de.json` | Deutsch |

**Format:** a JSON object `"English text": "translation"`. Placeholders such as `{0}`, `{1:HH:mm}` and `{{$Name}}`
must stay in the translation.

- **New language:** copy `de.json` to e.g. `fr.json` and translate the values. Power-Exec picks it up automatically
  (Settings › General › Language; “Like Windows” uses it when Windows runs in that language).
- **Missing entries** fall back to English.
- The test `LocalizationTests` checks that every text used in code (`L.T("…")`, `L.F("…")`) and XAML (`{l:T '…'}`)
  is in `de.json`, that placeholders match and that no unused entries remain.

The files are embedded when building – changes take effect with the next release.

---

Power-Exec ist auf **Englisch** geschrieben; der englische Text im Quelltext ist zugleich Schlüssel. Jede weitere
Sprache ist eine Datei hier (`de.json` = Deutsch). Neue Sprache: `de.json` kopieren (z. B. `fr.json`) und die Werte
übersetzen – fehlende Einträge erscheinen auf Englisch.
