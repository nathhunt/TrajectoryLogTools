# Save a comparison report

Save a comparison of two deliveries to a folder, as files you can keep or attach to a record.

1. [Compare the two deliveries](compare-deliveries.md).
2. Select **Save report…** (5).

    ![Save report… on the Compare page](../images/compare-page.png){ .shot }

3. Choose the folder and select **Select Folder**.

A message at the top of the window says how many files were saved and where:

| File | Contents |
|---|---|
| `report.txt` | The full report, as on the page under **Full report**. |
| `axis_summary.csv` | Every statistic by axis (mean, RMS, max, P95, P99 of A − B). |
| `per_leaf_A_minus_B.csv` | The difference for every leaf. |
| `per_beam_A.csv`, `per_beam_B.csv` | Each field of A and of B. |
| `beam_holds_A.csv`, `beam_holds_B.csv` | Each beam hold of A and of B. |
| `expected_vs_actual_comparison.png` | The figure: leaf difference maps, gantry and MU. |

Files of the same name in the folder are replaced. The report text gives angles in the log's own
scale (its header says which); the figure follows the **Angles** setting.
