// Shared core of the visual fidelity probe: compares where a client draws an entity with the path the
// server simulated for it. Only maths and buffers live here, no components and no networking library.
// Each demo's feeder pushes server states and a clock into VisualFidelityProbe.
// See VisualFidelity.md in the repository root for the setup and the meaning of every number.

using System;
using UnityEngine;

namespace PredictionDebug
{
    /// <summary>What kind of entity a probe watches. The live graphs and the summary CSV group by this.</summary>
    public enum FidelitySubject
    {
        LocalPlayer,
        RemotePlayer,
        Bot,
        Ball,
        Other,
    }

    /// <summary>What the entity was doing on the server at the moment a frame is compared with.</summary>
    public enum FidelityPhase
    {
        /// <summary>Every compared frame.</summary>
        All,
        /// <summary>Moving, with no collision nearby in time.</summary>
        Moving,
        /// <summary>Inside the window around an impact on this entity, or on one it was touching.</summary>
        Collision,
        /// <summary>Server speed below the idle threshold, with no collision nearby in time.</summary>
        Idle,
    }

    public enum FidelityMetric
    {
        /// <summary>Metres between the visual and the server at the same moment. Includes the visual delay.</summary>
        Raw,
        /// <summary>Metres between the visual and the server at the moment the library says the visual shows, i.e. with the reported delay taken out.</summary>
        Residual,
        /// <summary>Part of the residual along the server's direction of travel: timing error the reported delay doesn't account for.</summary>
        AlongTrack,
        /// <summary>Part of the residual across the server's direction of travel: how far the visual is off the server's path.</summary>
        CrossTrack,
        /// <summary>Degrees between the visual and the server rotation at the same moment.</summary>
        RotationRaw,
        /// <summary>Degrees between the visual and the server rotation, delay taken out.</summary>
        RotationResidual,
        /// <summary>m/s between the visual's frame-to-frame velocity and the server velocity, delay taken out. Catches judder and rubber-banding.</summary>
        VelocityError,
    }

    [Serializable]
    public sealed class FidelitySettings
    {
        [Header("Server path")]
        [Tooltip("Server states further apart than this, in seconds, are not interpolated across.")]
        public float maxServerGapSeconds = 0.25f;
        [Tooltip("A server step faster than this, in m/s, is a teleport or respawn, and the path is broken there.")]
        public float teleportSpeed = 150f;

        [Header("Phases")]
        [Tooltip("Below this server speed, in m/s, the entity counts as idle.")]
        public float idleSpeed = 0.05f;
        [Tooltip("A velocity change between consecutive server states larger than this, in m/s, is an impact.")]
        public float impactDeltaV = 2f;
        [Tooltip("Frames up to this long before an impact, in seconds, count as collision.")]
        public float collisionPreSeconds = 0.05f;
        [Tooltip("Frames up to this long after an impact, in seconds, count as collision.")]
        public float collisionPostSeconds = 0.5f;
        [Tooltip("Another entity's impact also counts for this one when their server positions were within both radii plus this, in metres.")]
        public float contactMargin = 0.5f;
        [Tooltip("After an impact the visual has settled once the residual error stays below this, in metres.")]
        public float settleEpsilon = 0.05f;

        [Header("Evaluation")]
        [Tooltip("A frame still not covered by server states after this many seconds is compared with what there is, or dropped.")]
        public float evalTimeoutSeconds = 2f;
    }

    /// <summary>One rendered frame compared with the server.</summary>
    public struct FidelitySample
    {
        /// <summary>Timeline time the frame was drawn at.</summary>
        public double Time;
        /// <summary>Rendered frame number (Time.frameCount) it was drawn in, for graphs that plot per frame.</summary>
        public int Frame;
        /// <summary>Visual delay the library reported for this frame: the residual compares with the server at Time - Delay. NaN when it reported none.</summary>
        public double Delay;
        public FidelityPhase Phase;
        public float Raw;
        public float Residual;
        public float AlongTrack;
        public float CrossTrack;
        public float RotationRaw;
        public float RotationResidual;
        public float VelocityError;
        public float ServerSpeed;
        public Vector3 Visual;
        /// <summary>Server position at Time.</summary>
        public Vector3 Server;
        /// <summary>Server position at Time - Delay (at Time when there is no delay).</summary>
        public Vector3 ServerDelayed;
    }

