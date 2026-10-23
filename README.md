# Lake Shore 336 Temperature Control

Windows desktop application for USB control of a Lake Shore 336. Provides an English-first interface with live Chinese switching, manual targets, timed temperature sequences, a live chart, and temperature logging with local timestamps.

## Build and test

Requires Windows, PowerShell 5.1 or later, and .NET Framework 4.7.2 or later. No NuGet packages or Python dependencies are needed. Run from the repository root:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1
```

The scripts use the Windows Framework C# compiler, so the build works on the lab PC without Visual Studio or the .NET targeting pack. The package script builds and runs all 46 self-tests before producing `artifacts/LakeShoreControl.zip`. Tests use simulated instruments and do not connect to hardware.

Alternatively, open `LakeShoreControl.sln` in Visual Studio with the .NET desktop development workload and the .NET Framework 4.7.2 targeting pack. Build Debug or Release. The runtime alone is sufficient for the scripts; Visual Studio/MSBuild requires the targeting pack.

## Run

```powershell
.\artifacts\bin\Release\LakeShoreControl.exe
.\artifacts\bin\Release\LakeShoreControl.exe --demo
```

The first command opens the GUI without connecting to hardware. Select the USB COM port and connect when ready. The default recipe loads without starting a sequence. English is the default; the selector at the top right switches to Chinese and remembers the choice.

In **Timed step** mode, the hold timer starts after the target has been written and verified. It does not wait for measured temperature to reach the target. Hold time accepts decimal minutes or `mm:ss`. The saved default is 23 points from 80 K to 298.15 K, with 10 K steps and a final smaller step, at 22 minutes 22 seconds each. Always review the loaded table before an experiment. Completing a program maintains its final target by default.

Detailed instructions: [English](docs/user-guide-en.txt) · [中文](docs/user-guide-zh.md).

## Project layout

```text
LakeShoreControl.sln
src/LakeShoreControl/     WinForms app, controller, engine, logging, translation
tests/SelfTests.cs        Simulated control and UI regression tests
recipes/                 Default and example JSON recipes
recipes/archive/         Previous recipe for reference
scripts/                 Build, test, and package commands
docs/                    User guides and development notes
artifacts/               Generated builds, test reports, and packages (ignored)
```

## Experiment records

Daily `temperature_data/*.csv` files contain `temperature_K,local_time` approximately every 10 seconds. Full samples and event records are written to `logs/`. Both directories are relative to the running executable and ignored by Git. The chart displays recent samples; files retain the recorded history.

This repository was organized from the existing bilingual application. The original runnable folder `../LakeShoreControl/` and its experiment records remain in place. Development changes belong here; rebuilding this project does not replace that deployed copy or reconnect its instrument. Archive or copy any edited recipe from a build folder before rebuilding, as build output is regenerated from `recipes/`.

## Instrument behavior

USB serial settings: 57600 baud, 7 data bits, odd parity, 1 stop bit, no flow control. The app supports inputs A-D and heater outputs 1-2. It checks sensor status, verifies control writes and retains the instrument's configured temperature limit. Actual cooling depends on the external cooling system.

Keep the computer and app running during a sequence. Communication loss can prevent confirmation that a heater was shut down; check the instrument and use its panel All Off if needed. Do not use a new build on an active experiment without first checking the current instrument and program state.

See [development notes](docs/development.md) for the code structure and verification workflow.
