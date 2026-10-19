# Mitwirken

Windows und das SDK aus `global.json` verwenden. Abhängigkeiten mit `dotnet restore --locked-mode` installieren. Nach Änderungen Release-Build und `--core-test` ausführen: [Testanleitung](docs/TESTING.md).

Pull Requests sollen Problem, Änderung und tatsächliche Tests beschreiben. Audio-/MIDI-Änderungen benötigen passende Gerätetests; Kernprüfungen ersetzen diese nicht. Beiträge werden unter MIT eingereicht.

Fehlerberichte: App-, Windows-, Controller-/Treiber-Version, Reproduktionsschritte, erwartetes/tatsächliches Verhalten und Audio-Signalwege nennen. Keine OBS-Passwörter oder unbereinigten Konfigurationen veröffentlichen. Geräte-IDs, Programm-, Fenster- und Szenennamen vor dem Teilen prüfen.

Keine Installer, persönlichen Profile, privaten Test-Screenshots oder Build-Ausgaben ins Repository aufnehmen. Abhängigkeiten samt Lizenzhinweisen dokumentieren; NuGet-Änderungen benötigen aktualisierte `packages.lock.json`.