    /// <summary>
    /// Log-spaced histogram for percentiles of non-negative values. 256 bins from 0.1 mm to 100 m, about 5%
    /// apart, so percentiles are accurate to a few percent. Merging two is a bin-wise sum.
    /// </summary>
    public sealed class LogHistogram
    {
        const int Bins = 256;
        const double MinValue = 1e-4;
        const double MaxValue = 1e2;
        static readonly double LogMin = Math.Log(MinValue);
        static readonly double BinsPerLog = (Bins - 1) / (Math.Log(MaxValue) - LogMin);

        readonly int[] _bins = new int[Bins];

        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double SumSquares { get; private set; }
        public double Max { get; private set; }

        public double Mean => Count > 0 ? Sum / Count : double.NaN;
        public double Rms => Count > 0 ? Math.Sqrt(SumSquares / Count) : double.NaN;

        public void Add(double value)
        {
            if (double.IsNaN(value))
                return;
            value = Math.Max(0.0, value);
            _bins[BinOf(value)]++;
            Count++;
            Sum += value;
            SumSquares += value * value;
            if (value > Max)
                Max = value;
        }

        /// <summary>Value at or below which a fraction q of the samples fall: the geometric centre of its bin, never above the true maximum.</summary>
        public double Percentile(double q)
        {
            if (Count == 0)
                return double.NaN;
            long target = Math.Max(1L, (long)Math.Ceiling(q * Count));
            long seen = 0;
            for (int i = 0; i < Bins; i++)
            {
                seen += _bins[i];
                if (seen >= target)
                    return Math.Min(Centre(i), Max);
            }
            return Max;
        }

        public void Merge(LogHistogram other)
        {
            for (int i = 0; i < Bins; i++)
                _bins[i] += other._bins[i];
            Count += other.Count;
            Sum += other.Sum;
            SumSquares += other.SumSquares;
            Max = Math.Max(Max, other.Max);
        }

        public void Clear()
        {
            Array.Clear(_bins, 0, Bins);
            Count = 0;
            Sum = SumSquares = Max = 0.0;
        }

        static int BinOf(double value)
        {
            if (value <= MinValue)
                return 0;
            return Math.Min(Bins - 1, 1 + (int)((Math.Log(value) - LogMin) * BinsPerLog));
        }

        static double Centre(int bin) => bin == 0 ? 0.5 * MinValue : Math.Exp(LogMin + (bin - 0.5) / BinsPerLog);
    }

    /// <summary>
    /// The server's path for one entity: states sorted by timeline time, interpolated linearly in between.
    /// Linear matches the physics step itself: a semi-implicit Euler step moves a body in a straight line at
    /// its new velocity, so there is no more detail between two ticks than the straight line carries.
    /// </summary>
    public sealed class ServerTrack
    {
        readonly double[] _times;
        readonly Vector3[] _positions;
        readonly Quaternion[] _rotations;
        // True when the step from the previous sample is a teleport, which nothing interpolates across.
        readonly bool[] _breakBefore;
        int _start;

        public ServerTrack(int capacity)
        {
            _times = new double[capacity];
            _positions = new Vector3[capacity];
            _rotations = new Quaternion[capacity];
            _breakBefore = new bool[capacity];
        }

        public int Count { get; private set; }
        public double StartTime => Count > 0 ? _times[_start] : double.NaN;
        public double EndTime => Count > 0 ? _times[Slot(Count - 1)] : double.NaN;

        public double TimeAt(int index) => _times[Slot(index)];
        public Vector3 PositionAt(int index) => _positions[Slot(index)];

        int Slot(int index) => (_start + index) % _times.Length;

        public void Clear()
        {
            _start = 0;
            Count = 0;
        }

