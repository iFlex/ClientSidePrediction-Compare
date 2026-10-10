using System.Collections.Generic;
using DefaultNamespace;
using PurrNet;
using PurrNet.Prediction;
using UnityEngine;
using static PredictionDebug.SettingsSheet;

namespace PredictionDebug
{
    // PurrDiction adapts most of its timing at runtime (input lead, view buffer) from constants it keeps private,
    // so several rows are read via reflection; the 10 second state history is hard-coded and only described.
    public class PredictionSettingsPanel : PredictionSettingsPanelBase
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => SpawnIfMissing<PredictionSettingsPanel>();

        protected override string LibraryName => "PurrNet";

        static PredictionManager Pm => FindAnyObjectByType<PredictionManager>();
        // The prediction manager only learns the tick rate when it spawns; the network manager knows it from the start.
        static int TickHz => NetworkManager.main != null ? NetworkManager.main.tickRate : FindAnyObjectByType<NetworkManager>().tickRate;

        static IEnumerable<GameObject> Prefabs()
        {
            var prefabs = Pm != null ? Pm.predictedPrefabs : null;
            if (prefabs == null)
                yield break;
            foreach (var entry in prefabs.prefabs)
                if (entry.prefab != null)
                    yield return entry.prefab;
        }

        static string Policy(PredictedIdentity identity)
        {
            PredictionPolicy policy;
            var scope = identity.predictionPolicySource == PredictionPolicySource.UseScope ? FindAnyObjectByType<PredictionPolicyScope>() : null;
            if (scope != null)
                policy = scope.configuredPredictionPolicy;
            else
                policy = (PredictionPolicy)Reflect.Get<object>(identity, "_predictionPolicy");

            return policy switch
            {
                PredictionPolicy.FullPrediction => "yes",
                PredictionPolicy.ServerRelay => "relay",
                PredictionPolicy.SoftCorrection => "soft",
                PredictionPolicy.PredictedIfOwned => "owned only",
                _ => policy.ToString(),
            };
        }

        static string Interp(System.Func<TransformInterpolationSettings, string> describe) => PerPrefab<PredictedTransform>(Prefabs(), t =>
        {
            var settings = Reflect.Get<TransformInterpolationSettings>(t, "_interpolationSettings");
            return settings == null ? None : describe(settings);
        });

        static string MinMax(Vector2 v) => $"{Num(v.x)}–{Num(v.y)}";

        protected override void Describe(SettingsSheet s)
        {
            // State and input both go out once per tick.
            s.Set(PredictionSetting.SimulationRate, () => Hz(TickHz));
            s.Set(PredictionSetting.StateSendRate, () => Hz(TickHz));
            s.Set(PredictionSetting.InputSendRate, () => Hz(TickHz));
            s.Set(PredictionSetting.RenderRate, RenderRate);
            s.Set(PredictionSetting.VSync, VSync);
            s.Set(PredictionSetting.PhysicsStep, () => Physics.simulationMode.ToString());

            s.Set(PredictionSetting.PredictRemote, () => PerPrefab<PredictedRigidbody>(Prefabs(), Policy));
            s.Set(PredictionSetting.InputRedundancy, () => Pm != null ? Pm.inputRedundancyTickCount.ToString() : "?");
            // Adaptive: the client runs ahead so its input reaches the server about this early.
            s.Set(PredictionSetting.ServerInputBuffer, () => "~" + Ms(Reflect.GetStatic<float>(typeof(PredictionManager), "InputMarginTargetSeconds")));
            // Hard-coded in PredictedIdentityStatefull: tickRate * 10.
            s.Set(PredictionSetting.ClientHistory, () => Ticks(TickHz * 10));
            // Every verified server frame rolls back and replays, there is no threshold.
            s.Set(PredictionSetting.ResimThreshold, () => "0 (always)");
            s.NotApplicable(PredictionSetting.MaxResimsPerTick);
            s.Set(PredictionSetting.ExtrapolateInput, () => PerPrefab<PredictedPlayerController>(Prefabs(), c => c.extrapolateInput ? "yes" : "no"));

            s.Set(PredictionSetting.Interpolation, () => Interp(i => i.useInterpolation ? "view lerp" : "off"));
            s.Set(PredictionSetting.InterpolationBuffer, () =>
                "≤ " + Ticks(Reflect.CallStatic<int>(typeof(PredictionManager), "GetViewInterpolationMaxBufferSize", TickHz)));
            s.Set(PredictionSetting.InterpolationDelay, () => "~" + Ticks(1));
            s.Set(PredictionSetting.CorrectionRate, () => Interp(i => MinMax(i.positionInterpolation.correctionRateMinMax) + "/s"));
            s.Set(PredictionSetting.SnapDistance, () => Interp(i => MinMax(i.positionInterpolation.teleportThresholdMinMax) + " m"));

            s.Extra("Input slack now", () => Pm != null ? Num(Pm.smoothedInputSlackMs, "0.#") + " ms" : "?");
            s.Extra("Repeat input factor", () => PerPrefab<PredictedPlayerController>(Prefabs(), c => Num(Reflect.Get<float>(c, "_repeatInputFactor"))));
        }
    }
}
