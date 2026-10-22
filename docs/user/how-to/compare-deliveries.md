# Compare two deliveries

Compare two deliveries, usually two fractions of the same plan, matched by control point.

1. Open both logs (see [Open logs](open-logs.md)).
2. Do one of these:
    - In **Open logs**, tick the two logs and select **Compare**.
    - On a log's page, select **Compare with…** and pick the other log.
3. Wait for the progress bar to finish. The **Compare deliveries** page shows:

    ![The Compare deliveries page](../images/compare-page.png){ .shot }

    - (2) **A** and **B**: patient, plan, date and file of each;
    - (3) what was compared: the same plan twice, or plans that differ;
    - (4) the MLC, gantry and MU differences, and the interruptions in each.

4. To change a log, select **Change…** next to it, or drop a log from File Explorer onto **A** or **B**.
5. To swap them, select **Swap A / B**. Differences are A − B, so their sign changes.
6. To go back to the log, select **←**.

!!! warning "Different patients"
    If A and B name different patients, a red warning says so at the top of the results.

## Next steps

- Open the details further down the page: the planned leaf differences, per field, beam holds, all
  statistics and the full report. See [Compare cards and statistics](../reference/compare.md).
- [Save a comparison report](save-comparison-report.md).
- Mark the differences as within or over tolerance: [Set Compare tolerances](set-tolerances.md).

!!! warning "To be confirmed"
    The department's Compare tolerances, if any, and when to compare deliveries.