        /// <summary>Inserts a state in time order.</summary>
        /// <returns>Index of the new state, or -1 when its time was already there (the first state for a time is kept) or is too old to keep.</returns>
        public int Add(double time, Vector3 position, Quaternion rotation, float teleportSpeed)
        {
            int capacity = _times.Length;
            int index;
            if (Count == 0 || time > EndTime)
            {
                if (Count == capacity)
                {
                    _start = (_start + 1) % capacity;
                    Count--;
                }
                index = Count;
            }
            else
            {
                // Arrived out of order, which the unreliable channels allow.
                index = UpperBound(time);
                if (index > 0 && Math.Abs(TimeAt(index - 1) - time) < 1e-9)
                    return -1;
                if (Count == capacity)
                {
                    if (index == 0)
                        return -1;
                    _start = (_start + 1) % capacity;
                    Count--;
                    index--;
                }
                for (int i = Count; i > index; i--)
                {
                    int to = Slot(i);
                    int from = Slot(i - 1);
                    _times[to] = _times[from];
                    _positions[to] = _positions[from];
                    _rotations[to] = _rotations[from];
                    _breakBefore[to] = _breakBefore[from];
                }
            }

            Count++;
            int slot = Slot(index);
            _times[slot] = time;
            _positions[slot] = position;
            _rotations[slot] = rotation;
            UpdateBreak(index, teleportSpeed);
            if (index + 1 < Count)
                UpdateBreak(index + 1, teleportSpeed);
            return index;
        }

        void UpdateBreak(int index, float teleportSpeed)
        {
            int slot = Slot(index);
            if (index == 0)
            {
                _breakBefore[slot] = false;
                return;
            }
            int previous = Slot(index - 1);
            double dt = _times[slot] - _times[previous];
            float distance = (_positions[slot] - _positions[previous]).magnitude;
            _breakBefore[slot] = dt <= 0.0 || distance / dt > teleportSpeed;
        }

        /// <summary>First index whose time is greater than t.</summary>
        int UpperBound(double t)
        {
            int lo = 0;
            int hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (TimeAt(mid) <= t)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        /// <summary>Finds the two states around t. False outside the track, across a teleport, or across a gap longer than maxGap.</summary>
        bool TryFindSegment(double t, double maxGap, out int from, out float alpha)
        {
            from = -1;
            alpha = 0f;
            if (Count < 2 || t < StartTime || t > EndTime)
                return false;

            int to = Math.Min(UpperBound(t), Count - 1);
            from = to - 1;
            double t0 = TimeAt(from);
            double gap = TimeAt(to) - t0;
            if (gap > maxGap || _breakBefore[Slot(to)])
                return false;
            alpha = gap > 0.0 ? (float)((t - t0) / gap) : 0f;
            return true;
        }

        /// <summary>True when a teleport lies between the two times, so the path on one side says nothing about the other.</summary>
        public bool HasBreakBetween(double t0, double t1)
        {
            if (t1 < t0)
                (t0, t1) = (t1, t0);
            // A break sits on the later state of its step. The steps overlapping [t0, t1] end at the first
            // state after t0 through the first state after t1.
            int last = Math.Min(UpperBound(t1), Count - 1);
            for (int i = Math.Max(1, UpperBound(t0)); i <= last; i++)
                if (_breakBefore[Slot(i)])
                    return true;
            return false;
        }

        /// <summary>Server pose at t, and the velocity of the step it falls in.</summary>
        public bool TrySample(double t, double maxGap, out Vector3 position, out Quaternion rotation, out Vector3 velocity)
        {
            if (!TryFindSegment(t, maxGap, out int from, out float alpha))
            {
                position = default;
                rotation = Quaternion.identity;
                velocity = default;
                return false;
            }

            int a = Slot(from);
            int b = Slot(from + 1);
            position = Vector3.LerpUnclamped(_positions[a], _positions[b], alpha);
            rotation = Quaternion.Slerp(_rotations[a], _rotations[b], alpha);
            velocity = (_positions[b] - _positions[a]) / (float)(_times[b] - _times[a]);
            return true;
        }

        public bool TryPosition(double t, double maxGap, out Vector3 position)
        {
            if (!TryFindSegment(t, maxGap, out int from, out float alpha))
            {
                position = default;
                return false;
            }
            position = Vector3.LerpUnclamped(_positions[Slot(from)], _positions[Slot(from + 1)], alpha);
            return true;
        }

        /// <summary>How much the velocity changed at state index: the step after it against the step before it.</summary>
        public bool TryVelocityChange(int index, double maxGap, out float deltaV)
        {
            deltaV = 0f;
            if (index < 1 || index + 1 >= Count)
                return false;

            int a = Slot(index - 1);
            int b = Slot(index);
            int c = Slot(index + 1);
            double before = _times[b] - _times[a];
            double after = _times[c] - _times[b];
            if (before <= 0.0 || after <= 0.0 || before > maxGap || after > maxGap || _breakBefore[b] || _breakBefore[c])
                return false;

            Vector3 v0 = (_positions[b] - _positions[a]) / (float)before;
            Vector3 v1 = (_positions[c] - _positions[b]) / (float)after;
            deltaV = (v1 - v0).magnitude;
            return true;
        }
    }

