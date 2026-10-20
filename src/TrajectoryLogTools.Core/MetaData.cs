// MetaData.cs - the header's metadata text: "Key:<tab>value" lines (Patient ID, Plan Name, ...),
// read and changed line by line so the lines that aren't changed stay exactly as stored.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TrajectoryLogTools.Core
{
    public static class MetaData
    {
        /// <summary>The metadata keys the app shows by name; the value of the first one present is used.</summary>
        public static readonly string[] PatientIdKeys = { "Patient ID", "PatientID", "Patient Id" };
        public static readonly string[] PlanNameKeys = { "Plan Name", "PlanName", "Plan Label", "Plan" };
        public static readonly string[] PlanUidKeys = { "Plan UID", "PlanUID", "SOP Instance UID", "Plan SOP Instance UID", "RT Plan UID" };
        /// <summary>The beam name line, which holds the name of the first subbeam (field).</summary>
        public static readonly string[] BeamNameKeys = { "BeamName", "Beam Name" };

        /// <summary>The key a value is stored under: the log's own spelling of one of <paramref name="keys"/>, else the first.</summary>
        public static string KeyFor(string text, string[] keys) { return FindKey(text, keys) ?? keys[0]; }

        /// <summary>
        /// The metadata edits to make for a v4.0 export: <paramref name="edits"/>, plus, when subbeam 1
        /// is renamed, the BeamName line (if the log has one and it isn't edited already), since it
        /// holds the first subbeam's name.
        /// </summary>
        public static List<KeyValuePair<string, string>> EditsWithBeamName(string text, IList<KeyValuePair<string, string>> edits, IDictionary<int, string> subbeamNames)
        {
            var all = new List<KeyValuePair<string, string>>(edits);
            string first;
            string beamKey = FindKey(text, BeamNameKeys);
            if (beamKey != null && subbeamNames != null && subbeamNames.TryGetValue(1, out first) &&
                !all.Any(e => string.Equals(e.Key.Trim(), beamKey, StringComparison.OrdinalIgnoreCase)))
                all.Add(new KeyValuePair<string, string>(beamKey, first));
            return all;
        }

        /// <summary>The text with the edits made, in order.</summary>
        public static string Apply(string text, IEnumerable<KeyValuePair<string, string>> edits)
        {
            foreach (var e in edits) text = Set(text, e.Key, e.Value);
            return text;
        }

        /// <summary>Most bytes of text the header holds (745 bytes, the last one the terminating zero).</summary>
        public const int MaxBytes = TrajectoryLog.MetaDataBytes - 1;

        /// <summary>The "Key: value" lines, in order: (key, value). Lines without a key are left out.</summary>
        public static List<KeyValuePair<string, string>> Entries(string text)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (string raw in Lines(text))
            {
                int i = raw.IndexOfAny(new[] { ':', '=' });
                if (i <= 0) continue;
                result.Add(new KeyValuePair<string, string>(raw.Substring(0, i).Trim(), raw.Substring(i + 1).Trim()));
            }
            return result;
        }

        /// <summary>Value of the line "Key: value" (case-insensitive key), or null.</summary>
        public static string Get(string text, string key)
        {
            foreach (var e in Entries(text))
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) return e.Value.Length > 0 ? e.Value : null;
            return null;
        }

        /// <summary>The first of <paramref name="keys"/> present in the text, or null.</summary>
        public static string FindKey(string text, IEnumerable<string> keys)
        {
            var present = Entries(text).Select(e => e.Key).ToList();
            return keys.Select(k => present.FirstOrDefault(p => string.Equals(p, k, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(k => k != null);
        }

        /// <summary>
        /// Sets the value of a key: replaces it in its line, keeping the line's separator ("Key:\t"),
        /// or adds "Key:\tvalue" at the end when the key isn't there. A null value removes the line.
        /// </summary>
        public static string Set(string text, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A metadata key is needed.");
            if (value != null && (value.Contains("\n") || value.Contains("\r"))) throw new ArgumentException("A metadata value must be one line (" + key + ").");
            string nl = text.Contains("\r\n") ? "\r\n" : text.Contains("\n") ? "\n" : "\r\n";
            var lines = Lines(text);
            bool found = false;
            for (int n = 0; n < lines.Count; n++)
            {
                string raw = lines[n];
                int i = raw.IndexOfAny(new[] { ':', '=' });
                if (i <= 0 || !string.Equals(raw.Substring(0, i).Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                if (value == null) { lines.RemoveAt(n); n--; continue; }
                int start = i + 1;
                while (start < raw.Length && (raw[start] == ' ' || raw[start] == '\t')) start++;
                lines[n] = raw.Substring(0, start) + value;
            }
            if (!found && value != null) lines.Add(key.Trim() + ":\t" + value);
            bool endsWithNewline = text.EndsWith("\n", StringComparison.Ordinal) || text.Length == 0;
            string result = string.Join(nl, lines);
            return result.Length > 0 && endsWithNewline ? result + nl : result;
        }

        /// <summary>The metadata as written to the header; throws if it doesn't fit.</summary>
        public static byte[] Encode(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > MaxBytes)
                throw new ArgumentException("The header metadata would be " + bytes.Length + " bytes; it holds at most " + MaxBytes + ". Shorten a value.");
            return bytes;
        }

        static List<string> Lines(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }
    }
}
