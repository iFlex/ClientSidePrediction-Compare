// PurrNet / PurrDiction collector for the ResimGraph overlay. Reads only the public API: PurrDiction's
// PredictionManager rollback, physics pass and diagnostic counters, PredictedRigidbody / PredictedTransform,
// PurrNet's NetworkManager tick events, TickManager and StatisticsManager.
// Setup: add this component to any GameObject in the Gameplay scene. See ResimGraph.md.

using PurrNet;
using PurrNet.Prediction;
using UnityEngine;

namespace PredictionDebug
{
    public class ResimGraph : ResimGraphBase
    {
        protected override string LibraryName => "PurrNet (PurrDiction)";

        [Header("PurrNet")]
        [Tooltip("PurrNet only measures ping jitter, packet loss and bandwidth in a StatisticsManager. Adds one at startup when the scene has none. It sends about 20 small ping packets a second, which show up in NET OUT.")]
        [SerializeField] bool addStatisticsManagerIfMissing = true;

        GraphStrip _jitter;
        GraphStrip _leadAdjust;
        GraphStrip _viewStarved;

        NetworkManager _nm;
        PredictionManager _pm;
        StatisticsManager _stats;
        bool _tickOnServer;

        protected override float TickStepMs => _pm != null && _pm.tickDelta > 0f ? _pm.tickDelta * 1000f : base.TickStepMs;

        readonly TickClock _clock = new TickClock();
        readonly PoseHistory _history = new PoseHistory();
        readonly VisualJumpDetector _jump = new VisualJumpDetector();

        PredictedRigidbody _local;
        PredictedTransform _localTransform;
        Transform _localRendered;
        float _nextLocalSearch;

        // Rollback in progress.
        double _rollbackStart;
        ulong _appliesAtStart;
        int _passesInRollback;
        bool _inRollback;
        bool _havePrePose;
        Vector3 _prePosition;
        Quaternion _preRotation;

        ulong _lastFramesReceived;
        float _lastStateArrival = -1f;
        ulong _lastLeadAdjusts;
        ulong _lastViewStarved;
        int _lastPredictedCount;

        protected override void Configure()
        {
            Resim.WithNote("A rollback that applied at least one server frame: PredictionManager.onStartingToRollback / onRollbackFinished, confirmed by tickPhaseFrameAppliesTotal + renderPhaseFrameAppliesTotal advancing. PurrDiction rolls back on every verified frame, not only on a mismatch.");
            ResimDepth.MarkComputed("Physics passes during the rollback (onBeforePhysicsPass while isReplaying): the verified frames plus the replay up to the local tick.");
            ResimCost.MarkComputed("Wall time between onStartingToRollback and onRollbackFinished.");
            ResimEntities.MarkComputed("PredictedRigidbody bodies in the scene, counted on frames that rolled back. PurrDiction rolls back and replays the whole predicted world together (non-physics identities too).");
            PredictionError.MarkComputed("Local player: rigidbody pose at onBeforePhysicsPass for each predicted tick vs the pose at the same point of the verified replay of that tick (isVerified).");
            Correction.MarkComputed("Local player rigidbody pose at onStartingToRollback vs onRollbackFinished: how far applying the server frame moved the present state.");
            TickCost.MarkComputed("Wall time from NetworkManager.onPreTick to onPostTick. NetworkManager forwards the tick before PurrDiction's own handler, so the forward simulation is included and the rollback (graphed as RESIM COST) is not.");
            TickGap.MarkComputed("Wall time between consecutive NetworkManager.onPreTick events.");
            ClockAdjust.WithNote("(PredictionManager.currentTickPacingScale - 1) x 100: PurrDiction's input-slack controller speeding up (+) or slowing down (-) the client tick, clamped to +-2%.");
            Latency.WithNote("TickManager.rtt, drawn when it updates.");
            TickLead.WithNote("PredictionManager.localTick minus the local player's PredictedIdentity.lastVerifiedTick.");
            SnapshotAge.MarkComputed("Time since PredictionManager.framesReceivedTotal last advanced.");
            InputBuffer.MarkSubstitute("INPUT SLACK",
                "The server's input queue is private. Shown instead: PredictionManager.lastInputSlackMs, which the server echoes to the client: how long before it was needed the newest input arrived, converted to ticks. Negative (clamped to 0 here) means late.");
            InputBuffer.Unit = "t";
            PacketLoss.WithNote("StatisticsManager.packetLoss: percent of its ping sequence packets lost over a 5 s window.");
            PacketLoss.Kind = StripKind.Bar;
            PacketLoss.Unit = "%";
            PacketLoss.Format = "0";
            PacketLoss.Scale = Mathf.Max(1f, packetLossScale);
            NetIn.WithNote("StatisticsManager.download.");
            NetOut.WithNote("StatisticsManager.upload.");
            VisualJump.MarkComputed("PurrDiction has no jump event. The graph applies Ursitoare's test (move > 0.35 m or 2.5 deg in one frame) to the local player's rendered transform.");
            Smoothing.MarkComputed("Local player: rendered transform (PredictedTransform.graphics, or the first Renderer) vs the rigidbody. The demo prefab leaves graphics unassigned, so the rendered object is the rigidbody and this stays 0.");

            _jitter = AddStrip("rtt_jitter", "RTT JITTER", "ms", StripKind.Bar,
                new Color(0.70f, 0.60f, 1f), new Color(1f, 0.40f, 0.30f), 50f, "0")
                .WithNote("StatisticsManager.jitter.");
            _leadAdjust = AddStrip("lead_adjust", "LEAD JUMP / PAUSE", "", StripKind.Event,
                new Color(1f, 0.65f, 0.25f), new Color(1f, 0.20f, 0.20f), 1f)
                .WithNote("leadJumpsTotal + leadPausesTotal + minLeadSnapsTotal + starvationJumpsTotal deltas: hard corrections of the prediction head on top of the tick pacing.");
            _viewStarved = AddStrip("view_starved", "VIEW STARVED", "", StripKind.Event,
                new Color(0.60f, 0.85f, 1f), new Color(1f, 0.30f, 0.80f), 1f)
                .WithNote("viewBufferStarvedFramesTotal delta: view updates that ran with an empty interpolation buffer and held the last sample.");
        }

