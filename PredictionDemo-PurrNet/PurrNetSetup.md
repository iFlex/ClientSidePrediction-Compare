# PurrNet prediction demo: setup

The same demo as the Ursitoare and FishNet variants: a box player driven by forces, and balls the server spawns with **B**. It's built on PurrDiction 1.3.3 / PurrNet 1.23.0-beta.51.

The scripts are written but haven't been compiled or run. The scene and prefab wiring below has to be done in the Unity editor.

## Scripts (`Assets/Scripts`)

| Script | Equivalent in the other demos | What it does |
|---|---|---|
| `PredictedPlayerController.cs` | `PredictedPlayerController` (FishNet) / `PredictablePlayerController` (Ursitoare) | Up/down arrows throttle, left/right arrows steer, Space boosts. Same constants: rotation 10, throttle 10, boost 50. Forces are applied through `PredictedRigidbody`. |
| `BallSpawner.cs` | `FishnetGameController` | When the server presses B, it spawns a ball with `hierarchy.Create` inside `Simulate`, so the ball is part of the predicted world. |

The ball doesn't need a script. A `PredictedRigidbody` on the prefab is enough, the same role `RigidbodySync` plays in FishNet.

## Editor wiring

1. **Player prefab** (`Assets/Prefabs/Player.prefab`)
   - Add `PredictedRigidbody`. The Rigidbody is already there, mass 1.
   - Add `PredictedPlayerController`.
2. **Ball prefab**: create `Assets/Prefabs/NonControllableBall.prefab`.
   - A sphere with a Rigidbody, mass 0.1 to match the other demos.
   - Add `PredictedRigidbody`.
3. **Predicted prefabs asset**: already exists as `Assets/PredictedPrefabs.asset`. Make sure the player and ball prefabs are listed in it. *autoGenerate* should add them, otherwise add them by hand.
4. **Scene** (`Assets/Scenes/Gameplay.unity`)
   - The `PredictionManager` GameObject is already in the scene, with `PredictedPrefabs.asset` assigned. Nothing to add.
   - Add a new GameObject with `PredictedPlayerSpawner`, and set its player prefab to `Player.prefab`. Spawn points are optional.
   - Add a GameObject with `BallSpawner`, set its ball prefab to the ball prefab, and optionally give it a spawn point.

5. **Start menu** (optional): add `PurrNetStartHUD` to any GameObject in the scene. It shows Host, Server Only and Client buttons, plus address and port fields for the UDP transport. It also switches off the NetworkManager's auto-start, so the editor no longer starts a host by itself. Untick *Disable Auto Start* on the component to keep the old behaviour.

## Keep it comparable with the other demos

- **Timing config:** add `PurrNetDemoConfig` to any GameObject in `Gameplay.unity`, for example the NetworkManager. At startup it sets the tick rate to 120 Hz, `targetFrameRate` to 120, `vSyncCount` to 0 and `runInBackground` to true. These match AntShipWars and the Ursitoare demo. It runs before the NetworkManager's own `Awake`, because PurrNet refuses tick rate changes once networking has started. Without it, the scene's `_tickRate: 20` applies. FishNet is still at 30.
- **Physics stepping:** PurrDiction switches physics to manual stepping itself (`PredictionManager.cs:128`), so nothing needs to change.
- **Enhanced Determinism** is off (Project Settings → Physics), as in the other two demos.

## Notes on the implementation

- Held keys are read in `GetFinalInput`, which PurrDiction calls once per tick on the controlling client. The B press is collected in `UpdateInput`, which runs every frame, so a press between two ticks isn't lost. PurrDiction resets the input after each tick.
- `BallSpawner` clears `spawn` in `ModifyExtrapolatedInput`. Without that, a client filling in missing server input by repeating the last one could predict several balls from one key press.
- `BallSpawner` has no owner, so the server is its controller (`PredictedIdentity.IsOwner(player, asServer)`), and only the server reads the B key.
