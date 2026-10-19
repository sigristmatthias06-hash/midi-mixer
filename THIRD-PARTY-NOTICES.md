# Komponenten und Lizenzhinweise

Die MIT-Lizenz im Projektstamm gilt für eigenen Code. Fremdkomponenten behalten ihre Bedingungen.

## NAudio 2.2.1

Pakete: NAudio, NAudio.Asio, NAudio.Core, NAudio.Midi, NAudio.Wasapi, NAudio.WinForms und NAudio.WinMM. Mark Heath und Mitwirkende, MIT. Vollständiger mitgelieferter Text: [licenses/NAudio-MIT.txt](licenses/NAudio-MIT.txt). [Quellprojekt](https://github.com/naudio/NAudio/tree/v2.2.1).

Der WDL-Resampler basiert auf Cockos WDL, nach C# portiert von Mark Heath. [Originalhinweis](licenses/WDL.txt), aus dem [NAudio-Quelltext](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Core/Dsp/WdlResampler.cs). Verwendung als Bestandteil der unveränderten Abhängigkeit.

## .NET und Windows Desktop Runtime

Das portable Paket enthält Microsoft .NET und Windows Desktop Runtime. Das Release-Skript übernimmt Lizenz-/Drittanbietertexte aus den tatsächlich verwendeten Laufzeitpaketen nach `licenses/runtime/` im ZIP. Paketversionen stehen dort in `runtime-packages.json`. Diese Texte gehören zur Binärdistribution. Quellen: [dotnet/runtime](https://github.com/dotnet/runtime), [dotnet/windowsdesktop](https://github.com/dotnet/windowsdesktop).

## Separate Produkte

OBS, Akai-Hardware und virtuelle Audiotreiber wie VB-CABLE werden nicht mitgeliefert. Für ihre Nutzung gelten eigene Bedingungen. Keine VB-Audio-Installer oder Downloads lizenzpflichtiger Pakete im Projekt. Produktnamen beschreiben die Kompatibilität.
