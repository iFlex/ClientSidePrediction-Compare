# Reading prediction settings: how open each library is

While building the on-screen prediction settings panel (`PredictionSettingsPanel`, F4), every setting that
could not be read through the library's public API was read with reflection instead (`Shared/.../PredictionSettings/Reflect.cs`).
Those values are marked with a † on screen. This file lists them per library, as a note on how inspectable
each library's tuning is. Only reading is covered here; see the earlier notes on what can be *changed* at runtime.

## Mirror: fully public

Nothing needed reflection.

- Every `PredictedRigidbody` setting is a public field: mode, history, correction thresholds, follow speeds, snap and teleport.
- `NetworkServer.sendRate`, `NetworkClient.sendRate`, `NetworkClient.bufferTime` and `NetworkClient.snapshotSettings` are public statics.

Public fields can also be changed by any script at any time, so nothing guards a value against being changed mid-session.

## Ursitoare: fully public, but global

Nothing needed reflection in the library.

- Almost all tuning is `public static` fields (`ServerPredictedEntity.BUFFER_FULL_THRESHOLD`, `PredictionManager.PREDICT_FOLLOWERS`, the resim checkers, ...).
  They are easy to read, but they are process-wide rather than per entity or per world.
- Some statics are only read when an entity is constructed, so changing them later silently does nothing for existing entities
  (`CATCHUP_SECTIONS`, `FOLLOWER_INSTANCE_RESIM_CHECKER`; noted in `NetworkPredictionManagerAdapter.ApplyConfig`).
- Interpolation settings live on the interpolator instance each entity creates (`INTERPOLATION_PROVIDER()`), not on anything the
  application can look up. The panel creates a throwaway instance to read the defaults. The constructor has a side effect
  (it bumps a debug counter), so the panel creates it only once.
- `MovingAverageInterpolator.minVisualTickDelay` / `MinVisualDelay` are public and computed, but unused: the autosizing code that reads them is commented out.

The demo's own code needed two getters (`NetworkPredictionManagerAdapter.Config`, `PredictedNetworkBehaviour.BufferSize`), which were added.

## FishNet: most prediction tuning is private

**`PredictionManager`**

| Setting | Access | How the panel reads it |
|---|---|---|
| `StateInterpolation`, `StateOrder`, `GetMaximumServerReplicates()` | public | directly |
| `RedundancyCount` (input/reconcile resend count) | internal | reflection † |
| `DropExcessiveReplicates`, `CreateLocalStates`, `MaximumPastReplicates` | internal | reflection † |
| `_reduceReconcilesWithFramerate`, `_minimumClientReconcileFramerate` | private, no accessor | reflection † |
| `NetworkManager.PredictionManager` | internal property | worked around with `GetComponent<PredictionManager>()` |

**`NetworkObject` (the Prediction section in the inspector)**

| Setting | Access | How the panel reads it |
|---|---|---|
| `EnablePrediction`, `EnableStateForwarding`, `GetGraphicalObject()` | public | directly |
| `_predictionType`, `_localReconcileCorrectionType` | private, and their enum types are `internal` too | reflection †, shown as text |
| `_ownerInterpolation`, `_spectatorInterpolation`, `_adaptiveInterpolation` | private, no getter | reflection † |
| `_ownerSmoothedProperties`, `_spectatorSmoothedProperties` | private, no getter | reflection † |
| `_enableTeleport`, `_teleportThreshold`, `_detachGraphicalObject` | private, no getter | reflection † |

Spectator interpolation and adaptive interpolation have public *setters* on `NetworkObject.PredictionSmoother`, but no getters.
That leaves the inspector as the only supported way to see most of what an object is configured with.

## PurrNet: adaptive timing driven by private constants

**`PredictionManager`**

| Setting | Access | How the panel reads it |
|---|---|---|
| `tickRate`, `inputRedundancyTickCount`, `smoothedInputSlackMs`, `desyncPolicy`, `predictedPrefabs` | public | directly |
| `_extrapolateMissingInputs`, `_updateViewMode` | private serialized, no getter | reflection † |
| Input lead `MinLead`, `TargetLead`, `AbsoluteMaxLead`, `InputMarginTargetSeconds` | private constants | reflection † |
| View buffer `ViewInterpolationMaxBufferSeconds`, `ViewInterpolationMaxBufferFloor`, `GetViewInterpolationMaxBufferSize()` | internal | reflection † |

**Identities**

| Setting | Access | How the panel reads it |
|---|---|---|
| `PredictedIdentity._predictionPolicy` (configured policy) | private; the resolved `predictionPolicy` is public but only set once spawned | reflection † on prefabs |
| `PredictedIdentity<I,S>._repeatInputFactor` | protected, no getter (`extrapolateInput` has one) | reflection † |
| `PredictedTransform._interpolationSettings` | private, no getter | reflection †; the settings asset's own fields are public |
| `PredictedRigidbody._softVelocityCorrectionRate` | private, no getter | reflection † |

**Hard-coded, not readable or configurable at all**

- State history: `world.tickRate * 10` (10 seconds) in `PredictedIdentityStatefull`. The panel describes it instead of reading it.
- Input history: `world.tickRate * 5` in `DeterministicIdentityWithInput`.

## Summary

| Library | Settings read via reflection | Not readable at all |
|---|---|---|
| Mirror | 0 | 0 |
| Ursitoare | 0 (globals; interpolator needs a throwaway instance) | 0 |
| FishNet | 16 | 0 |
| PurrNet | 13 | 2 (history lengths) |
