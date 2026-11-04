# Leistung

Ziel für 0.17: **ein Tastendruck im Suchfeld < 50 ms** bis der UI-Thread wieder frei ist,
mit 500 Einträgen in rund 20 Gruppen.

## Messen

| Was | Wo | Aufruf |
|---|---|---|
| Echte Oberfläche (WPF) | Selbsttest, letzter Schritt | läuft in jeder CI; Ergebnis in der Zusammenfassung des Laufs und im Artefakt `selftest-perf` |
| Datenteil des Rebuilds (ohne WPF) | `tests/PowerExec.Core.Benchmarks` | `dotnet run -c Release --project tests/PowerExec.Core.Benchmarks -- --filter *` oder Workflow „Benchmarks“ |
| Testbestand prüfen | Benchmarks | `… -- --check` |

Beide nutzen denselben Testbestand `PerfDataset` (`src/PowerExec.Core/Diagnostics`): 440 Dateien in
12 Unterordnern einer smarten Gruppe, 60 eigene Einträge, 8 eigene Gruppen (2 Untergruppen),
Schlagworte und Notizen an ~40 % der Dateien, 25 Favoriten, 120 Startstatistiken.

Szenario „Tippen“: `d`, `di`, `dis`, `disk`, `dis`, `di`, `d`, leer – eine Aufwärmrunde, dann 5 Runden.
„bis frei“ = bis der Dispatcher `ApplicationIdle` erreicht (Rebuild, Container, Layout).

## Ausgangswerte 0.16.2

### Selbsttest, Windows (GitHub-Runner `windows-latest`, CI-Lauf 84)

| Szenario | Eingabe | Rebuild (Median) | bis frei (Median) | bis frei (Max) |
|---|---|---:|---:|---:|
| Panel öffnen | – | 9.1 ms | 34.6 ms | 63.6 ms |
| Tippen | `d` (319 Treffer) | 68.2 ms | 1083.2 ms | 1227.4 ms |
| Tippen | `di` | 50.0 ms | 220.3 ms | 324.5 ms |
| Tippen | `dis` | 23.3 ms | 152.5 ms | 179.0 ms |
| Tippen | `disk` (14 Treffer) | 24.4 ms | 159.6 ms | 171.8 ms |
| Tippen | leer (500) | 51.1 ms | 1594.8 ms | 1985.4 ms |
| Filter | Scripte | 123.2 ms | 1574.5 ms | 1585.7 ms |
| Filter | Alle | 125.0 ms | 1908.3 ms | 1990.2 ms |

**Tippen gesamt:** Mittel 599 ms · p95 1595 ms · Max 1985 ms. Ziel: < 50 ms.

Der synchrone Rebuild macht nur 5–15 % aus. Der Rest entsteht in WPF beim Erzeugen der Container.
Hauptverdacht: `ItemMenu` (`x:Shared="False"`, 43 MenuItems) wird über `StaticResource` an jeder Zeile
**beim Erzeugen** instanziiert, also ~22.500 MenuItems bei einem vollen Rebuild; dazu `GroupMenu`
(64 MenuItems) zweimal je Gruppe.

### Benchmarks, Datenteil (Linux-Container, Xeon 2,1 GHz, 4 Kerne, .NET 8.0.33)

| Benchmark | Mittel | Allokiert |
|---|---:|---:|
| Rebuild „“ (500 Einträge) | 8.2 ms | 1.93 MB |
| Rebuild „d“ | 10.0 ms | 2.14 MB |
| Rebuild „disk“ | 11.5 ms | 4.24 MB |
| Rebuild „#ad“ | 4.6 ms | 1.22 MB |
| Rebuild „typ:ps1 user“ | 9.9 ms | 3.23 MB |
| Tippen d→disk→leer (8 Rebuilds) | 76.2 ms | 16.77 MB |

Stufen für „disk“:

| Stufe | Mittel | Allokiert |
|---|---:|---:|
| 1 CollectVisible | 1.48 ms | 618 KB |
| 2 Filtern + Bewerten | 9.42 ms | 3602 KB |
| 3 Gruppieren + Sortieren | 0.34 ms | 101 KB |
| 4 Daten je ItemViewModel (19 VMs) | 0.09 ms | 18 KB |

