# Murmel

Lokale Speech-to-Text Desktop-App für Windows, 100% offline. C# / .NET 8 / Avalonia UI,
NVIDIA Parakeet v3 (TDT 0.6B, int8) über sherpa-onnx für die Transkription. Push-to-talk
per globalem Hotkey, automatisches Einfügen (Clipboard + simuliertes Strg+V) in das gerade
aktive Fenster, lokaler Verlauf, Wortzähler, Tray-Icon mit Hintergrund-Widget.

## Über dieses Projekt / Zusammenarbeit

Kati (Projektinhaberin) möchte nicht selbst programmieren - Claude schreibt den gesamten
Code. Ihre Rolle: Anforderungen, Feedback, Testen auf echter Hardware, UX-Entscheidungen.
Kommunikation auf Deutsch. Sie ist nicht technisch versiert (z.B. wurde ein Download einer
.bat-Datei vom Browser blockiert, GitHub-Konto musste mit Anleitung erstellt werden) -
Erklärungen entsprechend einfach halten, Schritt für Schritt, ohne Fachjargon vorauszusetzen.

Dieses Projekt liegt hier als lokales Git-Repo (`murmel-repo`), separat vom Ordner
`Murmel` auf ihrem Desktop, in dem die LAUFFÄHIGE gebaute App liegt (Murmel.exe etc.).
Sie benutzt AUSSCHLIESSLICH `Desktop\Murmel\Murmel.exe` zum Testen, nie etwas aus dem
Repo-Ordner direkt.

