# Engine notes

How the conversion engine (`src/TrajectoryLogTools.Core`) treats Halcyon / Ethos trajectory logs.
Carried over from the TrajectoryLogConverter source. The engine follows the Varian specifications:

* *Halcyon and Ethos Radiotherapy System Trajectory Log File Specification* P1069495-001-A (May 2025): version 4.0 layout
* P1069495-002-B (May 2026): version 5.1 layout

```text
What to-v4 changes (per P1069495-002-B, "Introduction"):
  1. Version string "5.1" -> "4.0".
  2. Time axis (enum 43) removed from the axis enumeration, samples-per-axis arrays, axis
     count, and from every snapshot.
  3. Axes put in the order v4.0 machine logs use: Coll, Gantry, Y1, Y2, X1, X2, couch (6 - 11),
     MU, BeamHold, ControlPoint, MLC. HAL 5.0 writes Time, ControlPoint, MU, BeamHold, Gantry,
     Coll, ... instead; the spec does not give either order, so readers that assume the v4.0
     order would otherwise mislabel the axes.
  4. Machine specifier (1 byte) + serial number (6 bytes) are zeroed unless
     --keep-machine-info. The 5.1 spec lists them as new in 5.1, although the header table
     of the 4.0 spec (P1069495-001-A) also has them.
  5. Axis scale copied unchanged (5.1 writes 3, which is also a legal 4.0 value).
  Header rebuilt, subbeams copied verbatim, CRC recomputed.

What to-v5 changes (the reverse of to-v4; for validation and testing):
  1. Version string "4.0" -> "5.1".
  2. Time axis (enum 43, 1 sample) added, and the axes put in the order HAL 5.0 writes them:
     Time, ControlPoint, MU, BeamHold, Gantry, Coll, Y1, Y2, X1, X2, couch (6 - 11), MLC.
     The spec does not give the order; it is taken from machine logs. v4.0 logs do not record
     clock time, so it is either restored from a to-v4 "_time.csv" (exact round trip) or
     generated as start time + snapshot index x sampling interval. Per the spec the time is
     seconds since midnight in the "expected" record and the "actual" record is empty, so
     generated times write 0 as the actual value. Generated times leave out beam pauses.
  3. Machine specifier + serial number bytes copied as they are. If the specifier is 0
     (TrueBeam) and the serial number is empty - i.e. zeroed by to-v4 - the specifier is set to
     1 (Halcyon / Ethos). --serial sets the serial number.
  4. Axis scale copied unchanged unless --axis-scale (HAL 5.0 writes 3; the axis values are
     NOT converted, so a v4.0 log with axis scale 1 or 2 keeps that value and gets a warning).
  Header rebuilt, subbeams copied verbatim, CRC recomputed.

CSV notes:
  * Values are in the axis scale given in the header: cm for linear axes, degrees for
    rotations, MU for dose. Axes that hold the "no value" marker (float max) in every
    snapshot - the jaws, which Halcyon does not have - are left out (and listed in the
    console output); any single "no value" sample elsewhere is written as an empty cell.
  * Control point and time have no separate expected/actual value, so they get one column.
  * Time: v5.1 logs record clock time (seconds since midnight); v4.0 logs do not, so the
    elapsed time is computed as snapshot index x sampling interval (beam pauses are not
    recorded, so this can under-count real time).
  * MLC: 2 bank values followed by 114 leaves. The spec does not define the leaf order;
    in logs examined, leaves 1-57 are one bank and 58-114 the other (leaf n faces leaf n+57),
    with the first 29 and last 28 of each bank behaving as separate layers. Columns are
    therefore numbered by position in the file (Leaf001..Leaf114) rather than named.

Full dump (to-csv --full):
  Nothing is converted, reordered, scaled, rounded or left out. Sections, in file order:
  header fields with their byte offsets (axis enumeration and samples per axis listed one axis
  per row, metadata one text line per row), subbeams, snapshots, CRC. Every snapshot value -
  expected and actual, every sample of every axis, including the "no value" marker
  3.40282347E+38 - is written with 9 significant digits, which round-trips a float exactly.
  Snapshot columns are named AxisName(axis id)[sample]_Exp / _Act. Reserved / padding bytes
  are shown as hex. Text in (brackets) is an annotation, not file content.
```

## Beam on

The analyses (delivery comparison, MLC reconstruction, viewer charts) count a snapshot as **beam on**
unless the dose servo state is **Hold** (2). The original Python comparison counted only *Normal* (0)
and LogToEclipseMlc only `BeamHold_Actual == 0`. These rules differ only for *Freeze* (1) and
*Disabled* (3). The trajectory log specification's dose servo table (TrueBeam P1012906-005-E,
"Dose Servo States", which lists the same four states) says the MV beam is still delivered in
both, and that they occur only in Service mode, so clinical logs are not affected.
