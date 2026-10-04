// Ursitoare collector for the ResimGraph overlay. Reads only the public API of the Ursitoare package
// (PredictionManager, ClientPredictionManager, ServerPredictionManager, the predicted entities and
// PredictedEntityVisuals) plus Mirror's transport callbacks for bandwidth and NetworkTime.rtt / rttVariance for the ping.
// Setup: add this component to any GameObject in the Gameplay scene. See ResimGraph.md.

using System;
using System.Collections.Generic;
using Mirror;
using Prediction;
using Prediction.Components.Controllers;
using Prediction.Data;
using UnityEngine;

namespace PredictionDebug
{
    public class ResimGraph : ResimGraphBase
    {
        protected override string LibraryName => "Ursitoare";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => SpawnIfMissing<ResimGraph>();

        // Marker for the player loop hook that runs at the start of every fixed step, before the
        // prediction adapter's FixedUpdate calls PredictionManager.Tick().
        struct UrsitoareBeforeTick { }

        GraphStrip _freeze;
        GraphStrip _resimSkipped;
        GraphStrip _jitter;

        PredictionManager _pm;
        ClientPredictionManager _client;
        ServerPredictionManager _server;
        Transport _transport;
        bool _hookedLoop;

        readonly TickClock _clock = new TickClock();
        readonly ByteRate _in = new ByteRate();
        readonly ByteRate _out = new ByteRate();

        // Locally controlled entity and what hangs off it.
        ClientPredictedEntity _local;
        PredictedEntityVisuals _localVisuals;
        GameObject _localVisualsGO;

        // Correction: local rigidbody pose before the tick's resimulation check, compared in onPreTick.
        bool _havePreTickPose;
        Vector3 _preTickPosition;
        Quaternion _preTickRotation;

        uint _lastErrorTick;
        uint _lastSkipped;
        uint _lastSkippedNoHistory;
        float _lastStateArrival = -1f;
        double _lastPingRtt = double.NaN;
        uint _lastRewindTotal;
        uint _resimDepth;
        double _resimStart = -1.0;
        readonly Dictionary<ClientPredictedEntity, uint> _resimTicksSeen = new Dictionary<ClientPredictedEntity, uint>();

        protected override void Configure()
        {
            // TickStat.didResimulate/resimTicks/resimDuration can't be used: ClientPredictionManager resimulates
            // before base.Tick(), which resets those fields, so they always report no resimulation.
            Resim.WithNote("A resimulation ran: PredictionManager.resimulation (dispatched true when it starts, false when it ends).");
            ResimDepth.WithNote("Ticks rewound by the resimulation: totalRewindDistance delta.");
            ResimCost.WithNote("Wall time between the resimulation start and end events.");
            ResimEntities.MarkComputed("Client entities whose public resimTicks counter advanced this frame. Ursitoare replays every registered client entity.");
            PredictionError.MarkComputed("Local entity: localStateBuffer vs serverStateBuffer at the newest server tick, read before the tick's resimulation check. Rotation drives the colour.");
            Correction.MarkComputed("Local rigidbody pose right before PredictionManager.Tick() vs in onPreTick, i.e. how far the resimulation or snap moved the present state.");
            TickCost.WithNote("Wall time of the tick excluding resimulation: TickStat.duration.");
            TickGap.MarkComputed("Wall time between consecutive onPreTick events.");
            ClockAdjust.MarkSubstitute("TICK RATE DRIFT",
                "Ursitoare runs at a fixed rate from FixedUpdate and never speeds up or slows down its clock. Shown instead: the tick rate actually achieved over 2 s vs 1/fixedDeltaTime.");
            Latency.WithNote("Mirror NetworkTime.rtt (exponential moving average of pings), drawn when it updates. Ursitoare runs on Mirror, so this is the same ping the Mirror demo graphs.");
            InputRtt.WithNote("ClientPredictionManager.onTickRttDuration: client tick sent until a server state echoing that tick arrives. Includes the server input buffer and send batching. Library 5117080 stamps the tick one tick early, so this reads ~1 tick high.");
            TickLead.WithNote("ClientPredictedEntity.GetServerDelay() of the local entity: local tick minus the newest server state's tick.");
            SnapshotAge.MarkComputed("Time since onTickRttDuration last fired, which happens whenever a newer server state arrives.");
            InputBuffer.WithNote("Server only: largest ServerPredictedEntity.BufferFill() across entities (client inputs waiting to be simulated).");
            PacketLoss.WithNote("ClientPredictionManager.onPacketLoss: server ticks missing from the received state stream.");
            NetIn.WithNote("Mirror Transport.OnClientDataReceived / OnServerDataReceived bytes.");
            NetOut.WithNote("Mirror Transport.OnClientDataSent / OnServerDataSent bytes.");
            VisualJump.WithNote("PredictedEntityVisuals.onLargeTransformJumpGlobal for the local entity's visuals.");
            Smoothing.WithNote("PredictedEntityVisuals.GetInterpolationDistance(): distance between the interpolated visuals and the simulated body.");

            _freeze = AddStrip("freeze", "FREEZE / RESET", "", StripKind.Event,
                new Color(1f, 0.55f, 0.10f), new Color(1f, 0.20f, 0.05f), 1f)
                .WithNote("PredictionManager.onSnapToServer: history ran out, everything snapped to the latest server state.");
            _resimSkipped = AddStrip("resim_skipped", "RESIM SKIPPED", "", StripKind.Event,
                new Color(0.75f, 0.75f, 0.75f), new Color(1f, 0.45f, 0.45f), 1f)
                .WithNote("totalResimulationsSkipped (oversimulation protection) plus resimSkipNotEnoughHistory counter deltas.");
            _jitter = AddStrip("rtt_jitter", "RTT JITTER", "ms", StripKind.Bar,
                new Color(0.70f, 0.60f, 1f), new Color(1f, 0.40f, 0.30f), 50f, "0.0")
                .WithNote("Standard deviation from Mirror NetworkTime.rttVariance, the same ping statistics as LATENCY RTT and the Mirror demo's RTT JITTER.");
        }

