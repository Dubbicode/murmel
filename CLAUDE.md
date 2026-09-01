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

## Aktueller Stand (Stand: v0.9.0, siehe git log für Details)

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

## Notizen-Erweiterung (Konzept, Stand: 2026-09-01)

Neue Funktion in Planung, noch nicht gebaut. Kati möchte eine zweite Push-to-Talk-Taste
für Sprachnotizen (getrennt vom normalen Diktat), organisiert in einem Dashboard mit
Projekten, sortierbar nach Wichtigkeit oder Datum. Konkurrenzrecherche (Voicenotes,
Superwhisper, Wispr Flow, Granola, Plaud, Otter.ai, Drafts) hat folgendes Konzept ergeben,
mit Kati abgestimmt (2026-09-01):

- **Architektur**: Integration in Murmel, kein separates Programm - neuer Tab "Notizen"
  analog zu Wörterbuch/Verlauf. Begründung: nutzt bestehende Infrastruktur (Hotkey-System,
  STT-Pipeline, Einstellungen-Persistenz), keine zweite App, die Kati verwalten/schließen
  muss.
- **Zweiter Hotkey**: komplett eigener, frei wählbarer Push-to-Talk-Hotkey (nicht
  Zusatztaste zum bestehenden), der direkt in den Notiz-Modus geht statt zu diktieren/
  einzufügen.