Nachschlag-Funktionen, je 500 Aufrufe:

| Funktion | Mittel |
|---|---:|
| `Settings.FindEntry` | 0.88 ms |
| `SmartGroups.EffectiveGroup` | 0.99 ms |
| `RunStats.Frecency` | 0.51 ms |
| `IsFavorite` | 0.14 ms |
| `WorkspaceManager.ShowsGroup` | 0.06 ms |

`RunStats` wird ohne `StringComparer` geladen: Für jeden nie gestarteten Eintrag läuft `Frecency`
in die lineare Suche über alle Schlüssel.

In Stufe 2 entfallen ~3,4 ms auf Nachschlagen (LookupSnapshot) und ~6 ms auf die Bewertung
(`FuzzyMatcher`, kulturabhängiger Vergleich). Gemessen an 160–1600 ms in der Oberfläche fällt beides kaum ins Gewicht.

## Verlauf (Selbsttest, Windows-CI)

| Stand | CI-Lauf | Tippen Mittel | Tippen p95 | `d` | `disk` | leer | Filter Alle |
|---|---:|---:|---:|---:|---:|---:|---:|
| 0.16.2 | 84 | 599 ms | 1595 ms | 1083 ms | 160 ms | 1595 ms | 1908 ms |
| 0.16.2 (Wiederholung) | 85 | 937 ms | 2410 ms | 1731 ms | 273 ms | 2410 ms | 2867 ms |
| Schritt 2: Kontextmenüs einmal je Fenster | 86 | 226 ms | 630 ms | 381 ms | 69 ms | 630 ms | 652 ms |

| Schritt 3: ViewModels wiederverwenden, Listen abgleichen | 88 | 277 ms | 816 ms | 488 ms | 59 ms | 816 ms | 825 ms |
| … Gruppen auch bei Suche an/aus wiederverwenden | 89 | 372 ms | 1033 ms | 646 ms | 65 ms | 1033 ms | 1079 ms |

### A/B im selben Lauf (CI-Lauf 90)

| Tastendruck | ohne Wiederverwendung | mit | Listenänderungen (mit) |
|---|---:|---:|---|
| `d` (von leer) | 562 ms | 618 ms | +6 ↕278 −188 |
| `di` | 184 ms | 122 ms | +4 ↕8 −289 |
| `dis` | 109 ms | 54 ms | +1 −2 |
| `disk` | 74 ms | 66 ms | −2 |
| `d` (zurück von `di`) | 445 ms | 405 ms | +289 ↕8 −4 |
| leer (von `d`) | 782 ms | 773 ms | +188 ↕276 −1 |
| Filter Scripte | – | 595 ms | −96 |

Mittel 290 ms ohne, 273 ms mit Wiederverwendung (Faktor 1,06). Kleine Schritte werden bis zu 50 % schneller.
Beim Ein- und Ausschalten der Suche ändert sich die Sortierung in fast jeder Gruppe (Bewertung statt alphabetisch);
ein `Move` kostet so viel wie ein neuer Container. „Filter Scripte“ entfernt nur Einträge und braucht trotzdem
fast 600 ms – die Zeit steckt also nicht (nur) im Erzeugen der Container.

Läufe 84 und 85 haben denselben Code: Die Runner schwanken um etwa den Faktor 1,5.
Einzelne Läufe deshalb nur grob vergleichen; ein Grenzwert in der CI braucht Abstand oder mehrere Läufe.

## Grenzen der Messung

- CI-Runner sind virtuelle Maschinen und schwanken; ein Arbeitsplatz-PC ist meist deutlich schneller.
  Verglichen wird immer Lauf gegen Lauf auf derselben Art Runner.
- Der Selbsttest schaltet Animationen und Erreichbarkeitsprüfungen eigener Einträge während der Messung ab.
- Die Benchmarks bilden den Datenteil von `PanelViewModel.Rebuild()` nach (`RebuildMirror`). Commands,
  Symbole, Container und Layout misst nur der Selbsttest.
