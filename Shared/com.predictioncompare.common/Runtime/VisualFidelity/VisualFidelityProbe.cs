// Watches one predicted entity: where its visual is drawn every frame, compared with the server states
// its library's feeder reports. Identical in all four demo projects; only the feeder differs.
// See VisualFidelity.md in the repository root.

using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PredictionDebug
{
    /// <summary>
    /// Mount on a predicted entity, or let the demo's feeder add it at runtime. The feeder sets
    /// <see cref="VisualFidelity.Clock"/>, reports the server states the client receives with
    /// <see cref="ReportServerState"/>, and keeps the library's current visual delay up to date with
    /// <see cref="ReportVisualDelay"/>. The probe samples the visual itself once per rendered frame and stamps
    /// each frame with the delay last reported.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Prediction Debug/Visual Fidelity Probe")]
    public class VisualFidelityProbe : MonoBehaviour
    {
        [Tooltip("The transform the player sees. Left empty, this object's own transform. Feeders fill it in for libraries that draw a separate object.")]
        [SerializeField] Transform visual;
        [SerializeField] FidelitySubject subject = FidelitySubject.Other;
        [Tooltip("Name in the CSV files. Left empty, the GameObject's name.")]
        [SerializeField] string label = "";
        [Tooltip("Samples the visual transform once per rendered frame. Turn off to report frames yourself with ReportVisual.")]
        [SerializeField] bool sampleVisualEachFrame = true;
        [SerializeField] FidelitySettings settings = new FidelitySettings();

        public FidelityTracker Tracker { get; private set; }

        public Transform Visual
        {
            get => visual != null ? visual : transform;
            set => visual = value;
        }

        public FidelitySubject Subject
        {
            get => subject;
            set => subject = value;
        }

        public string Label
        {
            get => string.IsNullOrEmpty(label) ? name : label;
            set => label = value;
        }

        public FidelitySettings Settings => settings;

        /// <summary>The visual delay last reported by the feeder, in seconds, or NaN.</summary>
        public double VisualDelay { get; private set; } = double.NaN;

        bool _radiusMeasured;
        StreamWriter _recording;

        void Awake()
        {
            Tracker = new FidelityTracker(VisualFidelity.NextId(), settings, VisualFidelity.Impacts);
            Tracker.Evaluated += OnEvaluated;
        }

        void OnEnable() => VisualFidelity.Register(this);

        void OnDisable()
        {
            VisualFidelity.Unregister(this);
            StopRecording();
        }

        /// <summary>
        /// A server state for this entity at a time on the shared timeline, in seconds. Report every state the
        /// client receives, in any order; the first state for a given time is kept.
        /// </summary>
        public void ReportServerState(double time, Vector3 position, Quaternion rotation)
        {
            if (!_radiusMeasured)
                MeasureRadius();
            Tracker.AddServerState(time, position, rotation);
        }

        /// <summary>For tick based libraries: the state after a tick sits at tick × tickSeconds on the timeline.</summary>
        public void ReportServerTick(uint tick, double tickSeconds, Vector3 position, Quaternion rotation)
            => ReportServerState(tick * tickSeconds, position, rotation);

        /// <summary>
        /// The library's current visual delay: how far behind the clock the visual shows the server, in seconds.
        /// Negative when the library draws ahead (extrapolation); NaN when it can't tell. Report it every tick, or
        /// every frame when it changes within a tick; each sampled frame takes the value last reported. Without
        /// it, the residual is left out and only the raw error is measured.
        /// </summary>
        public void ReportVisualDelay(double seconds) => VisualDelay = seconds;

        /// <summary>Where the visual was drawn at a timeline time, and its delay. Only needed with "Sample Visual Each Frame" off.</summary>
        public void ReportVisual(double time, Vector3 position, Quaternion rotation, double delaySeconds)
            => Tracker.AddVisual(time, position, rotation, delaySeconds, Time.frameCount);

        /// <summary>Called by <see cref="VisualFidelity"/> once per rendered frame, after all smoothing has moved the visual.</summary>
        internal void OnFrame(double now)
        {
            if (sampleVisualEachFrame)
            {
                Transform v = Visual;
                if (v != null)
                {
                    v.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                    Tracker.AddVisual(now, position, rotation, VisualDelay, Time.frameCount);
                }
            }
            Tracker.Process(now);
        }

        /// <summary>Size for the shared impact test: the largest half extent of the entity's solid colliders.</summary>
        void MeasureRadius()
        {
            _radiusMeasured = true;
            bool any = false;
            Bounds bounds = default;
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger)
                    continue;
                if (any)
                    bounds.Encapsulate(c.bounds);
                else
                    bounds = c.bounds;
                any = true;
            }
            if (any)
                Tracker.Radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
        }

        internal void StartRecording(string folder)
        {
            StopRecording();
            string file = Path.Combine(folder, $"{subject}-{VisualFidelity.FileSafe(Label)}-{Tracker.Id}.csv");
            _recording = new StreamWriter(file, false, new UTF8Encoding(false), 1 << 16);
            _recording.WriteLine("time,delay,phase,raw,residual,along_track,cross_track,rotation_raw,rotation_residual,velocity_error,server_speed," +
                                 "visual_x,visual_y,visual_z,server_x,server_y,server_z,shown_x,shown_y,shown_z");
        }

        internal void StopRecording()
        {
            _recording?.Dispose();
            _recording = null;
        }

        void OnEvaluated(FidelitySample s)
        {
            VisualFidelity.PublishFrameError(this, s);
            if (_recording == null)
                return;
            CultureInfo c = CultureInfo.InvariantCulture;
            _recording.WriteLine(string.Format(c,
                "{0:0.######},{1:0.######},{2},{3:0.#####},{4:0.#####},{5:0.#####},{6:0.#####},{7:0.###},{8:0.###},{9:0.####},{10:0.####}," +
                "{11:0.#####},{12:0.#####},{13:0.#####},{14:0.#####},{15:0.#####},{16:0.#####},{17:0.#####},{18:0.#####},{19:0.#####}",
                s.Time, s.Delay, s.Phase, s.Raw, s.Residual, s.AlongTrack, s.CrossTrack, s.RotationRaw, s.RotationResidual, s.VelocityError, s.ServerSpeed,
                s.Visual.x, s.Visual.y, s.Visual.z, s.Server.x, s.Server.y, s.Server.z, s.ServerDelayed.x, s.ServerDelayed.y, s.ServerDelayed.z));
        }
    }
}
