# Intended use and limitations

!!! warning "To be confirmed"
    **Intended use statement.** The decisions the apps are used for in the department (for example
    delivery QA, rebuilding the delivered MLC for a dose recalculation, converting logs for a named
    downstream system), by whom, and what they must **not** be used for. This page is a placeholder
    until the department supplies it.

## What the apps do

- Read Halcyon and Ethos trajectory logs (versions 4.0 and 5.1), and CSV exports of them.
- Show what a log records, compare two deliveries, compare two files, rebuild Eclipse MLC files from a
  delivery, and write logs in other formats (v4.0, CSV, dumps).

## Limitations

- **Machines.** Halcyon and Ethos logs only. TrueBeam logs aren't supported.
- **MLC.** The MLC rebuild supports only the Halcyon / Ethos SX2 MLC.
- **What a log can tell you.** A trajectory log records what the machine's control system measured and
  intended, every 20 ms. It isn't an independent measurement of the delivery and doesn't record dose.
- **Comparisons** use the log's own expected and actual values; tolerances are yours to set and are
  shown only as *Within* or *Over*.
- **Rebuilt MLC files** are built from the log, with the checks in
  [Rebuild statuses and checks](../reference/rebuild-statuses.md). A file marked **Check first** needs
  review before use. The app doesn't import files into Eclipse.
- **Angles** are converted between IEC 61217 and Varian Standard for gantry and collimator only.
- **Version 1.0.0-beta.** See [Validation and commissioning](validation.md) for what this version is
  cleared for.
