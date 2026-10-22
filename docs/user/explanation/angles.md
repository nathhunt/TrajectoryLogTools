# IEC 61217 and Varian Standard

Gantry and collimator angles can be numbered on two scales.

- **IEC 61217** is the international standard for the coordinate systems and scales of radiotherapy
  equipment[^iec]. Eclipse and DICOM RT plans use it.
- **Varian Standard** (the machine scale) is Varian's own scale. Halcyon and Ethos logs record their
  angles in it: the log header's *axis scale* says which (axis scale 2 is IEC 61217; 1, 3 and CSV exports
  are Varian Standard).

The two are related by

> IEC = (180 − Varian) mod 360

so the zero is 180° apart and the direction is reversed. The relation is its own inverse. For example,
a gantry at Varian 180° is IEC 0° (beam from above), and Varian 60° is IEC 120°. A difference between
two angles (A − B, or actual − expected) keeps its size and changes sign.

This is the relation the MLC rebuild has always used, checked against Eclipse exports of the same beams.

## What the app shows

Trajectory Log Tools converts gantry and collimator angles to the scale of the **Angles** setting
(IEC 61217 by default) wherever it shows them: the charts, the tables, the cards. Headings say which scale.

Couch rotation, pitch and roll, and couch and jaw positions, are shown **as recorded**: their relations
between the two scales differ by axis and haven't been checked against data. (Halcyon has no jaws and no
couch rotation.)

## What files keep

The setting only changes what you see:

- exports (v4.0, CSV, dumps) keep the log's values, in the log's scale;
- Eclipse MLC files are always IEC 61217, as Eclipse expects;
- a Compare report's text is in the log's own scale, and says so; its figure follows the setting.

[^iec]: IEC 61217:2011, *Radiotherapy equipment – Coordinates, movements and scales*. International
    Electrotechnical Commission.
