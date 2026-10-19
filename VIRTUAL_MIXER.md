# Virtueller Mixer – Audio-Prototyp

Öffnen: **Virtueller Mixer** oder `MidiMixer.exe --virtual-mixer`.

Vier Eingänge (Musik, Spiel, Discord, Sonstiges) werden auf zwei getrennte Mischungen verteilt. Je Eingang eigene Pegel/Mutes für Kopfhörer und Stream, zusätzlich zwei Master. Die App nutzt vorhandene Aufnahmegeräte oder WASAPI-Loopback; sie erzeugt und installiert keine Treiber.

## Beispiel mit fünf Kabeln

| Programm-Ausgabe in Windows | Eingang der App | Kanal |
|---|---|---|
| CABLE-A Input | CABLE-A Output | Musik |
| CABLE-B Input | CABLE-B Output | Spiel |
| CABLE-C Input | CABLE-C Output | Discord |
| CABLE-D Input | CABLE-D Output | Sonstiges |

Lokale Ausgabe: Kopfhörer. Stream-Ausgabe: **CABLE Input**. OBS: **CABLE Output** als Audioeingabeaufnahme.

Input/Output beziehen sich auf das Kabel: Programme schreiben in dessen Input; aufnehmende Apps lesen dessen Output. Für dieses Beispiel werden VB-CABLE sowie A+B und C+D separat vom [Hersteller](https://vb-audio.com/Cable/) benötigt. Zusätzliche Pakete haben eigene Download-/Lizenzbedingungen. Keine Installer im Projekt; keine beliebige dynamische Kabelanzahl. Nach Installation den geforderten Neustart durchführen.

**Kabel & Treiber** zeigt beide Enden der fünf Kabelpaare und alle Windows-Geräte, auch deaktivierte/getrennte. Erkennung anhand Standardnamen; umbenannte Geräte bleiben in der Gesamtliste sichtbar. „Aktiv“ bestätigt Windows-Verfügbarkeit, keinen Hörtest. Die Prüfung ändert keine Einstellungen.

## Einrichtung

1. Kopfhörer verbinden und virtuelle Geräte separat installieren.
2. Programme in Windows auf getrennte Kabel-Inputs legen. Keine automatische Prozesszuordnung durch die App.
3. Aufnahme-Enden als Eingänge und zwei unterschiedliche Ausgänge wählen.
4. Zunächst einen Kanal testen. Neue Stream-Kanäle sind stumm, Kanal-/Masterpegel stehen auf 50 %.
5. **Audio starten**, gewünschten Stream-Kanal entstummen. Beide Mischungen getrennt bei geringer Hörlautstärke prüfen.
6. In OBS nur die neue Stream-Aufnahme für diese Mischung erfassen. Desktop-/Programmaufnahme und Monitoring können Audio verdoppeln oder rückführen.
7. Weitere Kanäle hinzufügen: beispielsweise Musik lokal an/Stream stumm, Spiel auf beiden Mischungen an.

Während die Engine läuft, erscheinen Ziele wie `Mix: Kopfhörer · Musik`, `Mix: Stream · Spiel` und beide Master in den APC-Auswahllisten. Die OBS-Stummtaste betrifft diese Engine nicht; dafür `Mix: Stream · Master` zuweisen.

Keine Ausgänge auf sich selbst zurückführen. Identische Loopback-Endpunkte und offensichtliche VB-CABLE-Paarschleifen werden erkannt, externe Mixer-Rückwege jedoch nicht zuverlässig. Andere virtuelle Mixergeräte sind nicht automatisch isolierte Kabelpaare.

## Lebenszyklus und Grenzen

Speicherung in `%LOCALAPPDATA%/MidiMixer/virtual-mixer.json`, derzeit separat vom Profilimport/-export. Kein automatischer Audiostart. Geräte erst nach **Audio stoppen** ändern. Schließen des virtuellen Mixerfensters stoppt die Engine; Ausblenden des Hauptfensters in den Infobereich nicht.

- Mono/Stereo, intern 48 kHz, Abtastratenkonvertierung.
- Je Eingang zwei getrennte Puffer für unabhängiges Lesen.
- WASAPI-Shared-Ausgabe mit angefragten 60 ms; Gesamtlatenz nicht garantiert.
- Puffer maximal 300 ms, Leeren bei Überlauf. Kein adaptiver Taktausgleich, keine sanften Gain-Rampen.
- Harte Begrenzung auf ±1 mit Clipping-Zähler, kein transparenter Limiter.
- Gemeldete Gerätefehler stoppen beide Mischungen; Neustart manuell.
- Keine automatische OBS-Quellenanlage oder eigenen signierten Treiber.

Synthetische Audiotests und ein stummer lokaler Gerätetest sind erfolgt. Vollständiger hörbarer OBS-/Kopfhörertest und längerer Stabilitätslauf stehen aus. [Tests](docs/TESTING.md).
