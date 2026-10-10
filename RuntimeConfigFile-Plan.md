# Plan: prediction tuning from a config file (not implemented)

Goal: each demo reads its prediction settings from a text file next to the executable at startup, so settings can be
changed between comparison runs without rebuilding. Values are applied before any networking starts, which makes them
as safe as editing them in the Unity editor.

## Why at startup, not live

Most prediction settings are only read when an object or the network starts, and some must match on server and clients.
Changing them mid-session is unsupported or unsafe (details below), so the file is applied once, before anything spawns.

## Design

- **One file per variant**, e.g. `prediction_tuning_fishnet.cfg`, read from the executable's folder
  (`Path.GetDirectoryName(Application.dataPath)`; the project root in the editor).
  If the file is missing, it is generated from the current editor values, so it always lists every available setting.
- **Format**: sections with `key = value` lines; values are JSON literals and enums are numbers, with their names listed in a comment.
  ```
  # FishNet prediction tuning, applied at startup before networking starts.
  # Delete this file to regenerate it from the editor values.
  [PredictionManager]
  _stateInterpolation = 2
  # _stateOrder: 0 Inserted, 1 Appended
  _stateOrder = 1

  [NonControllableBall : NetworkObject]
  _spectatorInterpolation = 2
  ```
- **Applying**: each section becomes a JSON object, applied with `JsonUtility.FromJsonOverwrite(json, component)`.
  That reaches private `[SerializeField]` fields and nested serializable structs without patching the libraries.
  Unknown keys log a warning and are skipped.
- **Whitelists**: each variant lists which fields of which component types are tunable.
  Never the whole component, which would include things like FishNet's `PrefabId` / `SceneId`.
- **Where it runs**: in each variant's existing `*DemoConfig` `Awake` (execution order -1000, before the network managers).
  The `*DemoConfig` component is itself a target, so tick rate, send rate and render rate come from the file too.
- **Finding the prefabs**: through each library's own prefab registry, so no scene or prefab edits are needed:
  - Mirror and Ursitoare: `NetworkManager.playerPrefab` and `spawnPrefabs`
  - FishNet: `NetworkManager.SpawnablePrefabs`
  - PurrNet: `PredictionManager.predictedPrefabs`
- **Scene objects** (FishNet `PredictionManager`, Ursitoare `NetworkPredictionManagerAdapter.config`, PurrNet `PredictionManager`)
  are changed directly.
- **Shared code**: the file parser and applier go in `Shared/com.predictioncompare.common`. Each variant only provides its whitelist.
- **Bot count**: `runtime_config.txt` (`BotGrid`) could be folded into the same file.

## Changing prefabs at runtime: conditions

At runtime a prefab reference is the loaded prefab object, so changing a component field on it before anything is spawned
affects every later instance, on the server and on the copies clients create. Conditions:

1. **It must run before the first instance exists.** Scene-placed objects and library pools created earlier keep the old values.
   **Unverified: whether PurrNet pre-creates pooled prefab instances before the loader runs.** Check this first.
2. **In the editor, the change outlives play mode.** The prefab *is* the asset, so a value from the file would replace the
   inspector value and could even be saved. The loader must record the original values and restore them when play mode exits
   (`EditorApplication.playModeStateChanged` / `Application.quitting`). This does not matter in builds.
3. **Each process applies its own file.** Server and clients must use the same file for settings that have to match
   (FishNet state forwarding, tick rates). Client-only settings (e.g. spectator interpolation) may differ per client on purpose.

Alternative that avoids condition 2: apply to each instance in an early `Awake` on the prefab instead of to the prefab asset.
This needs a small applier component added to every prefab.

Suggested first step: a play-mode check in one project that a value written to a prefab before spawning reaches the
spawned copies on server and client.

## What each library allows (research notes)

### FishNet

`NetworkObject` prediction section:

| Field | At runtime? | Notes |
|---|---|---|
| Spectator interpolation | yes: `PredictionSmoother.SetSpectatorInterpolation()` | read every tick, visual only, client-local; also turns adaptive interpolation off |
| Adaptive interpolation | yes: `PredictionSmoother.SetAdaptiveInterpolation()` | same |
| Graphical object | yes: `SetGraphicalObject()` | re-initialises the smoother |
| Owner interpolation, smoothed properties, teleport, detach | before the object initializes on that client | private, read once in `InitializeTickSmoother`; a forced re-init mid-game resets smoothing and records a wrong offset when the graphics are detached |
| Local reconcile correction type | works, no API | read on every reconcile, client-local |
| Enable prediction, prediction type, network transform | no | cached at init (subscriptions, rigidbody pauser, NetworkTransform setup) |
| State forwarding | no | read live on server and clients; must match everywhere |

`PredictionManager`:

| Setting | At runtime? | Notes |
|---|---|---|
| `StateInterpolation` | before `NetworkManager` initializes | no setter; read live in many places and also sets the resend count |
| `SetStateOrder()` | yes | affects only the client calling it |
| `SetMaximumServerReplicates()` | yes | server |

### PurrNet

No fixed interpolation delay. Input lead and buffering adapt at runtime from private constants. Supported at runtime:

- `SetPredictionPolicy` / `SetPredictionPolicyOverride`: the docs say switching is safest at ownership changes.
- `extrapolateInput`: has a public setter.

Not checked: whether changing `PredictedTransform` interpolation settings mid-game is safe.
The tick rate cannot change once a tick manager exists (see `PurrNetDemoConfig`).

### Mirror

`PredictedRigidbody` settings are public fields and `NetworkClient.snapshotSettings` is a public static.
Prefab values still need to be in place before spawning, because ghosts and cached squared thresholds are set up in `Awake`.

### Ursitoare

Tuning is in public static fields that `NetworkPredictionManagerAdapter.ApplyConfig` sets at network start.
The file only needs to fill the adapter's `config` before `Setup()` runs.
Some statics are only read when an entity is constructed (`CATCHUP_SECTIONS`, the follower resim checker).

## Related

- `LibrarySettingsAccess.md`: which settings each library exposes for reading.
- `PredictionSettingsPanel` (F4): shows the settings in effect, which is the quickest way to confirm a file was applied.
