# FishNet prediction demo: review

Reviewed 2026-10-03 against FishNet 4.7.3 (the copy in `Assets/FishNet`). I read the code and checked the scene and prefab settings; the demo was not run.

## 1. Bug: the player never sends reconcile states

**File:** `Assets/Scripts/PredictedPlayerController.cs`

`PredictedPlayerController` overrides `CreateReconcile()`, but nothing calls it. In FishNet 4.7.3, `NetworkBehaviour.CreateReconcile()` is an empty virtual method (`NetworkBehaviour.Prediction.cs:1242`). FishNet never calls it for you: you call it yourself, normally from post-tick. FishNet's own sample does this in `Assets/FishNet/Demos/Prediction/Rigidbody/Scripts/RigidbodyPrediction.cs:153-156`.

The prefab already subscribes to PostTick. `PredictedPlayer.prefab` has `_tickCallbacks: 6`, which means Tick | PostTick. But the controller doesn't override `TimeManager_OnPostTick`, so the base method runs and does nothing.

**What happens:**
- The server never sends the player's state to clients.
- The owning client predicts on its own and is never reconciled, so small differences keep growing. This shows up as client and server drifting apart.
- Other clients watching the player have no server states to follow either.

**Fix:** add this to `PredictedPlayerController`:

```csharp
protected override void TimeManager_OnPostTick()
{
    CreateReconcile();
}
```

The ball (`RigidbodySync.cs`) already does this correctly: it calls `RunInputs(default); CreateReconcile();` in `OnPostTick`. Only the player is affected.

## 2. Object cache mismatch

**File:** `Assets/Scripts/PredictedPlayerController.cs` (`Awake` / `OnDestroy`)

The player uses `ObjectCaches<PredictionRigidbody>`, but the ball uses `ResettableObjectCaches<PredictionRigidbody>`. `PredictionRigidbody` is resettable, and the plain cache doesn't reset it when it's stored. A reused instance could keep forces left over from its last use.

**Fix:** use `ResettableObjectCaches<PredictionRigidbody>.Retrieve()` and `ResettableObjectCaches<PredictionRigidbody>.StoreAndDefault(ref PredictionRigidbody)`, the same as `RigidbodySync`.

## 3. Cleanup (optional)

In `PredictedPlayerController`:
- `_renderer` and `_playerPrefab` are never used.
- `Update()` only contains commented-out code and can be removed.
- The comment above the `PredictionRigidbody` field says it's set in `OnStart/StopNetwork`, but it's actually set in `Awake`/`OnDestroy`.

## Already correct

- Forces go through `PredictionRigidbody` and then `Simulate()` at the end of the replicate, never directly on the Rigidbody.
- The input is built only for the owner and sent from `OnTick`.
- The replicate and reconcile structs implement `GetTick`/`SetTick`/`Dispose`, and the reconcile sends the whole `PredictionRigidbody`.
- NetworkObject (both prefabs): prediction is on, the type is Rigidbody, there's a graphical object for smoothing, state forwarding is on, and there's no NetworkTransform.
- `TimeManager` uses `_physicsMode: 1`, so FishNet steps physics once per tick. The project's `m_SimulationMode: 2` agrees with this.
- Run in background: FishNet's NetworkManager has `_runInBackground = true` by default and applies it on startup, so the Player Setting being off (`runInBackground: 0`) doesn't matter once it starts.

## Settings that differ from the other demos

These aren't bugs, but they make the comparison with the Ursitoare demo, which now runs AntShipWars' prediction settings, uneven.

| Setting | FishNet demo | Ursitoare demo | Where to change it |
|---|---|---|---|
| Tick rate | **30 Hz** | 120 Hz | `Gameplay.unity` → TimeManager → Tick Rate |
| Enhanced Determinism | Off | Off (AntShipWars: on) | Project Settings → Physics |

`Assets/Scripts/FishNetDemoConfig.cs` (added 2026-10-03) sets the tick rate to 120 Hz at runtime once you add it to the scene. Every build needs it, because FishNet doesn't sync the tick rate between server and clients.

At 30 Hz each tick covers four times as much movement as at 120 Hz, so each correction is larger and easier to see. To compare fairly, set the tick rate to 120 and turn Enhanced Determinism on in both projects.
