// FishNet collector for the ResimGraph overlay. Reads only FishNet's public API: TimeManager tick and RTT
// events, PredictionManager reconcile/replay events, the active Transport's receive events, the local
// connection's first object and NetworkObject.GetGraphicalObject().
// Setup: add this component to any GameObject in the Gameplay scene. See ResimGraph.md.

using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using FishNetPredictionManager = FishNet.Managing.Predicting.PredictionManager;

namespace PredictionDebug
{
    public class ResimGraph : ResimGraphBase
    {
        protected override string LibraryName => "FishNet";

        NetworkManager _nm;
        TimeManager _tm;
        FishNetPredictionManager _pm;
        Transport _transport;

        protected override float TickStepMs => _tm != null ? (float)(_tm.TickDelta * 1000.0) : base.TickStepMs;

        readonly TickClock _clock = new TickClock();
        readonly ByteRate _in = new ByteRate();
        readonly PoseHistory _history = new PoseHistory();
        readonly VisualJumpDetector _jump = new VisualJumpDetector();

        NetworkObject _localObject;
        Rigidbody _localBody;
        Transform _localRendered;

        // Reconcile in progress.
        double _reconcileStart;
        double _resimMsThisTick;
        int _replays;
        int _reconciledFrame = -1;
        bool _errorTaken;
        bool _havePrePose;
        Vector3 _prePosition;
        Quaternion _preRotation;

        uint _lastRemoteTick;
        float _lastStateArrival = -1f;
        int _serverPacketsThisTick;
        bool _serverTickOpen;

        protected override void Configure()
        {
            Resim.WithNote("A reconcile ran: PredictionManager.OnPreReconcile/OnPostReconcile. FishNet reconciles and replays on every received state, not only on a mismatch.");
            ResimDepth.WithNote("Replayed ticks counted from PredictionManager.OnPostReplicateReplay between the reconcile events.");
            ResimCost.MarkComputed("Wall time between OnPreReconcile and OnPostReconcile (state apply + all replays).");
            ResimEntities.MarkComputed("Spawned NetworkObjects with prediction enabled. A FishNet replay runs the replicate of every predicted object, so all of them resimulate together.");
            PredictionError.MarkComputed("Local player: rigidbody pose recorded in OnPostTick vs the reconciled server pose for the same tick, read in OnPostPhysicsTransformSync (needs TimeManager physics mode).");
            Correction.MarkComputed("Local player rigidbody pose at OnPreReconcile vs OnPostReconcile: how far the reconcile and replay moved the present state.");
            TickCost.MarkComputed("Wall time from TimeManager.OnPreTick to OnPostTick, minus the reconcile (graphed as RESIM COST).");
            TickGap.MarkComputed("Wall time between consecutive TimeManager.OnPreTick events.");
            ClockAdjust.MarkSubstitute("TICK RATE DRIFT",
                "FishNet speeds up and slows down the client tick (TimeManager's adjusted tick delta), but the value is private. Shown instead: the tick rate actually achieved over 2 s vs TimeManager.TickRate, which captures that adjustment.");
            Latency.WithNote("TimeManager.OnRoundTripTimeUpdated.");
            TickLead.MarkComputed("TimeManager.LocalTick minus the client tick of the latest reconcile (OnPreReconcile clientTick).");
            SnapshotAge.MarkComputed("Time since TimeManager.LastPacketTick.LastRemoteTick last advanced, checked on every Transport.OnClientReceivedData.");
            InputBuffer.MarkSubstitute("PACKETS IN / TICK",
                "The server's replicate queue is internal to NetworkBehaviour. Shown instead (server only): transport packets received from clients per server tick; 0 means no input arrived for that tick.");
            InputBuffer.Unit = "";
            InputBuffer.Scale = 4f;
            PacketLoss.MarkSubstitute("TICK GAPS (est.)",
                "FishNet exposes no loss counter. Shown instead: server ticks skipped between consecutive packets (LastPacketTick.LastRemoteTick). The server sends every tick while predicted objects move, so a gap usually means a lost or merged packet.");
            NetIn.WithNote("Transport.OnClientReceivedData / OnServerReceivedData bytes.");
            NetOut.MarkUnsupported("FishNet has no public outgoing data event. Its outbound counters (NetworkTrafficStatistics / Network Profiler window) are internal.");
            VisualJump.MarkComputed("FishNet has no jump event. The graph applies Ursitoare's test (move > 0.35 m or 2.5 deg in one frame) to the local player's graphical object.");
            Smoothing.MarkComputed("Local player: NetworkObject.GetGraphicalObject() position vs the rigidbody position (FishNet's tick smoother offset).");
        }

