#!/usr/bin/env python3
"""
Compare two trajectory-log CSV exports of the same VMAT field.

  * Expected A vs Expected B  (controller setpoints)
  * Actual   A vs Actual   B  (recorded machine positions)

Logs are matched by fractional ControlPoint using beam-on samples only
(DoseServoState == 'Normal'), so differences in timing, beam holds and
sampling rate between the two deliveries don't affect the comparison.
Control points passed through only while the beam is held (gantry moves
between static IMRT beams, collimator moves between arcs) are excluded.
Works for VMAT (arcs) and static-gantry IMRT (several Subbeams).

Usage:
    python compare_trajectory_logs.py logA.csv logB.csv [--outdir results]

Outputs (in --outdir):
    axis_summary.csv              mean / RMS / max of A-B for each axis
    per_leaf_A_minus_B.csv        per-leaf mean / RMS / max at the planned control points
    per_beam_A.csv / _B.csv       per Subbeam: gantry, MU, beam-on time, hold time, MLC error
    beam_holds_A.csv / _B.csv     every beam hold, classified as start/end/move/interruption
    expected_vs_actual_comparison.png
Requires: pandas, numpy, matplotlib
"""
import argparse
import os
import re

import numpy as np
import pandas as pd
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import LinearSegmentedColormap

AXES = {
    "Gantry (deg)": "GantryRtn_deg",
    "Collimator (deg)": "CollRtn_deg",
    "MU": "MU",
    "Couch Vrt (cm)": "CouchVrt_cm",
    "Couch Lng (cm)": "CouchLng_cm",
    "Couch Lat (cm)": "CouchLat_cm",
}


# ---------------------------------------------------------------- loading
def load_log(path):
    d = pd.read_csv(path)
    # Drop a stray first snapshot recorded well before the rest of the log
    # (seen in one export: a 2.48 s gap between snapshots 1 and 2).
    if len(d) > 2 and d.ElapsedTime_s.iloc[1] - d.ElapsedTime_s.iloc[0] > 0.5:
        d = d.iloc[1:].reset_index(drop=True)
        d["ElapsedTime_s"] -= d.ElapsedTime_s.iloc[0]
    return d


def leaf_names(d):
    return sorted({c.split("_cm_")[0] for c in d.columns if c.startswith("MLC_Leaf")})


def beam_on_by_cp(d):
    """Beam-on samples with strictly increasing ControlPoint (needed for interpolation)."""
    d = d[d.DoseServoState == "Normal"]
    _, idx = np.unique(d.ControlPoint.values, return_index=True)
    return d.iloc[idx]


def at_cp(d, col, x):
    return np.interp(x, d.ControlPoint.values, d[col].values)


def covered(d, x):
    """True where control point x lies within beam-on data of log d.

    Control points that are only passed through while the beam is held (e.g. the
    gantry moving between static IMRT beams, or the collimator turning between
    arcs) have no beam-on samples; interpolating across them would compare
    made-up values, so they are excluded.
    """
    cp = d.ControlPoint.values
    step = np.median(np.diff(cp))
    ok = (x >= cp[0]) & (x <= cp[-1])
    for i in np.where(np.diff(cp) > max(0.3, 4 * step))[0]:
        ok &= ~((x > cp[i]) & (x < cp[i + 1]))
    return ok


def stats(diff):
    diff = np.asarray(diff)
    return diff.mean(), np.sqrt((diff ** 2).mean()), np.abs(diff).max()


