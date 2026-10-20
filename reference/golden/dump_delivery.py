#!/usr/bin/env python3
"""
Runs the original compare_trajectory_logs.py analysis on two CSV logs and writes every number
it produces, at full precision, to a JSON file. The golden tests compare the C# port
(DeliveryComparison) with this file.

    python3 dump_delivery.py logA.csv logB.csv out.json
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import compare_trajectory_logs as ct  # noqa: E402


def num(v):
    try:
        f = float(v)
    except (TypeError, ValueError):
        return str(v)
    return None if math.isnan(f) else f


def main():
    path_a, path_b, out = sys.argv[1:4]
    A, B = ct.load_log(path_a), ct.load_log(path_b)
    leaves = [l for l in ct.leaf_names(A) if l in set(ct.leaf_names(B))]
    summary, per_leaf, D, fine, a, b, extra = ct.compare(A, B, leaves)
    res = {
        "summary": [{k: num(v) if k not in ("Axis", "Values", "Sampling") else v for k, v in row.items()}
                    for row in summary.to_dict("records")],
        "per_leaf_columns": list(per_leaf.columns),
        "per_leaf": [{"Leaf": int(i), **{c: num(per_leaf.loc[i, c]) for c in per_leaf.columns}} for i in per_leaf.index],
        "extra": {k: num(v) for k, v in extra.items()},
        "logs": {},
    }
    for n, d in (("A", A), ("B", B)):
        steps = {kind: [num(x) for x in ct.max_leaf_step(d, leaves, kind)] for kind in ("Expected", "Actual")}
        res["logs"][n] = {
            "rows": len(d),
            "beams": [{k: num(v) for k, v in r.items()} for r in ct.per_beam(d, leaves).to_dict("records")],
            "holds": [{k: num(v) for k, v in r.items()} for r in ct.hold_segments(d, leaves).to_dict("records")],
            "steps": steps,
            "mu_planned": num(d.MU_Expected.max()),
            "mu_delivered": num(d.MU_Actual.max()),
            "total_time": num(d.ElapsedTime_s.max()),
            "couch": {c: [num(x) for x in v] for c, v in ct.couch_tracking(d).items()},
        }
    with open(out, "w") as f:
        json.dump(res, f, indent=1)


if __name__ == "__main__":
    main()