    /// <summary>An impact seen on some entity's server path.</summary>
    public struct FidelityImpact
    {
        public double Time;
        public Vector3 Position;
        /// <summary>Size of the entity it happened to, for the "was another entity touching it" test.</summary>
        public float Radius;
        public int Source;
        public float DeltaV;
    }

    /// <summary>
    /// Impacts from every probe in the process. Shared because a collision often barely changes one of the
    /// two bodies (a player hitting a light ball), and the frames of both should still count as collision.
    /// </summary>
    public sealed class ImpactRegistry
    {
        readonly FidelityImpact[] _items;
        int _next;

        public ImpactRegistry(int capacity = 512)
        {
            _items = new FidelityImpact[capacity];
        }

        public int Count { get; private set; }

        /// <summary>Impacts in no particular order.</summary>
        public FidelityImpact this[int index] => _items[index];

        public void Add(in FidelityImpact impact)
        {
            for (int i = 0; i < Count; i++)
                if (_items[i].Source == impact.Source && Math.Abs(_items[i].Time - impact.Time) < 1e-6)
                    return;
            _items[_next] = impact;
            _next = (_next + 1) % _items.Length;
            if (Count < _items.Length)
                Count++;
        }

        public void Clear()
        {
            _next = 0;
            Count = 0;
        }
    }

    /// <summary>Statistics of compared frames, per phase and metric, plus collision episodes. Merges across probes for the summary CSV.</summary>
    public sealed class FidelityStats
    {
        public const int PhaseCount = 4;
        public const int MetricCount = 7;

        readonly LogHistogram[] _histograms = new LogHistogram[PhaseCount * MetricCount];

        /// <summary>Distance the visual travelled over the compared frames, per phase.</summary>
        public readonly double[] VisualPath = new double[PhaseCount];
        /// <summary>Distance the server travelled over the same frames, per phase.</summary>
        public readonly double[] ServerPath = new double[PhaseCount];

        /// <summary>Sum of the reported visual delays, in seconds, per phase, over the frames that had one.</summary>
        public readonly double[] DelaySum = new double[PhaseCount];
        public readonly long[] DelayFrames = new long[PhaseCount];

        /// <summary>Highest residual error of each collision episode, in metres.</summary>
        public readonly LogHistogram EpisodePeak = new LogHistogram();
        /// <summary>Seconds from the impact until the residual error last exceeded the settle threshold.</summary>
        public readonly LogHistogram EpisodeSettle = new LogHistogram();
        public long Episodes;
        /// <summary>Episodes whose error was still above the settle threshold when the collision window ended.</summary>
        public long UnsettledEpisodes;
        /// <summary>Residual error integrated over time across all episodes, in metre-seconds.</summary>
        public double EpisodeIntegral;