        protected override bool TrySubscribe()
        {
            var pm = PredictionManager.Instance;
            if (pm == null)
                return false;

            _pm = pm;
            _client = pm as ClientPredictionManager;
            _server = pm as ServerPredictionManager;

            pm.onPreTick.AddEventListener(OnPreTick);
            pm.onTickStat.AddEventListener(OnTickStat);
            pm.resimulation.AddEventListener(OnResimulation);
            pm.onSnapToServer.AddEventListener(OnSnapToServer);
            if (_client != null)
            {
                _client.onTickRttDuration.AddEventListener(OnTickRtt);
                _client.onPacketLoss.AddEventListener(OnPacketLoss);
            }
            PredictedEntityVisuals.onLargeTransformJumpGlobal.AddEventListener(OnLargeTransformJump);

            _hookedLoop = PlayerLoopHooks.Insert(typeof(UnityEngine.PlayerLoop.FixedUpdate), null,
                typeof(UrsitoareBeforeTick), BeforeTick, null, null);

            HookTransport();

            _lastSkipped = pm.totalResimulationsSkipped;
            _lastSkippedNoHistory = pm.resimSkipNotEnoughHistory;
            _lastRewindTotal = pm.totalRewindDistance;
            _resimStart = -1.0;
            _clock.Reset();

            bool isClient = _client != null;
            foreach (var s in new[] { Resim, ResimDepth, ResimCost, ResimEntities, PredictionError, Correction, Latency, InputRtt, TickLead, SnapshotAge, PacketLoss, VisualJump, Smoothing, _freeze, _resimSkipped, _jitter })
            {
                s.Inactive = !isClient;
                s.InactiveReason = "(client only)";
            }
            InputBuffer.Inactive = isClient;
            InputBuffer.InactiveReason = "(server only)";
            return true;
        }

        protected override bool IsSubscriptionStale() => PredictionManager.Instance != _pm;