# ---------------------------------------------------------------- analysis
def compare(A, B, leaves):
    a, b = beam_on_by_cp(A), beam_on_by_cp(B)
    n_cp = int(round(max(A.ControlPoint.max(), B.ControlPoint.max()))) + 1
    nodes = np.arange(n_cp)                                   # planned control points
    fine_all = np.linspace(0.5, n_cp - 1.5, (n_cp - 2) * 20 + 1)  # every 0.05 CP
    nodes = nodes[covered(a, nodes) & covered(b, nodes)]
    fine_ok = covered(a, fine_all) & covered(b, fine_all)
    fine = fine_all[fine_ok]

    rows = []
    for name, col in AXES.items():
        if f"{col}_Expected" not in A or f"{col}_Expected" not in B:
            continue
        for kind in ("Expected", "Actual"):
            for label, x in (("nodes", nodes), ("continuous", fine)):
                d = at_cp(a, f"{col}_{kind}", x) - at_cp(b, f"{col}_{kind}", x)
                rows.append([name, kind, label, *stats(d), np.nan, np.nan])

    leaf_rows, D = [], {}
    for kind in ("Expected", "Actual"):
        for label, x in (("nodes", nodes), ("continuous", fine)):
            M = np.array([at_cp(a, f"{l}_cm_{kind}", x) - at_cp(b, f"{l}_cm_{kind}", x)
                          for l in leaves]) * 10.0           # cm -> mm
            rows.append(["MLC all leaves (mm)", kind, label, *stats(M),
                         np.percentile(np.abs(M), 95), np.percentile(np.abs(M), 99)])
            if label == "nodes":
                for i, l in enumerate(leaves):
                    leaf_rows.append([int(l[-3:]), kind, *stats(M[i])])
            else:
                D[kind] = M

    summary = pd.DataFrame(rows, columns=["Axis", "Values", "Sampling", "Mean A-B",
                                          "RMS A-B", "Max |A-B|", "P95 |A-B|", "P99 |A-B|"])
    per_leaf = (pd.DataFrame(leaf_rows, columns=["Leaf", "Values", "Mean", "RMS", "Max"])
                .pivot(index="Leaf", columns="Values"))
    per_leaf.columns = [f"{s}_{k}_mm" for s, k in per_leaf.columns]

    # How much of the Actual difference is explained by the Expected difference?
    extra = {
        "corr(ExpectedDiff, ActualDiff)": np.corrcoef(D["Expected"].ravel(), D["Actual"].ravel())[0, 1],
        "RMS of (ActualDiff - ExpectedDiff) mm": np.sqrt(((D["Actual"] - D["Expected"]) ** 2).mean()),
        "Max of |ActualDiff - ExpectedDiff| mm": np.abs(D["Actual"] - D["Expected"]).max(),
    }
    # For plotting: put the values back on the full grid, with gaps as NaN
    D_plot = {}
    for kind, M in D.items():
        full = np.full((M.shape[0], fine_all.size), np.nan)
        full[:, fine_ok] = M
        D_plot[kind] = full
    return summary, per_leaf, D_plot, (fine_all, fine_ok), a, b, extra


def max_leaf_step(d, leaves, kind):
    """Largest per-snapshot leaf step (mm); reveals the controller's leaf-speed cap."""
    s = np.abs(np.diff(d[[f"{l}_cm_{kind}" for l in leaves]].values, axis=0)) * 10
    return s.max(), np.percentile(s[s > 0.5], 90) if (s > 0.5).any() else np.nan


def leaf_err(rows, leaves):
    if rows.empty:
        return np.nan
    return np.abs(rows[[f"{l}_cm_Actual" for l in leaves]].values
                  - rows[[f"{l}_cm_Expected" for l in leaves]].values).max() * 10


def hold_segments(d, leaves):
    """Every beam hold: when, where in the plan, what moved, and leaf behaviour.

    type: 'start'/'end' = log edges; 'move' = gantry or collimator moved
    during the hold (planned transition between beams/arcs); 'interruption' =
    nothing moved, so the hold was not part of the plan's motion.
    """
    h = d.DoseServoState == "Hold"
    grp = (h != h.shift()).cumsum()
    out = []
    for _, s in d[h].groupby(grp[h]):
        g_move = abs(s.GantryRtn_deg_Actual.iloc[-1] - s.GantryRtn_deg_Actual.iloc[0])
        c_move = abs(s.CollRtn_deg_Actual.iloc[-1] - s.CollRtn_deg_Actual.iloc[0])
        t0, t1 = s.ElapsedTime_s.iloc[0], s.ElapsedTime_s.iloc[-1]
        if s.index[0] <= d.index[0] + 3:
            kind = "start"
        elif s.index[-1] >= d.index[-1] - 1:
            kind = "end"
        elif g_move > 0.5 or c_move > 0.5:
            kind = "move"
        else:
            kind = "interruption"
        after = d[(d.ElapsedTime_s > t1) & (d.ElapsedTime_s <= t1 + 1.0)]
        out.append(dict(type=kind, beam=int(s.Subbeam.iloc[0]), t_start_s=t0,
                        duration_s=t1 - t0 + d.ElapsedTime_s.diff().median(),
                        CP=s.ControlPoint.iloc[0], MU=s.MU_Actual.iloc[0],
                        MU_during=s.MU_Actual.iloc[-1] - s.MU_Actual.iloc[0],
                        gantry=f"{s.GantryRtn_deg_Actual.iloc[0]:.1f}->{s.GantryRtn_deg_Actual.iloc[-1]:.1f}",
                        coll=f"{s.CollRtn_deg_Actual.iloc[0]:.1f}->{s.CollRtn_deg_Actual.iloc[-1]:.1f}",
                        leaf_err_during_mm=leaf_err(s, leaves),
                        leaf_err_1s_after_mm=leaf_err(after, leaves)))
    return pd.DataFrame(out)


