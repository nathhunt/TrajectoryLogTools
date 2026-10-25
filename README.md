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
(see [below](#trajectory-log-converter)). It uses the same conversion code.

## The application

The window has two parts: **Open logs** on the left and the selected log (or what you're doing
with it) on the right.

**Open logs.** Logs are opened from anywhere and each keeps its own location, so two logs from
different folders or drives sit side by side. No folder is remembered as a default.
* **Ways to open:**
  * **Open** (Ctrl+O) picks one or more files;
  * the **▾** menu has **Add from folder…** and **Recent logs**. Add from folder can include
    subfolders and filter by when the log was treated:
    * a **period**: from a start date and time to an end date and time (e.g. 20/10/2026 11:00 to
      21/10/2026 14:30; the end minute is included);
    * **times of day**, on every day (within the period when both are ticked), e.g. 08:00 to
      12:00 for the morning sessions; 22:00 to 02:00 runs past midnight;
  * you can drag files or folders anywhere onto the window;
  * the Eclipse launcher, or a file dragged onto the exe, opens a log straight away.
* **Each entry** shows the plan name and **patient ID** (from the log's metadata), fields,
  treatment date, version and folder. The filter matches the patient ID too.
  The patient ID is also shown on the log page, on both sides of Compare (with a warning when
  A and B are different patients), on the Rebuild page and its deliveries, in Export and in the
  details windows. Badges mark **Interrupted** deliveries and logs whose **CRC failed** (still
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

From a log: **Compare with…** (another log, or the plan's DICOM RT Plan), **Rebuild Eclipse MLC…** and **Export…**.

**Compare** (two ticked logs, or *Compare with…*) works answer first:
* it says what was compared (same plan delivered twice, or plans that differ);
* cards show the MLC, gantry and MU differences, marked within or over your tolerances if you set
  them in Settings;
* below are the leaf-difference map and the gantry and MU charts, then folded details: planned
  differences, per field, beam holds, all statistics, the full report;
* the gantry and MU charts share one legend, above them, and zoom and pan separately;
* **File differences (validation) → Compare the files…** opens the **File comparison** window,
  which compares every stored value, for example to check a v4.0 conversion against its original.
  At the top: what is compared, the verdict and one row of counts (header, metadata, subbeams,
  axes, stored values), and what was ignored. Below, tabs that fill the window:
  * **Axes:** every axis (how many values differ, expected / actual, in how many snapshots, the
    largest difference, values that are "no value" in one log only, and where it starts) and,
    under it, the values that differ on the axis picked;
  * **Header and subbeams:** the fields that differ, A and B side by side, one row per metadata
    line (Patient ID, Plan Name, …);
  * **As text:** what the command line prints.

The **←** button goes back to the log. You can drop a log onto A or B to replace it. **Save report…** writes the report, tables and figure.

**Compare with plan.** B can also be the plan's **DICOM RT Plan** (`RP….dcm`, as exported from
Eclipse / ARIA): pick it with *Compare with…* on a log, with *Change…* on B, or drop it on B (or
on the window while a log is shown). A DICOM file is recognised by its content, with or without
the `.dcm` extension. The plan is read for that comparison only: it is not added to Open logs or
Recent logs. Each field of the log is paired with a treatment beam of the plan by name (else by
number of control points and the plan values that fit best); set-up (kV) beams are ignored, and
plan beams not in the log are listed (usual when each field has its own log). The page answers
three questions:
* **Is it this plan?** Patient ID and plan UID (the log records the plan's SOP Instance UID): a red
  warning when they differ.
* **Did the machine get the plan as approved?** The *plan check* compares the log's planned
  (expected) leaf positions, gantry, collimator and MU with the plan. The machine's planned
  trajectory is the plan's straight lines except that it rounds the corners where a leaf turns
  back, so the check uses the snapshots in the middle of each control-point interval; there they
  match the plan to within 0.001 mm, and a change of 0.1 mm to one control point of the plan is
  found. *Differs* means another plan or plan version, or a transfer problem.
* **How accurately was it delivered?** The plan gives values at its control points and moves every
  axis linearly in MU between them, so it is known at every moment. The cards compare **every
  beam-on snapshot** with the plan at the MU delivered so far (MLC max, RMS and 95th percentile;
  gantry; MU delivered vs planned), against the tolerances in Settings if set. The leaf map shows
  actual − plan **at each control point** (actual positions from the least-squares fit the MLC
  rebuild uses), with the gantry and cumulative MU differences beside it. Beam holds are left out.

Folded below: the per-field table (control points covered, MU plan / log planned / delivered,
gantry, collimator, plan check, MLC and gantry statistics), the plan-check map and the full
report. A log that covers part of a beam (an interrupted delivery resumed in another log) is
aligned on the plan control points it covers and marked *partial*. **Save report…** writes
`plan_comparison_report.txt` and, per field, a table and a figure by control point. Supported:
photon RT Plans for the Halcyon / Ethos SX2 MLC (as the rest of the app).

**Rebuild Eclipse MLC** writes, for each field, the Eclipse MLC file of the leaf positions as
delivered. It works on the ticked logs (**Rebuild MLC…** under the list), or on the shown log
(**Rebuild Eclipse MLC…** on its page; if that log is ticked, all the ticked logs).
1. The fields are found automatically and listed by delivery: one box per plan and day, its
   fields in the order delivered. A plan delivered as one log per field and a plan delivered as one
   log with several fields look the same; each field says where it came from ("Own log" or
   "Field 2 of 3 in one log", the time and the file). A field found in two logs of one delivery,
   usually one resumed after an interruption, is flagged; each part gets its own file. Logs that
   can't be rebuilt are listed under **Not included** with the reason. Untick any field you don't
   want, or a whole delivery with its box's tick.
2. Choose where to save: next to each log, or one other folder (refused if two logs have the same
   file name, since their files would overwrite each other).
3. **Advanced** (folded) holds the method options. The defaults are the log's **expected
   (planned)** leaf positions, placed between control points by the **log's control point**;
   actual (as delivered) positions and the delivered-MU axis are the alternatives. **Use Eclipse exports**,
   off by default, makes the file keep an Eclipse export's header, meterset weights, jaws and
   angles; the exports are matched to the fields by control points and gantry angle.
4. **Also save each field's QA report** (off by default) writes `_report.txt` beside each MLC
   file; the report is in **Details…** either way.

Each field gets a status:
* **Ready to import:** no leaf-gap problem left, gantry rebuilt cleanly, every control point had
  data;
* **Check first:** written, with the reasons listed;
* **Failed:** nothing written, with the reason.

Each result shows delivered vs planned (for information), the leaf-gap rules and the gantry.
**Details…** opens the field in a window of its own: status and what to check, the QA report's
numbers as cards (delivered vs planned, leaf-gap rules, gantry, control points, MU, snapshots
used, how it was built, and the Eclipse export check when one was used), then the difference map,
a leaf-by-leaf view and the full report. **‹ ›** step through the fields of the rebuild.

**Export** works on the ticked logs (or the shown one), wherever they are:
* **What to write:** v4.0 `.bin`, CSV, full dump CSV and/or raw dump text, each with its own options.
* **v4.0 header** (every log): machine **TrueBeam (0)** (default) or **Halcyon (1)**; serial
  number **removed** (default), kept or changed; couch rotation, pitch and roll **removed**
  (default) or kept.
* **Header details…** (one v5.1 log only) changes the patient ID, plan name, plan UID and field
  (beam / subbeam) names written to the v4.0 file. Each shows the value in the log beside the one
  to write; anything not changed is copied exactly. Renaming field 1 also changes the header's
  BeamName line, which holds the first field's name. The original log is never changed, and the
  CSV and dump exports are written from it.
* **Where:** one folder for all, or next to each log in a `converted` subfolder.
* Progress is shown per log. **Also save a run log** (off by default) writes
  `TrajectoryLogConversion_<date_time>.log` in each output folder.

**Scrolling and zooming.** Wherever the mouse is, exactly one thing scrolls:
* on a page (log, compare, rebuild) and in the Export and Header details windows, tables and
  texts are shown at full length with no scroll bar of their own, so the wheel always scrolls
  the page, whether the mouse is over a table or not;
* in the File comparison window, the tables fill the window and nothing around them scrolls, so
  the wheel scrolls the table under the mouse (drag the bar between the two tables to share the
  room differently);
* **Shift + wheel** scrolls a table that is wider than its space sideways;
* **Ctrl + wheel** zooms a chart on a page (the page doesn't move); drag pans, double-click goes
  back to the full view. Right-click → *Open in New Window* shows a chart in a window of its own,
  where the wheel alone zooms (as in the Rebuild details window).

**Angles: IEC 61217 or Varian Standard.** The **Angles** drop-down at the top of the window (also
in Settings) sets how gantry and collimator angles are shown: in the axis charts, the field and
beam-hold tables, the Compare charts and tables, the Rebuild page and the details windows.
**IEC 61217 is the default.** A log stores its angles in the scale its header names (Halcyon:
machine scale, i.e. Varian Standard), and the two are related by IEC = (180 − Varian) mod 360,
the direction reversed, so a difference only changes sign. This is the relation the MLC rebuild
has always used, checked against Eclipse exports. Couch rotation, pitch, roll and couch / jaw
positions are shown as recorded (labelled so). Nothing written to files changes: exports keep
the log's values, Eclipse MLC files are always IEC 61217, and Compare's report text is in the
log's own scale (its header says which); Save report…'s figure follows the setting.

**Dates** are shown and written in the New Zealand format, dd/MM/yyyy with 24-hour times,
whatever the PC's regional settings (the date pickers too).

**Errors** are logged in full to `%LocalAppData%\TrajectoryLogTools\errors.log`; the message
shown names the file.

**Settings** (gear icon): theme (light by default, dark, or follow Windows; ◐ toggles), applied when you press Save, optional Compare
tolerances, and clearing the recent logs list. They are saved per user in
`%AppData%\TrajectoryLogTools\settings.json`.

The app opens no ESAPI session and needs no Eclipse login or script approval. It only reads and
writes files.

## Trajectory Log Converter

`TrajectoryLogConverter.exe`, started from Eclipse by `LaunchTrajectoryLogConverter.cs`, converts
v5.1 logs to v4.0 and nothing else:
* **From:** the logs in the default input folder (optionally with subfolders), filtered by when
  they were treated, as in Add from folder: a period (start date and time to end date and time)
  and / or times of day on every day. Or **one file**, chosen with *Choose file…*.
* **The list** shows, compactly, every log that will be converted (treated, patient · plan ·
  fields, file, subfolder), each with a tick to leave it out, and a count. Logs that won't be
  converted are hidden unless asked for, with the reason: already v4.0, already in the output
  folder, same file name as another log in the list, can't be read, or the output folder is
  the log's own folder.
* **To:** the default output folder, each log keeping its file name. *Replace logs already in
  the output folder* is off by default.
* **What is written:** the v4.0 log only, always the same way: machine specifier TrueBeam (0),
  serial number removed, couch axes kept, everything else in the header (patient, plan, field
  names, metadata) as it is. No time-axis file, CSV or run log.

**Settings** holds the default input and output folders (and *include subfolders*). They are
saved in `TrajectoryLogConverter.settings.json` **in the app's folder**, so everyone using that
copy gets the same folders; whoever changes them needs write access to the folder. A folder
chosen in the main window is for that session only (*Back to the default folder* returns).
Publishing doesn't overwrite the settings file. Example:

```json
{
  "InputFolder": "\\\\server\\Halcyon\\TrajectoryLogs",
  "OutputFolder": "\\\\server\\Halcyon\\TrajectoryLogs v4",
  "IncludeSubfolders": false
}
```

It is published into its own folder, `Deploy\TrajectoryLogConverter\`, which has its own runtime
and shares nothing with Trajectory Log Tools. Publishing Trajectory Log Tools publishes it too. To
publish only the converter: `dotnet publish src/TrajectoryLogTools.Converter -p:PublishProfile=Deploy`.

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
| Publish (deploy folder) | Right-click **TrajectoryLogTools.App > Publish** > *Deploy* > **Publish** | **Run Task** > *publish* | `dotnet publish src/TrajectoryLogTools.App -p:PublishProfile=Deploy` or `scripts\publish.ps1` |

Publishing writes two separate folders. Each is self-contained (its own copy of the .NET 10
runtime), so either one can be deployed, moved or removed without the other:

```
Deploy\TrajectoryLogTools\             (about 180 MB)
  TrajectoryLogTools.exe              the application
  TrajectoryLogCli.exe                the command-line tool
  LaunchTrajectoryLogTools.cs         the Eclipse launcher script for the application
  *.dll                               the .NET 10 runtime and libraries - keep everything together
Deploy\TrajectoryLogConverter\         (about 160 MB)
  TrajectoryLogConverter.exe          the v5.1 -> v4.0 converter
  LaunchTrajectoryLogConverter.cs     the Eclipse launcher script for the converter
  TrajectoryLogConverter.settings.json  its default folders (written by Settings, kept on redeploy)
  *.dll                               its own .NET 10 runtime and libraries
```

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
4. For the converter, the same with `LaunchTrajectoryLogConverter.cs` (its `ExePath` points to
   `TrajectoryLogConverter.exe` in the deployed `TrajectoryLogConverter` folder, copied whole like
   the first). Then open it once and set the default folders in **Settings**.

The first time, check the line at the bottom of the window. It shows the app's version, the Windows version, the
.NET runtime and the folder the app runs from. If the app doesn't start, the usual cause is
application whitelisting (AppLocker / WDAC); ask IT to allow the deployed folder.

The application's own arguments, all optional: log files to open (`.bin` or `.csv`), a folder
to add from, and `--start-folder <folder>` for the Open dialogs. Arguments of earlier versions
(`--input`, `--output`, `--mode`, `--tab`) are still accepted.

## Command-line tool

The first five commands do what the app does, with the same options, defaults and output files
(they call the same code):

```
TrajectoryLogCli export <log | folder> ... [-o <folder>] [--v4] [--csv] [--full-csv] [--dump]
                        [--machine truebeam|halcyon] [--serial remove|keep] [--new-serial <text>] [--couch remove|keep] [--save-time]
                        [--patient-id <text>] [--plan-name <text>] [--plan-uid <text>] [--field-name N=NAME ...]   (one log)
                        [--no-mlc] [--actual-only]
TrajectoryLogCli compare-deliveries <logA> <logB> [-o <folder>] [--no-plot]
TrajectoryLogCli compare-plan <log.bin | log.csv> <plan.dcm> [-o <folder>] [--no-plot]
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
* `compare-plan` works as the Compare page with a plan as B: it prints the report, and with `-o`
  also writes the report, a table and a figure per field. Exit code 1 when a field can't be
  compared, the plan check differs, or the patient or plan UID differ.

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
  TrajectoryLogTools.Analysis   DeliveryRecord, DeliveryComparison, MlcReconstruction, TemplateMatcher,
                                RtPlan (DICOM, via fo-dicom) and PlanComparison (net10.0 + net48)
  TrajectoryLogTools.Charts     ScottPlot charts shared by the GUI and the CLI
  TrajectoryLogTools.Cli        TrajectoryLogCli.exe
  TrajectoryLogTools.App        TrajectoryLogTools.exe (WPF, Fluent theme, MVVM)
  TrajectoryLogTools.Converter  TrajectoryLogConverter.exe (WPF): v5.1 -> v4.0 from default folders
  TrajectoryLogTools.Wpf        what the two apps share: styles, converters, time picker, log filter
                                panel, dialogs, error log, NZ dates, wheel scrolling
launcher/                       Eclipse scripts (.NET Framework 4.8, compiled by Eclipse), one per app
tests/TrajectoryLogTools.Tests  xUnit: the converter's 363 tests + analysis, CLI and golden tests
reference/                      the original tools, unchanged, and the golden-file generator
scripts/                        publish.ps1, RoundTripTest.ps1, make_icons.py
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