        protected override void Unsubscribe()
        {
            if (_pm != null)
            {
                _pm.onPreTick.RemoveEventListener(OnPreTick);
                _pm.onTickStat.RemoveEventListener(OnTickStat);
                _pm.resimulation.RemoveEventListener(OnResimulation);
                _pm.onSnapToServer.RemoveEventListener(OnSnapToServer);
            }
            if (_client != null)
            {
                _client.onTickRttDuration.RemoveEventListener(OnTickRtt);
                _client.onPacketLoss.RemoveEventListener(OnPacketLoss);
            }
            PredictedEntityVisuals.onLargeTransformJumpGlobal.RemoveEventListener(OnLargeTransformJump);

            if (_hookedLoop)
                PlayerLoopHooks.Remove(typeof(UrsitoareBeforeTick));
            _hookedLoop = false;

            UnhookTransport();

            _pm = null;
            _client = null;
            _server = null;
            _local = null;
            _localVisuals = null;
            _localVisualsGO = null;
            _resimTicksSeen.Clear();
            _lastPingRtt = double.NaN;
        }

        void HookTransport()
        {
            _transport = Transport.active;
            if (_transport == null)
                return;
            _transport.OnClientDataReceived += OnClientReceived;
            _transport.OnClientDataSent += OnClientSent;
            _transport.OnServerDataReceived += OnServerReceived;
            _transport.OnServerDataSent += OnServerSent;
        }

        void UnhookTransport()
        {
            if (_transport == null)
                return;
            _transport.OnClientDataReceived -= OnClientReceived;
            _transport.OnClientDataSent -= OnClientSent;
            _transport.OnServerDataReceived -= OnServerReceived;
            _transport.OnServerDataSent -= OnServerSent;
            _transport = null;
        }

        void OnClientReceived(ArraySegment<byte> data, int channel) => _in.Add(data.Count);
        void OnClientSent(ArraySegment<byte> data, int channel) => _out.Add(data.Count);
        void OnServerReceived(int conn, ArraySegment<byte> data, int channel) => _in.Add(data.Count);
        void OnServerSent(int conn, ArraySegment<byte> data, int channel) => _out.Add(data.Count);

        /// <summary>
        /// Start of a fixed step, before the adapter ticks. Server states received this frame are already
        /// buffered, so this is the last moment the local prediction for that tick is still unmodified.
        /// </summary>
        void BeforeTick()
        {
            if (_client == null)
                return;

            RefreshLocalEntity();
            if (_local == null || _local.rigidbody == null)
            {
                _havePreTickPose = false;
                return;
            }

            _preTickPosition = _local.rigidbody.position;
            _preTickRotation = _local.rigidbody.rotation;
            _havePreTickPose = true;

            uint serverTick = _local.serverStateBuffer.GetEndTick();
            if (serverTick == _lastErrorTick)
                return;

            PhysicsStateRecord server = _local.serverStateBuffer.Get(serverTick);
            PhysicsStateRecord predicted = _local.localStateBuffer.Get((int)serverTick);
            if (server == null || predicted == null || predicted.tickId != serverTick)
                return;

            _lastErrorTick = serverTick;
            PushPoseError(PredictionError, predicted.position, predicted.rotation, server.position, server.rotation);
        }

        void OnPreTick(uint tickId)
        {
            _clock.Begin(TickGap);

            // The resimulation check and any rewind ran between BeforeTick and here.
            if (_havePreTickPose && _local != null && _local.rigidbody != null)
            {
                Vector3 position = _local.rigidbody.position;
                Quaternion rotation = _local.rigidbody.rotation;
                if (position != _preTickPosition || rotation != _preTickRotation)
                    PushPoseError(Correction, position, rotation, _preTickPosition, _preTickRotation);
            }
            _havePreTickPose = false;
        }

        void OnTickStat(PredictionManager.TickStat stat)
        {
            TickCost.Max((float)(stat.duration * 1000.0));
        }

        void OnResimulation(bool starting)
        {
            if (starting)
            {
                // totalRewindDistance is bumped right before the start event.
                _resimDepth = _pm.totalRewindDistance - _lastRewindTotal;
                _lastRewindTotal = _pm.totalRewindDistance;
                _resimStart = TickClock.NowSeconds;
                return;
            }

            if (_resimStart < 0.0)
                return;
            Resim.Event();
            ResimDepth.Max(_resimDepth);
            ResimCost.Add((float)((TickClock.NowSeconds - _resimStart) * 1000.0));
            _resimStart = -1.0;
        }

        void OnTickRtt(PredictionManager.TickRttDuration rtt)
        {
            InputRtt.Max((float)(rtt.duration * 1000.0));
            _lastStateArrival = Time.unscaledTime;
        }