def per_beam(d, leaves):
    """One row per Subbeam: gantry, collimator, MU, beam-on time, hold time, leaf error."""
    rows = []
    dt = d.ElapsedTime_s.diff()
    for s, g in d.groupby("Subbeam"):
        on = g[g.DoseServoState == "Normal"]
        dmu = g.MU_Actual.diff() > 1e-6
        e = (on[[f"{l}_cm_Actual" for l in leaves]].values
             - on[[f"{l}_cm_Expected" for l in leaves]].values) * 10
        rows.append(dict(beam=int(s),
                         gantry_deg=round(on.GantryRtn_deg_Expected.median(), 2)
                         if on.GantryRtn_deg_Expected.std() < 0.5 else
                         f"{on.GantryRtn_deg_Expected.iloc[0]:.0f}->{on.GantryRtn_deg_Expected.iloc[-1]:.0f}",
                         coll_deg=round(on.CollRtn_deg_Expected.median(), 2),
                         CP=f"{g.ControlPoint.min():.0f}-{g.ControlPoint.max():.0f}",
                         MU_planned=g.MU_Expected.max() - g.MU_Expected.min(),
                         MU_delivered=g.MU_Actual.max() - g.MU_Actual.min(),
                         beam_on_s=dt[g.index][dmu].sum(),
                         hold_s=dt[g.index][g.DoseServoState == "Hold"].sum(),
                         MLC_rms_err_mm=np.sqrt((e ** 2).mean()),
                         MLC_max_err_mm=np.abs(e).max()))
    return pd.DataFrame(rows)


def couch_tracking(d):
    out = {}
    for c in ("CouchVrt_cm", "CouchLng_cm", "CouchLat_cm"):
        out[c] = (d[c + "_Expected"].iloc[0], d[c + "_Actual"].mean(),
                  d[c + "_Actual"].mean() - d[c + "_Expected"].iloc[0])
    return out


# ---------------------------------------------------------------- plotting
def plot(D, grid_info, a, b, label_a, label_b, path):
    fine, ok = grid_info
    CE, CA = "#2a78d6", "#eb6834"
    ink, mute, grid = "#2b2b2b", "#6b6b6b", "#e6e6e6"
    plt.rcParams.update({"font.size": 10, "axes.edgecolor": "#bbbbbb", "axes.labelcolor": ink,
                         "xtick.color": mute, "ytick.color": mute,
                         "axes.spines.top": False, "axes.spines.right": False})
    fig, ax = plt.subplots(2, 2, figsize=(13, 8.5), dpi=150)

    for k, (col, unit, title) in enumerate([("GantryRtn_deg", "deg", "Gantry angle difference"),
                                            ("MU", "MU", "MU difference")]):
        p = ax[0, k]
        da = np.where(ok, at_cp(a, col + "_Actual", fine) - at_cp(b, col + "_Actual", fine), np.nan)
        de = np.where(ok, at_cp(a, col + "_Expected", fine) - at_cp(b, col + "_Expected", fine), np.nan)
        p.plot(fine, da, color=CA, lw=1.1, label="Actual A − Actual B")
        p.plot(fine, de, color=CE, lw=1.4, label="Expected A − Expected B")
        p.axhline(0, color="#999", lw=0.6)
        p.grid(color=grid, lw=0.6)
        p.set_xlabel("Control point")
        p.set_ylabel(unit)
        p.set_title(title, loc="left", color=ink, fontsize=11, pad=10)
    h, l = ax[0, 0].get_legend_handles_labels()
    fig.legend(h, l, frameon=False, ncol=2, loc="upper right", bbox_to_anchor=(0.99, 0.985))

    cmap = LinearSegmentedColormap.from_list("div", ["#1f5fae", "#f0efec", "#c43c3b"])
    n_leaves = D["Actual"].shape[0]
    for k, kind in enumerate(("Expected", "Actual")):
        M, p = D[kind], ax[1, k]
        im = p.imshow(M, aspect="auto", cmap=cmap, vmin=-0.2, vmax=0.2,
                      extent=[fine[0], fine[-1], n_leaves + 0.5, 0.5], interpolation="nearest")
        p.set_title(f"{kind} leaf positions, A − B   (RMS {np.sqrt(np.nanmean(M**2)):.3f} mm, "
                    f"max {np.nanmax(np.abs(M)):.2f} mm)", loc="left", color=ink, fontsize=11, pad=10)
        p.set_facecolor("#ffffff")
        p.set_xlabel("Control point (blank = no beam-on data, e.g. gantry move)")
        p.set_ylabel("Leaf number")
    cax = fig.add_axes([0.915, 0.07, 0.012, 0.33])
    cb = fig.colorbar(im, cax=cax)
    cb.set_label("mm (clipped at ±0.2)", color=ink)
    cb.outline.set_visible(False)

    fig.suptitle("Log A vs Log B — matched by control point, beam-on only",
                 x=0.01, y=0.985, ha="left", fontsize=13, color=ink, fontweight="bold")
    fig.text(0.01, 0.935, f"A: {label_a}     B: {label_b}", ha="left", fontsize=9, color=mute)
    fig.subplots_adjust(left=0.075, right=0.9, top=0.88, bottom=0.07, hspace=0.38, wspace=0.18)
    fig.savefig(path)
    plt.close(fig)