        /// <summary>Frames that could not be compared: no server state around their time.</summary>
        public long Unmatched;

        public FidelityStats()
        {
            for (int i = 0; i < _histograms.Length; i++)
                _histograms[i] = new LogHistogram();
        }

        public LogHistogram Get(FidelityPhase phase, FidelityMetric metric) => _histograms[(int)phase * MetricCount + (int)metric];

        public long Frames(FidelityPhase phase) => Get(phase, FidelityMetric.Raw).Count;

        /// <summary>How much further the visual travelled than the server over the same frames, as a fraction. NaN when the server barely moved.</summary>
        public double ExtraPath(FidelityPhase phase)
        {
            double server = ServerPath[(int)phase];
            return server > 0.5 ? VisualPath[(int)phase] / server - 1.0 : double.NaN;
        }

        /// <summary>Mean visual delay the library reported, in seconds. NaN when it reported none.</summary>
        public double MeanDelay(FidelityPhase phase)
        {
            long frames = DelayFrames[(int)phase];
            return frames > 0 ? DelaySum[(int)phase] / frames : double.NaN;
        }

        internal void Add(in FidelitySample s)
        {
            if (!double.IsNaN(s.Delay))
            {
                DelaySum[(int)FidelityPhase.All] += s.Delay;
                DelayFrames[(int)FidelityPhase.All]++;
                if (s.Phase != FidelityPhase.All)
                {
                    DelaySum[(int)s.Phase] += s.Delay;
                    DelayFrames[(int)s.Phase]++;
                }
            }
            AddMetric(s.Phase, FidelityMetric.Raw, s.Raw);
            AddMetric(s.Phase, FidelityMetric.Residual, s.Residual);
            AddMetric(s.Phase, FidelityMetric.AlongTrack, s.AlongTrack);
            AddMetric(s.Phase, FidelityMetric.CrossTrack, s.CrossTrack);
            AddMetric(s.Phase, FidelityMetric.RotationRaw, s.RotationRaw);
            AddMetric(s.Phase, FidelityMetric.RotationResidual, s.RotationResidual);
            AddMetric(s.Phase, FidelityMetric.VelocityError, s.VelocityError);
        }

        void AddMetric(FidelityPhase phase, FidelityMetric metric, float value)
        {
            if (float.IsNaN(value))
                return;
            Get(FidelityPhase.All, metric).Add(value);
            if (phase != FidelityPhase.All)
                Get(phase, metric).Add(value);
        }

        internal void AddPath(FidelityPhase phase, float visual, float server)
        {
            VisualPath[(int)FidelityPhase.All] += visual;
            ServerPath[(int)FidelityPhase.All] += server;
            if (phase == FidelityPhase.All)
                return;
            VisualPath[(int)phase] += visual;
            ServerPath[(int)phase] += server;
        }

        internal void AddEpisode(float peak, double settleSeconds, double integral, bool unsettled)
        {
            Episodes++;
            if (unsettled)
                UnsettledEpisodes++;
            EpisodePeak.Add(peak);
            EpisodeSettle.Add(settleSeconds);
            EpisodeIntegral += integral;
        }

        public void Merge(FidelityStats other)
        {
            for (int i = 0; i < _histograms.Length; i++)
                _histograms[i].Merge(other._histograms[i]);
            for (int i = 0; i < PhaseCount; i++)
            {
                VisualPath[i] += other.VisualPath[i];
                ServerPath[i] += other.ServerPath[i];
                DelaySum[i] += other.DelaySum[i];
                DelayFrames[i] += other.DelayFrames[i];
            }
            EpisodePeak.Merge(other.EpisodePeak);
            EpisodeSettle.Merge(other.EpisodeSettle);
            Episodes += other.Episodes;
            UnsettledEpisodes += other.UnsettledEpisodes;
            EpisodeIntegral += other.EpisodeIntegral;
            Unmatched += other.Unmatched;
        }

