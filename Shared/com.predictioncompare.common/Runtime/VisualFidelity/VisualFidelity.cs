// What the visual fidelity probes in one process share. Identical in all four demo projects.
// See VisualFidelity.md in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PredictionDebug
{
    /// <summary>
    /// The clock that puts frames and server states on one timeline, the impacts every probe can see, and
    /// the per-frame sampling of all probes. Sampling runs in Application.onBeforeRender, after every
    /// Update and LateUpdate, so whatever smoothing a library applies has already moved the visuals.
    /// </summary>
    public static class VisualFidelity
    {
        /// <summary>
        /// Current time on the timeline the feeder reports server states on, in seconds. Set by the library's
        /// feeder; probes stay idle while it is null.
        /// </summary>
        public static Func<double> Clock;

        /// <summary>Written to the CSV files and used in output folder names.</summary>
        public static string LibraryName = "";

        /// <summary>Where summaries and recordings are written. Defaults to a VisualFidelity folder under Application.persistentDataPath.</summary>
        public static string OutputRoot;

        public static readonly ImpactRegistry Impacts = new ImpactRegistry();

        /// <summary>One compared frame, for live graphs.</summary>
        public struct FrameError
        {
            /// <summary>Time.frameCount of the frame the visual was drawn in.</summary>
            public int Frame;
            public FidelitySubject Subject;
            /// <summary>Metres, and degrees for the rotation, at the same moment (delay included).</summary>
            public float Raw;
            public float RotationRaw;
            /// <summary>Metres and degrees with the reported delay taken out. NaN when the library reported no delay.</summary>
            public float Residual;
            public float RotationResidual;
        }

        static readonly List<VisualFidelityProbe> _probes = new List<VisualFidelityProbe>();
        static readonly List<FrameError> _frameErrors = new List<FrameError>();
        // Only a live graph drains the queue; without one, the oldest entries are dropped past this.
        const int MaxFrameErrors = 8192;
        static bool _hooked;
        static int _lastId;

        public static IReadOnlyList<VisualFidelityProbe> Probes => _probes;

        /// <summary>Folder the per-frame CSV files go to while recording, otherwise null.</summary>
        public static string RecordingFolder { get; private set; }
        public static bool IsRecording => RecordingFolder != null;

        // Statics survive entering play mode when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (_hooked)
                Application.onBeforeRender -= OnBeforeRender;
            _hooked = false;
            _probes.Clear();
            _frameErrors.Clear();
            Impacts.Clear();
            Clock = null;
            LibraryName = "";
            RecordingFolder = null;
            _lastId = 0;
        }

        internal static int NextId() => ++_lastId;

        internal static void Register(VisualFidelityProbe probe)
        {
            if (!_probes.Contains(probe))
                _probes.Add(probe);
            if (!_hooked)
            {
                Application.onBeforeRender += OnBeforeRender;
                _hooked = true;
            }
            VisualFidelityExport.EnsureExists();
            if (IsRecording)
                probe.StartRecording(RecordingFolder);
        }

        internal static void Unregister(VisualFidelityProbe probe) => _probes.Remove(probe);

        static void OnBeforeRender()
        {
            Func<double> clock = Clock;
            if (clock == null)
                return;
            double now = clock();
            for (int i = 0; i < _probes.Count; i++)
                _probes[i].OnFrame(now);
        }

        internal static void PublishFrameError(VisualFidelityProbe probe, in FidelitySample s)
        {
            if (_frameErrors.Count >= MaxFrameErrors)
                _frameErrors.RemoveRange(0, MaxFrameErrors / 2);
            _frameErrors.Add(new FrameError
            {
                Frame = s.Frame,
                Subject = probe.Subject,
                Raw = s.Raw,
                RotationRaw = s.RotationRaw,
                Residual = s.Residual,
                RotationResidual = s.RotationResidual,
            });
        }

        /// <summary>
        /// Hands every frame compared since the last call to <paramref name="handler"/>, oldest first, and
        /// forgets them. Frames are compared about a round trip after they were drawn, so the errors arrive
        /// late; FrameError.Frame says which frame they belong to. A null handler just discards them.
        /// </summary>
        public static void DrainFrameErrors(Action<FrameError> handler)
        {
            if (handler != null)
                for (int i = 0; i < _frameErrors.Count; i++)
                    handler(_frameErrors[i]);
            _frameErrors.Clear();
        }

        /// <summary>Forgets what belongs to the previous session's timeline. Feeders call it when a session starts or ends.</summary>
        public static void ResetSession() => Impacts.Clear();

        /// <summary>Clears every probe's statistics. The delay fits and buffered paths are kept.</summary>
        public static void ResetStats()
        {
            foreach (VisualFidelityProbe probe in _probes)
                probe.Tracker.ResetStats();
        }

        /// <summary>Merges the statistics of every probe watching this kind of subject into <paramref name="into"/>.</summary>
        /// <returns>How many probes watch this kind of subject.</returns>
        public static int Combine(FidelitySubject subject, FidelityStats into)
        {
            into.Clear();
            int count = 0;
            foreach (VisualFidelityProbe probe in _probes)
            {
                if (probe.Subject != subject)
                    continue;
                count++;
                into.Merge(probe.Tracker.Stats);
            }
            return count;
        }

        public static void StartRecording()
        {
            if (IsRecording)
                return;
            RecordingFolder = CreateRunFolder("frames");
            foreach (VisualFidelityProbe probe in _probes)
                probe.StartRecording(RecordingFolder);
            Debug.Log($"[VisualFidelity] Recording every compared frame to {RecordingFolder}");
        }

        public static void StopRecording()
        {
            if (!IsRecording)
                return;
            foreach (VisualFidelityProbe probe in _probes)
                probe.StopRecording();
            Debug.Log($"[VisualFidelity] Recording stopped: {RecordingFolder}");
            RecordingFolder = null;
        }

        /// <summary>Writes every probe's statistics, and each kind of subject's combined, to a long-format CSV file.</summary>
        /// <returns>Path of the file.</returns>
        public static string WriteSummary()
        {
            string path = Path.Combine(CreateRunFolder("summary"), "summary.csv");
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("library,scope,subject,label,phase,metric,unit,count,mean,rms,p50,p95,p99,max");
                var merged = new FidelityStats();
                foreach (FidelitySubject subject in Enum.GetValues(typeof(FidelitySubject)))
                {
                    if (Combine(subject, merged) > 0)
                        WriteStats(writer, "subject", subject, "", merged);
                }
                foreach (VisualFidelityProbe probe in _probes)
                    WriteStats(writer, "entity", probe.Subject, probe.Label, probe.Tracker.Stats);
            }
            Debug.Log($"[VisualFidelity] Summary written to {path}");
            return path;
        }

        static readonly string[] MetricNames = { "raw", "residual", "along_track", "cross_track", "rotation_raw", "rotation_residual", "velocity_error" };
        static readonly string[] MetricUnits = { "m", "m", "m", "m", "deg", "deg", "m/s" };

        static void WriteStats(StreamWriter w, string scope, FidelitySubject subject, string label, FidelityStats stats)
        {
            string prefix = $"{Csv(LibraryName)},{scope},{subject},{Csv(label)}";
            w.WriteLine($"{prefix},All,unmatched_frames,count,{stats.Unmatched},,,,,,");

            for (int p = 0; p < FidelityStats.PhaseCount; p++)
            {
                var phase = (FidelityPhase)p;
                if (stats.Frames(phase) == 0)
                    continue;
                w.WriteLine($"{prefix},{phase},reported_delay,s,{stats.DelayFrames[p]},{Num(stats.MeanDelay(phase))},,,,,");
                for (int m = 0; m < FidelityStats.MetricCount; m++)
                    WriteHistogram(w, $"{prefix},{phase},{MetricNames[m]},{MetricUnits[m]}", stats.Get(phase, (FidelityMetric)m));
                w.WriteLine($"{prefix},{phase},extra_path,ratio,1,{Num(stats.ExtraPath(phase))},,,,,");
            }

            if (stats.Episodes == 0)
                return;
            WriteHistogram(w, $"{prefix},Collision,episode_peak,m", stats.EpisodePeak);
            WriteHistogram(w, $"{prefix},Collision,episode_settle,s", stats.EpisodeSettle);
            w.WriteLine($"{prefix},Collision,episode_unsettled,count,{stats.UnsettledEpisodes},,,,,,");
            w.WriteLine($"{prefix},Collision,episode_error_integral,m*s,{stats.Episodes},{Num(stats.EpisodeIntegral / stats.Episodes)},,,,,");
        }

        static void WriteHistogram(StreamWriter w, string prefix, LogHistogram h)
        {
            if (h.Count == 0)
                return;
            w.WriteLine($"{prefix},{h.Count},{Num(h.Mean)},{Num(h.Rms)},{Num(h.Percentile(0.5))},{Num(h.Percentile(0.95))},{Num(h.Percentile(0.99))},{Num(h.Max)}");
        }

        static string Num(double v) => double.IsNaN(v) ? "" : v.ToString("0.######", CultureInfo.InvariantCulture);

        static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";

        static string CreateRunFolder(string kind)
        {
            string root = string.IsNullOrEmpty(OutputRoot) ? Path.Combine(Application.persistentDataPath, "VisualFidelity") : OutputRoot;
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            string folder = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{FileSafe(LibraryName)}-pid{pid}-{kind}");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string FileSafe(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "unnamed";
            var sb = new StringBuilder(s.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char ch in s)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 || ch == ' ' || ch == '#' ? '_' : ch);
            return sb.ToString();
        }
    }
}