- **Projekt-Zuordnung**: Kati legt Projekte selbst an (wie Wörterbuch-Einträge). Zuordnung
  einer Notiz zu einem Projekt läuft per Sprachbefehl am Anfang der Notiz ("Projekt
  Website: ..."), rein lokaler Musterabgleich gegen die vorher angelegten Projektnamen -
  KEIN LLM, gleiches Prinzip wie `Services/CorrectionCommand.cs` und
  `Services/DictionaryProcessor.cs`. Ohne erkannten Projektnamen landet die Notiz in einer
  Inbox zur späteren manuellen Zuordnung.
- **Wichtigkeit**: rein manuell im Dashboard markierbar (einfaches Flag/Stern, keine
  Prioritätsstufen, kein Sprachbefehl dafür) - Entscheidung gegen Sprachbefehl, um das
  Diktieren nicht zu verkomplizieren.
- **Sortierung**: nach Datum oder nach Wichtigkeit, kombinierbar.
- **Dashboard** (überarbeitet mit Kati, 2026-09-01): doch ein Kanban-artiges Board -
  Projekte stehen als Spalten nebeneinander (inkl. Inbox als eigene Spalte), Notizen als
  Kacheln darunter. Innerhalb jeder Spalte drei feste Wichtigkeits-Stufen (Wichtig/Normal/
  Unwichtig) als Bereiche, keine Sprachbefehle oder Sterne mehr dafür - Zuordnung zur Stufe
  UND zum Projekt läuft rein per Drag & Drop (Notiz zwischen Stufen oder zwischen Spalten
  verschieben, z.B. um eine falsch zugeordnete Notiz zu korrigieren). Datenmodell also:
  Notiz hat Projekt + Wichtigkeits-Stufe (Enum, kein Freitext-Rank). Freitextsuche wie im
  Verlauf ergänzt werden, sobald es an die Umsetzung geht.
  Später (bewusst zurückgestellt, nicht jetzt bauen): ein Reiter/Filter "nur Wichtiges
  zeigen" vs. "alles zeigen".
- **Viele Projekte** (mit Kati abgestimmt 2026-09-01): Spalten-Zeile bekommt horizontales
  Scrollen (Scrollbar unten im Board), sobald mehr Projekte da sind als ins Fenster passen.
  Spalten selbst sind ebenfalls per Drag umsortierbar (Griff-Symbol im Spaltenkopf) - eigene
  Reihenfolge unabhängig von der Notizen-Sortierung innerhalb einer Spalte.
- **Erledigt-Spalte** (mit Kati abgestimmt 2026-09-01): eigene, letzte Spalte im Board für
  als erledigt markierte Notizen (kein Wichtigkeits-Tiering dort, flache Liste). Jede Notiz
  bekommt ein Kreis-Symbol zum Markieren; erledigt verschiebt in diese Spalte, per Klick
  rückgängig machbar (falls fälschlich abgehakt). Die Spalte selbst lässt sich über einen
  Schalter oben rechts auf der Notizen-Seite ("Erledigt ausblenden"/"einblenden")
  aus-/einblenden, damit sie das Board nicht dauerhaft zumüllt.
- **Notiz bearbeiten** (mit Kati abgestimmt 2026-09-01): jede Notiz-Kachel bekommt ein
  Stift-Symbol, um den Text nachträglich per Tastatur zu ergänzen/korrigieren (falls beim
  Diktieren etwas vergessen wurde) - unabhängig vom Wichtigkeits-Kreis und vom Drag-Griff.
- **Zugriff von mehreren Orten** (z.B. Arbeit + Zuhause, mit Kati abgestimmt 2026-09-01):
  Speicherort der Notizen-Datei wird in den Einstellungen frei wählbar (statt fest auf
  lokalen AppData-Ordner), damit er auf einen Cloud-Sync-Ordner zeigen kann. Kati nutzt
  Google Drive - Notizen-Datei landet in einem Google-Drive-Ordner, das Sync übernimmt die
  Google-Drive-Desktop-App, Murmel selbst braucht keine eigene Cloud-Anbindung/kein
  Konto-Login. Voraussetzung: Murmel muss auch am Arbeits-PC installiert sein und dort auf
  denselben (dort ebenfalls von Google Drive gesyncten) Ordner zeigen. Bekannte Grenze für
  v1: kein Konfliktmanagement bei echt gleichzeitigem Schreiben von zwei Geräten (unwahr-
  scheinlich bei Einzelnutzung, wird bewusst nicht für die erste Version gelöst).
- **Design von Kati final bestätigt (2026-09-01)**, vier Iterationsrunden als Artifact-
  Mockups (Link: siehe Session vom 2026-09-01, ggf. nicht mehr aktuell/erreichbar -
  Endstand ist unten zusammengefasst).
- **Alpha v1 gebaut (2026-09-01), nach ..\Murmel\ published, noch nicht von Kati getestet.**
  Umfang siehe unten. `dotnet build` fehlerfrei, aber NICHT live gegengeprüft (computer-use
  auf Murmel schlägt fehl, siehe UI-Redesign-Eintrag) - insbesondere Drag & Drop unbedingt
  mit Kati durchgehen, das ist die riskanteste Stelle ohne visuelle Prüfung.
  - Neue Dateien: `Models/NoteEntry.cs`, `Models/NoteImportance.cs`,
    `Services/NotesStore.cs` (`%AppData%\Typr\notes.json`, gleiches Muster wie
    DictionaryStore/HistoryStore/StatsStore), `Services/NoteProjectMatcher.cs` (lokaler
    Regex-Musterabgleich "Projekt X: ..." gegen `NotesStore.Data.Projects`, kein LLM).
  - Zweiter Hotkey: eigene zweite `GlobalHotkeyService`-Instanz (`_notesHotkey` in
    `MainWindow.axaml.cs`), Default `CtrlAlt` (`AppSettingsData.NotesHotkey`) - noch NICHT
    in den Einstellungen änderbar (kommt in Runde 2). `_recordingTarget`-Enum
    (Dictation/Note) wird beim jeweiligen `HotkeyPressed` gesetzt und in
    `StopRecordingAndTranscribeAsync()` ausgewertet: Notiz-Pfad läuft NUR durch
    DictionaryProcessor (Korrekturen/Snippets), NICHT durch CorrectionCommandParser -
    landet nie in History/Stats/Zwischenablage.
  - Board (`NotesPage`/`TabNotesBtn`) wird komplett per Code (nicht XAML-Templates)
    in `RenderNotesBoard()` + Hilfsmethoden gebaut - bewusste Entscheidung, um
    Drag&Drop (`Avalonia.Input.DragDrop`) sauber pro Notiz/Spalte zu verdrahten, ohne
    Tag/DataContext-Fummelei durch verschachtelte ItemsControls. Nach JEDER Mutation
    (Drag, Edit, Erledigt-Toggle, Projekt hinzufügen/entfernen) wird das ganze Board neu
    aufgebaut, gleiche "immer neu rendern"-Philosophie wie der Rest der App.
  - Farben im Board sind NICHT `{DynamicResource}` (da in Code gebaut), sondern eine
    kleine `NoteColor(key)`-Hilfsfunktion mit hartkodierten Hex-Werten pro Theme
    (`Application.Current.ActualThemeVariant`) - Theme-Wechsel während die Notizen-Seite
    offen ist, löst deshalb explizit `RenderNotesBoard()` erneut aus
    (`OnThemeToggleClicked`), sonst blieben die alten Farben stehen.
  - Projekte: kein eigener Verwaltungs-Screen in dieser Runde - Projekte werden direkt im
    Board angelegt (Textfeld + "+ Projekt"-Spalte ganz rechts) und über einen
    "Entfernen"-Link im Spaltenkopf wieder entfernt (Notizen darin fallen zurück in die
    Inbox statt gelöscht zu werden).
  - Bewusst NICHT in dieser Runde: eigene Notizen-Einstellungsseite (Hotkey änderbar
    machen, Google-Drive-Speicherordner wählen - `NotesStore` nutzt aktuell immer den
    festen `%AppData%\Typr`-Pfad), Spalten selbst per Drag umsortieren (Griff-Symbol im
    Spaltenkopf existiert dafür bewusst noch nicht, um keine Funktion vorzutäuschen, die
    nicht da ist), "nur Wichtiges zeigen"-Reiter (war eh schon zurückgestellt).
  - Von Kati getestet und funktional bestätigt (2026-09-01). Feintuning direkt danach:
    größere Kacheln/Schrift (Spaltenbreite 188→226px, Notiztext 11→13.5px, Symbole für
    Bearbeiten/Erledigt 20→28px), Schlagschatten auf den Kacheln (`Border.BoxShadow`) für
    einen 3D-Effekt, und ein manueller Weg eine Notiz per Tastatur anzulegen: neuer
    "+ Notiz"-Button oben auf der Seite legt eine leere Notiz in der Inbox an und öffnet
    sie sofort im Bearbeiten-Modus (wiederverwendet denselben Inline-Edit-Mechanismus wie
    das Stift-Symbol); bricht man ohne Text abzuschicken ab, wird die leere Notiz wieder
    verworfen statt als leere Kachel liegen zu bleiben.
  - Zwei Nachbesserungen nach erstem Test (2026-09-01):
    a) Enter im Bearbeiten-Modus fügte einen Zeilenumbruch ein statt zu bestätigen -
       TextBox verarbeitet Enter (AcceptsReturn) intern als Class-Handler VOR einem
       normal registrierten (Bubble-)Handler auf demselben Control, der eigene Handler kam
       nie zum Zug. Fix: Handler stattdessen auf der Tunnel-Route registriert
       (`box.AddHandler(InputElement.KeyDownEvent, ..., RoutingStrategies.Tunnel)`), fängt
       Enter ab bevor TextBox es verarbeitet. Umschalt+Enter fügt weiterhin einen echten
       Zeilenumbruch ein.
    b) Kartenschatten (`BoxShadow`) - ein schwarzer Schatten ist auf dem fast-schwarzen
       Dark-Theme-Hintergrund unsichtbar, egal wie stark. Lösung: im Dark Mode ein heller
       Schimmer statt dunkler Schatten (`TileShadow()`-Hilfsmethode, prüft
       `Application.Current.ActualThemeVariant`), im Light Mode normaler dunkler Schatten.
       Gilt jetzt auch für die 5 Dashboard-Kacheln auf der Aufnahme-Startseite (Verlauf,
       Transkript, Statistik, 7-Tage-Chart, Wörterbuch) - da `BoxShadow` keine
       `{DynamicResource}`-fähige Eigenschaft ist, wird das per `ApplyHomeCardShadows()`
       im Code gesetzt (Konstruktor + bei jedem Theme-Wechsel neu).
  - Kacheln/Schrift/Symbole auf Katis Wunsch vergrößert (Spaltenbreite 226px, Notiztext
    13.5px, Bearbeiten/Erledigt-Symbole 28px), "+ Notiz"-Button zum manuellen Anlegen.
  - **Von Kati final bestätigt, sieht gut aus (2026-09-01).** Runde 1 damit abgeschlossen.
