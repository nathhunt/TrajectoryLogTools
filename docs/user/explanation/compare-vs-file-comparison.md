# Compare and File comparison

Trajectory Log Tools has two ways to compare two logs. They answer different questions.

| | **Compare** (delivery comparison) | **File comparison** |
|---|---|---|
| Question | How differently did the machine deliver these? | Do these two files hold the same values? |
| Typical use | Two fractions of a plan; a delivery and a re-delivery | A v4.0 conversion against its v5.1 original |
| Compares | Gantry, MU and every MLC leaf, expected and actual | Every header field, metadata line, subbeam, and every stored value of every axis |
| Samples | Beam on only, matched by **control point** | Every snapshot, matched by **position** in the file |
| Timing, holds, sampling | Don't matter | Every difference counts |
| Result | Max, RMS, percentiles of A − B; maps and charts; optional tolerances | A count of differences, and where they are |
| Works on | `.bin` logs and CSV exports | `.bin` logs only |

## Compare

Two deliveries are never sampled at the same moments: one may start later, pause, or run at another
speed. Compare therefore takes only the beam-on samples of each and lines them up by control point,
the plan's own progress through the field. At each point it takes A − B of the expected values (are
the plans the same?) and of the actual values (did the machine do the same?). That is why the page
first says whether the plans differ: if they do, the actual differences include the plan differences.

## File comparison

A conversion should change nothing but what the format requires. File comparison reads both files and
compares them value by value, exactly (no tolerance). With **Ignore what differs between v4.0 and v5.1 by
design**, it leaves out the changes a conversion makes on purpose (the version, the time axis, the axis
order, the machine specifier and serial number), so a correct conversion reads *Same content*.

See [Validate a v4.0 conversion](../how-to/validate-conversion.md).
