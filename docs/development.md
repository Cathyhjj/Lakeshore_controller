# Development notes

The application targets .NET Framework 4.7.2, uses C# 5-compatible syntax and has no third-party runtime dependencies.

| File | Responsibility |
| --- | --- |
| `Program.cs` | Entry point, command-line modes, single GUI instance |
| `MainForm.cs` | WinForms controls, recipes, chart, serialized GUI operations |
| `Core.cs` | Recipe validation, numeric conversions and sequence timing |
| `Device.cs` | Serial protocol, verified writes and simulation instrument |
| `Session.cs` | Control ownership, PID checks, pause/restart and fault handling |
| `TemperatureLog.cs` | Daily local-time data files and 10-second sampling buckets |
| `UiText.cs` | English/Chinese display translation and preference persistence |
| `tests/SelfTests.cs` | 46 regression checks using fake instruments and offline forms |

The tests remain linked into the executable so `--self-test <report-path>` works without another test framework. They run before GUI and serial initialization. The Visual Studio project and command-line build include the same application and test sources.

Language changes operate only at the presentation boundary. Recipe mode values and engine states are retained; translation must not alter hardware commands, timers, pause state, or measurement records. Tests verify those properties. UI language preference is stored at `%LOCALAPPDATA%/LakeShoreControl/ui-language.txt`, outside the repository.

Run `scripts/test.ps1` after code changes. The checks cover timed and stability-based steps, bounds and sensor errors, shutdown confirmation, pause/restart, exact hold durations, log append behavior, and localization. Reports are generated under `artifacts/test-results/`. Packaging uses an explicit file allowlist to exclude measurements.

This project was extracted from the deployed application on 2026-10-22. The source files, serial implementation, recipes, and tests were copied without behavior changes. Do not commit hardware audit files, experiment CSVs, generated binaries, or local recovery state. No remote repository or automatic deployment is configured.
