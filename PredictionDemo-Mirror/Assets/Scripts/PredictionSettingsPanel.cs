using System.Collections.Generic;
using Mirror;
using UnityEngine;
using static PredictionDebug.SettingsSheet;

namespace PredictionDebug
{
    // Mirror's prediction lives in PredictedRigidbody: every prefab with one runs local Unity physics and is corrected
    // from server state, there is no tick, input buffer or resimulation. Per-prefab values come from the registered prefabs.
    public class PredictionSettingsPanel : PredictionSettingsPanelBase
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn() => SpawnIfMissing<PredictionSettingsPanel>();

        protected override string LibraryName => "Mirror";

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

        static string Rb(System.Func<PredictedRigidbody, string> describe) => PerPrefab(Prefabs(), describe);

        protected override void Describe(SettingsSheet s)
        {
            s.Set(PredictionSetting.SimulationRate, () => Hz(1f / Time.fixedDeltaTime));
            s.Set(PredictionSetting.StateSendRate, () => Hz(NetworkServer.sendRate));
            s.Set(PredictionSetting.InputSendRate, () => Hz(NetworkClient.sendRate));
            s.Set(PredictionSetting.RenderRate, RenderRate);
            s.Set(PredictionSetting.VSync, VSync);
            s.Set(PredictionSetting.PhysicsStep, () => Physics.simulationMode.ToString());

            // Every PredictedRigidbody runs local physics, but only the local player's input is applied.
            s.Set(PredictionSetting.PredictRemote, () => "physics only");
            s.NotApplicable(PredictionSetting.InputRedundancy);
            s.Set(PredictionSetting.ServerInputBuffer, () => Ticks(0));
            s.Set(PredictionSetting.ClientHistory, () => Rb(rb => $"{rb.stateHistoryLimit} × {Ms(rb.recordInterval)}"));
            s.Set(PredictionSetting.ResimThreshold, () => Rb(rb => $"{Num(rb.positionCorrectionThreshold)} m / {Num(rb.rotationCorrectionThreshold)}°"));
            // No resimulation: the server state is applied and recorded deltas are re-applied on top.
            s.NotApplicable(PredictionSetting.MaxResimsPerTick);
            s.NotApplicable(PredictionSetting.ExtrapolateInput);

            s.Set(PredictionSetting.Interpolation, () => Rb(rb => rb.mode.ToString()));
            s.NotApplicable(PredictionSetting.InterpolationBuffer);
            s.Set(PredictionSetting.InterpolationDelay, () => Ticks(0));
            s.Set(PredictionSetting.CorrectionRate, () => Rb(rb => rb.mode == PredictionMode.Smooth ? $"{Num(rb.positionInterpolationSpeed)}/s" : None));
            s.Set(PredictionSetting.SnapDistance, () => Rb(rb => $"{Num(rb.teleportDistanceMultiplier)}× size"));

            s.Extra("Snapshot buffer", () => Ms(NetworkClient.bufferTime));
            s.Extra("Snap below speed", () => Rb(rb => $"{Num(rb.snapThreshold)} m/s"));
        }
    }
}