        public void Clear()
        {
            foreach (var h in _histograms)
                h.Clear();
            Array.Clear(VisualPath, 0, PhaseCount);
            Array.Clear(ServerPath, 0, PhaseCount);
            Array.Clear(DelaySum, 0, PhaseCount);
            Array.Clear(DelayFrames, 0, PhaseCount);
            EpisodePeak.Clear();
            EpisodeSettle.Clear();
            Episodes = UnsettledEpisodes = Unmatched = 0;
            EpisodeIntegral = 0.0;
        }
    }


    /// <summary>
    /// Compares one entity's rendered frames with its server path.
    ///
    /// Frames and server states share one timeline, in seconds: for a tick based library the state after
    /// tick T sits at T × tick length, and a frame drawn a fraction f into the next tick sits at (T + f) ×
    /// tick length. A frame is only compared once server states cover it, because the server state for a
    /// predicted tick arrives a round trip after the client drew it.
    ///
    /// Every compared frame gets two errors. Raw compares with the server at the same moment, which is what
    /// the player sees against the truth. Residual compares with the server at the moment the library says
    /// the visual shows: the frame's time minus the visual delay the library reported for that frame. That
    /// separates the deliberate delay (interpolation, smoothing) from the error in the shape of the path.
    /// </summary>
    public sealed class FidelityTracker
    {
        const int PendingCapacity = 2048;
        const int ServerCapacity = 2048;
        // Extra server coverage required past a frame, so the step after it is known for impacts.
        const double ReadyMarginSeconds = 0.05;
        // A clock this far behind the buffered data means a new session restarted the timeline.
        const double TimelineJumpSeconds = 10.0;

        public readonly int Id;
        public readonly FidelitySettings Settings;
        public readonly ServerTrack Track = new ServerTrack(ServerCapacity);
        public readonly FidelityStats Stats = new FidelityStats();
        /// <summary>Size of the entity, for the shared impact test. Feeders or the probe set it from the colliders.</summary>
        public float Radius = 1f;

        readonly ImpactRegistry _impacts;

        /// <summary>Raised for every compared frame, e.g. to write it to a file.</summary>
        public event Action<FidelitySample> Evaluated;

        public bool HasLast { get; private set; }
        public FidelitySample Last { get; private set; }

        // Frames waiting for server coverage, oldest first.
        readonly double[] _pendingTimes = new double[PendingCapacity];
        readonly double[] _pendingDelays = new double[PendingCapacity];
        readonly int[] _pendingFrames = new int[PendingCapacity];
        readonly Vector3[] _pendingPositions = new Vector3[PendingCapacity];
        readonly Quaternion[] _pendingRotations = new Quaternion[PendingCapacity];
        int _pendingStart;
        int _pendingCount;

        // Previous compared frame, for velocity and path length.
        bool _havePrevious;
        bool _previousResidualKnown;
        double _previousTime;
        Vector3 _previousVisual;
        Vector3 _previousServer;

        // Open collision episode.
        bool _inEpisode;
        double _episodeImpact;
        float _episodePeak;
        double _episodeIntegral;
        double _lastAbove;
        bool _endedAbove;

        public FidelityTracker(int id, FidelitySettings settings, ImpactRegistry impacts)
        {
            Id = id;
            Settings = settings ?? new FidelitySettings();
            _impacts = impacts ?? new ImpactRegistry();
        }

        public void AddServerState(double time, Vector3 position, Quaternion rotation)
        {
            if (Track.Count > 0 && time < Track.EndTime - TimelineJumpSeconds)
                ResetTimeline();

            int index = Track.Add(time, position, rotation, Settings.teleportSpeed);

            // Only appends are checked: states arrive in order nearly always, and a late one is rare enough to skip.
            if (index > 1 && index == Track.Count - 1 &&
                Track.TryVelocityChange(index - 1, Settings.maxServerGapSeconds, out float deltaV) &&
                deltaV > Settings.impactDeltaV)
            {
                _impacts.Add(new FidelityImpact
                {
                    Time = Track.TimeAt(index - 1),
                    Position = Track.PositionAt(index - 1),
                    Radius = Radius,
                    Source = Id,
                    DeltaV = deltaV,
                });
            }
        }

