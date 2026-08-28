# Murmel

Lokale Speech-to-Text Desktop-App für Windows – 100% offline, keine Cloud.

- **Engine:** NVIDIA Parakeet v3 (TDT 0.6B, int8) über [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) – läuft komplett lokal auf der CPU
- **Stack:** C# / .NET 8 / Avalonia UI
- **Funktionen:** globaler Hotkey zum Diktieren (push-to-talk), automatisches Einfügen in das aktive Fenster, Verlauf, Wortzähler (heute + insgesamt), Light/Dark Mode, Start mit Windows

## Nutzung

Diese App ist für den persönlichen Gebrauch gedacht. Fertige, lauffähige Versionen (nicht nur Quellcode) gibt's unter [Releases](../../releases) – dort einfach die passende .zip herunterladen, entpacken und `Murmel.exe` starten. Das Parakeet-Modell (~640MB) wird beim ersten Start automatisch heruntergeladen.

Für Entwickler: `dotnet build` bzw. `dotnet publish -r win-x64 --self-contained false` im Projektordner.

---

Entwickelt in Zusammenarbeit mit Claude.