        protected virtual void Start()
        {
            if (!addStatisticsManagerIfMissing)
                return;
            _stats = FindAnyObjectByType<StatisticsManager>();
            if (_stats == null && NetworkManager.main != null)
                _stats = NetworkManager.main.gameObject.AddComponent<StatisticsManager>();
        }

        protected override bool TrySubscribe()
        {
            var nm = NetworkManager.main;
            if (nm == null || (!nm.isServer && !nm.isClient))
                return false;

            var pm = FindAnyObjectByType<PredictionManager>();
            if (pm == null || !pm.isSpawned)
                return false;

            _nm = nm;
            _pm = pm;
            if (_stats == null)
                _stats = FindAnyObjectByType<StatisticsManager>();

            // PurrDiction ticks on the server tick manager when there is one, the client's otherwise.
            _tickOnServer = nm.isServer;

            nm.onPreTick += OnPreTick;
            nm.onPostTick += OnPostTick;
            pm.onStartingToRollback += OnStartingToRollback;
            pm.onRollbackFinished += OnRollbackFinished;
            pm.onBeforePhysicsPass += OnBeforePhysicsPass;

            _clock.Reset();
            _history.Clear();
            _lastFramesReceived = pm.framesReceivedTotal;
            _lastLeadAdjusts = LeadAdjustTotal(pm);
            _lastViewStarved = pm.viewBufferStarvedFramesTotal;
            _lastStateArrival = -1f;
            _nextLocalSearch = 0f;

            bool predicting = nm.isClientOnly;
            foreach (var s in new[] { Resim, ResimDepth, ResimCost, ResimEntities, PredictionError, Correction, ClockAdjust, Latency, TickLead, SnapshotAge, InputBuffer, VisualJump, Smoothing, _leadAdjust, _viewStarved })
            {
                s.Inactive = !predicting;
                s.InactiveReason = nm.isServer ? "(no rollback on server/host)" : "(client only)";
            }
            bool haveStats = _stats != null;
            foreach (var s in new[] { PacketLoss, NetIn, NetOut, _jitter })
            {
                s.Inactive = !haveStats;
                s.InactiveReason = "(needs a StatisticsManager)";
            }
            return true;
        }

        protected override bool IsSubscriptionStale() =>
            _nm == null || _pm == null || !_pm.isSpawned || (!_nm.isServer && !_nm.isClient);

        protected override void Unsubscribe()
        {
            if (_nm != null)
            {
                _nm.onPreTick -= OnPreTick;
                _nm.onPostTick -= OnPostTick;
            }
            if (_pm != null)
            {
                _pm.onStartingToRollback -= OnStartingToRollback;
                _pm.onRollbackFinished -= OnRollbackFinished;
                _pm.onBeforePhysicsPass -= OnBeforePhysicsPass;
            }
            _nm = null;
            _pm = null;
            _local = null;
            _localTransform = null;
            _localRendered = null;
            _inRollback = false;
        }

        static ulong LeadAdjustTotal(PredictionManager pm) =>
            pm.leadJumpsTotal + pm.leadPausesTotal + pm.minLeadSnapsTotal + pm.starvationJumpsTotal;

        static ulong AppliesTotal(PredictionManager pm) =>
            pm.tickPhaseFrameAppliesTotal + pm.renderPhaseFrameAppliesTotal;

        void RefreshLocalPlayer()
        {
            if (_local != null && _local.IsOwner())
                return;
            if (Time.unscaledTime < _nextLocalSearch)
                return;
            _nextLocalSearch = Time.unscaledTime + 0.5f;

            _local = null;
            _localTransform = null;
            _localRendered = null;
            foreach (var body in FindObjectsByType<PredictedRigidbody>(FindObjectsSortMode.None))
            {
                if (!body.IsOwner())
                    continue;
                _local = body;
                break;
            }
            if (_local == null)
                return;

            _localTransform = _local.GetComponent<PredictedTransform>();
            _localRendered = _localTransform != null && _localTransform.graphics != null ? _localTransform.graphics : null;
            if (_localRendered == null)
            {
                var renderer = _local.GetComponentInChildren<Renderer>();
                _localRendered = renderer != null ? renderer.transform : _local.transform;
            }
            _history.Clear();
        }

