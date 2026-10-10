// Ursitoare feeder for the visual fidelity probes. Public API only: each client entity's serverStateBuffer
// and its PredictedEntityVisuals.
// Nothing to set up: it creates itself after the first scene loads. See VisualFidelity.md.

using System;
using System.Collections.Generic;
using DefaultNamespace;
using Sector0.Ursitoare;
using Sector0.Ursitoare.Components;
using Sector0.Ursitoare.Data;
using Sector0.Ursitoare.Utils;
using UnityEngine;

namespace PredictionDebug
{
    /// <summary>
    /// On a client-only instance, puts a <see cref="VisualFidelityProbe"/> on every predicted entity and feeds it
    /// the server states the client receives and the visuals' delay. The visual is the detached
    /// PredictedEntityVisuals object.
    ///
    /// Timeline: Ursitoare stamps every server state, the local player's and the followers' alike, with
    /// this client's tick that the server had reached for it. So server states and frames share the client
    /// tick timeline, and a state for tick T sits at T × fixedDeltaTime.
    ///
    /// Visual delay: PredictedEntityVisuals.GetVisualDelay() says how far the visuals trail the newest simulated
    /// state, the averaging window included. The feeder adds how far the clock has moved past that tick.
    /// </summary>
    public class VisualFidelityFeeder : MonoBehaviour
    {
        [Tooltip("Adds a probe to every predicted entity that doesn't have one. Off: only entities whose prefab carries a probe are measured.")]
        [SerializeField] bool attachToAllEntities = true;

        // Upper bound on states pushed for one entity in one frame, after a long hitch.
        const uint MaxBackfillTicks = 2048;

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
            public VisualFidelityProbe Probe;
            public PredictedEntityVisuals Visuals;
            public FidelitySubject RemoteKind;
            public uint NextTick;
        }

        readonly Dictionary<ClientPredictedEntity, Feed> _feeds = new Dictionary<ClientPredictedEntity, Feed>();
        readonly List<ClientPredictedEntity> _gone = new List<ClientPredictedEntity>();
        ClientPredictionManager _client;

        void OnDisable()
        {
            if (_client != null)
                VisualFidelity.Clock = null;
            _client = null;
            _feeds.Clear();
        }

        // Server states land in Mirror's early update; the probes sample in onBeforeRender, after this.
        void LateUpdate()
        {
            var client = PredictionManager.Instance as ClientPredictionManager;
            if (client != _client)
            {
                _client = client;
                _feeds.Clear();
                VisualFidelity.ResetSession();
                VisualFidelity.LibraryName = "Ursitoare";
                VisualFidelity.Clock = client != null ? Clock : (Func<double>)null;
            }
            if (client == null)
                return;

            double tickSeconds = Time.fixedDeltaTime;
            foreach (PredictedEntity entity in client.GetPredictedEntities())
            {
                ClientPredictedEntity clientEntity = entity?.GetClientEntity();
                if (clientEntity == null || clientEntity.gameObject == null)
                    continue;

                if (!_feeds.TryGetValue(clientEntity, out Feed feed))
                {
                    feed = Attach(entity, clientEntity);
                    _feeds[clientEntity] = feed;
                }
                if (feed.Probe == null)
                    continue;

                // Ownership can arrive after the entity registered, so this is checked every frame.
                feed.Probe.Subject = clientEntity.isControlledLocally ? FidelitySubject.LocalPlayer : feed.RemoteKind;
                PushServerStates(clientEntity, feed, tickSeconds);
                // The visuals moved in Update; nothing moves them again before the probe samples them.
                feed.Probe.ReportVisualDelay(VisualDelay(feed.Visuals));
            }

            _gone.Clear();
            foreach (KeyValuePair<ClientPredictedEntity, Feed> pair in _feeds)
                if (pair.Key.gameObject == null)
                    _gone.Add(pair.Key);
            foreach (ClientPredictedEntity entity in _gone)
                _feeds.Remove(entity);
        }

        Feed Attach(PredictedEntity entity, ClientPredictedEntity clientEntity)
        {
            GameObject root = clientEntity.gameObject;
            var feed = new Feed { RemoteKind = Classify(root) };

            feed.Probe = root.GetComponent<VisualFidelityProbe>();
            if (feed.Probe == null)
            {
                if (!attachToAllEntities)
                    return feed;
                feed.Probe = root.AddComponent<VisualFidelityProbe>();
                feed.Probe.Label = $"{root.name.Replace("(Clone)", "")} #{clientEntity.id}";
            }

            PredictedEntityVisuals visuals = feed.Visuals = entity.GetVisualsControlled();
            if (visuals != null && visuals.visualsEntity != null)
                feed.Probe.Visual = visuals.visualsEntity.transform;
            return feed;
        }

        static FidelitySubject Classify(GameObject root)
        {
            // The bot controller derives from the player controller, so it is tested first.
            if (root.GetComponentInChildren<PredictableBotController>(true) != null)
                return FidelitySubject.Bot;
            if (root.GetComponentInChildren<CustomPredictablePlayerController>(true) != null)
                return FidelitySubject.RemotePlayer;
            // The only other predicted prefab in the demo.
            return FidelitySubject.Ball;
        }

        static void PushServerStates(ClientPredictedEntity clientEntity, Feed feed, double tickSeconds)
        {
            TickIndexedBuffer<PhysicsStateRecord> buffer = clientEntity.serverStateBuffer;
            if (buffer == null || buffer.GetFill() == 0)
                return;

            uint end = buffer.GetEndTick();
            // Ticks restarted (new session on the same entity object).
            if (feed.NextTick > end + MaxBackfillTicks)
                feed.NextTick = 0;
            if (end < feed.NextTick)
                return;

            uint start = Math.Max(feed.NextTick, buffer.GetStartTick());
            if (end - start > MaxBackfillTicks)
                start = end - MaxBackfillTicks;
            for (uint tick = start; tick <= end; tick++)
            {
                PhysicsStateRecord state = buffer.Get(tick);
                if (state != null)
                    feed.Probe.ReportServerTick(tick, tickSeconds, state.position, state.rotation);
            }
            feed.NextTick = end + 1;
        }

        /// <summary>
        /// Ursitoare ticks in FixedUpdate and advances tickId at the end of Tick(), so the newest simulated tick
        /// is GetTickId() - 1, and its state belongs to Time.fixedTime. A frame is drawn up to one step later.
        /// </summary>
        static double Clock()
        {
            PredictionManager pm = PredictionManager.Instance;
            if (pm == null)
                return 0.0;
            return (pm.GetTickId() - 1.0 + TickFraction()) * Time.fixedDeltaTime;
        }

        /// <summary>
        /// How far behind the clock the visuals are drawn, in seconds: the interpolator's delay behind the newest
        /// simulated tick, plus the fraction of a tick the clock has moved past that tick. NaN until they draw.
        /// </summary>
        static double VisualDelay(PredictedEntityVisuals visuals)
            => visuals != null ? visuals.GetVisualDelay() + TickFraction() * Time.fixedDeltaTime : double.NaN;

        /// <summary>How far into the next tick the frame is drawn, 0 to 1.</summary>
        static double TickFraction() => Math.Clamp((Time.timeAsDouble - Time.fixedTimeAsDouble) / Time.fixedDeltaTime, 0.0, 1.0);
    }
}
