// FishNet feeder for the visual fidelity probes. Public API: TimeManager's tick and tick percent, the
// rigidbody poses PredictionManager reconciles to, and NetworkObject.GetGraphicalObject(). One opt-in
// exception: the smoother's delay, read by reflection only while that toggle is on (see SmootherDelay).
// Nothing to set up: it creates itself after the first scene loads. See VisualFidelity.md.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using DefaultNamespace;
using FishNet;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using FishNetPredictionManager = FishNet.Managing.Predicting.PredictionManager;

namespace PredictionDebug
{
    /// <summary>
    /// On a client-only instance, puts a <see cref="VisualFidelityProbe"/> on every predicted NetworkObject with a
    /// Rigidbody and feeds it the states the client reconciles to. The visual is the graphical object.
    ///
    /// Timeline: the client's LocalTick. A reconcile carries the client tick of the last input the server ran
    /// for this client, and every object reconciled in it is set to the server's state at that moment, so
    /// all server states land on the client tick timeline. The poses are read in OnPostPhysicsTransformSync,
    /// after the reconcile is applied and before the replay, from the objects with IsObjectReconciling set.
    ///
    /// Truth caveat: FishNet reconciles an object to the server's state when the packet carried one, and
    /// otherwise to the client's own stored state for that tick; which one it used is internal
    /// (IsReconcileRemote). The server keeps sending an object's state while its transform changes and stops
    /// once it rests, so the stored state stands in only for objects at rest on the server.
    ///
    /// Visual delay: FishNet keeps its smoother's state private (no getter). By default objects with a graphical
    /// object report none and only their raw error is measured. With "Read Smoother Delay By Reflection" on, the
    /// delay is read from the smoother's private queue and move rates instead. An object without a graphical
    /// object draws its rigidbody, which is the fraction of a tick behind the clock that the frame is drawn
    /// into the next tick.
    /// </summary>
    public class VisualFidelityFeeder : MonoBehaviour
    {
        [Tooltip("Adds a probe to every predicted object that doesn't have one. Off: only objects whose prefab carries a probe are measured.")]
        [SerializeField] bool attachToAllEntities = true;

        [Tooltip("Reads the tick smoother's private queue and move rates by reflection to get the visual delay, so FishNet gets a residual too. Off: public API only, raw error only. Can be flipped at runtime, also with the key below.")]
        [SerializeField] bool readSmootherDelayByReflection;
#if ENABLE_INPUT_SYSTEM
        [SerializeField] Key reflectionToggleKey = Key.F10;
#else
        [SerializeField] KeyCode reflectionToggleKey = KeyCode.F10;
#endif

        // How often the spawned objects are looked through for new ones.
        const float ScanSeconds = 0.25f;

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
            public NetworkObject Object;
            public Rigidbody Body;
            public VisualFidelityProbe Probe;
            public FidelitySubject RemoteKind;
            public bool Smoothed;
        }

        readonly Dictionary<NetworkObject, Feed> _feeds = new Dictionary<NetworkObject, Feed>();
        readonly List<NetworkObject> _gone = new List<NetworkObject>();
        NetworkManager _nm;
        TimeManager _tm;
        FishNetPredictionManager _pm;
        float _nextScan;
        bool _reflectionLogged;

        void OnDisable() => Detach();

        void LateUpdate()
        {
            if (WasPressed(reflectionToggleKey))
                readSmootherDelayByReflection = !readSmootherDelayByReflection;
            if (readSmootherDelayByReflection != _reflectionLogged)
            {
                _reflectionLogged = readSmootherDelayByReflection;
                Debug.Log(readSmootherDelayByReflection
                    ? "[VisualFidelity][FishNet] Smoother delay by reflection: ON (TransformTickSmoother._transformProperties, _moveRates)." +
                      (SmootherAccess.Available ? "" : $" Unavailable, so still raw error only: {SmootherAccess.Error}")
                    : "[VisualFidelity][FishNet] Smoother delay by reflection: OFF, public API only. Smoothed objects get raw error only.");
            }

            NetworkManager nm = InstanceFinder.NetworkManager;
            // The host's own client doesn't reconcile; only client-only instances predict.
            if (nm == null || !nm.IsClientOnlyStarted)
                nm = null;
            // Reference comparison: a destroyed manager compares equal to null and would otherwise go unnoticed.
            if (!ReferenceEquals(nm, _nm))
            {
                Detach();
                if (nm is not null)
                    Attach(nm);
            }
            if (_nm is null)
                return;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanSeconds;
                Scan();
            }

