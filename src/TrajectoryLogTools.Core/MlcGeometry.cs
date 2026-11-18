// MlcGeometry.cs - what the analysis needs to know about an MLC: how many leaves the log
// records, how they pair up into opposing leaves, how log values map to Eclipse IEC 61217
// X1 / X2 positions, and the leaf-gap rules Eclipse applies.
//
// Defined: the Halcyon / Ethos SX2 (verified against Eclipse exports and RT Plans) and the
// TrueBeam Millennium 120 and HD120 (2 carriage values, then bank A leaves 1-60, then bank B
// leaves 1-60; verified against RT Plans of TrueBeam deliveries). Another MLC is added by creating
// an instance with its own mapping and adding it to Known; until then the analyses refuse logs
// whose MLC is not in Known, rather than guess the mapping.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrajectoryLogTools.Core
{
    public sealed class MlcGeometry
    {
        /// <summary>Short name; also the "MLC Model" line Eclipse writes in an MLC export.</summary>
        public string Name { get; private set; }
        public string Description { get; private set; }
        /// <summary>Samples recorded before the leaves (banks / carriages).</summary>
        public int BankSamples { get; private set; }
        /// <summary>Leaf values per snapshot (after the bank samples); always 2 x PairCount.</summary>
        public int LeafCount { get; private set; }
        public int PairCount { get { return LeafCount / 2; } }
        /// <summary>Smallest opposing-leaf gap Eclipse accepts other than exactly closed, in mm.</summary>
        public double MinDynamicGapMm { get; private set; }

        Func<int, int, string> _layer;
        Func<int, int, LeafMapping> _toIec;

        /// <summary>Layer of a leaf pair (0-based), e.g. "distal", or "" for a single-layer MLC.</summary>
        public string LayerOfPair(int pair) { return _layer(pair, PairCount); }

        /// <summary>
        /// Where log leaf <paramref name="leaf"/> (0-based, after the bank samples) goes in Eclipse:
        /// its pair, its bank (X1 / X2) and the sign that turns the log value into IEC 61217.
        /// </summary>
        public LeafMapping ToIec(int leaf) { return _toIec(leaf, PairCount); }

        /// <summary>
        /// Halcyon / Ethos dual-layer SX2: 2 bank values, then 114 leaves. Log leaves 1-57 are the X2
        /// bank (same sign), 58-114 the X1 bank (sign flipped); leaf n faces leaf n+57. Of the 57
        /// pairs the first 28 are the distal (patient side) layer and the last 29 the proximal layer,
        /// which is also the order Eclipse lists them in.
        /// </summary>
        public static readonly MlcGeometry Sx2 = new MlcGeometry
        {
            Name = "SX2",
            Description = "Halcyon / Ethos SX2 (dual layer, 28 distal + 29 proximal pairs)",
            BankSamples = 2,
            LeafCount = 114,
            MinDynamicGapMm = 0.5,
            _layer = (pair, pairs) => pair < pairs / 2 ? "distal" : "proximal",
            _toIec = (leaf, pairs) => leaf < pairs
                ? new LeafMapping(leaf, false, 1.0)
                : new LeafMapping(leaf - pairs, true, -1.0),
        };

        /// <summary>
        /// TrueBeam 120-leaf MLC, Millennium 120 (log MLC model 2): 2 carriage values, then bank A
        /// leaves 1-60 = X2 (same sign), then bank B leaves 1-60 = X1 (sign flipped); leaf n of each
        /// bank is pair n, from Y = -200 mm. The same pattern as the SX2; checked against RT Plans:
        /// the log's planned positions equal the plan's at the control points.
        /// </summary>
        public static readonly MlcGeometry Millennium120 = new MlcGeometry
        {
            Name = "Millennium 120",
            Description = "TrueBeam Millennium 120 (60 pairs)",
            BankSamples = 2,
            LeafCount = 120,
            MinDynamicGapMm = 0.5,
            _layer = (pair, pairs) => "",
            _toIec = (leaf, pairs) => leaf < pairs
                ? new LeafMapping(leaf, false, 1.0)
                : new LeafMapping(leaf - pairs, true, -1.0),
        };

        /// <summary>TrueBeam High Definition 120 (log MLC model 3): laid out in the log as the Millennium 120.</summary>
        public static readonly MlcGeometry Hd120 = new MlcGeometry
        {
            Name = "HD120",
            Description = "TrueBeam HD120 (60 pairs)",
            BankSamples = 2,
            LeafCount = 120,
            MinDynamicGapMm = 0.5,
            _layer = (pair, pairs) => "",
            _toIec = (leaf, pairs) => leaf < pairs
                ? new LeafMapping(leaf, false, 1.0)
                : new LeafMapping(leaf - pairs, true, -1.0),
        };

        /// <summary>The log header's MLC model value of the HD120 (the Millennium 120 is 2, the SX2 6).</summary>
        public const int Hd120Model = 3;

        /// <summary>MLC types the analyses support.</summary>
        public static readonly IReadOnlyList<MlcGeometry> Known = new[] { Sx2, Millennium120, Hd120 };

        /// <summary>The known MLC with this many leaf values, or null (120 leaves: the Millennium 120; see <see cref="FromLeafCount(int, int)"/>).</summary>
        public static MlcGeometry FromLeafCount(int leaves)
        {
            return Known.FirstOrDefault(g => g.LeafCount == leaves);
        }

        /// <summary>The known MLC with this many leaf values, told apart by the log header's MLC model where two have the same count.</summary>
        public static MlcGeometry FromLeafCount(int leaves, int mlcModel)
        {
            if (leaves == 120) return mlcModel == Hd120Model ? Hd120 : Millennium120;
            return FromLeafCount(leaves);
        }

        /// <summary>The known MLC recorded in this log, or null (no MLC axis, or an MLC not in Known).</summary>
        public static MlcGeometry FromLog(TrajectoryLog log)
        {
            int i = Array.IndexOf(log.AxisIds, TrajectoryLog.MlcAxisId);
            if (i < 0) return null;
            MlcGeometry g = Known.FirstOrDefault(x => x.BankSamples + x.LeafCount == log.SamplesPerAxis[i]);
            return g == null ? null : FromLeafCount(g.LeafCount, log.MlcModel);
        }

        public static string UnsupportedMessage(int leaves)
        {
            return "This MLC (" + leaves + " leaves) is not supported yet. Supported: " +
                   string.Join(", ", Known.Select(g => g.Description)) + ".";
        }

        public override string ToString() { return Name; }
    }

    /// <summary>Position of a log leaf in Eclipse: pair (0-based), bank and sign to IEC 61217.</summary>
    public struct LeafMapping
    {
        public readonly int Pair;
        public readonly bool IsX1;
        public readonly double Sign;

        public LeafMapping(int pair, bool isX1, double sign) { Pair = pair; IsX1 = isX1; Sign = sign; }

        /// <summary>Index in an Eclipse control-point line: X1(p0), X2(p0), X1(p1), X2(p1), ...</summary>
        public int EclipseIndex { get { return 2 * Pair + (IsX1 ? 0 : 1); } }
    }
}