**Publish-Rhythmus (zwei getrennte Dinge!):**
- Nach `..\Murmel\` kopieren (damit sie testen kann): jederzeit, bei jeder Änderung die
  getestet werden soll - dafür braucht's kein Nachfragen, einfach machen (Murmel muss
  dafür kurz geschlossen sein, siehe unten).
- Zu GitHub pushen (`git push`): NICHT nach jeder Änderung. Lokal committen wie gewohnt,
  aber den Push nach GitHub nur gebündelt, etwa einmal pro Woche oder wenn eine größere
  Änderung ansteht.

**Wichtige Einschränkung:** Murmel.exe/.dll sind gesperrt, solange die App läuft - vor
jedem Überschreiben muss sie geschlossen sein (Tray-Icon, oder `exitmurmel.bat` auf ihrem
Desktop, die sie sich dafür angelegt hat).

## Build & Publish

```bash
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
```

Framework-dependent ist der Standard (klein, ~24MB an geänderten Dateien; braucht .NET 8
Desktop Runtime, die auf ihrem PC schon installiert ist). Self-contained (~119MB, keine
Abhängigkeiten) nur auf expliziten Wunsch, z.B. für Weitergabe an Dritte ohne .NET.

Nach dem Publish liegen die Dateien in `bin/Release/net8.0/win-x64/publish/` - von dort
(sofern Murmel geschlossen ist) nach `C:\Users\dubbi\Desktop\Murmel\` kopieren.

## GitHub

Repo: github.com/Dubbicode/murmel (öffentlich). Direktes Pushen von hier aus sollte jetzt
funktionieren (lokales Git auf ihrem PC, keine Sandbox-Restriktion mehr) - vorher lief die
Entwicklung in einer Cloud-Sandbox, die das blockiert hat; das ist mit dem Umstieg auf
Claude Code hinfällig. Sie hat außerdem GitHub Desktop installiert und kann darüber genauso
committen/pushen, falls das lieber ist.

## Aktueller Stand (Stand: v0.8.1, siehe git log für Details)

- Kernfunktionen (Aufnahme, Transkription, Verlauf, Stats, Hintergrund-Widget, Hotkey,
  Autostart) sind fertig und laufen.
- **Sprachkorrektur-Befehl** (`Services/CorrectionCommand.cs`) gerade neu gebaut und in
  Testphase: erkennt sowohl eine Selbstkorrektur INNERHALB eines Diktats ("Wir treffen uns
  am Montag. Nein, am Dienstag." - alles in einer Aufnahme) als auch einen separaten
  Korrektur-Befehl NACH einem Diktat ("korrigiere Montag zu Dienstag"). Rein lokales
  Regex-Pattern-Matching, kein LLM (bewusste Design-Entscheidung, damit alles offline
  bleibt). Noch nicht von Kati auf echter Hardware bestätigt - das ist der nächste
  Testschritt.
- Roadmap danach (Stand 2026-08-29, ihre Priorität in dieser Reihenfolge):
  1. **Umschalt-Modus fürs Aufnehmen** - ✅ fertig gebaut (`RecordingMode` in
     AppSettings.cs, Schalter "Umschalt-Modus statt Halten" in den Einstellungen).
     Push-to-Talk bleibt als Standard/Alternative erhalten. Noch nicht von Kati
     bestätigt getestet.
     Eine Doppel-Tipp-Variante (2x kurz drücken = Haltemodus einrasten, statt über den
     Schalter) wurde bewusst NICHT gebaut - hätte geraten müssen, ob ein kurzes Drücken
     eine Geste oder eine echte kurze Diktion ist, mit Risiko dass Audio verworfen wird.
     Kati hat sich für den bestehenden, eindeutigen Schalter entschieden - nicht von
     selbst nochmal vorschlagen.
  2. **Wörterbuch** - ✅ fertig gebaut UND von Kati bestätigt getestet (2026-08-30).
     Design an Wispr Flow/Superwhisper angelehnt. Zwei Teile, neuer Tab "Wörterbuch"
     (`Services/DictionaryStore.cs` persistiert nach `%AppData%\Typr\dictionary.json`),
     Einträge direkt in der Liste bearbeitbar (anklicken, tippen, Fokus verlassen
     speichert):
     a) **Korrekturen** (`DictionaryProcessor.ApplyCorrections`): falsch erkannte
        Begriffe → richtige Schreibweise, wortweise Ersetzung auf jedem Diktat.
     b) **Sprach-Snippets** (`DictionaryProcessor.ApplySnippets`): Auslöser-Phrase +
        Verb ("einfügen"/"hinzufügen"/"einsetzen", auch "füge ... ein/hinzu") wird
        ÜBERALL im Satz gefunden (nicht nur wenn es die ganze Aufnahme ist) und nur die
        betroffene Stelle ersetzt - "Du kannst mich auch unter meiner Handynummer
        hinzufügen erreichen" → "...unter 0123456 erreichen". Vergleich läuft komplett
        buchstabenbasiert (Leerzeichen/Bindestriche/Satzzeichen bei Auslöser UND
        Aufnahme entfernt, `BuildLetterMap`/`ReplaceSpan`), deckt daher Schreibweisen-
        Varianten wie "Handynummer"/"Handy Nummer" automatisch ab. Pro Eintrag mehrere
        kommagetrennte Auslöser-Synonyme möglich ("meine Handynummer, meine
        Telefonnummer"). Fuzzy-/Freitext-Matching bewusst nicht gebaut (würde Richtung
        LLM gehen, siehe Design-Entscheidungen).
     Nebenbei behoben: ein Avalonia-Absturz beim Listen-Refresh
     (`ItemsSource = null` gefolgt von neuer Liste crasht in dieser Version -
     `RefreshDictionaryLists`/`RefreshHistoryList` weisen jetzt immer eine neue
     Listen-Instanz zu; diese Falle bei künftigen ItemsControl-Refreshes vermeiden).
  3. **Statistik-Graph** ("Graphenverlauf") - ✅ fertig gebaut (2026-08-30): kleines
     7-Tage-Balkendiagramm ("LETZTE 7 TAGE") unten in der Stat-Karte auf der
     Aufnahme-Seite, Wörter pro Tag. Neuer `DailyWordCounts`-Dictionary in
     `Services/StatsStore.cs` (unabhängig vom 200er-Cap/"Verlauf leeren" des
     Verlaufs, wie schon `TotalWordsSpoken`), mit einmaligem Backfill aus der
     bestehenden Historie (`SeedDailyIfEmpty`) für Bestandsnutzer. Nebenbei einen
     latenten Bug behoben: "Heute diktiert" wurde bisher live aus den Verlauf-Einträgen
     aufsummiert statt aus den persistenten Stats - nach "Verlauf leeren" wäre die
     Heute-Zahl fälschlich auf 0 gesprungen.
     Von Kati bestätigt getestet UND verfeinert (2026-08-30): erst Balken mit Zahl
     darüber, dann Zahl in den Balken, am Ende auf ihren Wunsch zu einem echten
     Liniendiagramm umgebaut (7 Punkte, verbunden, Wert über jedem Punkt, heutiger Tag
     fett) - das war die von ihr bevorzugte Darstellung. Finaler Stand bestätigt gut.
  4. **Durchsuchbarer Verlauf** - ✅ fertig gebaut (2026-08-30): Suchfeld oben auf der
     Verlauf-Seite (`HistorySearchBox`), filtert live (Substring, Groß-/Kleinschreibung
     egal) über `RefreshHistoryList`; "Keine Treffer"-Hinweis wenn die Suche nichts
     findet. Auf ihren Wunsch direkt noch Tagestrennungen ergänzt: neues Model
     `HistoryDateHeader`, Liste mischt jetzt Header + Einträge, zwei DataTemplates
     (per `DataType`) im `HistoryList`-ItemsControl. Labels "Heute"/"Gestern"/Datum.
     Von Kati bestätigt getestet und gut befunden (2026-08-30).
  5. **GPU-Beschleunigung** - ❌ bewusst nicht umgesetzt (2026-08-30, nach kurzer
     Recherche): kein fertiges CUDA/GPU-NuGet-Paket für die C#-Anbindung von
     sherpa-onnx vorhanden. Würde Selbst-Kompilieren mit CUDA Toolkit + cuDNN
     erfordern, und laut mehreren offenen GitHub-Issues im sherpa-onnx-Projekt
     funktioniert die GPU-Nutzung danach oft trotzdem nicht (stillschweigend, ohne
     Fehlermeldung). Kati hat sich nach dieser Einschätzung bewusst dagegen
     entschieden - nicht von selbst wieder aufgreifen, außer sie fragt danach oder es
     gibt neue Infos (z.B. ein offizielles GPU-NuGet-Paket).
  6. **Auto-Update** - ausdrücklich ganz hinten - "keine Notwendigkeit momentan", nicht
     von selbst vorschlagen.

  Damit ist die am 2026-08-29 aufgestellte Roadmap komplett abgearbeitet (Stand
  2026-08-30). Nächste Prioritäten mit Kati klären, wenn neue Wünsche aufkommen.
  Ein LLM-basierter Korrektur-Modus (näher an WisprFlows Command Mode) wurde als
  Nice-to-have eingestuft und bewusst zurückgestellt, siehe Design-Entscheidungen unten.

## UI-Redesign (2026-08-30) - "Richtung H"

Auf Katis Wunsch (inspiriert von drei mitgeschickten Dashboard-Referenzbildern) komplett
neu umgebaut, ausgehandelt über drei Iterationsrunden als Artifact-Mockups (Link nicht
mehr aktuell/relevant - Endergebnis ist der jetzige Live-Code):

- **Struktur**: obere Tab-Leiste durch eine schmale Icon-Seitenleiste links ersetzt
  (Aufnahme/Verlauf/Wörterbuch oben, Einstellungen + Theme-Toggle unten). Der große
  zentrale Mikrofon-Knopf ist einer kleinen Status-Leiste gewichen (30px-Button + Status-
  text + Hotkey-Badge) - Kati nutzt zum Aufnehmen ohnehin den Hotkey, nicht den Klick.
  Die alte, immer laufende Ping-Ring-Animation um den Mic-Button wurde dafür komplett
  entfernt (`_micAnimTimer`/`AnimateMic`/`AnimateRing` - ersatzlos gestrichen, nicht nur
  verkleinert), da sie für einen so kleinen, bewusst zurückgenommenen Button nicht mehr
  passte.
- **Aufnahme-Seite** ist jetzt ein Kachel-Dashboard statt reinem Diktier-Bildschirm: links
  zwei große Kacheln (Verlauf-Vorschau der letzten 4 Einträge, Transkript-Textfeld mit
  Kopieren/Löschen), rechts drei kleinere (Statistik-Zahlen, 7-Tage-Liniendiagramm,
  Wörterbuch-Vorschau als Chips). "Alle →"/"Bearbeiten →" springen zu den vollen Seiten.
  Die Vorschau-Listen (`RefreshHistoryPreview`/`RefreshDictionaryPreview`) hängen an
  denselben Refresh-Aufrufen wie die vollen Listen, damit sie nie veraltet sind - lief
  vorher teils nur, wenn die jeweilige Seite gerade sichtbar war (`if (HistoryPage.
  IsVisible) ...`), das wurde entfernt, da jetzt immer eine Vorschau sichtbar sein kann.
- **Farben**: Akzent von Grün (#10B981) auf Amber (#F0B429) geändert, Hintergrund auf
  warmes Nah-Schwarz (dunkel) bzw. warmes Off-White (hell) - beide Themes weiterhin
  vorhanden und funktionsfähig (Kati hat den Toggle bisher genutzt, daher bewusst nicht
  gestrichen, obwohl der Entwurf nur dunkel gezeigt wurde). Icon auf dem Record-Button ist
  jetzt dunkel statt weiß (bessere Lesbarkeit auf hellem Amber).
- **Schrift**: Mockup nutzte Space Grotesk/Work Sans/JetBrains Mono (Google Fonts) -
  bewusst NICHT als eingebettete Schriftdateien übernommen (Aufwand/Lizenz für eine
  Desktop-App nicht gerechtfertigt). Läuft weiter mit dem bisherigen Avalonia.Fonts.Inter
  + "Consolas,monospace" für Zahlen/Badges wie zuvor.
- Konnte NICHT live gegengeprüft werden (Murmel ist keine übers Startmenü auflösbare App,
  computer-use-Zugriff schlug fehl) - nur über `dotnet build` (fehlerfrei) validiert, nicht
  visuell. Nach dem Deploy unbedingt mit Kati durchgehen, ob Abstände/Ausrichtung passen.

## Bekannte Design-Entscheidungen

- Alles lokal/offline - kein LLM, keine Cloud-Verarbeitung von Diktaten. Das gilt auch für
  zukünftige Features (z.B. wurde WisprFlows LLM-basierter "Command Mode" bewusst NICHT
  übernommen, stattdessen eine simple lokale Find/Replace-Variante gebaut). Nochmal
  bestätigt: ein lokales Sprachmodell für natürlicheres Korrekturverständnis wäre technisch
  möglich, ist für sie aber nur Nice-to-have (sie spricht notfalls einfach nochmal ein) -
  nicht von selbst vorschlagen, nur auf Nachfrage wieder aufgreifen.
- Lieferung von Datei-Änderungen direkt in ihre Ordner, nicht als Zip zum Entpacken.
