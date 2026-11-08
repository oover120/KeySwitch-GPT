<div align="center">

# KeySwitch — automatic EN ↔ RU keyboard layout switcher

**Wrong layout. Right words.**

An offline Windows 11 tray app that corrects words typed in the wrong English or Russian keyboard layout.

[Download](https://github.com/oover120/KeySwitch-GPT/releases/latest) · [Русский](README.md) · [Report a bug](https://github.com/oover120/KeySwitch-GPT/issues)

</div>

## Type naturally

Type `ghbdtn` and press Space: KeySwitch can replace it with `привет` and switch the active window to Russian. In the other direction, `руддщ` becomes `hello`.

Detection runs locally using bundled language models. Known source-language words are preserved; ambiguous or short words may be skipped.

## Features

**New in 1.0.3:** browse for applications, folders and games; remove selected exclusions; repair stale startup paths; and use the new keyboard icon.

- Automatic English US / Russian ЙЦУКЕН layout correction at word boundaries.
- Selected-text conversion, last-word conversion and undo shortcuts.
- Per-application, folder and word exclusions; local learning from explicit corrections and undo.
- Optional typo correction, capitalization fixes, curated Russian ё replacement and sounds.
- Tray interface with WPF settings and configurable shortcuts.
- No account, network APIs or telemetry in the application.

An open-source option for users looking for a Punto Switcher alternative for EN/RU. This is an independent project with no affiliation to Punto Switcher.

## Download

Requires **Windows 11 x64**. Choose the installer or portable ZIP from [Releases](https://github.com/oover120/KeySwitch-GPT/releases/latest). Extract the entire ZIP before running `KeySwitch.exe`. The runtime is included; no separate .NET installation is required.

Double-click the tray icon to open settings. Startup with Windows is optional.

| Action | Default shortcut |
| :--- | :--- |
| Undo the last replacement | Pause/Break or two quick Shift taps |
| Convert current / last word when undo is unavailable | Pause/Break |
| Convert selected text | Ctrl+Shift+Space |
| Pause / resume automatic correction | Ctrl+Shift+F12 |

Undo requires the original input position. It is unavailable after Enter/Tab, which may submit a message or move focus.

## Privacy and compatibility

Settings and explicitly learned words stay in `%APPDATA%\KeySwitch\settings.json`. The technical `debug.jsonl` log includes process names, word lengths, scores and decisions, but **never typed words, window titles or URLs**. Extra focus diagnostics are opt-in with `--debug`.

**ESET keyboard protection is incompatible with the current global keyboard hook in secured browsers.** A user-controlled test confirmed that automatic switching works with that option off and stops when it is restored. KeySwitch does not change antivirus settings. See [ESET's description](https://help.eset.com/essp/20/en-US/idh_config_sb.html). An ordinary antivirus scanning exclusion does not resolve this issue.

Terminals, code editors and RDP clients default to manual mode. Explicit UI Automation password fields are excluded; if the provider fails or returns an unknown state, ordinary processing may continue. Protection in every custom password control is not guaranteed.

Elevated processes, games, virtual machines, protected input and custom editors may restrict operation. The full browser and application compatibility matrix has not been verified. See the [Russian README](README.md) for test results and limitations.

## Build

Use Windows and .NET SDK 8.0.403 or a compatible 8.0 SDK according to `global.json`. Models are included in the repository.

```powershell
git clone https://github.com/oover120/KeySwitch-GPT.git
cd KeySwitch-GPT
dotnet build KeySwitch.sln -c Release
dotnet test tests/KeySwitch.Tests -c Release
dotnet publish src/KeySwitch.App -c Release -r win-x64 --self-contained true -o artifacts/publish -p:DebugType=None
```

`./tools/Build.ps1 -Installer` runs the full build, tests, corpus evaluation, publish and installer pipeline. Installer compilation requires Inno Setup.

## Development and licensing

See [architecture](ARCHITECTURE.md), [decisions](DECISIONS.md), [development log](DEVLOG.md) and [data attribution](licenses/ATTRIBUTION.md).

Code is licensed under [MIT](LICENSE). Derived language models and corpus data are separately licensed under **CC BY-SA 4.0**. Do not include personal learned words or your settings file in public bug reports.

`KeySwitch-GPT` identifies this implementation's repository. Runtime correction uses local language models and does not call GPT or any cloud API.
