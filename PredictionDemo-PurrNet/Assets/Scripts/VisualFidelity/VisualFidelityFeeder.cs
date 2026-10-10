// PurrNet (PurrDiction) feeder for the visual fidelity probes. Public API only: PredictionManager's tick and
// verified physics pass, TickManager.floatingPoint, and PredictedRigidbody / PredictedTransform.
// Nothing to set up: it creates itself after the first scene loads. See VisualFidelity.md.

using System;
using System.Collections.Generic;
using DefaultNamespace;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Prediction;
using UnityEngine;

namespace PredictionDebug
{
    /// <summary>
    /// On a client-only instance, puts a <see cref="VisualFidelityProbe"/> on every PredictedRigidbody and feeds
    /// it the server-verified states the client replays.
    ///
    /// Timeline: PredictionManager.localTick. The server sends the frame for tick S as the state entering S,
    /// i.e. after tick S - 1, with tick S's inputs. During a rollback the client loads each verified frame and
    /// simulates it with isVerified set; at that tick's onBeforePhysicsPass the bodies still hold the loaded
    /// server state, which is reported at tick S - 1. A forward tick L leaves the bodies after tick L and then
    /// advances localTick, so the newest simulated tick is localTick - 1.
    ///
    /// Visual delay: the demo leaves PredictedTransform.graphics unassigned, so the rigidbody itself is drawn,
    /// at the newest simulated tick: the delay is the fraction of a tick the frame is drawn into the next one
    /// (TickManager.floatingPoint). An object with graphics assigned is drawn through PurrDiction's view
    /// interpolation, whose buffer is internal, so it reports no delay and only its raw error is measured.
    /// </summary>
    public class VisualFidelityFeeder : MonoBehaviour
    {
        [Tooltip("Adds a probe to every predicted rigidbody that doesn't have one. Off: only objects whose prefab carries a probe are measured.")]
        [SerializeField] bool attachToAllEntities = true;

        // How often the scene is looked through for new predicted rigidbodies, and for the managers before a session.
        const float ScanSeconds = 0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
#if !VISUAL_FIDELITY_NO_AUTOSPAWN
            if (FindAnyObjectByType<VisualFidelityFeeder>(FindObjectsInactive.Include) != null)
                return;
            var go = new GameObject("VisualFidelityFeeder (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<VisualFidelityFeeder>();
#endif
        }

        sealed class Feed
        {
            public PredictedRigidbody Body;
            public PredictedPlayerController Player;
            public VisualFidelityProbe Probe;
            public bool Smoothed;
        }

        readonly Dictionary<PredictedRigidbody, Feed> _feeds = new Dictionary<PredictedRigidbody, Feed>();
        readonly List<PredictedRigidbody> _gone = new List<PredictedRigidbody>();
        NetworkManager _nm;
        PredictionManager _pm;
        float _nextScan;

        void OnDisable() => Detach();

        void LateUpdate()
        {
            // Only client-only instances predict; the host draws the server's own objects.
            bool clientOnly = _nm != null && _nm.isClientOnly && _pm != null && _nm.tickModule != null;
            if (_pm is not null && !clientOnly)
                Detach();

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanSeconds;
                if (_pm is null)
                    TryAttach();
                if (_pm is not null)
                    Scan();
            }
            if (_pm is null)
                return;

            double rawDelay = TickFraction() * _pm.tickDelta;
            foreach (Feed feed in _feeds.Values)
            {
                if (feed.Probe == null || feed.Body == null)
                    continue;
                feed.Probe.Subject = Classify(feed);
                feed.Probe.ReportVisualDelay(feed.Smoothed ? double.NaN : rawDelay);
            }
        }

        void TryAttach()
        {
            NetworkManager nm = NetworkManager.main;
            if (nm == null || !nm.isClientOnly || nm.tickModule == null)
                return;
            PredictionManager pm = FindAnyObjectByType<PredictionManager>();
            if (pm == null)
                return;

            _nm = nm;
            _pm = pm;
            _pm.onBeforePhysicsPass += OnBeforePhysicsPass;
            VisualFidelity.ResetSession();
            VisualFidelity.LibraryName = "PurrNet";
            VisualFidelity.Clock = Clock;
        }

        void Detach()
        {
            if (_pm is not null)
            {
                _pm.onBeforePhysicsPass -= OnBeforePhysicsPass;
                VisualFidelity.Clock = null;
                VisualFidelity.ResetSession();
            }
            _feeds.Clear();
            _nm = null;
            _pm = null;
        }

        void Scan()
        {
            foreach (PredictedRigidbody body in FindObjectsByType<PredictedRigidbody>(FindObjectsSortMode.None))
            {
                if (_feeds.ContainsKey(body) || body.rb == null)
                    continue;

                var probe = body.GetComponent<VisualFidelityProbe>();
                if (probe == null)
                {
                    if (!attachToAllEntities)
                        continue;
                    probe = body.gameObject.AddComponent<VisualFidelityProbe>();
                    probe.Label = $"{body.name.Replace("(Clone)", "")} #{body.GetInstanceID()}";
                }

                var predictedTransform = body.GetComponent<PredictedTransform>();
                Transform graphics = predictedTransform != null ? predictedTransform.graphics : null;
                probe.Visual = graphics != null ? graphics : body.transform;
                _feeds[body] = new Feed
                {
                    Body = body,
                    Player = body.GetComponent<PredictedPlayerController>(),
                    Probe = probe,
                    Smoothed = graphics != null,
                };
            }

            _gone.Clear();
            foreach (KeyValuePair<PredictedRigidbody, Feed> pair in _feeds)
                if (pair.Key == null)
                    _gone.Add(pair.Key);
            foreach (PredictedRigidbody body in _gone)
                _feeds.Remove(body);
        }

        static FidelitySubject Classify(Feed feed)
        {
            if (feed.Player == null)
                return FidelitySubject.Ball;
            // Bots are the player prefab created without an owner.
            if (!feed.Player.owner.HasValue)
                return FidelitySubject.Bot;
            return feed.Body.IsOwner() ? FidelitySubject.LocalPlayer : FidelitySubject.RemotePlayer;
        }

        void OnBeforePhysicsPass()
        {
            if (!_pm.isVerified)
                return;
            ulong frameTick = _pm.localTickInContext;
            if (frameTick == 0)
                return;

            // The verified frame for tick S holds the server's state entering S, i.e. after tick S - 1.
            double time = (frameTick - 1) * (double)_pm.tickDelta;
            foreach (Feed feed in _feeds.Values)
            {
                if (feed.Probe == null || feed.Body == null)
                    continue;
                Rigidbody rb = feed.Body.rb;
                if (rb != null)
                    feed.Probe.ReportServerState(time, rb.position, rb.rotation);
            }
        }

        /// <summary>A forward tick advances localTick after its physics pass, so the newest simulated tick is localTick - 1.</summary>
        double Clock() => (_pm.localTick - 1.0 + TickFraction()) * _pm.tickDelta;

        /// <summary>How far into the next tick the frame is drawn, 0 to 1.</summary>
        double TickFraction()
        {
            TickManager ticks = _nm.tickModule;
            return ticks != null ? Math.Clamp(ticks.floatingPoint, 0.0, 1.0) : 0.0;
        }
    }
}