        protected override bool TrySubscribe()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null || (!nm.IsClientStarted && !nm.IsServerStarted))
                return false;

            _nm = nm;
            _tm = nm.TimeManager;
            _pm = nm.GetComponent<FishNetPredictionManager>();
            _transport = nm.TransportManager.Transport;

            _tm.OnPreTick += OnPreTick;
            _tm.OnPostTick += OnPostTick;
            _tm.OnRoundTripTimeUpdated += OnRtt;
            if (_pm != null)
            {
                _pm.OnPreReconcile += OnPreReconcile;
                _pm.OnPostPhysicsTransformSync += OnPostPhysicsTransformSync;
                _pm.OnPostReplicateReplay += OnPostReplicateReplay;
                _pm.OnPostReconcile += OnPostReconcile;
            }
            if (_transport != null)
            {
                _transport.OnClientReceivedData += OnClientReceived;
                _transport.OnServerReceivedData += OnServerReceived;
            }

            _clock.Reset();
            _history.Clear();
            _lastRemoteTick = 0;
            _lastStateArrival = -1f;

            // The host's own client doesn't reconcile; only remote clients predict.
            bool predicting = nm.IsClientOnlyStarted;
            foreach (var s in new[] { Resim, ResimDepth, ResimCost, ResimEntities, PredictionError, Correction, Latency, TickLead, SnapshotAge, PacketLoss, VisualJump, Smoothing })
            {
                s.Inactive = !predicting;
                s.InactiveReason = nm.IsServerStarted ? "(no prediction on server/host)" : "(client only)";
            }
            InputBuffer.Inactive = !nm.IsServerStarted;
            InputBuffer.InactiveReason = "(server only)";
            return true;
        }

        protected override bool IsSubscriptionStale() => _nm == null || (!_nm.IsClientStarted && !_nm.IsServerStarted);

        protected override void Unsubscribe()
        {
            if (_tm != null)
            {
                _tm.OnPreTick -= OnPreTick;
                _tm.OnPostTick -= OnPostTick;
                _tm.OnRoundTripTimeUpdated -= OnRtt;
            }
            if (_pm != null)
            {
                _pm.OnPreReconcile -= OnPreReconcile;
                _pm.OnPostPhysicsTransformSync -= OnPostPhysicsTransformSync;
                _pm.OnPostReplicateReplay -= OnPostReplicateReplay;
                _pm.OnPostReconcile -= OnPostReconcile;
            }
            if (_transport != null)
            {
                _transport.OnClientReceivedData -= OnClientReceived;
                _transport.OnServerReceivedData -= OnServerReceived;
            }

            _nm = null;
            _tm = null;
            _pm = null;
            _transport = null;
            _localObject = null;
            _localBody = null;
            _localRendered = null;
        }

        void RefreshLocalPlayer()
        {
            var connection = _nm != null && _nm.IsClientStarted ? _nm.ClientManager.Connection : null;
            var first = connection != null ? connection.FirstObject : null;
            if (first == _localObject && (first == null || _localBody != null))
                return;

            _localObject = first;
            _localBody = first != null ? first.GetComponent<Rigidbody>() : null;
            _localRendered = first != null ? first.GetGraphicalObject() : null;
            if (_localRendered == null && first != null)
            {
                var renderer = first.GetComponentInChildren<Renderer>();
                _localRendered = renderer != null ? renderer.transform : first.transform;
            }
            _history.Clear();
        }

        void OnPreTick()
        {
            _clock.Begin(TickGap);
            _resimMsThisTick = 0.0;

            if (_nm != null && _nm.IsServerStarted)
            {
                if (_serverTickOpen)
                    InputBuffer.Max(_serverPacketsThisTick);
                _serverPacketsThisTick = 0;
                _serverTickOpen = true;
            }
        }

        void OnPostTick()
        {
            _clock.End(TickCost, _resimMsThisTick);

            RefreshLocalPlayer();
            if (_localBody != null)
                _history.Record(_tm.LocalTick, _localBody.position, _localBody.rotation);
        }

        void OnRtt(long ms) => Latency.Max(ms);

        void OnPreReconcile(uint clientTick, uint serverTick)
        {
            _reconcileStart = TickClock.NowSeconds;
            _replays = 0;
            _errorTaken = false;

            RefreshLocalPlayer();
            _havePrePose = _localBody != null;
            if (_havePrePose)
            {
                _prePosition = _localBody.position;
                _preRotation = _localBody.rotation;
            }

            if (_tm.LocalTick >= clientTick)
                TickLead.Max(_tm.LocalTick - clientTick);
        }

        /// <summary>Reconcile data has been applied and synced to physics, replay hasn't started: the body sits on the server state for clientTick.</summary>
        void OnPostPhysicsTransformSync(uint clientTick, uint serverTick)
        {
            if (_errorTaken || _localBody == null)
                return;
            _errorTaken = true;

            if (_history.TryGet(clientTick, out var predictedPosition, out var predictedRotation))
                PushPoseError(PredictionError, predictedPosition, predictedRotation, _localBody.position, _localBody.rotation);
        }

        void OnPostReplicateReplay(uint clientTick, uint serverTick) => _replays++;

        void OnPostReconcile(uint clientTick, uint serverTick)
        {
            double ms = (TickClock.NowSeconds - _reconcileStart) * 1000.0;
            _resimMsThisTick += ms;
            _reconciledFrame = Time.frameCount;

            Resim.Event();
            ResimDepth.Max(_replays);
            ResimCost.Add((float)ms);

            if (_havePrePose && _localBody != null)
            {
                Vector3 position = _localBody.position;
                Quaternion rotation = _localBody.rotation;
                if (position != _prePosition || rotation != _preRotation)
                    PushPoseError(Correction, position, rotation, _prePosition, _preRotation);
            }
            _havePrePose = false;
        }

        /// <summary>FishNet's own handler subscribed first and has already parsed this packet, so LastPacketTick reflects it.</summary>
        void OnClientReceived(ClientReceivedDataArgs args)
        {
            _in.Add(args.Data.Count);

            uint remote = _tm != null ? _tm.LastPacketTick.LastRemoteTick : TimeManager.UNSET_TICK;
            if (remote == TimeManager.UNSET_TICK || remote <= _lastRemoteTick)
                return;

            if (_lastRemoteTick != 0 && remote > _lastRemoteTick + 1)
            {
                uint missed = remote - _lastRemoteTick - 1;
                for (uint i = 0; i < missed && i < 64; i++)
                    PacketLoss.Event();
            }
            _lastRemoteTick = remote;
            _lastStateArrival = Time.unscaledTime;
        }

        void OnServerReceived(ServerReceivedDataArgs args)
        {
            _in.Add(args.Data.Count);
            _serverPacketsThisTick++;
        }

        protected override void Collect()
        {
            float drift = _clock.RateDeviationPercent(_tm.TickRate);
            if (!float.IsNaN(drift))
                ClockAdjust.Set(drift);

            NetIn.Set(_in.KBps());

            if (!_nm.IsClientOnlyStarted)
                return;

            if (_lastStateArrival >= 0f)
                SnapshotAge.Set((Time.unscaledTime - _lastStateArrival) * 1000f);

            // Only on frames that reconciled, like the other libraries' entity counts.
            if (_reconciledFrame == Time.frameCount)
            {
                int predicted = 0;
                foreach (var nob in _nm.ClientManager.Objects.Spawned.Values)
                    if (nob != null && nob.EnablePrediction)
                        predicted++;
                ResimEntities.Max(predicted);
            }

            RefreshLocalPlayer();
            _jump.Sample(this, _localRendered);
            if (_localRendered != null && _localBody != null)
                Smoothing.Set((_localRendered.position - _localBody.position).magnitude);
        }
    }
}
