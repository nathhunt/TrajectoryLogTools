# How the MLC rebuild works

**Rebuild Eclipse MLC** turns a delivery log into Eclipse MLC files (the *MLCPlan serialization* CSV
Eclipse exports and imports for a beam's MLC sequence), one per field. It is the method of the earlier
*LogToEclipseMlc* tool, unchanged; the project's golden tests check that it gives the same numbers.

!!! warning "To be confirmed"
    What the department uses the rebuilt files for, and the Eclipse import procedure.

## The method, per field

1. **Split the fields.** A log may hold several fields (subbeams). Each becomes its own file. The
   control points and MU in such a log run on across fields, so each field's first control point and
   starting MU are subtracted, and the set-up snapshots before it (no dose, leaves and gantry moving to
   the field) are skipped. A Halcyon field starts at its own first control point, after the transition step.
2. **Keep beam-on snapshots only.** Snapshots with the dose servo in *Hold* deliver no dose; during an
   interruption of an arc the machine also steps the control point back. They're left out unless you tick
   **Also use beam-hold snapshots (not recommended)**.
3. **Place each snapshot between control points.** Two ways (**Advanced > Where each sample sits between
   control points**):
    - **From the log's control point** (the default): the control point the log records, which follows
      the planned MU;
    - **From the delivered MU**: the actual MU delivered, mapped onto the plan's MU at each control
      point, that is, where the leaves were when that fraction of the dose was delivered.
4. **Fit the control points.** Eclipse moves every axis linearly in MU between control points. So for
   each leaf, a piecewise-linear curve with its knots on the control points is fitted (least squares)
   to all the beam-on samples; the knots are the rebuilt positions. Interpolating the 20 ms samples at
   each control point instead would cut the corners of the trajectory, by up to about 0.7 mm on
   clinical logs.
5. **Leaf positions: planned or delivered** (**Advanced > Leaf positions**):
    - **Expected (planned)** (the default): the log's expected positions;
    - **Actual (as delivered)**: what the leaves did.
6. **Apply Eclipse's leaf-gap rules.** Gaps under the minimum dynamic leaf gap (0.5 mm for SX2, or
   **Minimum leaf gap**) are closed exactly or opened to the minimum; closed pairs are kept still.
7. **Write the file** in IEC 61217 millimetres, the banks X1/X2 in Eclipse's order. The gantry comes
   from the plan (**Plan (rebuilt exactly)**, the default) or the actual values; jaws are ±140 mm
   (Halcyon has none).

With **Use Eclipse exports**, the file keeps the export's header, meterset weights, jaws, gantry and
collimator, and only the leaf positions are replaced. This is the safest way to get a file Eclipse
accepts back onto the beam; the export is also checked against the log (the *method check*).

## The defaults

The defaults (planned positions, placed by the log's control point) changed from LogToEclipseMlc's
(actual positions, delivered-MU axis). The *Advanced* summary on the page always says which method is
used.

!!! warning "To be confirmed"
    The rationale for these defaults in the department's workflow.

## Limitations

- **Only the Halcyon / Ethos SX2 MLC** (dual layer, 28 distal and 29 proximal pairs). Logs of other MLCs
  are listed under **Not included**.
- **Sampling.** Snapshots are every 20 ms; between them the fit assumes linear motion.
- **No data between some control points** (for example a very short segment, or an interruption): the
  positions there are interpolated, and the field is **Check first**.
- **The gantry of an arc** is rebuilt exactly only when it's evenly spaced; otherwise it's rounded to
  0.01°, and the field is **Check first**.
- **Without an export**, the header, meterset weights and jaws are rebuilt from the log, not copied from
  the plan.
- The rebuilt file describes the leaves; it is not a dose calculation or a check of one.

See [Rebuild statuses and checks](../reference/rebuild-statuses.md) for what each status means.
