using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace PredictionDebug
{
    /// <summary>
    /// The rows every library's settings panel shows, in this order, so the four demos can be compared line by line.
    /// A library without an equivalent shows a dash.
    /// </summary>
    public enum PredictionSetting
    {
        // Timing
        SimulationRate,
        StateSendRate,
        InputSendRate,
        RenderRate,
        VSync,
        PhysicsStep,
        // Prediction
        PredictRemote,
        InputRedundancy,
        ServerInputBuffer,
        ClientHistory,
        ResimThreshold,
        MaxResimsPerTick,
        ExtrapolateInput,
        // Smoothing
        Interpolation,
        InterpolationBuffer,
        InterpolationDelay,
        CorrectionRate,
        SnapDistance,
    }

    /// <summary>
    /// What a library's panel shows. Values are lambdas, evaluated again on every refresh, so the panel follows
    /// runtime changes and survives values that are not available yet (a manager that has not started).
    /// </summary>
    public sealed class SettingsSheet
    {
        public const string None = "—";

        public sealed class Row
        {
            public string Label;
            public Func<string> Value;
            public string Cached;
            public bool ViaReflection;
        }

        public static readonly (string title, PredictionSetting first, PredictionSetting last)[] Sections =
        {
            ("Timing", PredictionSetting.SimulationRate, PredictionSetting.PhysicsStep),
            ("Prediction", PredictionSetting.PredictRemote, PredictionSetting.ExtrapolateInput),
            ("Smoothing", PredictionSetting.Interpolation, PredictionSetting.SnapDistance),
        };

        public static string LabelOf(PredictionSetting setting) => setting switch
        {
            PredictionSetting.SimulationRate => "Sim rate",
            PredictionSetting.StateSendRate => "State send",
            PredictionSetting.InputSendRate => "Input send",
            PredictionSetting.RenderRate => "Render",
            PredictionSetting.VSync => "vSync",
            PredictionSetting.PhysicsStep => "Physics step",
            PredictionSetting.PredictRemote => "Predict remote",
            PredictionSetting.InputRedundancy => "Input redundancy",
            PredictionSetting.ServerInputBuffer => "Server input buffer",
            PredictionSetting.ClientHistory => "Client history",
            PredictionSetting.ResimThreshold => "Resim threshold",
            PredictionSetting.MaxResimsPerTick => "Max resims / tick",
            PredictionSetting.ExtrapolateInput => "Extrapolate input",
            PredictionSetting.Interpolation => "Interpolation",
            PredictionSetting.InterpolationBuffer => "Interp buffer",
            PredictionSetting.InterpolationDelay => "Interp delay",
            PredictionSetting.CorrectionRate => "Correction rate",
            PredictionSetting.SnapDistance => "Snap distance",
            _ => setting.ToString(),
        };

        internal readonly Dictionary<PredictionSetting, Row> Standard = new();
        internal readonly List<Row> Extras = new();

        public void Set(PredictionSetting setting, Func<string> value)
        {
            Standard[setting] = new Row { Label = LabelOf(setting), Value = value };
        }

        public void NotApplicable(PredictionSetting setting) => Set(setting, () => None);

        /// <summary>Library-specific settings with no counterpart in the others, listed under the standard rows.</summary>
        public void Extra(string label, Func<string> value)
        {
            Extras.Add(new Row { Label = label, Value = value });
        }

        internal void Refresh()
        {
            foreach (Row row in Standard.Values)
                Evaluate(row);
            foreach (Row row in Extras)
                Evaluate(row);
        }

        static void Evaluate(Row row)
        {
            Reflect.Used = false;
            try
            {
                row.Cached = row.Value() ?? None;
            }
            catch (Exception)
            {
                // Typically a manager that has not started yet; the next refresh tries again.
                row.Cached = "?";
            }
            row.ViaReflection = Reflect.Used;
        }

        // ---------------------------------------------------------------- formatting helpers for the libraries

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Num(double value, string format = "0.##") => value.ToString(format, Inv);

        public static string Hz(double hz) => hz <= 0 || double.IsInfinity(hz) ? "∞" : $"{Num(hz, "0.#")} Hz";

        public static string Ticks(double ticks) => $"{Num(ticks, "0.#")} ticks";

        public static string Ms(double seconds) => $"{Num(seconds * 1000.0, "0.#")} ms";

        public static string OnOff(bool value) => value ? "on" : "off";

        public static string RenderRate() => Application.targetFrameRate > 0 ? $"{Application.targetFrameRate} fps" : "∞";

        public static string VSync() => QualitySettings.vSyncCount > 0 ? QualitySettings.vSyncCount.ToString() : "off";

        /// <summary>
        /// One value per prefab: just the value when the prefabs agree, otherwise "Player 0.1 · Ball 0.2".
        /// </summary>
        public static string PerPrefab<T>(IEnumerable<GameObject> prefabs, Func<T, string> describe) where T : Component
        {
            var groups = new List<(string value, List<string> names)>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !prefab.TryGetComponent(out T component))
                    continue;
                string value = describe(component);
                var group = groups.FirstOrDefault(g => g.value == value);
                if (group.names == null)
                    groups.Add((value, new List<string> { prefab.name }));
                else
                    group.names.Add(prefab.name);
            }
            if (groups.Count == 0)
                return None;
            if (groups.Count == 1)
                return groups[0].value;
            return string.Join(" · ", groups.Select(g => $"{string.Join("/", g.names)} {g.value}"));
        }
    }
}