        void OnPacketLoss(int lost)
        {
            for (int i = 0; i < lost; i++)
                PacketLoss.Event();
        }

        void OnSnapToServer(uint tickId) => _freeze.Event();

        void OnLargeTransformJump(PredictedEntityVisuals.GlobalTransformJump jump)
        {
            if (_localVisualsGO == null || jump.entity != _localVisualsGO)
                return;

            float p = jumpScaleMeters > 0f ? jump.jump.positionDiff.magnitude / jumpScaleMeters : 1f;
            float a = jumpScaleDegrees > 0f ? Quaternion.Angle(Quaternion.identity, jump.jump.rotationDiff) / jumpScaleDegrees : 1f;
            VisualJump.Event(Mathf.Max(p, a));
        }

        /// <summary>
        /// The jump event only carries the visuals GameObject, so we resolve the one belonging to the
        /// locally controlled entity. Retried while it comes up empty because the visuals may register
        /// after the entity becomes local.
        /// </summary>
        void RefreshLocalEntity()
        {
            if (_client == null)
                return;

            ClientPredictedEntity local = null;
            foreach (var e in _client.GetLocalEntities())
            {
                local = e;
                break;
            }

            if (local == _local && (local == null || _localVisuals != null))
                return;

            _local = local;
            _localVisuals = null;
            _localVisualsGO = null;
            _lastErrorTick = 0;
            if (local == null)
                return;

            foreach (PredictedEntity entity in _pm.GetPredictedEntities())
            {
                if (entity == null || entity.GetClientEntity() != local)
                    continue;
                _localVisuals = entity.GetVisualsControlled();
                if (_localVisuals != null)
                    _localVisualsGO = _localVisuals.gameObject;
                break;
            }
        }

        protected override void Collect()
        {
            float drift = _clock.RateDeviationPercent(1.0 / Time.fixedDeltaTime);
            if (!float.IsNaN(drift))
                ClockAdjust.Set(drift);

            NetIn.Set(_in.KBps());
            NetOut.Set(_out.KBps());

            if (_server != null)
            {
                int fill = 0;
                foreach (PredictedEntity entity in _pm.GetPredictedEntities())
                {
                    var server = entity?.GetServerEntity();
                    if (server != null)
                        fill = Mathf.Max(fill, server.BufferFill());
                }
                InputBuffer.Set(fill);
                return;
            }

            if (_client == null)
                return;

            RefreshLocalEntity();

            if (_local != null)
                TickLead.Set(_local.GetServerDelay());
            if (_localVisuals != null)
                Smoothing.Set(_localVisuals.GetInterpolationDistance());
            if (_lastStateArrival >= 0f)
                SnapshotAge.Set((Time.unscaledTime - _lastStateArrival) * 1000f);

            double pingRtt = NetworkTime.rtt;
            if (pingRtt != _lastPingRtt)
            {
                _lastPingRtt = pingRtt;
                Latency.Max((float)(pingRtt * 1000.0));
                _jitter.Max((float)(Math.Sqrt(Math.Max(0.0, NetworkTime.rttVariance)) * 1000.0));
            }

            int resimmed = 0;
            int entityCount = 0;
            foreach (PredictedEntity entity in _pm.GetPredictedEntities())
            {
                var client = entity?.GetClientEntity();
                if (client == null)
                    continue;
                entityCount++;
                if (_resimTicksSeen.TryGetValue(client, out uint seen) && client.resimTicks != seen)
                    resimmed++;
                _resimTicksSeen[client] = client.resimTicks;
            }
            if (resimmed > 0)
                ResimEntities.Max(resimmed);
            // Despawned entities are never removed one by one; dropping the lot costs one frame of data.
            if (_resimTicksSeen.Count > entityCount * 2 + 16)
                _resimTicksSeen.Clear();

            uint skipped = _pm.totalResimulationsSkipped + _pm.resimSkipNotEnoughHistory;
            uint before = _lastSkipped + _lastSkippedNoHistory;
            for (uint i = before; i < skipped; i++)
                _resimSkipped.Event();
            _lastSkipped = _pm.totalResimulationsSkipped;
            _lastSkippedNoHistory = _pm.resimSkipNotEnoughHistory;
        }
    }
}