        /// <param name="delay">
        /// How far behind the frame's time the visual shows the server, in seconds, as the library reports it
        /// for this frame. Negative when it draws ahead (extrapolation). NaN when the library can't tell.
        /// </param>
        /// <param name="frame">Rendered frame number, carried through to the sample for per-frame graphs.</param>
        public void AddVisual(double time, Vector3 position, Quaternion rotation, double delay, int frame = 0)
        {
            // Nothing to compare with yet. Not counted as unmatched: the entity just appeared.
            if (Track.Count < 2)
                return;

            if (_pendingCount > 0 && time < _pendingTimes[(_pendingStart + _pendingCount - 1) % PendingCapacity] - TimelineJumpSeconds)
            {
                _pendingCount = 0;
                _havePrevious = false;
            }
            if (_pendingCount == PendingCapacity)
                EvaluateOldest();

            int slot = (_pendingStart + _pendingCount) % PendingCapacity;
            _pendingTimes[slot] = time;
            _pendingDelays[slot] = delay;
            _pendingFrames[slot] = frame;
            _pendingPositions[slot] = position;
            _pendingRotations[slot] = rotation;
            _pendingCount++;
        }

        /// <summary>Compares every waiting frame the server path now covers. now is the current timeline time.</summary>
        public void Process(double now)
        {
            double margin = Settings.collisionPreSeconds + ReadyMarginSeconds;
            while (_pendingCount > 0)
            {
                double t = _pendingTimes[_pendingStart];
                double delay = _pendingDelays[_pendingStart];
                // The frame is compared at t and at t - delay, which is later than t for a visual drawn ahead.
                double latest = double.IsNaN(delay) ? t : Math.Max(t, t - delay);
                bool covered = Track.Count >= 2 && Track.EndTime >= latest + margin;
                if (!covered && now - t < Settings.evalTimeoutSeconds)
                    break;
                EvaluateOldest();
            }
        }

        /// <summary>Clears the statistics. The buffered paths are kept.</summary>
        public void ResetStats()
        {
            Stats.Clear();
            _inEpisode = false;
        }

        void ResetTimeline()
        {
            Track.Clear();
            _pendingCount = 0;
            _havePrevious = false;
            _inEpisode = false;
        }

        void EvaluateOldest()
        {
            int slot = _pendingStart;
            _pendingStart = (_pendingStart + 1) % PendingCapacity;
            _pendingCount--;
            Evaluate(_pendingTimes[slot], _pendingPositions[slot], _pendingRotations[slot], _pendingDelays[slot], _pendingFrames[slot]);
        }

