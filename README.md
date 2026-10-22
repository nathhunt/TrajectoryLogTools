# Trajectory Log Tools

One Windows application for Halcyon / Ethos trajectory logs (`.bin`, versions 4.0 and 5.1). It
combines four earlier tools:

| Earlier tool | Now |
|---|---|
| TrajectoryLogConverter (GUI + `HalcyonTrajectoryLogTool`) | **Export** (v4.0 `.bin`, CSV, full CSV); CLI `to-v4`, `to-csv`, `to-v5`, `version`, `compare` |
| `TrajectoryLogDump.cs` | **Raw** tab of a log, and **Export > Raw dump**; CLI `dump` |
| `compare_trajectory_logs.py` | **Compare**; CLI `compare-delivery` |
| `LogToEclipseMlc.cs` | **Rebuild Eclipse MLC**; CLI `reconstruct-mlc` |

The analyses work straight from the `.bin` log; you no longer have to export a CSV first. CSV
exports, from this tool or the old converter, still work as input.

A second, small app, **Trajectory Log Converter**, does one job: converting v5.1 logs to v4.0
(see the [user guide](#user-guide)). It uses the same conversion code.

## User guide

How to use the two apps is in the **user guide**: `docs/user`, built into an offline HTML site that
ships in `Help\` of both deploy folders and opens from **Help** (or F1) in each app. Every screenshot
in it is generated from the real windows by `tools/TrajectoryLogTools.DocShots`. See
[docs/CONTRIBUTING-docs.md](docs/CONTRIBUTING-docs.md) to add a page or a screenshot, and
[docs/SHOTLIST.md](docs/SHOTLIST.md) for the few Eclipse screens that can't be generated.

This README is for developers: building, testing, publishing, versioning and the code layout.

**Icons:** both exes carry an icon (blue: Trajectory Log Tools; orange: the converter), drawn by
`scripts/make_icons.py`.

## Requirements

| To | You need |
|---|---|
| **Run** (Eclipse workstations) | Windows 10 1607 / Windows Server 2016 or later, 64-bit. **Nothing to install**: the published folder is self-contained (it carries its own .NET 10 runtime). |
| **Build in Visual Studio** | **Visual Studio 2026** (version 18) with the *.NET desktop development* workload. Visual Studio 2022 can't target .NET 10 ([Microsoft: SDK / Visual Studio versions](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs)). |
| **Build in VS Code** | The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the **C#** extension (`ms-dotnettools.csharp`; VS Code offers it when the folder is opened). |

The first build downloads packages from nuget.org. `global.json` asks for a .NET 10 SDK (10.0.100 or later).

## Build, test, publish

| | Visual Studio | VS Code | Command line |
|---|---|---|---|
| Build | **Build > Build Solution** | **Ctrl+Shift+B** | `dotnet build TrajectoryLogTools.sln` |
| Run / debug the GUI | **F5** (TrajectoryLogTools.App is the startup project) | **F5** > *GUI* | `dotnet run --project src/TrajectoryLogTools.App` |
| Tests | **Test > Run All Tests** | **Run Task** > *test* | `dotnet test tests/TrajectoryLogTools.Tests` |
| User guide | | **Run Task** > *docs: build* / *docs: serve* / *docs: screenshots* | `python -m mkdocs build --strict`; `dotnet run -c Release --project tools/TrajectoryLogTools.DocShots` |
| Publish (deploy folder) | Right-click **TrajectoryLogTools.App > Publish** > *Deploy* > **Publish** | **Run Task** > *publish* | `dotnet publish src/TrajectoryLogTools.App -p:PublishProfile=Deploy` or `scripts\publish.ps1` |

Publishing writes two separate folders. Each is self-contained (its own copy of the .NET 10
runtime), so either one can be deployed, moved or removed without the other:

```
Deploy\TrajectoryLogTools\             (about 180 MB)
  TrajectoryLogTools.exe              the application
  TrajectoryLogCli.exe                the command-line tool
  LaunchTrajectoryLogTools.cs         the Eclipse launcher script for the application
  Help\                               the user guide (copied from site\, see below)
  *.dll                               the .NET 10 runtime and libraries - keep everything together
Deploy\TrajectoryLogConverter\         (about 160 MB)
  TrajectoryLogConverter.exe          the v5.1 -> v4.0 converter
  LaunchTrajectoryLogConverter.cs     the Eclipse launcher script for the converter
  TrajectoryLogConverter.settings.json  its default folders (written by Settings, kept on redeploy)
  Help\                               the user guide
  *.dll                               its own .NET 10 runtime and libraries
```

**The user guide in the deploy folders.** Publishing copies the built guide (`site\`, from
`python -m mkdocs build`) into `Help\` of both folders (`src/Help.targets`). It copies rather than
builds, so publishing needs no Python; if `site\index.html` is missing it warns (TLT0001) and goes
on, and the apps' Help button says the guide isn't installed. Build the guide first (VS Code:
*docs: build*, needs `python -m pip install -r docs/requirements.txt`), or download the
`user-guide-site` artifact of the *User guide* GitHub workflow and unzip it into `site\`.

It is a self-contained **folder**, not a single-file exe. A single-file WPF app unpacks native
DLLs to `%TEMP%` when it first starts, and application whitelisting on clinical PCs and Citrix
servers often blocks that. To publish straight to a share, use
`scripts\publish.ps1 -DeployDir \\server\ESAPI\TrajectoryLogTools` or the VS Code task *publish to folder*;
the converter then goes to `\\server\ESAPI\TrajectoryLogConverter` (beside it).

**Version number:** set `<Version>` in `Directory.Build.props` (all projects share it) and commit
it before publishing; `-p:Version=1.2.0` on the publish command overrides it for one build. Both
apps show it in the title bar (e.g. *Trajectory Log Tools 1.2.0*) and, with the git commit the build
came from, on the line at the bottom of the window (e.g. *Version 1.2.0+aeaf93d*); the error log
records the same. Each exe's file properties carry it too.

The `.vscode` folder has tasks for: build, release build, CLI-only build, clean, rebuild,
publish, publish to folder, tests, tests with coverage, the CLI commands, the round-trip test,
golden-file regeneration, and the user guide (docs: build, docs: serve, docs: screenshots). It also has debug configurations (GUI, GUI pre-filled as from
Eclipse, and the CLI commands). In Visual Studio, `Properties/launchSettings.json` adds a
pre-filled GUI profile.

The GUI builds on Linux and macOS too (`EnableWindowsTargeting`), so CI can compile it, but it
runs on Windows only. The libraries, the CLI and the tests run on any OS.

## Launching from Eclipse

Eclipse runs scripts in-process on .NET Framework 4.8, so the launchers (`launcher/`) stay 4.8
single-file scripts. Each starts its app as a separate process, which is why the apps can be .NET 10.
Setting them up (ExePath, StartFolder, the scripts folder) is in the user guide:
*Getting started > Set up the Eclipse launchers*.

The application's own arguments, all optional: log files to open (`.bin` or `.csv`), a folder
to add from, and `--start-folder <folder>` for the Open dialogs. Arguments of earlier versions
(`--input`, `--output`, `--mode`, `--tab`) are still accepted.

## Command-line tool

The first four commands do what the app does, with the same options, defaults and output files
(they call the same code):

```
TrajectoryLogCli export <log | folder> ... [-o <folder>] [--v4] [--csv] [--full-csv] [--dump]
                        [--machine truebeam|halcyon] [--serial remove|keep] [--new-serial <text>] [--couch remove|keep] [--save-time]
                        [--patient-id <text>] [--plan-name <text>] [--plan-uid <text>] [--field-name N=NAME ...]   (one log)
                        [--no-mlc] [--actual-only]
TrajectoryLogCli compare-deliveries <logA> <logB> [-o <folder>] [--no-plot]
TrajectoryLogCli compare-files <a.bin | folderA> <b.bin | folderB> [--ignore-version] [--tolerance x] [--max-diffs n]
TrajectoryLogCli rebuild-mlc <log | folder> ... [-o <folder>] [-e export.csv ...] [--positions actual|expected] [--axis mu|cp]
                        [--gantry plan|actual] [--min-gap <mm>] [--include-hold] [--field n,m] [--verbose]
```

* `export` defaults as the Export window: v4.0 only, next to each log in a `converted` subfolder
  (with `-o`, one folder); machine TrueBeam, serial removed, couch axes removed; `--run-log`
  also saves a run log.
  The header details change one log at a time; renaming field 1 also sets the header's BeamName.
* `rebuild-mlc` works as the Rebuild page: any number of logs, every field (or `--field`), next to
  each log or in one folder; Eclipse exports are matched to the fields by control points and
  gantry angle, so their order doesn't matter. It prints each field's status. Defaults as the
  app: expected positions, control-point axis, no report files (`--report` saves them).
  `reconstruct-mlc` keeps LogToEclipseMlc's method defaults (actual, MU axis); like every command
  it writes report files only when asked (`--save-report` or `--report <file>`).

The original commands do one kind of output per run and are kept for scripts:

```
TrajectoryLogCli to-v4   <input.bin | folder> [-o <file|folder>] [--keep-machine-info] [--time-csv] [--drop-couch-rotations] [--axis-scale 1|2|3]
                         [--machine truebeam|halcyon] [--keep-serial | --serial <text>] [--set-meta "KEY=VALUE" ...] [--subbeam-name N=NAME ...]
TrajectoryLogCli to-csv  <input.bin | folder> [-o <folder>] [--no-mlc] [--actual-only] [--full]
TrajectoryLogCli dump    <input.bin | folder> [-o <folder>]
TrajectoryLogCli compare-delivery <logA> <logB> [-o <folder>] [--no-plot] [--keep-first-snapshot]
TrajectoryLogCli reconstruct-mlc  <log.bin | log.csv> [-o out.csv] [-t template.csv ...] [--positions actual|expected]
                                  [--axis mu|cp] [--gantry plan|actual] [--include-hold] [--subbeam all|n|n,m]
                                  [--mlc-model SX2] [--scale auto|varian|iec] [--min-gap 0.5] [--save-report | --report file]
TrajectoryLogCli to-v5   <input.bin | folder> [-o <file|folder>] [--time-from <csv>] [--start-time hh:mm:ss] [--serial <text>] [--add-couch-rotations]
TrajectoryLogCli version <input.bin | folder> [--expect 4.0|5.1]
TrajectoryLogCli compare <a.bin | folderA> <b.bin | folderB> [--ignore-version] [--tolerance x] [--max-diffs n]
Common: --overwrite, --ignore-crc.   Exit code: 0 OK, 1 failure / difference, 2 bad arguments.
```

`TrajectoryLogCli` with no arguments prints the full help. The conversion commands and their
options are unchanged from `HalcyonTrajectoryLogTool`; see [docs/engine-notes.md](docs/engine-notes.md)
for what each conversion does. The analysis commands take the options of the scripts they
replace, with two changes:

* `--scale` defaults to `auto`: the angle scale is read from the `.bin` header.
* `--mlc-model` and `--min-gap` default to the log's MLC.

`scripts\RoundTripTest.ps1` converts each log to the other version and back and compares the
result with the original; every log should come back byte-for-byte identical.

## Changes from the original tools

The numbers are the same as the originals produced: the golden tests check this, see
[reference/README.md](reference/README.md). What differs:

* **Beam on** means the dose servo is not in *Hold*. This only matters for the Service-mode states
  *Freeze* and *Disabled* ([details](docs/engine-notes.md#beam-on)).
* **MLC reconstruction**:
  * reads the angle scale from the log header;
  * refuses a log whose MLC it doesn't know (see below) instead of assuming SX2;
  * can write to any output folder;
  * in the GUI, matches templates to fields automatically.
* **Delivery comparison** accepts `.bin` logs directly. The figure is drawn with ScottPlot
  instead of matplotlib (same layout), with its colour scale fitted to the data rather than fixed
  at ±0.2 mm.
* **Raw dump** is part of the converter. It gives the same text as `TrajectoryLogDump`, and
  `to-csv --full` still gives the same dump as CSV.

## Solution layout

```
TrajectoryLogTools.sln
src/
  TrajectoryLogTools.Core       engine: parse, v4.0 <-> v5.1, file compare, CSV and raw dumps, log finder,
                                MLC geometry (net10.0 + net48)
  TrajectoryLogTools.Analysis   DeliveryRecord, DeliveryComparison, MlcReconstruction, TemplateMatcher (net10.0 + net48)
  TrajectoryLogTools.Charts     ScottPlot charts shared by the GUI and the CLI
  TrajectoryLogTools.Cli        TrajectoryLogCli.exe
  TrajectoryLogTools.App        TrajectoryLogTools.exe (WPF, Fluent theme, MVVM)
  TrajectoryLogTools.Converter  TrajectoryLogConverter.exe (WPF): v5.1 -> v4.0 from default folders
  TrajectoryLogTools.Wpf        what the two apps share: styles, converters, time picker, log filter
                                panel, dialogs, error log, NZ dates, wheel scrolling
launcher/                       Eclipse scripts (.NET Framework 4.8, compiled by Eclipse), one per app
tests/TrajectoryLogTools.Tests  xUnit: the converter's 363 tests + analysis, CLI and golden tests
tools/TrajectoryLogTools.DocShots  the user guide's screenshots from the real windows (Windows; not deployed)
docs/user/                      the user guide (MkDocs Material, mkdocs.yml at the root; built into site/)
reference/                      the original tools, unchanged, and the golden-file generator
scripts/                        publish.ps1, RoundTripTest.ps1, make_icons.py
docs/engine-notes.md            how the engine converts and exports
docs/CONTRIBUTING-docs.md       how to add a page or a screenshot, and regenerate the guide
.github/workflows/docs.yml      CI on Windows: build, test, screenshots, strict guide build, publish check
```

Core and Analysis also build for .NET Framework 4.8, so they could be used from an ESAPI script
or a 4.8 tool if ever needed.

## Adding TrueBeam later

TrueBeam support isn't included yet, but the code is arranged for it. The TrueBeam 2.7 MR2+
format (*TrueBeam Trajectory Log File Specification* P1012906-005-E) has the same header,
subbeam and snapshot layout. What would be added:

1. **MLC geometry**: an `MlcGeometry` instance for each MLC (Millennium 120 / HD120: 2
   carriage values, then bank A leaves 1–60, then bank B leaves 1–60). It also needs the bank
   → X1/X2 and sign mapping, which must be verified against an Eclipse export of the same beam.
   Add it to `MlcGeometry.Known`, and the analyses accept the logs.
2. **Versions**: add `"3.0"` to `TrajectoryLog.SupportedVersions`. TrueBeam 2.7 MR2+ also
   writes `"4.0"`, so the machine has to be told apart by MLC (model field and sample count),
   and the Halcyon v4.0/v5.1 conversion offered only for Halcyon logs.
3. **Jaws**: TrueBeam records real jaw positions, so an MLC rebuilt without a template should
   take its jaws from the log (Halcyon has none and uses ±140 mm).
4. **Tracking axes** 60–64 (tracking beams only): names and units in `TrajectoryLog.AxisName` / `AxisUnit`.
5. **Tests**: golden cases from real, anonymised TrueBeam logs and their Eclipse exports.
