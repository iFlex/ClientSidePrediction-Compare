using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;
using static PredictionDebug.SettingsSheet;
using FishNetPredictionManager = FishNet.Managing.Predicting.PredictionManager;

namespace PredictionDebug
{
    // FishNet splits its prediction settings between the PredictionManager (buffering, resends) and each
    // NetworkObject's prediction section (smoothing). Most of the NetworkObject ones are private, hence the daggers.
    public class PredictionSettingsPanel : PredictionSettingsPanelBase
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => SpawnIfMissing<PredictionSettingsPanel>();

        protected override string LibraryName => "FishNet";

        static NetworkManager Nm => InstanceFinder.NetworkManager;
        static TimeManager Tm => Nm.TimeManager;
        // NetworkManager.PredictionManager is internal; the component itself is public.
        static FishNetPredictionManager Pm => Nm.GetComponent<FishNetPredictionManager>();
        static double TickHz => Tm.TickRate;

        static IEnumerable<GameObject> Prefabs()
        {
            var prefabs = Nm != null ? Nm.SpawnablePrefabs : null;
            if (prefabs == null)
                yield break;
            for (int i = 0; i < prefabs.GetObjectCount(); i++)
            {
                NetworkObject nob = prefabs.GetObject(true, i);
                if (nob != null)
                    yield return nob.gameObject;
            }
        }

        static string Nob(System.Func<NetworkObject, string> describe) => PerPrefab(Prefabs(), describe);

        static string SpectatorInterpolation(NetworkObject nob)
        {
            string adaptive = Reflect.Get<object>(nob, "_adaptiveInterpolation").ToString();
            return adaptive == "Off" ? Reflect.Get<byte>(nob, "_spectatorInterpolation").ToString() : adaptive;
        }

        protected override void Describe(SettingsSheet s)
        {
            // Inputs and reconciles go out every tick (reconciles only while an object has input or moves).
            s.Set(PredictionSetting.SimulationRate, () => Hz(TickHz));
            s.Set(PredictionSetting.StateSendRate, () => Hz(TickHz));
            s.Set(PredictionSetting.InputSendRate, () => Hz(TickHz));
            s.Set(PredictionSetting.RenderRate, RenderRate);
            s.Set(PredictionSetting.VSync, VSync);
            s.Set(PredictionSetting.PhysicsStep, () => Tm.PhysicsMode.ToString());

            s.Set(PredictionSetting.PredictRemote, () => Nob(nob => nob.EnablePrediction && nob.EnableStateForwarding ? "yes" : "no"));
            s.Set(PredictionSetting.InputRedundancy, () => Reflect.Get<byte>(Pm, "RedundancyCount").ToString());
            s.Set(PredictionSetting.ServerInputBuffer, () => Ticks(Pm.StateInterpolation));
            s.Set(PredictionSetting.ClientHistory, () => Ticks(Reflect.Get<ushort>(Pm, "MaximumPastReplicates")));
            // Every received reconcile rolls back and replays, there is no threshold.
            s.Set(PredictionSetting.ResimThreshold, () => "0 (always)");
            s.NotApplicable(PredictionSetting.MaxResimsPerTick);
            s.Set(PredictionSetting.ExtrapolateInput, () => "no");

            s.Set(PredictionSetting.Interpolation, () => Nob(nob => nob.GetGraphicalObject() != null ? "TickSmoother" : "off"));
            s.Set(PredictionSetting.InterpolationBuffer, () => Nob(nob =>
                $"own {Reflect.Get<byte>(nob, "_ownerInterpolation")} / other {SpectatorInterpolation(nob)} ticks"));
            // Spectators also run StateInterpolation ticks behind before their smoothing.
            s.Set(PredictionSetting.InterpolationDelay, () => Nob(nob =>
                $"own {Reflect.Get<byte>(nob, "_ownerInterpolation")} / other {Pm.StateInterpolation}+{SpectatorInterpolation(nob)} ticks"));
            s.NotApplicable(PredictionSetting.CorrectionRate);
            s.Set(PredictionSetting.SnapDistance, () => Nob(nob => Reflect.Get<bool>(nob, "_enableTeleport")
                ? $"{Num(Reflect.Get<float>(nob, "_teleportThreshold"))} m"
                : "off"));

            s.Extra("Max server queue", () => Pm.GetMaximumServerReplicates().ToString());
            s.Extra("State order", () => Pm.StateOrder.ToString());
        }
    }
}
