# OBS einrichten

Diese Anleitung betrifft **Audio-Routing / OBS**, nicht die eigene Engine des virtuellen Mixers.

1. OBS starten, unter **Werkzeuge → WebSocket-Servereinstellungen** WebSocket 5 mit Passwort aktivieren. Die App liest `%APPDATA%/obs-studio/plugin_config/obs-websocket/config.json`; portable Installationen werden nicht automatisch gefunden. Die App aktiviert den Server nicht selbst.
2. Programme öffnen und suchen. Neue Programme benötigen ein sichtbares Hauptfenster für diese Auswahl.
3. **OBS verbinden**, Zielszene wählen, gewünschte Programme markieren.
4. **Routing in OBS anwenden** erstellt/aktualisiert Quellen mit Präfix `MIDI Mixer · `. Nicht mehr markierte verwaltete Quellen werden stummgeschaltet. Kein Szenenwechsel; Monitoring bleibt aus.

**Andere OBS-Desktop-/Programm-Audioquellen stummschalten** ist standardmäßig aktiv. Die Option verändert entsprechende fremde Quell-Mutes global, auch in anderen Szenen. Mikrofone bleiben erhalten. Vor dem Anwenden abwählen, falls unerwünscht. Browser-, Medien- und integriertes Spiel-/Fenster-Audio können weiterhin Ton liefern. Rückgängig: betroffene Quellen in OBS entstummen und Mixer-Quellen stummschalten/entfernen.

**Auswahl speichern** allein ändert OBS nicht. Nach Fehlern können erfolgreiche Teilschritte bestehen bleiben. Quellen und deren Mutes gelten in OBS global.

Normale Ziele steuern Windows-Sitzungen, `OBS:`-Ziele steuern verwaltete OBS-Quellen. Aufnahmeverhalten bei Windows-Programm-Mute pro Anwendung testen. **Stream stumm** stellt beim erneuten Drücken vorherige Mutes wieder her, betrifft keine Mikrofone, fremden Quellen oder den virtuellen Stream-Master.

**Als Windows-Ausgabe** setzt Console/Multimedia, nicht das Kommunikationsgerät. Apps mit festem Ausgang separat im Windows-Mixer zuordnen.

Referenzen: [OBS-Programmaufnahme](https://obsproject.com/kb/application-audio-capture-guide), [WebSocket-Protokoll](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md).