            // The smoothers moved the graphical objects in Update; nothing moves them again before the probes sample.
            double rawDelay = TickFraction() * _tm.TickDelta;
            foreach (Feed feed in _feeds.Values)
            {
                if (feed.Probe == null || feed.Object == null)
                    continue;
                feed.Probe.Subject = feed.Object.IsOwner ? FidelitySubject.LocalPlayer : feed.RemoteKind;
                feed.Probe.ReportVisualDelay(!feed.Smoothed ? rawDelay
                    : readSmootherDelayByReflection ? SmootherDelay(feed.Object)
                    : double.NaN);
            }
        }

        void Attach(NetworkManager nm)
        {
            _nm = nm;
            _tm = nm.TimeManager;
            _pm = nm.GetComponent<FishNetPredictionManager>();
            if (_pm != null)
                _pm.OnPostPhysicsTransformSync += OnPostPhysicsTransformSync;
            else
                Debug.LogWarning("[VisualFidelity][FishNet] No PredictionManager on the NetworkManager: no server states, nothing can be compared.");

            _nextScan = 0f;
            VisualFidelity.ResetSession();
            VisualFidelity.LibraryName = "FishNet";
            VisualFidelity.Clock = Clock;
        }

        void Detach()
        {
            if (_pm is not null)
                _pm.OnPostPhysicsTransformSync -= OnPostPhysicsTransformSync;
            if (_nm is not null)
            {
                VisualFidelity.Clock = null;
                VisualFidelity.ResetSession();
            }
            _feeds.Clear();
            _nm = null;
            _tm = null;
            _pm = null;
        }

        void Scan()
        {
            foreach (NetworkObject nob in _nm.ClientManager.Objects.Spawned.Values)
            {
                if (nob == null || !nob.EnablePrediction || _feeds.ContainsKey(nob))
                    continue;
                var body = nob.GetComponent<Rigidbody>();
                if (body == null)
                    continue;

                var probe = nob.GetComponent<VisualFidelityProbe>();
                if (probe == null)
                {
                    if (!attachToAllEntities)
                        continue;
                    probe = nob.gameObject.AddComponent<VisualFidelityProbe>();
                    probe.Label = $"{nob.name.Replace("(Clone)", "")} #{nob.ObjectId}";
                }

                Transform graphics = nob.GetGraphicalObject();
                probe.Visual = graphics != null ? graphics : nob.transform;
                _feeds[nob] = new Feed
                {
                    Object = nob,
                    Body = body,
                    Probe = probe,
                    RemoteKind = Classify(nob),
                    Smoothed = graphics != null,
                };
            }

            _gone.Clear();
            foreach (KeyValuePair<NetworkObject, Feed> pair in _feeds)
                if (pair.Key == null || !pair.Key.IsSpawned)
                    _gone.Add(pair.Key);
            foreach (NetworkObject nob in _gone)
                _feeds.Remove(nob);
        }

        static FidelitySubject Classify(NetworkObject nob)
        {
            // Bots are the player prefab spawned without an owner.
            if (nob.GetComponent<PredictedPlayerController>() != null)
                return nob.Owner.IsValid ? FidelitySubject.RemotePlayer : FidelitySubject.Bot;
            if (nob.GetComponent<RigidbodySync>() != null)
                return FidelitySubject.Ball;
            return FidelitySubject.Other;
        }

        void OnPostPhysicsTransformSync(uint clientTick, uint serverTick)
        {
            double tickSeconds = _tm.TickDelta;
            foreach (Feed feed in _feeds.Values)
            {
                if (feed.Probe == null || feed.Body == null || !feed.Object.IsObjectReconciling)
                    continue;
                feed.Probe.ReportServerTick(clientTick, tickSeconds, feed.Body.position, feed.Body.rotation);
            }
        }

        /// <summary>
        /// How far behind the clock FishNet's tick smoother draws the graphical object, in seconds, or NaN.
        ///
        /// After each tick the smoother queues the object's pose for that tick. The graphical object moves to the
        /// oldest queued pose over one tick length (the move rates' TimeRemaining counts down, sped up or slowed
        /// down by the smoother's multiplier), then the next. So it shows the moment
        /// first queued tick - TimeRemaining / tick length, which accounts for adaptive interpolation and
        /// the multiplier as they are this frame.
        /// </summary>
        // TransformTickSmoother is marked obsolete for FishNet 5, but in 4.x it is what NetworkObject's graphical
        // object setting (the one the demo prefabs use) runs on; NetworkTickSmoother is the separate component.