        void Evaluate(double t, Vector3 visual, Quaternion visualRotation, double delay, int frame)
        {
            FidelitySettings s = Settings;
            double maxGap = s.maxServerGapSeconds;

            if (!Track.TrySample(t, maxGap, out Vector3 server, out Quaternion serverRotation, out Vector3 velocity))
            {
                Stats.Unmatched++;
                _havePrevious = false;
                return;
            }

            bool delayKnown = !double.IsNaN(delay);
            double shown = delayKnown ? t - delay : t;
            Vector3 delayed = server;
            Quaternion delayedRotation = serverRotation;
            // Across a teleport the two sides of the comparison are different places, not an error.
            if (delayKnown && (Track.HasBreakBetween(shown, t) ||
                               !Track.TrySample(shown, maxGap, out delayed, out delayedRotation, out velocity)))
            {
                Stats.Unmatched++;
                _havePrevious = false;
                return;
            }

            float speed = velocity.magnitude;
            bool collision = TryGetImpact(shown, out double impactTime);
            FidelityPhase phase = collision ? FidelityPhase.Collision
                : speed < s.idleSpeed ? FidelityPhase.Idle
                : FidelityPhase.Moving;

            // Without a delay, everything measured against the delayed server would be the raw error again,
            // so it is left out. A still object is the exception: any delay gives the same answer.
            bool residualKnown = delayKnown || phase == FidelityPhase.Idle;

            Vector3 error = visual - delayed;
            float along = float.NaN;
            float cross = float.NaN;
            if (residualKnown && speed > s.idleSpeed)
            {
                Vector3 direction = velocity / speed;
                float a = Vector3.Dot(error, direction);
                along = Mathf.Abs(a);
                cross = (error - a * direction).magnitude;
            }

            bool continuous = _havePrevious && _previousResidualKnown && residualKnown;
            float dt = _havePrevious ? (float)(t - _previousTime) : 0f;
            float velocityError = float.NaN;
            if (continuous && dt > 1e-5f)
                velocityError = ((visual - _previousVisual) / dt - velocity).magnitude;

            var sample = new FidelitySample
            {
                Time = t,
                Frame = frame,
                Delay = delay,
                Phase = phase,
                Raw = (visual - server).magnitude,
                Residual = residualKnown ? error.magnitude : float.NaN,
                AlongTrack = along,
                CrossTrack = cross,
                RotationRaw = Quaternion.Angle(visualRotation, serverRotation),
                RotationResidual = residualKnown ? Quaternion.Angle(visualRotation, delayedRotation) : float.NaN,
                VelocityError = velocityError,
                ServerSpeed = speed,
                Visual = visual,
                Server = server,
                ServerDelayed = delayed,
            };

            Stats.Add(sample);
            if (continuous && dt > 0f)
                Stats.AddPath(phase, (visual - _previousVisual).magnitude, (delayed - _previousServer).magnitude);
            if (residualKnown)
                UpdateEpisode(shown, collision, impactTime, sample.Residual, Mathf.Clamp(dt, 0f, 0.1f));

            _havePrevious = true;
            _previousResidualKnown = residualKnown;
            _previousTime = t;
            _previousVisual = visual;
            _previousServer = delayed;
            Last = sample;
            HasLast = true;
            Evaluated?.Invoke(sample);
        }

        /// <summary>
        /// Latest impact whose collision window contains t. The entity's own impacts always count. Another
        /// entity's count when this entity's server position at that moment was within reach of it.
        /// </summary>
        bool TryGetImpact(double t, out double impactTime)
        {
            FidelitySettings s = Settings;
            impactTime = double.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < _impacts.Count; i++)
            {
                FidelityImpact impact = _impacts[i];
                double since = t - impact.Time;
                if (since < -s.collisionPreSeconds || since > s.collisionPostSeconds || impact.Time <= impactTime)
                    continue;

                if (impact.Source != Id)
                {
                    if (!Track.TryPosition(impact.Time, s.maxServerGapSeconds, out Vector3 position))
                        continue;
                    float reach = impact.Radius + Radius + s.contactMargin;
                    if ((position - impact.Position).sqrMagnitude > reach * reach)
                        continue;
                }

                impactTime = impact.Time;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// A collision episode is a run of consecutive collision frames. Settle time runs from the latest
        /// impact in the episode until the residual error last exceeded the settle threshold.
        /// </summary>
        void UpdateEpisode(double shown, bool collision, double impactTime, float residual, float dt)
        {
            if (!collision)
            {
                if (_inEpisode)
                    CloseEpisode();
                return;
            }

            if (!_inEpisode)
            {
                _inEpisode = true;
                _episodeImpact = impactTime;
                _episodePeak = 0f;
                _episodeIntegral = 0.0;
                _lastAbove = double.NaN;
            }
            else if (impactTime > _episodeImpact)
            {
                _episodeImpact = impactTime;
            }

            _episodePeak = Mathf.Max(_episodePeak, residual);
            _episodeIntegral += residual * dt;
            _endedAbove = residual > Settings.settleEpsilon;
            if (_endedAbove)
                _lastAbove = shown;
        }

        void CloseEpisode()
        {
            _inEpisode = false;
            double settle = double.IsNaN(_lastAbove) ? 0.0 : Math.Max(0.0, _lastAbove - _episodeImpact);
            Stats.AddEpisode(_episodePeak, settle, _episodeIntegral, _endedAbove);
        }
    }
}
