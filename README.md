# MIDI Mixer für Windows

Windows-Lautstärken mit dem **Akai APC mini mk2** steuern: acht Programmkanäle, ein Master, acht Belegungsseiten und LEDs für den Stummzustand. Entwickelt von Matthias Sigrist.

**0.1.0-alpha – experimentell.** Der Windows-/MIDI-Mixer und die OBS-Steuerung sind implementiert. Der virtuelle Mixer ist ein Audio-Prototyp; längere Hörtests und Tests auf weiteren PCs stehen aus. Die App enthält und erstellt keine virtuellen Audiotreiber.

## Funktionen

- Programm- und Masterlautstärke, Mute, Pegelanzeigen und MIDI-Learn.
- Acht Seiten mit eigenen Zielen/Namen; Seitenwechsel verändert nicht das Routing.
- Fader-Abholung (Soft Takeover) verhindert Lautstärkesprünge.
- Rote Track-LEDs zeigen Mute; eine grüne Szenen-LED zeigt die aktive Seite.
- RGB-Pad-Farbeditor, MIDI-Wiederverbindung, Profile, Infobereich und optionaler Autostart.
- Lokale OBS-Steuerung für verwaltete Programm-Audioquellen.
- Experimenteller virtueller Mixer: vier Eingänge und getrennte Kopfhörer-/Stream-Mischungen.
- Übersicht virtueller Kabel und aktiver, getrennter oder deaktivierter Windows-Audiogeräte.

## Voraussetzungen und Start

Windows x64 und ein aktives Audioausgabegerät. Bedienung per Maus ist ohne MIDI möglich. Andere Controller benötigen passende Zuweisungen; LED-Protokoll und Seiten-/Mute-Tasten sind APC-spezifisch. Optional: OBS mit WebSocket 5 und Windows-Programmaufnahme; lokal mit OBS 32.2.2 erprobt. Virtuelle Audiotreiber sind separat zu beziehen.

Ein Release-ZIP vollständig in einen beschreibbaren Ordner entpacken und `MidiMixer.exe` starten. Alle Dateien zusammen behalten. Das win-x64-Paket enthält die .NET-Laufzeit. Die App benötigt keine Administratorrechte. Die Alpha-EXE ist nicht codesigniert. Nur eine Instanz gleichzeitig verwenden.

1. APC per USB verbinden, MIDI-Eingang wählen und **Verbinden** anklicken.
2. Programme in den Kanälen zuweisen. Dazu müssen sie aktive Audiositzungen haben; gegebenenfalls kurz Ton abspielen.
3. Fader 1–8 steuern die zugewiesenen Ziele, Fader 9 standardmäßig die Gesamtlautstärke.
4. Track-Tasten 1–8 schalten stumm. Pegel 0 allein gilt nicht als Mute.

Nach Verbindung, Seitenwechsel oder externen Änderungen den Hardwarefader zum angezeigten Wert bewegen oder darüber hinweg: **abholen** zeigt diesen Zustand. CC-Schaltfläche anklicken und Regler bewegen für MIDI-Learn; erneutes Anklicken bricht ab. Doppelte CC-Zuweisungen steuern mehrere Kanäle.

**Das X blendet die App standardmäßig nur aus.** Vollständig schließen: Rechtsklick auf das Symbol im Infobereich → **Beenden**. Verhalten und Autostart unter **Profile / Optionen** ändern.

## Seiten und Farben

Die acht rechten Szenen-Tasten wechseln die Belegung. Jede Seite hat neun Ziele/Namen; laufendes Audio und Routing bleiben bestehen.

**APC-Farben** öffnet den RGB-Editor. Den normalen APC-mini-mk2-Ausgang verwenden, nicht MIDIOUT2. 128 Farbwerte und sieben Helligkeitsstufen; Bildschirmfarben sind Näherungen. Track-/Szenen-LEDs werden automatisch gesteuert. Das RGB-Pad oben rechts ist für OBS-Stumm reserviert. Andere MIDI-Programme können LED-Zustände überschreiben.

## OBS und Audio-Routing

- **Audio-Routing / OBS** erstellt und steuert Programmquellen über die lokale WebSocket-Verbindung. Anleitung und Nebenwirkungen: [OBS-Einrichtung](docs/OBS.md).
- **Virtueller Mixer** mischt vorhandene Eingänge selbst zu zwei Ausgaben. Einrichtung: [Virtueller Mixer](VIRTUAL_MIXER.md).

**Stream stumm** und das APC-Pad oben rechts betreffen nur verwaltete OBS-Programmquellen, keine Mikrofone, fremden Quellen oder den virtuellen Stream-Master. Für diesen das Ziel `Mix: Stream · Master` zuweisen.

## Einstellungen und Daten

- `%LOCALAPPDATA%/MidiMixer/settings.json`: Seiten, MIDI, Farben und OBS-Auswahl.
- `%LOCALAPPDATA%/MidiMixer/virtual-mixer.json`: virtuelle Geräte und Pegel; derzeit separat vom Profilimport/-export.
- OBS-Port/-Passwort werden aus der lokalen Standard-OBS-Konfiguration gelesen. Verbindung ausschließlich an `127.0.0.1`; kein Passwort im Mixer-Profil.
- Keine eingebaute Telemetrie, Cloud-Synchronisation oder automatische Updateprüfung. Herstellerlinks öffnen den Browser.
- Profile und Diagnosen können Programm-, Fenster- und Gerätenamen enthalten. Vor dem Teilen prüfen; beide Einstellungsdateien vor Alpha-Updates sichern.

## Bauen und testen

Windows und das .NET-SDK aus [global.json](global.json) verwenden:

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet bin/Release/net10.0-windows/MidiMixer.dll --core-test
./scripts/Prepare-Release.ps1
```

Das Skript erstellt ein portables ZIP mit Lizenztexten und SHA-256-Prüfsumme in einem neuen Unterordner von `artifacts`. Es lädt NuGet-Abhängigkeiten, führt aber keinen GitHub-Upload aus. Eigenen SDK-Pfad über `-Dotnet 'Pfad/zur/dotnet.exe'` übergeben. Details: [Tests](docs/TESTING.md), [Veröffentlichung](docs/RELEASING.md).

## Bekannte Grenzen

- Alpha ohne garantierte Latenz oder Produktionsreife; Bluetooth kann zusätzliche Verzögerungen verursachen.
- Virtueller Mixer: noch kein adaptiver Taktausgleich; Pufferkorrekturen können hörbare Aussetzer erzeugen. Harte Begrenzung statt transparentem Limiter.
- Keine Treibererstellung oder automatische Zuordnung individueller Windows-App-Ausgänge.
- Normale Programmziele gruppieren gleichnamige Prozesse über aktive Wiedergabegeräte; externe Änderungen werden etwa alle 1,5 Sekunden abgeglichen.
- Setzen des Standardausgangs nutzt die undokumentierte IPolicyConfig-Schnittstelle. Bei Problemen Windows-Soundeinstellungen verwenden.
- Keine Pakete für Linux, macOS, Windows ARM64 oder 32 Bit; bislang keine breite Hardware-Kompatibilitätsmatrix.

## Mitwirken und Lizenz

[CONTRIBUTING.md](CONTRIBUTING.md), [Änderungen](CHANGELOG.md).

Eigener Quellcode: **MIT**, Copyright © 2026 Matthias Sigrist, siehe [LICENSE](LICENSE). Fremdkomponenten behalten ihre Bedingungen: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Unabhängiges Projekt, kein offizielles Produkt von Akai, OBS oder VB-Audio.
