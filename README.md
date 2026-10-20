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

## The application

The window has two parts: **Open logs** on the left and the selected log (or what you're doing
with it) on the right.

**Open logs.** Logs are opened from anywhere and each keeps its own location, so two logs from
different folders or drives sit side by side. No folder is remembered as a default.
* **Ways to open:**
  * **Open** (Ctrl+O) picks one or more files;
  * the **▾** menu has **Add from folder…** (optionally with subfolders and a treatment date
    range) and **Recent logs**;
  * you can drag files or folders anywhere onto the window;
  * the Eclipse launcher, or a file dragged onto the exe, opens a log straight away.
* **Each entry** shows the plan name (from the log's metadata), fields, treatment date,
  version and folder. Badges mark **Interrupted** deliveries and logs whose **CRC failed** (still
  opened, with a warning).
* **Ticking** logs selects them for **Compare** (two) or **Export…** (any number). **✕** closes
  them; nothing is deleted.
* The pane folds to a narrow strip (‹) when you want the room.

**A log** has key facts across the top (MU delivered vs planned, duration, interruptions, MLC
error, file status) and four tabs:
* **Overview:** the delivery timeline (beam on, interruptions and planned holds, with details on
  hover), the per-field table (gantry and collimator in IEC, MU, beam-on time, MLC error), beam
  holds, and the header and metadata.
* **Axes:** pick an axis to see expected and actual, and actual − expected underneath (zooming
  one zooms both), against time or control point.
* **MLC:** actual − expected for every leaf, beam on, against time or control point, with the
  RMS per leaf beside it and the worst leaves named.
* **Raw:** the whole file as stored (header with byte offsets, subbeams, every value, CRC), with
  find.

From a log: **Compare with…**, **Rebuild Eclipse MLC…** and **Export…**.

**Compare** (two ticked logs, or *Compare with…*) works answer first:
* it says what was compared (same plan delivered twice, or plans that differ);
* cards show the MLC, gantry and MU differences, marked within or over your tolerances if you set
  them in Settings;
* below are the leaf-difference map and the gantry and MU charts, then folded details: planned
  differences, per field, beam holds, all statistics, the full report;
* **File differences (validation)** compares every stored value, for example to check a v4.0
  conversion against its original.

You can drop a log onto A or B to replace it. **Save report…** writes the report, tables and figure.

**Rebuild Eclipse MLC** writes, for each field of a log, the Eclipse MLC file of the leaf
positions as delivered:
1. The fields are found automatically; untick any you don't want.
2. Choose where to save: next to the log, or another folder.
3. **Advanced** (folded) holds the method options with their defaults. **Use Eclipse exports**,
   off by default, makes the file keep an Eclipse export's header, meterset weights, jaws and
   angles; the exports are matched to the fields by control points and gantry angle.

Each field gets a status:
* **Ready to import:** no leaf-gap problem left, gantry rebuilt cleanly, every control point had
  data;
* **Check first:** written, with the reasons listed;
* **Failed:** nothing written, with the reason.

Each result shows delivered vs planned (for information), the leaf-gap rules and the gantry, with
the difference map, a leaf-by-leaf view and the QA report.

**Export** works on the ticked logs (or the shown one), wherever they are:
* **What to write:** v4.0 `.bin`, CSV, full dump CSV and/or raw dump text, each with its own options.
* **Where:** one folder for all, or next to each log in a `converted` subfolder.
* Progress is shown per log. A run log is saved in the export folder.

**Settings** (gear icon): theme (follow Windows, light or dark; ◐ toggles), optional Compare
tolerances, and clearing the recent logs list. They are saved per user in
`%AppData%\TrajectoryLogTools\settings.json`.

The app opens no ESAPI session and needs no Eclipse login or script approval. It only reads and
writes files.

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
| Publish (deploy folder) | Right-click **TrajectoryLogTools.App > Publish** > *Deploy* > **Publish** | **Run Task** > *publish* | `dotnet publish src/TrajectoryLogTools.App -p:PublishProfile=Deploy` or `scripts\publish.ps1` |

Publishing writes **`Deploy\TrajectoryLogTools\`**, one folder (about 180 MB) that holds:

```
TrajectoryLogTools.exe         the application
TrajectoryLogCli.exe           the command-line tool
LaunchTrajectoryLogTools.cs    the Eclipse launcher script
*.dll                          the .NET 10 runtime and libraries - keep everything together
```

It is a self-contained **folder**, not a single-file exe. A single-file WPF app unpacks native
DLLs to `%TEMP%` when it first starts, and application whitelisting on clinical PCs and Citrix
servers often blocks that. To publish straight to a share, use
`scripts\publish.ps1 -DeployDir \\server\ESAPI\TrajectoryLogTools` or the VS Code task *publish to folder*.

The `.vscode` folder has tasks for: build, release build, CLI-only build, clean, rebuild,
publish, publish to folder, tests, tests with coverage, the CLI commands, the round-trip test
and golden-file regeneration. It also has debug configurations (GUI, GUI pre-filled as from
Eclipse, and the CLI commands). In Visual Studio, `Properties/launchSettings.json` adds a
pre-filled GUI profile.

The GUI builds on Linux and macOS too (`EnableWindowsTargeting`), so CI can compile it, but it
runs on Windows only. The libraries, the CLI and the tests run on any OS.

## Launching from Eclipse

Eclipse runs scripts in-process on .NET Framework 4.8, so the launcher stays a 4.8 single-file
script. It starts `TrajectoryLogTools.exe` as a separate process, which is why the application
can be .NET 10.

1. Publish, and copy the whole `Deploy\TrajectoryLogTools` folder to a location the Eclipse
   workstations (or the Citrix servers Eclipse runs on) can read.
2. Edit the constants at the top of `LaunchTrajectoryLogTools.cs`: `ExePath` (required), and
   optionally `StartFolder`, the folder the Open dialogs start in.
3. Copy the script into your Eclipse scripts folder (e.g. `\\<server>\va_data$\ProgramData\Vision\PublishedScripts`)
   and run it from **Tools > Scripts**.

The first time, check the line at the bottom of the window. It shows the Windows version, the
.NET runtime and the folder the app runs from. If the app doesn't start, the usual cause is
application whitelisting (AppLocker / WDAC); ask IT to allow the deployed folder.

The application's own arguments, all optional: log files to open (`.bin` or `.csv`), a folder
to add from, and `--start-folder <folder>` for the Open dialogs. Arguments of earlier versions
(`--input`, `--output`, `--mode`, `--tab`) are still accepted.

## Command-line tool

```
TrajectoryLogCli to-v4   <input.bin | folder> [-o <file|folder>] [--keep-machine-info] [--time-csv] [--drop-couch-rotations] [--axis-scale 1|2|3]
TrajectoryLogCli to-csv  <input.bin | folder> [-o <folder>] [--no-mlc] [--actual-only] [--full]
TrajectoryLogCli dump    <input.bin | folder> [-o <folder>]
TrajectoryLogCli compare-delivery <logA> <logB> [-o <folder>] [--no-plot] [--keep-first-snapshot]
TrajectoryLogCli reconstruct-mlc  <log.bin | log.csv> [-o out.csv] [-t template.csv ...] [--positions actual|expected]
                                  [--axis mu|cp] [--gantry plan|actual] [--include-hold] [--subbeam all|n|n,m]
                                  [--mlc-model SX2] [--scale auto|varian|iec] [--min-gap 0.5] [--report file]
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
launcher/LaunchTrajectoryLogTools.cs   Eclipse script (.NET Framework 4.8, compiled by Eclipse)
tests/TrajectoryLogTools.Tests  xUnit: the converter's 363 tests + analysis, CLI and golden tests
reference/                      the original tools, unchanged, and the golden-file generator
scripts/                        publish.ps1, RoundTripTest.ps1
docs/engine-notes.md            how the engine converts and exports
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
