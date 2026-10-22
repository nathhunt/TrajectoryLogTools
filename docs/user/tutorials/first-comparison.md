# Your first delivery comparison

In this tutorial you compare two deliveries of the same plan, for example two fractions, and read
how much they differ.

You need: two logs of the same plan.

## 1. Open both logs

1. Select **Open files…**, select both logs (hold ++ctrl++ to select more than one) and select **Open**.
2. In **Open logs**, tick both logs. The footer shows *2 selected*.

![Open logs with two logs ticked](../images/open-logs-list.png){ .shot }

## 2. Compare them

Select **Compare** (under the list). The **Compare deliveries** page opens.

![The Compare deliveries page](../images/compare-page.png){ .shot }

At the top (2) are **A** and **B**: the patient ID, plan and date of each log, and its file. If A and B
are different patients, a red warning says so.

## 3. Read the answer

The first box (3) says what was compared:

- *Same plan, delivered twice*: the planned positions are identical, so the differences are the
  machine's.
- *The plans differ*: the planned positions differ too; the delivered differences include them.

The cards (4) give the size of the differences:

![The verdict and the cards](../images/compare-cards.png){ .shot }

- **MLC actual, A − B**: the largest leaf difference, the RMS, and the 95th percentile.
- **Gantry actual, A − B** and **MU actual, A − B**: the largest difference and the RMS.
- **Interruptions** in A and in B. Matching by control point removes their effect.

If tolerances are set in [Settings](../how-to/set-tolerances.md), each card says *Within* or *Over*
and turns green or red. In the example the tolerances are 1 mm, 0.5° and 0.5 MU.

## 4. Look at the charts

Below the cards:

- the **leaf difference map**: actual A − actual B for every leaf and control point;
- the **gantry** and **MU** differences against control point, actual (orange) and expected (blue).

The comparison uses only beam-on samples, matched by control point, so timing, beam holds and
sampling don't affect it.

## 5. Open the details

Further down, folded, are the planned leaf differences, a table per field, the beam holds of A and
B, every statistic by axis, and the full report. See
[Compare cards and statistics](../reference/compare.md).

## What you've learnt

You compared two deliveries and read the verdict, the cards and the charts. Next, you might:

- [Save a comparison report](../how-to/save-comparison-report.md);
- [Set Compare tolerances](../how-to/set-tolerances.md);
- learn [what Compare compares](../explanation/compare-vs-file-comparison.md).
