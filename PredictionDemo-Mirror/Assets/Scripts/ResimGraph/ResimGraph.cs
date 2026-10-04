// Mirror collector for the ResimGraph overlay. Reads only Mirror's public API: PredictedRigidbody's public
// fields, NetworkTime, NetworkClient, NetworkDiagnostics and the transport's data callbacks.
// Setup: add this component to any GameObject in the Gameplay scene. See ResimGraph.md.
//
// Mirror's PredictedRigidbody has no prediction tick and no public correction event. It corrects inside
// OnDeserialize, which runs in Mirror's NetworkEarlyUpdate player loop system, by writing the server
// state (shifted along its recorded history) straight into the Rigidbody. Nothing else moves a
// Rigidbody during EarlyUpdate, so this graph brackets that system: any change in a body's position,
// rotation or velocity between "before" and "after" is a correction.

using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace PredictionDebug
{
    public class ResimGraph : ResimGraphBase
    {
        protected override string LibraryName => "Mirror (PredictedRigidbody)";

        struct MirrorBeforeReceive { }
        struct MirrorAfterReceive { }
        struct MirrorStepBegin { }
        struct MirrorStepEnd { }

        struct BodyState
        {
            public Rigidbody rb;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        [Header("Mirror")]
        [Tooltip("How often the list of PredictedRigidbody components in the scene is refreshed.")]
        [SerializeField] float bodyRefreshInterval = 0.5f;

        GraphStrip _jitter;

        bool _hookedReceive;
        bool _hookedStep;
        Transport _transport;
        bool _diagnostics;

        readonly TickClock _clock = new TickClock();
        readonly ByteRate _in = new ByteRate();
        readonly ByteRate _out = new ByteRate();

        readonly List<PredictedRigidbody> _bodies = new List<PredictedRigidbody>();
        readonly Dictionary<PredictedRigidbody, BodyState> _before = new Dictionary<PredictedRigidbody, BodyState>();
        float _nextBodyRefresh;
        double _receiveStart;

        PredictedRigidbody _localBody;
        readonly VisualJumpDetector _jump = new VisualJumpDetector();

        double _lastRemoteTimestamp = double.NaN;
        float _lastStateArrival = -1f;
        double _lastRtt = double.NaN;
        double _lastPredictionOffset = double.NaN;
        int _commandsThisFrame;

        protected override void Configure()
        {
            Resim.MarkSubstitute("CORRECTION EVENT",
                "Mirror never replays physics. Shown instead: frames where at least one PredictedRigidbody was corrected (pose or velocity written during network receive).");
            ResimDepth.MarkUnsupported("Mirror doesn't step physics again. It shifts its recorded state history by the correction delta, and how many history entries that touched is internal. TICK LEAD shows how far back corrections reach.");
            ResimCost.MarkSubstitute("NET RECV COST",
                "Wall time of Mirror's NetworkEarlyUpdate, which deserializes server state and runs every correction. Corrections have no separate timing.");
            ResimEntities.MarkComputed("Number of PredictedRigidbody bodies corrected during this frame's network receive.");
            PredictionError.MarkUnsupported("Mirror compares the server state with an interpolated sample of its private stateHistory; neither value is exposed. CORRECTION shows the resulting jump of the present state.");
            Correction.MarkComputed("Local player body: pose before vs after Mirror's NetworkEarlyUpdate on frames it was corrected.");
            TickCost.MarkComputed("Mirror predicts in Unity's FixedUpdate, so the tick is one fixed step: wall time of the whole FixedUpdate phase (scripts + physics).");
            TickGap.MarkComputed("Wall time between the starts of consecutive fixed steps.");
            ClockAdjust.MarkComputed("Change of NetworkTime.predictionErrorUnadjusted, the offset Mirror adds to predictedTime from server ping feedback. Mirror's interpolation timescale is internal.");
            ClockAdjust.Unit = "ms";
            ClockAdjust.Scale = 5f;
            ClockAdjust.GridStep = 1f;
            Latency.WithNote("NetworkTime.rtt (exponential moving average of pings), drawn when it updates.");
            TickLead.MarkComputed("Prediction horizon: (NetworkTime.predictedTime - NetworkClient.connection.remoteTimeStamp) in fixed steps. Mirror predicts in time, not ticks.");
            SnapshotAge.MarkComputed("Time since NetworkClient.connection.remoteTimeStamp last changed, i.e. since a server batch arrived.");
            InputBuffer.MarkSubstitute("CMDS RECEIVED / FRAME",
                "Mirror runs a [Command] as soon as it arrives, so the server keeps no input buffer. Shown instead (server only): CommandMessages received per frame via NetworkDiagnostics.InMessageEvent; 0 means a starved frame, >1 bunching.");
            InputBuffer.Unit = "";
            InputBuffer.Scale = 4f;
            PacketLoss.MarkUnsupported("Mirror and its KCP transport expose no loss counter for the unreliable channel PredictedRigidbody state travels on.");
            NetIn.WithNote("Transport.OnClientDataReceived / OnServerDataReceived bytes.");
            NetOut.WithNote("Transport.OnClientDataSent / OnServerDataSent bytes.");
            VisualJump.MarkComputed("Mirror has no jump event. The graph applies Ursitoare's test (move > 0.35 m or 2.5 deg in one frame) to the local player's rendered transform.");
            Smoothing.MarkComputed("Local player: rendered transform vs PredictedRigidbody.predictedRigidbody (the physics ghost while moving). 0 while idle, when Mirror keeps the Rigidbody on the rendered object.");

            _jitter = AddStrip("rtt_jitter", "RTT JITTER", "ms", StripKind.Bar,
                new Color(0.70f, 0.60f, 1f), new Color(1f, 0.40f, 0.30f), 50f, "0.0")
                .WithNote("Standard deviation from NetworkTime.rttVariance.");
        }

        protected override bool TrySubscribe()
        {
            if (!NetworkClient.active && !NetworkServer.active)
                return false;

            _hookedReceive = PlayerLoopHooks.Insert(typeof(UnityEngine.PlayerLoop.EarlyUpdate), typeof(NetworkLoop),
                typeof(MirrorBeforeReceive), BeforeReceive, typeof(MirrorAfterReceive), AfterReceive);
            if (!_hookedReceive)
                Debug.LogWarning("[ResimGraph][Mirror] NetworkLoop not found in EarlyUpdate, correction graphs stay empty.");

            _hookedStep = PlayerLoopHooks.Insert(typeof(UnityEngine.PlayerLoop.FixedUpdate), null,
                typeof(MirrorStepBegin), StepBegin, typeof(MirrorStepEnd), StepEnd);

            _transport = Transport.active;
            if (_transport != null)
            {
                _transport.OnClientDataReceived += OnClientReceived;
                _transport.OnClientDataSent += OnClientSent;
                _transport.OnServerDataReceived += OnServerReceived;
                _transport.OnServerDataSent += OnServerSent;
            }

            NetworkDiagnostics.InMessageEvent += OnInMessage;
            _diagnostics = true;

            _clock.Reset();
            _nextBodyRefresh = 0f;
            _lastRemoteTimestamp = double.NaN;
            _lastStateArrival = -1f;
            _lastRtt = double.NaN;
            _lastPredictionOffset = double.NaN;

            // Host mode doesn't predict at all (PredictedRigidbody only corrects on client-only).
            bool predicting = NetworkClient.active && !NetworkServer.active;
            foreach (var s in new[] { Resim, ResimCost, ResimEntities, Correction, ClockAdjust, Latency, TickLead, SnapshotAge, VisualJump, Smoothing, _jitter })
            {
                s.Inactive = !predicting;
                s.InactiveReason = NetworkServer.active ? "(no prediction on server/host)" : "(client only)";
            }
            InputBuffer.Inactive = !NetworkServer.active;
            InputBuffer.InactiveReason = "(server only)";
            return true;
        }

        protected override bool IsSubscriptionStale() => !NetworkClient.active && !NetworkServer.active;

        protected override void Unsubscribe()
        {
            if (_hookedReceive)
                PlayerLoopHooks.Remove(typeof(MirrorBeforeReceive), typeof(MirrorAfterReceive));
            if (_hookedStep)
                PlayerLoopHooks.Remove(typeof(MirrorStepBegin), typeof(MirrorStepEnd));
            _hookedReceive = _hookedStep = false;

            if (_transport != null)
            {
                _transport.OnClientDataReceived -= OnClientReceived;
                _transport.OnClientDataSent -= OnClientSent;
                _transport.OnServerDataReceived -= OnServerReceived;
                _transport.OnServerDataSent -= OnServerSent;
                _transport = null;
            }

            if (_diagnostics)
                NetworkDiagnostics.InMessageEvent -= OnInMessage;
            _diagnostics = false;

            _bodies.Clear();
            _before.Clear();
            _localBody = null;
        }

        void OnClientReceived(ArraySegment<byte> data, int channel) => _in.Add(data.Count);
        void OnClientSent(ArraySegment<byte> data, int channel) => _out.Add(data.Count);
        void OnServerReceived(int conn, ArraySegment<byte> data, int channel) => _in.Add(data.Count);
        void OnServerSent(int conn, ArraySegment<byte> data, int channel) => _out.Add(data.Count);

        void OnInMessage(NetworkDiagnostics.MessageInfo info)
        {
            if (NetworkServer.active && info.message is CommandMessage)
                _commandsThisFrame += info.count;
        }

        void StepBegin() => _clock.Begin(TickGap);
        void StepEnd() => _clock.End(TickCost);

        void RefreshBodies()
        {
            if (Time.unscaledTime < _nextBodyRefresh)
                return;
            _nextBodyRefresh = Time.unscaledTime + bodyRefreshInterval;

            _bodies.Clear();
            _bodies.AddRange(FindObjectsByType<PredictedRigidbody>(FindObjectsSortMode.None));

            var player = NetworkClient.localPlayer;
            _localBody = player != null ? player.GetComponent<PredictedRigidbody>() : null;
        }

        void BeforeReceive()
        {
            _receiveStart = TickClock.NowSeconds;
            if (!NetworkClient.active || NetworkServer.active)
                return;

            RefreshBodies();
            _before.Clear();
            foreach (var body in _bodies)
            {
                if (body == null || body.predictedRigidbody == null)
                    continue;
                var rb = body.predictedRigidbody;
                _before[body] = new BodyState
                {
                    rb = rb,
                    position = rb.position,
                    rotation = rb.rotation,
                    velocity = rb.linearVelocity,
                    angularVelocity = rb.angularVelocity,
                };
            }
        }

        void AfterReceive()
        {
            double elapsedMs = (TickClock.NowSeconds - _receiveStart) * 1000.0;
            if (!NetworkClient.active || NetworkServer.active)
                return;

            ResimCost.Add((float)elapsedMs);

            int corrected = 0;
            foreach (var pair in _before)
            {
                var body = pair.Key;
                var was = pair.Value;
                // The ghost may have been swapped in or out, in which case there is nothing to compare.
                if (body == null || body.predictedRigidbody != was.rb)
                    continue;

                var rb = was.rb;
                Vector3 position = rb.position;
                Quaternion rotation = rb.rotation;
                if (position == was.position && rotation == was.rotation &&
                    rb.linearVelocity == was.velocity && rb.angularVelocity == was.angularVelocity)
                    continue;

                corrected++;
                if (body == _localBody)
                    PushPoseError(Correction, position, rotation, was.position, was.rotation);
            }

            if (corrected > 0)
            {
                Resim.Event();
                ResimEntities.Max(corrected);
            }
        }

        protected override void Collect()
        {
            NetIn.Set(_in.KBps());
            NetOut.Set(_out.KBps());

            if (NetworkServer.active)
            {
                InputBuffer.Set(_commandsThisFrame);
                _commandsThisFrame = 0;
                return;
            }
            _commandsThisFrame = 0;

            if (!NetworkClient.active)
                return;

            RefreshBodies();

            double rtt = NetworkTime.rtt;
            if (rtt != _lastRtt)
            {
                _lastRtt = rtt;
                Latency.Max((float)(rtt * 1000.0));
                _jitter.Max((float)(Math.Sqrt(Math.Max(0.0, NetworkTime.rttVariance)) * 1000.0));
            }

            double offset = NetworkTime.predictionErrorUnadjusted;
            if (!double.IsNaN(_lastPredictionOffset) && offset != _lastPredictionOffset)
                ClockAdjust.Set((float)((offset - _lastPredictionOffset) * 1000.0));
            _lastPredictionOffset = offset;

            var connection = NetworkClient.connection;
            if (connection != null)
            {
                double remote = connection.remoteTimeStamp;
                if (remote != _lastRemoteTimestamp)
                {
                    _lastRemoteTimestamp = remote;
                    _lastStateArrival = Time.unscaledTime;
                }
                if (remote > 0.0 && Time.fixedDeltaTime > 0f)
                    TickLead.Set((float)((NetworkTime.predictedTime - remote) / Time.fixedDeltaTime));
            }
            if (_lastStateArrival >= 0f)
                SnapshotAge.Set((Time.unscaledTime - _lastStateArrival) * 1000f);

            if (_localBody != null)
            {
                Transform rendered = _localBody.transform;
                _jump.Sample(this, rendered);
                var rb = _localBody.predictedRigidbody;
                if (rb != null)
                    Smoothing.Set((rendered.position - rb.position).magnitude);
            }
            else
            {
                _jump.Sample(this, null);
            }
        }
    }
}
