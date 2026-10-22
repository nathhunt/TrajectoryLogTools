# Files and folders

The apps only read the files you open and write the files you ask for. They never change or delete a log.

## What the apps read

| File | Read by |
|---|---|
| Trajectory logs, `.bin` (v4.0, v5.1) | Both apps |
| CSV exports of a log (`.csv`) | Trajectory Log Tools: view, Compare, Rebuild (not File comparison or Export) |
| Eclipse MLC exports (`.csv`, *MLCPlan serialization*) | Rebuild, with **Use Eclipse exports** |

## What the apps write

| File | Where | When |
|---|---|---|
| v4.0 logs, CSV, `_full.csv`, `.txt` dumps, `_time.csv` | The folder you choose, or `converted\` beside each log | Export |
| `TrajectoryLogConversion_<date_time>.log` | Each export folder | Export, only with **Also save a run log** |
| `<log>[_subN]_reconstructed_MLC.csv` | Beside each log, or the folder you choose | Rebuild |
| `<…>_reconstructed_MLC_report.txt` | Beside each MLC file | Rebuild, only with **Also save each field's QA report** |
| `report.txt`, `axis_summary.csv`, `per_leaf_A_minus_B.csv`, `per_beam_A.csv`, `per_beam_B.csv`, `beam_holds_A.csv`, `beam_holds_B.csv`, `expected_vs_actual_comparison.png` | The folder you choose | Compare > **Save report…** |
| v4.0 logs | The converter's output folder | Trajectory Log Converter |

## The apps' own files

| File | What | Per |
|---|---|---|
| `%AppData%\TrajectoryLogTools\settings.json` | Trajectory Log Tools' settings and recent files | Windows user |
| `%LocalAppData%\TrajectoryLogTools\errors.log` | Errors, with full details (both apps) | Windows user |
| `TrajectoryLogConverter.settings.json`, beside `TrajectoryLogConverter.exe` | The converter's default folders | Deployed copy (every user) |
| `Help\` beside each exe | This guide | Deployed copy |

## Dates and times

Treatment dates come from the `_yyyyMMddHHmmss` time stamp in a log's file name (the machine names
them so), or the file's modified time. Every date the apps show or write is in the New Zealand format,
dd/MM/yyyy, with 24-hour times.