#pragma warning disable CS0618
        double SmootherDelay(NetworkObject nob)
        {
            TransformTickSmoother smoother = nob.PredictionSmoother;
            if (smoother == null || !SmootherAccess.Available)
                return double.NaN;
            try
            {
                // An empty queue means the graphical object is standing still, not drawing any particular tick.
                if (SmootherAccess.QueueCount(smoother) == 0)
                    return double.NaN;
                float remaining = SmootherAccess.TimeRemaining(smoother);
                if (float.IsNaN(remaining) || float.IsInfinity(remaining))
                    return double.NaN;
                double tickSeconds = _tm.TickDelta;
                double drawnTick = SmootherAccess.FirstTick(smoother) - Math.Clamp(remaining / tickSeconds, 0.0, 1.0);
                return (_tm.LocalTick - 1.0 + TickFraction() - drawnTick) * tickSeconds;
            }
            catch (Exception e)
            {
                SmootherAccess.Disable(e);
                return double.NaN;
            }
        }

        /// <summary>
        /// Getters for the TransformTickSmoother fields SmootherDelay needs, compiled once from expression trees so
        /// that reading them each frame neither allocates nor goes through reflection. The queued entry type is
        /// private to FishNet, so the reads can't be written as ordinary code.
        /// </summary>
        static class SmootherAccess
        {
            public static Func<TransformTickSmoother, int> QueueCount;
            public static Func<TransformTickSmoother, uint> FirstTick;
            public static Func<TransformTickSmoother, float> TimeRemaining;
            public static bool Available;
            public static string Error;

            static SmootherAccess()
            {
                try
                {
                    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                    FieldInfo queueField = typeof(TransformTickSmoother).GetField("_transformProperties", Private);
                    FieldInfo ratesField = typeof(TransformTickSmoother).GetField("_moveRates", Private);
                    if (queueField == null || ratesField == null)
                        throw new MissingFieldException("TransformTickSmoother has no _transformProperties / _moveRates field any more.");

                    ParameterExpression smoother = Expression.Parameter(typeof(TransformTickSmoother), "smoother");
                    Expression queue = Expression.Field(smoother, queueField);
                    QueueCount = Expression.Lambda<Func<TransformTickSmoother, int>>(
                        Expression.Property(queue, "Count"), smoother).Compile();
                    FirstTick = Expression.Lambda<Func<TransformTickSmoother, uint>>(
                        Expression.Field(Expression.Call(queue, "Peek", null), "Tick"), smoother).Compile();
                    TimeRemaining = Expression.Lambda<Func<TransformTickSmoother, float>>(
                        Expression.Field(Expression.Field(smoother, ratesField), nameof(MoveRates.TimeRemaining)), smoother).Compile();
                    Available = true;
                }
                catch (Exception e)
                {
                    Error = e.Message;
                }
            }

            public static void Disable(Exception e)
            {
                Available = false;
                Error = e.Message;
                Debug.LogWarning($"[VisualFidelity][FishNet] Reading the smoother delay failed, back to raw error only: {e.Message}");
            }
        }
#pragma warning restore CS0618

#if ENABLE_INPUT_SYSTEM
        static bool WasPressed(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }
#else
        static bool WasPressed(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);
#endif

        /// <summary>FishNet increments LocalTick after OnPostTick, so the newest simulated tick is LocalTick - 1.</summary>
        double Clock() => (_tm.LocalTick - 1.0 + TickFraction()) * _tm.TickDelta;

        /// <summary>How far into the next tick the frame is drawn, 0 to 1.</summary>
        double TickFraction() => Math.Clamp(_tm.GetTickPercentAsDouble(), 0.0, 1.0);
    }
}
