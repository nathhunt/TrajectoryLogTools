# Reference: the original tools

The three tools Trajectory Log Tools replaces, kept **unchanged** so the ports can be checked
against them:

| File | Ported to |
|---|---|
| `compare_trajectory_logs.py` | `src/TrajectoryLogTools.Analysis/DeliveryComparison.cs` (+ `DeliveryCharts.cs` for the figure) |
| `LogToEclipseMlc/LogToEclipseMlc.cs` | `src/TrajectoryLogTools.Analysis/MlcReconstruction.cs` |
| `TrajectoryLogDump/TrajectoryLogDump.cs` | `src/TrajectoryLogTools.Core/RawDump.cs` |

The converter engine itself (`HalcyonTrajectoryLogTool.cs` in nathhunt/TrajectoryLogConverter)
was moved into `src/TrajectoryLogTools.Core` and keeps its own unit tests.

None of this folder is part of the solution or the deployed application.

## Golden tests

`tests/TrajectoryLogTools.Tests/Golden` holds what the original tools produced for four simulated
deliveries (two VMAT, two static-field IMRT; built by `TestSupport/DeliverySimulator.cs`, no
patient data). The golden tests run the ports on the same logs and require:

* **Delivery comparison:** every number within 1e-9 (relative) of the Python script's, and the
  CSV files written identically (`delivery/*.json`, `delivery/*.csv`).
* **MLC reconstruction:** the MLC files and QA reports identical line by line, apart from the
  export date and file paths, for five option combinations, including failing subbeams
  (`mlc/<case>/`).
* **Raw dump:** the dump text identical to TrajectoryLogDump's (SHA-256 in `dump_sha256.txt`).

To rebuild the golden files, which you need to do only if the simulated deliveries change, run:

```
bash reference/golden/regenerate.sh
```

This needs the .NET 10 SDK and Python 3 with pandas, numpy and matplotlib (on Windows: Git Bash or WSL).
