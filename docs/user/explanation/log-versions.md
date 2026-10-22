# Log versions 4.0 and 5.1

Halcyon and Ethos write trajectory logs in a binary format that Varian specifies:

- **version 4.0**: *Halcyon and Ethos Radiotherapy System Trajectory Log File Specification*, P1069495-001-A;
- **version 5.1** (Halcyon 5.0 and later): P1069495-002-B.

!!! warning "To be confirmed"
    The department's machines (Halcyon and/or Ethos) and their software versions.

Both have the same layout: a 1024-byte header (signature, version, sampling interval, the axes recorded,
the axis scale, the subbeams, the MLC model, metadata such as the patient ID and plan name), one record
per subbeam (field), then one snapshot every 20 ms with an expected and an actual value of every axis,
and a CRC checksum.

## What 5.1 adds

- A **time axis**: the clock time of each snapshot (seconds since midnight). v4.0 logs don't record it.
- The axes in another order: time, control point, MU, beam hold, gantry, collimator, and so on.
- The **machine specifier and serial number** in the header.

Some software reads only v4.0 logs. That is why the apps can convert.

!!! warning "To be confirmed"
    Which software reads the v4.0 logs in the department, and why it needs v4.0.

## What conversion to v4.0 changes

Export (with **v4.0 log**) and Trajectory Log Converter make a v4.0 log from a v5.1 log by:

1. setting the version to 4.0;
2. removing the time axis from the header and from every snapshot (Export can keep it in `_time.csv`);
3. putting the axes in the order v4.0 logs use: collimator, gantry, jaws, couch, MU, beam hold, control
   point, MLC;
4. writing the machine specifier (TrueBeam, 0, by default) and removing the serial number by default;
5. removing couch rotation, pitch and roll if asked (Export removes them by default; the converter keeps them);
6. optionally, changing the patient ID, plan name, plan UID or field names ([Header details](../how-to/export-header-details.md)).

Everything else is copied: the subbeams, every expected and actual value, the metadata. The CRC is
recomputed. Converting back with the command-line tool gives the original byte for byte, which the
project's round-trip test checks.

To check a conversion, see [Validate a v4.0 conversion](../how-to/validate-conversion.md).
