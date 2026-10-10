using System.Collections.Generic;
using Mirror;
using Sector0.Ursitoare;
using Sector0.Ursitoare.Components;
using Sector0.Ursitoare.Interpolation;
using Sector0.Ursitoare.Resimulation.Detection;
using Sector0.UrsitoareMirror;
using UnityEngine;
using static PredictionDebug.SettingsSheet;

namespace PredictionDebug
{
    // Ursitoare keeps most of its tuning in static fields that NetworkPredictionManagerAdapter.ApplyConfig sets once
    // the network starts, so before that the rows show the library defaults; the panel always shows what is in effect.
    public class PredictionSettingsPanel : PredictionSettingsPanelBase
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => SpawnIfMissing<PredictionSettingsPanel>();

        protected override string LibraryName => "Ursitoare";

        static IEnumerable<GameObject> Prefabs()
        {
            var nm = NetworkManager.singleton;
            if (nm == null)
                yield break;
            if (nm.playerPrefab != null)
                yield return nm.playerPrefab;
            foreach (GameObject prefab in nm.spawnPrefabs)
                yield return prefab;
        }

        static double SimHz => 1.0 / Time.fixedDeltaTime;

        // The interpolator every client entity gets; a throwaway instance shows its type and defaults.
        // Created once: the constructor has side effects (a debug counter), and the provider does not change at runtime.
        static VisualsInterpolationsProvider _sample;
        static VisualsInterpolationsProvider SampleInterpolator() => _sample ??= PredictionManager.INTERPOLATION_PROVIDER();

        static string Threshold(SingleSnapshotInstanceResimChecker checker) => checker is SimpleConfigurableResimulationDecider d
            ? $"{Num(d.distResimThreshold)} m / {Num(d.rotationResimThreshold)}°"
            : checker?.GetType().Name ?? None;

        protected override void Describe(SettingsSheet s)
        {
            // State and input go out as one message per tick, batched and flushed by Mirror at its send rate.
            s.Set(PredictionSetting.SimulationRate, () => Hz(SimHz));
            s.Set(PredictionSetting.StateSendRate, () => Hz(NetworkServer.sendRate));
            s.Set(PredictionSetting.InputSendRate, () => Hz(NetworkClient.sendRate));
            s.Set(PredictionSetting.RenderRate, RenderRate);
            s.Set(PredictionSetting.VSync, VSync);
            s.Set(PredictionSetting.PhysicsStep, () => Physics.simulationMode.ToString());

            s.Set(PredictionSetting.PredictRemote, () => !ClientPredictionManager.PREDICT_FOLLOWERS ? "no"
                : ClientPredictedEntity.APPLY_SERVER_INPUT_TO_FOLLOWERS ? "with input" : "no input");
            s.NotApplicable(PredictionSetting.InputRedundancy);
            s.Set(PredictionSetting.ServerInputBuffer, () => Ticks(ServerPredictedEntity.USE_BUFFERING ? ServerPredictedEntity.BUFFER_FULL_THRESHOLD : 0));
            s.Set(PredictionSetting.ClientHistory, () => PerPrefab<AbstractPredictedNetworkBehaviour>(Prefabs(), b => Ticks(b.BufferSize)));
            s.Set(PredictionSetting.ResimThreshold, () => ClientPredictionManager.PREDICTION_ENABLED ? Threshold(PredictionManager.SNAPSHOT_INSTANCE_RESIM_CHECKER) : "off");
            s.Set(PredictionSetting.MaxResimsPerTick, () => PredictionManager.Instance != null ? PredictionManager.Instance.maxTickResimulationCount.ToString() : "?");
            s.NotApplicable(PredictionSetting.ExtrapolateInput);

            s.Set(PredictionSetting.Interpolation, () => SampleInterpolator().GetType().Name.Replace("Interpolator", ""));
            s.Set(PredictionSetting.InterpolationBuffer, () => SampleInterpolator() is MovingAverageInterpolator m ? Ticks(m.slidingWindowTickSize) : "?");
            // A moving average over N ticks trails the newest tick by about (N - 1) / 2 ticks.
            s.Set(PredictionSetting.InterpolationDelay, () => SampleInterpolator() is MovingAverageInterpolator m ? "~" + Ticks((m.slidingWindowTickSize - 1) / 2.0) : "?");
            s.NotApplicable(PredictionSetting.CorrectionRate);
            s.Set(PredictionSetting.SnapDistance, () => ClientPredictionManager.DO_SNAP ? "on" : "off");

            s.Extra("Follower threshold", () => Threshold(PredictionManager.FOLLOWER_INSTANCE_RESIM_CHECKER));
            s.Extra("Catch-up sections", () => ServerPredictedEntity.CATCHUP ? ServerPredictedEntity.CATCHUP_SECTIONS.ToString() : "off");
        }
    }
}
