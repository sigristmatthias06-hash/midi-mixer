# Veröffentlichung vorbereiten

Die lokale Vorbereitung ist von einer Veröffentlichung getrennt. Keine Pushes, Tags oder GitHub-Releases ohne ausdrückliche Freigabe des Eigentümers.

## Lokales Paket

1. Version in `MidiMixer.csproj` und CHANGELOG prüfen. Lizenz, Autoren und Drittanbietertexte prüfen.
2. `./scripts/Prepare-Release.ps1` ausführen. Das Skript restauriert gesperrte Abhängigkeiten, baut, testet und erstellt ein frisches win-x64-ZIP samt SHA256SUMS.txt unter `artifacts/release-*`.
3. ZIP entpacken, Start und relevante Gerätefunktionen prüfen. Offene Hör-/Langzeittests im Release nennen.
4. Nur freigegebene Quelltexte und Dokumentation versionieren. `drivers`, `dist`, `dist-next`, `artifacts`, persönliche Profile und Diagnoseausgaben bleiben ausgeschlossen. `.gitignore` schützt nicht vor absichtlichem `git add -f`.
5. Vor Upload `git diff --cached --check` und die vollständige Dateiliste prüfen. Autorname ist öffentlich; keine privaten E-Mail-Adressen unbeabsichtigt als Commit-Autor verwenden.

## Erst nach endgültiger Freigabe

Repository-Ziel/Name mit Eigentümer klären, öffentliche Sichtbarkeit bestätigen, dann den geprüften Quellstand übertragen. CI muss grün sein, bevor ein Release veröffentlicht wird. Tag `v0.1.0-alpha` und als **Pre-release** markieren; ZIP und Prüfsumme anhängen. Keine Treiberpakete, persönlichen Logs oder alten Arbeitsordner hochladen.

Der vorhandene Workflow nutzt Windows, gepinnte Actions und nur Leserechte für Repository-Inhalte. Er baut/testet und bewahrt CI-Artefakte zur Prüfung auf, erstellt aber keine Releases oder Tags. Er läuft erst, wenn die Dateien später auf GitHub liegen und ein vorgesehenes Ereignis ausgelöst wird. Ein erfolgreicher lokaler Lauf bestätigt nicht die Ausführung auf GitHub.

## Entwurf des Release-Texts

MIDI Mixer 0.1.0-alpha: Windows-/MIDI-Lautstärkesteuerung für APC mini mk2, acht Belegungsseiten, Mute-LEDs, Farbeditor, OBS-Steuerung und experimenteller virtueller Mixer mit zwei Mischungen.

Windows x64; ZIP vollständig entpacken, MidiMixer.exe starten. .NET-Laufzeit enthalten, EXE nicht codesigniert. Virtuelle Audiotreiber separat beziehen; keine Kabelerstellung durch die App. Vor Updates beide Mixer-Konfigurationsdateien sichern.

Alpha-Grenzen: kein adaptiver Taktausgleich im virtuellen Mixer; längere Hörtests und breitere Hardwaretests stehen aus. OBS-Stummtaste betrifft nur verwaltete OBS-Programmquellen. Siehe README, VIRTUAL_MIXER.md und docs/TESTING.md.