def short_label(path):
    """File name without upload-id prefix, extension or long middle part."""
    name = os.path.splitext(os.path.basename(path))[0]
    name = re.sub(r"^[0-9a-f]{8}-", "", name)
    return name if len(name) <= 45 else name[:20] + "…" + name[-22:]


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("log_a")
    ap.add_argument("log_b")
    ap.add_argument("--outdir", default="results")
    args = ap.parse_args()
    os.makedirs(args.outdir, exist_ok=True)

    A, B = load_log(args.log_a), load_log(args.log_b)
    leaves = [l for l in leaf_names(A) if l in set(leaf_names(B))]
    label_a, label_b = short_label(args.log_a), short_label(args.log_b)

    summary, per_leaf, D, fine, a, b, extra = compare(A, B, leaves)

    pd.set_option("display.width", 200)
    print("\n=== A − B by axis (nodes = planned control points; continuous = every 0.05 CP) ===")
    print(summary.to_string(index=False, float_format=lambda v: f"{v:.4f}"))
    print("\n=== Actual difference vs Expected difference (MLC, continuous) ===")
    for k, v in extra.items():
        print(f"  {k}: {v:.4f}")
    print("\n=== Leaves with largest max |A−B| at control points (mm) ===")
    print("  Expected:", per_leaf["Max_Expected_mm"].nlargest(5).round(3).to_dict())
    print("  Actual:  ", per_leaf["Max_Actual_mm"].nlargest(5).round(3).to_dict())
    print("\n=== Largest per-snapshot leaf step (mm): max, 90th pct of moving steps ===")
    for n, d in (("A", A), ("B", B)):
        for kind in ("Expected", "Actual"):
            mx, p90 = max_leaf_step(d, leaves, kind)
            print(f"  {n} {kind}: max {mx:.3f}, p90 {p90:.3f}")
    print("\n=== Per beam (Subbeam) ===")
    beams = {}
    for n, d in (("A", A), ("B", B)):
        beams[n] = per_beam(d, leaves)
        print(f"  Log {n}:")
        print(beams[n].to_string(index=False, float_format=lambda v: f"{v:.3f}"))
    print("\n=== Beam holds ('interruption' = hold with no planned gantry/collimator move) ===")
    holds = {}
    for n, d in (("A", A), ("B", B)):
        holds[n] = hold_segments(d, leaves)
        print(f"  Log {n}:")
        print(holds[n].to_string(index=False, float_format=lambda v: f"{v:.3f}"))
    print("\n=== Total MU, planned vs delivered ===")
    for n, d in (("A", A), ("B", B)):
        print(f"  {n}: planned {d.MU_Expected.max():.3f}, delivered {d.MU_Actual.max():.3f}, "
              f"diff {d.MU_Actual.max() - d.MU_Expected.max():+.3f} MU, total time {d.ElapsedTime_s.max():.2f} s")
    print("\n=== Couch (expected, mean actual, actual − expected) ===")
    for n, d in (("A", A), ("B", B)):
        for c, (e, act, diff) in couch_tracking(d).items():
            print(f"  {n} {c}: expected {e:.3f}, actual {act:.4f}, diff {diff * 10:+.3f} mm")

    summary.to_csv(os.path.join(args.outdir, "axis_summary.csv"), index=False, float_format="%.5f")
    per_leaf.to_csv(os.path.join(args.outdir, "per_leaf_A_minus_B.csv"), float_format="%.4f")
    for n in ("A", "B"):
        beams[n].to_csv(os.path.join(args.outdir, f"per_beam_{n}.csv"), index=False, float_format="%.4f")
        holds[n].to_csv(os.path.join(args.outdir, f"beam_holds_{n}.csv"), index=False, float_format="%.4f")
    plot(D, fine, a, b, label_a, label_b,
         os.path.join(args.outdir, "expected_vs_actual_comparison.png"))
    print(f"\nWrote results to {os.path.abspath(args.outdir)}")


if __name__ == "__main__":
    main()
