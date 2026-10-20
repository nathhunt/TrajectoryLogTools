#!/usr/bin/env bash
# Regenerates the golden files in tests/TrajectoryLogTools.Tests/Golden by running the ORIGINAL
# tools (reference/) on the simulated logs the tests build. Run it only when the simulated logs
# change; the golden files are the record of what the original tools produced.
#
# Needs the .NET 10 SDK and Python 3 with pandas, numpy and matplotlib.
#   bash reference/golden/regenerate.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
work="$(mktemp -d)"
golden="$root/tests/TrajectoryLogTools.Tests/Golden"
trap 'rm -rf "$work"' EXIT

echo "== simulated logs -> $work"
TLT_GOLDEN_INPUTS="$work" dotnet test "$root/tests/TrajectoryLogTools.Tests" --filter WriteGoldenInputs -v q > /dev/null

echo "== original tools"
dotnet build "$here/../LogToEclipseMlc" -c Release -v q -o "$work/bin/mlc" > /dev/null
dotnet build "$here/../TrajectoryLogDump" -c Release -v q -o "$work/bin/dump" > /dev/null
mlc="$work/bin/mlc/LogToEclipseMlc"
dump="$work/bin/dump/TrajectoryLogDump"

rm -rf "$golden"
mkdir -p "$golden/delivery" "$golden/mlc"

echo "== compare_trajectory_logs.py"
for p in vmat imrt; do
    python3 -I "$here/dump_delivery.py" "$work/${p}_A.csv" "$work/${p}_B.csv" "$golden/delivery/$p.json"
    python3 -I "$root/reference/compare_trajectory_logs.py" "$work/${p}_A.csv" "$work/${p}_B.csv" \
        --outdir "$work/delivery_$p" > "$golden/delivery/${p}_console.txt"
    cp "$work/delivery_$p"/*.csv "$golden/delivery/" 2>/dev/null || true
    for f in "$work/delivery_$p"/*.csv; do mv "$golden/delivery/$(basename "$f")" "$golden/delivery/${p}_$(basename "$f")"; done
done

echo "== LogToEclipseMlc"
# Each case: name | log | options (output paths are added below)
cases=(
  "vmat_A_default|vmat_A.csv|"
  "vmat_A_template|vmat_A.csv|-t $work/vmat_field1_eclipse.csv -t $work/vmat_field2_eclipse.csv"
  "vmat_B_cp_expected|vmat_B.csv|--axis cp --positions expected"
  "imrt_A_sub2_gantry_actual|imrt_A.csv|--subbeam 2 --gantry actual"
  "imrt_B_template_include_hold|imrt_B.csv|--include-hold -t $work/imrt_field1_eclipse.csv -t $work/imrt_field2_eclipse.csv -t $work/imrt_field3_eclipse.csv"
)
for c in "${cases[@]}"; do
    IFS='|' read -r name log opts <<< "$c"
    mkdir -p "$golden/mlc/$name"
    # shellcheck disable=SC2086
    "$mlc" "$work/$log" -o "$golden/mlc/$name/out.csv" $opts > "$golden/mlc/$name/console.txt" 2>&1 || true
    sed -i "s|$work/||g; s|$golden/mlc/$name/||g" "$golden/mlc/$name"/*
done

echo "== TrajectoryLogDump (SHA-256 of each dump, Source file line left out)"
: > "$golden/dump_sha256.txt"
for n in vmat_A vmat_B imrt_A imrt_B; do
    "$dump" "$work/$n.bin" -o "$work/dump" > /dev/null
    h=$(grep -v '^Source file : ' "$work/dump/$n.txt" | sha256sum | cut -d' ' -f1)
    echo "$n $h" >> "$golden/dump_sha256.txt"
done

echo "Golden files written to $golden"
