# ClientSidePrediction-Compare
Repo containing comparative implementations of client side prediction for Unity Game Objects.

The same small physics game is built four times, once for each networking solution, so you can compare how each one handles client-side prediction, reconciliation and drift under the same conditions.

## The demo

Every project contains the same `Gameplay` scene: an arena with obstacles and a floor, a player-controlled physics box, and free physics balls.

| Input | Action |
|---|---|
| Up / Down arrows | Throttle forward / backward |
| Left / Right arrows | Steer |
| Space | Boost |
| B (server) | Spawn a ball (FishNet, Mirror and PurrNet; the Ursitoare project has the ball prefab but no spawner yet) |

The player is a Rigidbody driven only by forces: rotation power 10, throttle 10, boost 50, mass 1. Balls have mass 0.1 and no controller; they move only through physics and collisions.

## Projects

Each folder is a separate Unity project. Open them one at a time in Unity Hub.

| Folder | Networking | Prediction |
|---|---|---|
| `PredictionDemo-Ursitoare` | Mirror | [Ursitoare](https://github.com/iFlex/Ursitoare) |
| `PredictionDemo-FishNet` | FishNet | FishNet built-in prediction (`[Replicate]` / `[Reconcile]` + `PredictionRigidbody`) |
| `PredictionDemo-Mirror` | Mirror | Mirror's `PredictedRigidbody` component |
| `PredictionDemo-PurrNet` | PurrNet | PurrDiction (`PredictedIdentity` + `PredictedRigidbody`) |

Each project also has a timing config script, so all four run on comparable settings: 120 Hz simulation and rendering, vSync off, run in background.

| Project | Config | Network rate |
|---|---|---|
| Ursitoare | `PredictionDemoConfig` + `NetworkPredictionManagerAdapter.ApplyConfig()` | 60 Hz |
| FishNet | `FishNetDemoConfig` | 120 Hz (sends once per tick) |
| Mirror | `MirrorDemoConfig` | 60 Hz |
| PurrNet | `PurrNetDemoConfig` | 120 Hz (sends once per tick) |

`FishNetDemoConfig`, `MirrorDemoConfig` and `PurrNetDemoConfig` are components and only take effect once they're added to a GameObject in the scene. The Ursitoare config is built into its prediction adapter and always runs.

## Prerequisites

All projects use **Unity 6000.3.7f1** (Unity 6.3) and URP.

### Packages imported automatically

These are listed in each project's `Packages/manifest.json`, which is committed. Unity's Package Manager downloads them when the project is opened, so there's nothing to do beyond having internet access the first time.

| Package | Ursitoare | FishNet | Mirror | PurrNet |
|---|---|---|---|---|
| Input System (`com.unity.inputsystem` 1.18.0) | ✓ | ✓ | ✓ | ✓ |
| Universal RP (`com.unity.render-pipelines.universal` 17.3.0) | ✓ | ✓ | ✓ | ✓ |
| Newtonsoft Json (`com.unity.nuget.newtonsoft-json` 3.2.2) | ✓ | | ✓ | ✓ |
| Collections (`com.unity.collections` 2.6.8) | | | | ✓ |
| Mathematics (`com.unity.mathematics` 1.3.3) | | | | ✓ |
| Mono Cecil (`com.unity.nuget.mono-cecil` 1.11.6) | | | | ✓ |
| Ursitoare (`sector0.ursitoare`, git) | ✓ | | | |
| SafeEventDispatcher (`sector0.safe-event-dispatcher`, git) | ✓ | | | |

The two git packages need [Git](https://git-scm.com/) installed and on your `PATH`, otherwise the Package Manager can't fetch them.

### Imported manually

These libraries live in each project's `Assets/` folder and are **not committed** (see `.gitignore`). Import them yourself **before** opening the scenes, or the scripts won't compile and the scenes will have missing references. Use the versions listed; the demos were written against them.

| Project | Import into `Assets/` | Version | Source |
|---|---|---|---|
| **Ursitoare** | Mirror | 96.0.1 | [Asset Store](https://assetstore.unity.com/packages/tools/network/mirror-129321) or [GitHub releases](https://github.com/MirrorNetworking/Mirror/releases) |
| **FishNet** | FishNet | 4.7.3 | [Asset Store](https://assetstore.unity.com/packages/tools/network/fish-net-networking-evolved-207815) or [GitHub releases](https://github.com/FirstGearGames/FishNet/releases) |
| | Scalable Grid Prototype Materials | — | Unity Asset Store |
| **Mirror** | Mirror | 96.0.1 | [Asset Store](https://assetstore.unity.com/packages/tools/network/mirror-129321) or [GitHub releases](https://github.com/MirrorNetworking/Mirror/releases) |
| | Scalable Grid Prototype Materials | — | Unity Asset Store |
| **PurrNet** | PurrNet | 1.23.0-beta.51 | [purrnet.dev](https://purrnet.dev/) |
| | PurrDiction (PurrNet's prediction addon) | 1.3.3 | [purrnet.dev](https://purrnet.dev/) |
| | Scalable Grid Prototype Materials | — | Unity Asset Store |

The Ursitoare project already includes the grid materials, so it doesn't need that import.

**Order:** open the project, let the Package Manager finish, import the manual packages, then open `Assets/Scenes/Gameplay.unity`.

## Running a test

1. Build the project, or use a second editor instance (for example a ParrelSync or Multiplayer Play Mode clone).
2. Start one instance as host or server and the other as client.
   - **PurrNet:** add `PurrNetStartHUD` to the scene for Host / Server / Client buttons. Without it, the NetworkManager starts automatically.
3. Drive the box and spawn balls. Compare how closely the client follows the server.

## Shared assets

The `.unitypackage` files in the repo root hold the parts every variant shares. Use them when adding a new networking solution.

| Package | Contents |
|---|---|
| `CommonGameplayScene.unitypackage` | `Assets/Scenes/Gameplay.unity` (the arena) |
| `CommonConfigs.unitypackage` | `Assets/Configurations/BallMaterial` and `PlayerMaterial` physics materials |
| `PredictedPlayer.unitypackage` | `Assets/Prefabs/PredictedPlayer.prefab` |
| `NonControllableBall.unitypackage` | `Assets/Prefabs/NonControllableBall.prefab` |

## Per-project notes

- **FishNet:** `PredictionDemo-FishNet/FishNetPredictionReview.md` lists a known issue. The player never sends reconcile states, so the client isn't corrected. It also covers the fix and the settings that differ from the other demos.
- **PurrNet:** `PredictionDemo-PurrNet/PurrNetSetup.md` covers the remaining scene and prefab wiring: `PredictedRigidbody` on the prefabs, `PredictedPlayerSpawner`, `BallSpawner`, `PurrNetDemoConfig`, `PurrNetStartHUD`.
- **Physics determinism:** *Enhanced Determinism* (Project Settings → Physics) is off in all four projects. Turn it on in all of them for a fairer comparison.
