# What's new

## 1.0.0-beta

The first release of **Trajectory Log Tools** and **Trajectory Log Converter**.

!!! warning "To be confirmed"
    The release date, and whether this release is for testing only.

### Trajectory Log Tools

One app in place of four earlier tools: the *TrajectoryLogConverter* GUI and *HalcyonTrajectoryLogTool*,
*TrajectoryLogDump*, `compare_trajectory_logs.py` and *LogToEclipseMlc*.

- **Open logs** from any folders, with **Add from folder** (subfolders, a period, times of day), drag and
  drop, and **Recent logs**. Patient IDs everywhere a log appears; **Interrupted** and **CRC failed** badges.
- **The log page**: key facts, the delivery timeline, fields and beam holds, every axis, the MLC error map,
  and the raw file with find.
- **Compare** two deliveries straight from the `.bin` logs, answer first, with optional tolerances, and
  **Save report…**.
- **File comparison**: every stored value of two logs, for checking a v4.0 conversion.
- **Rebuild Eclipse MLC**: any number of logs, fields found and grouped by delivery automatically, a status
  per field (*Ready to import*, *Check first*, *Failed*), a details window, and Eclipse exports matched
  automatically. Defaults: planned positions, placed by the log's control point.
- **Export** to v4.0, CSV, full dump CSV and raw dump, with header options and **Header details…**.
- **Angles** in IEC 61217 (default) or Varian Standard.
- New Zealand dates (dd/MM/yyyy, 24-hour); light theme by default; run logs and report files only when asked.
- **Help** and ++f1++ open this guide.

### Trajectory Log Converter

- Converts v5.1 logs to v4.0 from a default input folder to a default output folder, filtered by
  treatment date and time, or one file. Always the same options. Shows why a log is left out.

### Changes from the original tools

- *Beam on* means the dose servo isn't in *Hold* (this differs only for the service-mode states).
- The MLC rebuild reads the angle scale from the log header and refuses MLCs it doesn't know instead of
  assuming SX2.
- The comparison figure is drawn by the app, with its colour scale fitted to the data.