- **Runde 2 gebaut (2026-09-01), nach ..\Murmel\ published, noch nicht von Kati getestet.**
  Beide zurückgestellten Punkte aus Runde 1 umgesetzt:
  - **Notizen-Einstellungen**: eigener "NOTIZEN"-Abschnitt unten auf der bestehenden
    Einstellungen-Seite (kein separater Reiter - konsistent mit dem Rest der App, die
    schon alle Einstellungen an einem Ort sammelt). Drei Karten: Notiz-Hotkey (zweite
    ComboBox `NotesHotkeyPresetCombo`, mit Kollisions-Check gegen den Diktat-Hotkey und
    umgekehrt - identische Kombo für beide wird abgelehnt), Projekte verwalten (eigene,
    umbenennen-fähige Liste - anders als das Board, das nur Anlegen/Entfernen kann;
    Umbenennen aktualisiert automatisch alle betroffenen Notizen), Speicherort (Button
    öffnet Avalonias `IStorageProvider.OpenFolderPickerAsync`, `NotesStore.ChangeFolder()`
    schreibt die aktuellen Daten sofort an den neuen Ort um - Google Drive wählbar).
  - **Spalten-Reihenfolge**: Griff-Symbol im Kopf jeder Projekt-Spalte (nicht bei
    Inbox/Erledigt - die bleiben an fester Position) startet einen Drag mit eigenem
    Datenschlüssel `"projectName"` (getrennt von `"noteId"` für Notiz-Drags), die ganze
    Spalte ist Drop-Ziel dafür und tauscht die Position in `NotesStore.Data.Projects`.
    Bugfix nebenbei: Drop-Handler für Notizen setzen jetzt `e.Handled = true`, sonst wäre
    ein Notiz-Drop auf eine Stufe zusätzlich beim umgebenden Spalten-Reorder-Handler
    angekommen (Routed-Event-Bubbling).
  - `dotnet build` fehlerfrei, wie immer NICHT live gegengeprüft - beide neuen
    Drag-Interaktionen (Spalten-Reorder zusätzlich zum bestehenden Notiz-Drag) unbedingt
    mit Kati durchgehen.
  - Nach Katis Feedback direkt ergänzt (2026-09-01): "+ Projekt"-Button jetzt auch oben
    im Seitenkopf neben "+ Notiz" (legt ein Projekt mit Platzhaltername "Neues Projekt"
    an und öffnet den Spaltenkopf direkt zum Umbenennen, Text vorausgewählt - wie
    "Neuer Ordner" im Windows-Explorer). Spaltenköpfe im Board selbst sind jetzt auch
    direkt umbenennbar (TextBox statt TextBlock, nicht bei Inbox/Erledigt) - die
    Umbenennen-Logik (`RenameProject()`) ist jetzt eine gemeinsame Methode, die sowohl
    vom Board-Spaltenkopf als auch von der Einstellungen-Projektliste aufgerufen wird,
    damit beide Stellen nicht auseinanderlaufen.
  - Auf Katis Wunsch (2026-09-01) zweite Sprach-Trigger-Formulierung ergänzt in
    `Services/NoteProjectMatcher.cs`: neben "Projekt X: ..." jetzt auch "neue Notiz für
    X, ..." / "Notiz für X: ..." (Katis eigenes Beispiel: "neue Notiz für Arbeit, Thomas
    anrufen"). Umgesetzt als Pattern-Familie (Array von Regex, der Reihe nach probiert),
    gleiches Prinzip wie `CorrectionCommandParser`s FullPatterns/ShortPatterns/
    BarePatterns. Wichtig, X muss weiterhin ein bereits angelegtes Projekt sein - kein
    Auto-Anlegen aus der Sprache heraus, das war schon in der ursprünglichen
    Konzept-Entscheidung so festgelegt (keine KI-Erkennung, siehe Notizen-Konzept oben).
  - Bug direkt nach erstem Voice-Trigger-Test gefunden und behoben (2026-09-01): Parakeet
    transkribiert die gesprochene Pause nach dem Projektnamen mal als Komma, mal als Punkt
    (oder ! / ?) - `NoteProjectMatcher`s Trennzeichen-Klasse akzeptierte bisher nur ",",
    dadurch schlug z.B. "Neue Notiz für Sport. Fahrrad putzen." fehl (landete unverändert
    in der Inbox). Jetzt `[:,.!?]` statt nur `[:,]`.
  - Auf Katis Wunsch ergänzt (2026-09-01):
    a) "Notiz löschen"-Button im Bearbeiten-Modus jeder Notiz-Kachel (unter dem Textfeld) -
       vorher gab es gar keine Möglichkeit, eine bestehende Notiz zu löschen.
    b) Projekt-Erkennung per Sprache funktioniert jetzt auch bei langen/komplexen
       Projektnamen ("AB25-005 Douglas Sells Convention"): `NoteProjectMatcher` versucht
       zuerst exakten Treffer, fällt sonst auf Ganzwort-Teiltreffer zurück (z.B. "Douglas"
       oder "AB25-005" allein reichen dann) - aber nur, wenn das eindeutig genau ein
       Projekt trifft; träfe es mehrere, bewusst kein Treffer statt zu raten. Dritte
       Trigger-Formulierung "Projektnummer X: ..." ergänzt, zusätzlich zu "Projekt X"
       und "Notiz für X".
  - Auf Katis Wunsch entfernt (2026-09-01): die "Neues Projekt…"-Spalte ganz rechts im
    Board (`BuildAddProjectColumn`) - macht der "+ Projekt"-Button oben im Seitenkopf
    jetzt überflüssig.
  - Nächster Schritt: Kati testet Runde 2 (inkl. dieser Fixes).

## Bekannte Design-Entscheidungen

- Alles lokal/offline - kein LLM, keine Cloud-Verarbeitung von Diktaten. Das gilt auch für
  zukünftige Features (z.B. wurde WisprFlows LLM-basierter "Command Mode" bewusst NICHT
  übernommen, stattdessen eine simple lokale Find/Replace-Variante gebaut). Nochmal
  bestätigt: ein lokales Sprachmodell für natürlicheres Korrekturverständnis wäre technisch
  möglich, ist für sie aber nur Nice-to-have (sie spricht notfalls einfach nochmal ein) -
  nicht von selbst vorschlagen, nur auf Nachfrage wieder aufgreifen.
- Lieferung von Datei-Änderungen direkt in ihre Ordner, nicht als Zip zum Entpacken.
