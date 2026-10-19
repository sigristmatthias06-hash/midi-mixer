# Tests

## Ohne Geräte (lokal und CI)

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet bin/Release/net10.0-windows/MidiMixer.dll --core-test
```

Sechs Gruppen: Fader-Abholung; Mute-/Seiten-LEDs mit Fake-Ausgang; APC-Palettenprotokoll; synthetischer Audiopfad mit zehn Prüfungen; Migration/Seitentrennung/Persistenz; OBS-Fensterkodierung. Kein Fenster, kein Laden persönlicher Profile, keine Geräteabfrage, kein MIDI-/OBS-Verkehr. Exitcode 1 bei Fehlern. Dies ist ein eingebauter Testläufer, kein `dotnet test`-Projekt.

## Manuelle Windows-Prüfungen

Diese Befehle auf einem lokalen Windows-Desktop ausführen, nicht auf einem CI-Runner. Für WinExe in PowerShell auf Abschluss und Exitcode warten:

```powershell
$test = Start-Process ./MidiMixer.exe -ArgumentList '--smoke-test' -Wait -PassThru
$test.ExitCode
```

| Option | Voraussetzung / Wirkung |
|---|---|
| `--smoke-test` | Aktive Standardausgabe; liest Audio/MIDI, prüft Layouts und 10.000 zusammengefasste MIDI-Ereignisse. Keine Lautstärkeschreibvorgänge. |
| `--virtual-mixer-test` | Windows-Desktop/Audio-APIs; synthetische Tests plus Inventar/UI-Bild. Kein echter Audiostart. |
| `--cable-devices-test` | Geräteinventar inklusive getrennter Geräte und UI-Bild, keine Einstellungen geändert. |
| `--virtual-device-test` | Spezieller lokaler Fixture-Test: Elgato Music, Realtek-Lautsprecher, Elgato System. Öffnet zwei Ausgänge für zwei Sekunden, alle Pegel null/stumm; sonst SKIP. Kein allgemeiner Kabeltest. |
| `--obs-test` | OBS mit lokaler Standardkonfiguration; erstellt temporäre Szene/Quelle, verändert diese und entfernt sie anschließend. Kein Stream-/Aufnahmestart. Nach Fehlern mögliche Testreste kontrollieren. |

Berichte/Bilder entstehen neben der EXE, können Gerätenamen und IDs enthalten und gehören nicht in ein Release. UI-Tests verwenden keine gespeicherten Mixer-Profile und speichern keine Einstellungen. OBS-Test liest ausdrücklich die echte lokale OBS-Verbindungskonfiguration.

## Vor einer stabilen Version noch offen

- Physische Fader-/Tastenreaktion, LEDs, USB-Trennen und Wiederverbinden auf weiteren PCs.
- Hörbarer Nachweis „Musik nur lokal, Spiel lokal + Stream“; beide Pegel/Mutes unabhängig.
- Mindestens eine längere Sitzung mit Puffer-/Clipping-Beobachtung, ohne Doppelaufnahme oder Rückkopplung.
- Gerätewechsel, Energiesparen, OBS-Neustart und Bluetooth-Verbindung.
- Frisches Windows-Benutzerprofil, keine Audiogeräte, DPI-/Fensterskalierung.

Ein stummer lokaler Hardwaretest ersetzt keinen Hörtest. Die Alpha darf mit diesen dokumentierten Grenzen erprobt werden; sie ist nicht als produktionsreif bestätigt.