        void OnPreTick(bool asServer)
        {
            if (asServer == _tickOnServer)
                _clock.Begin(TickGap);
        }

        void OnPostTick(bool asServer)
        {
            if (asServer == _tickOnServer)
                _clock.End(TickCost);
        }

        void OnBeforePhysicsPass()
        {
            if (_pm == null || _local == null || _local.rb == null)
                return;

            if (_inRollback && _pm.isReplaying)
                _passesInRollback++;

            Rigidbody rb = _local.rb;
            ulong tick = _pm.localTickInContext;

            if (!_pm.isReplaying)
            {
                _history.Record(tick, rb.position, rb.rotation);
                return;
            }

            // The verified pass of a tick starts from the server's state for that tick.
            if (_pm.isVerified && !_pm.isCatchingUpFrames && _history.TryGet(tick, out var predictedPosition, out var predictedRotation))
                PushPoseError(PredictionError, predictedPosition, predictedRotation, rb.position, rb.rotation);
        }

        void OnStartingToRollback()
        {
            _inRollback = true;
            _rollbackStart = TickClock.NowSeconds;
            _appliesAtStart = AppliesTotal(_pm);
            _passesInRollback = 0;

            _havePrePose = _local != null && _local.rb != null;
            if (_havePrePose)
            {
                _prePosition = _local.rb.position;
                _preRotation = _local.rb.rotation;
            }
        }

        void OnRollbackFinished()
        {
            _inRollback = false;
            if (_pm == null || AppliesTotal(_pm) == _appliesAtStart)
                return;

            Resim.Event();
            ResimDepth.Max(_passesInRollback);
            ResimCost.Add((float)((TickClock.NowSeconds - _rollbackStart) * 1000.0));
            if (_lastPredictedCount > 0)
                ResimEntities.Max(_lastPredictedCount);

            if (_havePrePose && _local != null && _local.rb != null)
            {
                Vector3 position = _local.rb.position;
                Quaternion rotation = _local.rb.rotation;
                if (position != _prePosition || rotation != _preRotation)
                    PushPoseError(Correction, position, rotation, _prePosition, _preRotation);
            }
            _havePrePose = false;
        }

        float _nextEntityCount;
        double _lastRtt = double.NaN;

        protected override void Collect()
        {
            if (_stats != null)
            {
                NetIn.Set(_stats.download);
                NetOut.Set(_stats.upload);
                _jitter.Max(_stats.jitter);
                PacketLoss.Set(_stats.packetLoss);
            }

            if (!_nm.isClientOnly)
                return;

            RefreshLocalPlayer();

            if (Time.unscaledTime >= _nextEntityCount)
            {
                _nextEntityCount = Time.unscaledTime + 0.5f;
                _lastPredictedCount = FindObjectsByType<PredictedRigidbody>(FindObjectsSortMode.None).Length;
            }

            ClockAdjust.Set((float)((_pm.currentTickPacingScale - 1.0) * 100.0));

            var tickModule = _nm.tickModule;
            if (tickModule != null && tickModule.rtt != _lastRtt)
            {
                _lastRtt = tickModule.rtt;
                Latency.Max((float)(tickModule.rtt * 1000.0));
            }

            if (_pm.framesReceivedTotal != _lastFramesReceived)
            {
                _lastFramesReceived = _pm.framesReceivedTotal;
                _lastStateArrival = Time.unscaledTime;
            }
            if (_lastStateArrival >= 0f)
                SnapshotAge.Set((Time.unscaledTime - _lastStateArrival) * 1000f);

            if (_pm.hasInputSlackFeedback && _pm.tickDelta > 0f)
                InputBuffer.Set(Mathf.Max(0f, (float)(_pm.lastInputSlackMs / (_pm.tickDelta * 1000.0))));

            ulong leadAdjusts = LeadAdjustTotal(_pm);
            for (ulong i = _lastLeadAdjusts; i < leadAdjusts; i++)
                _leadAdjust.Event();
            _lastLeadAdjusts = leadAdjusts;

            ulong starved = _pm.viewBufferStarvedFramesTotal;
            if (starved > _lastViewStarved)
                _viewStarved.Event();
            _lastViewStarved = starved;

            if (_local != null)
            {
                if (_local.lastVerifiedTick.HasValue && _pm.localTick >= _local.lastVerifiedTick.Value)
                    TickLead.Set(_pm.localTick - _local.lastVerifiedTick.Value);

                _jump.Sample(this, _localRendered);
                if (_localRendered != null && _local.rb != null)
                    Smoothing.Set((_localRendered.position - _local.rb.position).magnitude);
            }
            else
            {
                _jump.Sample(this, null);
            }
        }
    }
}
