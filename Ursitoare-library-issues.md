# Ursitoare: issues found while profiling the prediction demo

Package: `sector0.ursitoare` from https://github.com/iFlex/Ursitoare.git, commit `5117080f1dd8` (as resolved in `PredictionDemo-Ursitoare/Packages/packages-lock.json`).
All paths below are relative to the package's `Runtime/src/`.

Found while comparing the four demos. Ursitoare used more CPU than the others although it resimulates less, and its resimulation graph stayed empty.

| # | Issue | Impact |
|---|---|---|
| 1 | Logging that runs every tick and every frame is on by default | High CPU: thousands of `Debug.Log` calls per second |
| 2 | Some per-frame and per-tick logs have no flag at all | They can't be turned off without editing the package |
| 3 | `TickStat` always reports that no resimulation happened | Metrics and tooling built on `onTickStat` never see a resimulation |
| 4 | `GetMinSqrDistToAllLocalEnts` measures each entity against itself | Follower distance thresholds don't work |
| 5 | The interpolator allocates two records per entity per tick | Steady GC pressure |
| 6 | The server queues a state every tick, but Mirror flushes at the network rate | When the simulation rate is above the network rate, states pile up in each flush (e.g. 2× at 120 Hz sim / 60 Hz net). Not triggered by the demo scene, which runs 60/60 |
| 7 | Each received message allocates new records and arrays | GC pressure that grows with entities × clients × tick rate |
| 8 | `TickIndexedBuffer` eviction is O(capacity) | A full-key scan on most adds and removes on hot buffers |
| 9 | The server samples inputs every tick and throws them away | Wasted work; it writes into the previous tick's input; it can throw on a headless server |
| 10 | Rigidbody state is read and written twice per tick and per rewind | Extra native calls into physics |
| 11 | The resimulation check repeats on an unchanged server state; the gap event never fires | Minor wasted work; `GAP_IN_SERVER_STREAM` is dead |
| 12 | Small per-frame waste in `PredictedEntityVisuals.Update` | Minor |
| 13 | The tick RTT timestamp is taken one tick early | `onTickRttDuration` / `lastClientTickRTT` read about one tick (~17 ms at 60 Hz) too high |

**Status:** items 1 and 2 are fixed in the local clone (`C:/Development/Ursitoare`, not yet pushed). Every log is behind a flag, and every logging flag defaults to `false`. That includes `DEBUG_OWNERSHIP` and two new flags, `PredictionManager.LOG_EVENTS` and `LOG_ERRORS`. Items 3–12 are open.

Line numbers below refer to commit `5117080`. The local clone's line numbers have shifted because of the logging changes.

---

## 1. Expensive logging is on by default

These static flags default to `true`:

| Flag | File | What it logs |
|---|---|---|
| `MovingAverageInterpolator.DEBUG` | `Interpolation/MovingAverageInterpolator.cs:16` | Every frame per entity: the `ApplyState` log (l.93) and `VISUAL_ADVANCE` (l.161). Every tick per entity: the `[Add]` log (l.284) and two `LogBufferEndStats` logs (l.288, l.292). |
| `MovingAverageInterpolator.LOG_POS` | `Interpolation/MovingAverageInterpolator.cs:17` | Three logs every tick per entity (l.255, l.274, l.276) |
| `CustomVisualInterpolator.DEBUG` / `LOG_POS` | `Interpolation/CustomVisualInterpolator.cs:16-17` | The same set of logs, for users who pick this interpolator |
| `ClientPredictedEntity.LOG_ADDED_SERVER_STATES` | `Components/Controllers/ClientPredictedEntity.cs:17` | One log per entity for every server state received (l.404) |
| `ClientPredictedEntity.LOG_RESIMULATION_STEPS` | `Components/Controllers/ClientPredictedEntity.cs:25` | One `LogState()` call per entity in every resimulation step (l.556). It formats eight vectors to 10 decimal places. |
| `PredictionManager.DEBUG_OWNERSHIP` | `PredictionManager.cs:18` | Ownership changes. These are rare, so this is low cost; listed for consistency. |

Several of these logs also call `PhysicsStateRecord.ToString()` (`Data/PhysicsStateRecord.cs:102`), which formats four vectors to `F10`. That costs a lot of string work and creates garbage on every call.

**Rough cost:** take 10 entities, 120 Hz simulation and 120 fps. That is about 10 × 3 × 120 = 3,600 logs/s from frames, plus about 10 × 6 × 120 = 7,200 logs/s from ticks, before counting server states or resimulations. With Unity's default "ScriptOnly" stack traces, each call costs tens of microseconds on the main thread, plus a write to `Player.log`. That adds up to a noticeable share of a frame.

`LOG_RESIMULATION_STEPS` also makes every resimulation slower. This inflates any resimulation-duration measurement, including Ursitoare's own `lastResimDuration`.

**Suggested fix:** make every logging flag default to `false`. Treat logging as opt-in diagnostics.

## 2. Logs with no flag

These run whatever the flags are set to:

| Location | Log | How often |
|---|---|---|
| `Interpolation/PosAnalyser.cs:19`, called from `MovingAverageInterpolator.ApplyState` (l.154) | `[Visuals][State][SELF_LERP]` | **Every frame, per entity** |
| `Interpolation/MovingAverageInterpolator.cs:262` (also `CustomVisualInterpolator.cs:242`) | `[DT_DBG][LATE_ADD]` | Every tick per entity whenever the time between two adds is longer than `fixedDeltaTime`. That means every tick when the frame rate is below the tick rate, and often when frame times jitter. |
| `Interpolation/MovingAverageInterpolator.cs:231` (also `CustomVisualInterpolator.cs:211`) | `[TIME_PAST_END_OF_BFR]` | Every frame while the playback position is past the end of the buffer, for example during a stall |
| `Interpolation/MovingAverageInterpolator.cs:87`, `:117` (also `CustomVisualInterpolator.cs:85`, `:97`) | `no data!`, `NOT_ENOUGH_DATA_IN_BUFFER` | Every frame while the buffer is starved |
| `Interpolation/MovingAverageInterpolator.cs:217` (also `CustomVisualInterpolator.cs:197`) | `negative interpolation target` | Whenever it happens |
| `ClientPredictionManager.cs:551` | `[SnapAllToServerAndReset]` | Every freeze or reset |
| `Components/Controllers/ClientPredictedEntity.cs:355` | `[RESIMULATION][SKIP_CHECK]` (has a `//TODO: toggle this log`) | Every check, when `TRUST_ALREADY_RESIMULATED_TICKS` is on |
| `Components/Controllers/ClientPredictedEntity.cs:105, 110`; `ServerPredictedEntity.cs:62, 67`; `PredictedEntityVisuals.cs:89` | Constructor, destructor and destroy logs | Every spawn and despawn |

`PosAnalyser` is the worst of these. It does its vector maths and a formatted log every frame for every visual, and nothing reads its result.

**Suggested fix:**
- Put each of these behind the class's `DEBUG` flag, or behind a dedicated flag such as `LOG_TIMING_WARNINGS` for `LATE_ADD` and `TIME_PAST_END_OF_BFR`.
- Only call `_posAnalyser.LogAndPrintPosRot(...)` when a flag is set, for example `if (LOG_POS)`.
- For warnings that should stay on (buffer starved, negative target), consider rate-limiting them or turning them into counters or events, the way `onPacketLoss` and `onSnapToServer` already work, so the host decides whether to log.

## 3. `TickStat` never reports a resimulation

`ClientPredictionManager.Tick()` (`ClientPredictionManager.cs:90-98`) runs the resimulation check **before** calling `base.Tick()`:

```csharp
public override void Tick()
{
    if (PREDICTION_ENABLED)
        ClientResimulationCheckPass();   // Resimulate() sets resimulatedThisTick, lastResimDuration, lastResimmedTicks
    base.Tick();
```

`PredictionManager.Tick()` (`PredictionManager.cs:167-171`) resets those fields before it fills in the `TickStat`:

```csharp
ticksSinceResim++;
resimulatedThisTick = false;
shouldResimThisTick = false;
lastResimDuration = 0;
lastResimmedTicks = 0;
```

`shouldResimThisTick` is reset the same way, so the `shouldResim` value in the `LOG_TIMING`/`DEBUG` tick log is also always 0. (`ticksSinceResim` is incremented there too, but `CanResiumlate()` still behaves sensibly. It sees `k` ticks after a resimulation at tick N+k, so don't change this when you move the reset.)

**Result:** `onTickStat` always reports `didResimulate = false`, `resimTicks = 0` and `resimDuration = 0` on the client. Anything built on it, such as the demo's HEAVY_TICK warning and the ResimGraph overlay, never sees a resimulation. The ResimGraph overlay now works around this by listening to `PredictionManager.resimulation` and the change in `totalRewindDistance`.

**Suggested fix (any one of these):**
- Move the per-tick reset out of `Tick()` into a `protected void BeginTick()` that the client calls before `ClientResimulationCheckPass()`, and that `PredictionManager.Tick()` calls only when it hasn't run yet this tick.
- Or move the reset to the end of `Tick()`, after `onTickStat.Dispatch(...)`, so values set before `base.Tick()` survive until they are reported.
- Or let `PredictionManager.Tick()` call a `protected virtual void PreTickCheck()` after the reset and before `onPreTick`, and move `ClientResimulationCheckPass()` into the client's override. Note that this changes the order relative to `onPreTick`: the check would then run after the reset but before listeners see the tick start. That probably reads more naturally anyway.

Also consider adding a unit test that checks `TickStat.didResimulate` is true on a tick where `Resimulate()` ran.

## 4. `GetMinSqrDistToAllLocalEnts` always returns 0

`ClientPredictionManager.cs:265-284`:

```csharp
float GetMinSqrDistToAllLocalEnts()
{
    ...
    foreach (var ent in localEntity)
    {
        ...
        //TODO: review
        float intermediary = (ent.gameObject.transform.position - ent.gameObject.transform.position).sqrMagnitude;
```

This subtracts the local entity's position from itself, so the result is 0 whenever there is at least one local entity. The function also never receives the follower it is supposed to measure from. As a result, in `ConfigureFollowerResimulation`:
- `RESIMULATE_PRECISE_FOLLOWERS_SQR_DISTANCE_THRESHOLD > 0` makes **every** follower use the precise checker, however far away it is.
- `RESIMULATE_FOLLOWERS_SQR_DISTANCE_THRESHOLD > 0` makes **every** follower predicted, however far away it is.

The demo sets `precise_resim_followers_distance_treshold = 3`, which therefore applies to all followers instead of only those within 3 m.

**Suggested fix:** pass in the follower and measure from it:

```csharp
float GetMinSqrDistToAllLocalEnts(ClientPredictedEntity follower)
{
    float result = float.MaxValue;
    Vector3 p = follower.gameObject.transform.position;
    foreach (var ent in localEntity)
    {
        if (!ent.gameObject) { ...; continue; }
        float d = (ent.gameObject.transform.position - p).sqrMagnitude;
        if (d < result) result = d;
    }
    return result;
}
```

`ConfigureFollowerResimulation(ent)` should then call `GetMinSqrDistToAllLocalEnts(ent)`. It currently runs for every client entity on every tick, including local ones, and could skip entities that are controlled locally.

## 5. The interpolator allocates every tick

`MovingAverageInterpolator.Add()` creates two new `PhysicsStateRecord` objects per entity per tick: `newData` (l.267) and the smoothed record from `GetNextProcessedState()` (l.346). `PhysicsStateRecord.Alloc()` just calls `Empty()` and doesn't reuse anything. Both ring buffers overwrite their oldest slot, so the evicted record could be reused instead of allocating a new one. This is minor next to the logging, but it is steady garbage at the simulation rate times the number of entities.

Related: `MovingAverageInterpolator.Reset()` clears `buffer` but not `averagedBuffer` (there's already a `//TODO`). After a reset, the visuals keep interpolating towards old smoothed states until the smoothing buffer has refilled.

## 6. The server sends a state every tick, but Mirror flushes at the network rate

`ServerPredictionManager.PostSimTick()` (l.98) calls `SendServerState()` (l.327) for every entity on every tick. That sends one message per entity, per connection, per tick. The demo's `NetworkHz` only sets Mirror's `NetworkManager.sendRate`. Mirror batches outgoing messages and flushes them in `NetworkServer.NetworkLateUpdate()` → `Broadcast()`, only once each `sendInterval` has passed.

Whenever the simulation rate is above the network rate, each flush carries more than one state per entity per client. At 120 Hz simulation and 60 Hz network, which are the code defaults in `PredictionDemoConfig`, that is **two states per entity per client**. The demo scene overrides the defaults to 60 Hz / 60 Hz, so it is not affected as configured. With two states per flush:
- The server serializes twice as many states as the network rate implies, and spends twice the bandwidth.
- The client deserializes and buffers both. The older of the two is usually superseded within the same frame.

`useServerWorldStateMessage` defaults to `false` (`PredictionManager.cs:54`), so every state is a separate message, each with its own RPC header and dispatch.

**Suggested fix:**
- Give the server a send interval in ticks (for example `sendEveryNTicks`, or derived from a configured network rate) and only call `SendServerState`/`SendWorldState` on those ticks. Clients already tolerate gaps in the server stream.
- Consider making `useServerWorldStateMessage = true` the default, so each client gets one message per send instead of one per entity.

## 7. Every received state and input allocates

The library ships no network serializers, so Mirror's Weaver generates them from the public fields. Reading a message creates a new `PhysicsStateRecord`, a new `PredictionInputRecord` for `input`, and new `float[]` and `bool[]` arrays. On the client that happens for every state, per entity, per client, per tick. The server pays the same for every input message it receives.

The generated format also sends fields the receiver doesn't need:
- `scalarFillIndex` and `binaryFillIndex` (8 bytes)
- a null marker for `componentState`
- one byte per `bool`

**Suggested fix:**
- Ship hand-written writers and readers, or document a recommended pair for each transport, that read into pooled or preallocated records.
- Pack the bools into a bitmask.
- `ClientPredictedEntity` already preallocates its local buffers. The server state buffer could copy into preallocated records the same way, instead of keeping the deserialized instance.

## 8. `TickIndexedBuffer` eviction is O(capacity)

`Utils/TickIndexedBuffer.cs`: once the buffer is full, every `Add` calls `PopOldest()` (l.24). That calls `GetNextTick()` → `FindClosestTick()` (l.180), which loops over **every key** in the dictionary to find the next start. `Remove()` of the first or last tick does the same through `PopOldest()`/`PopNewest()`. The class already has a `//TODO: implement an efficient version of this`.

Hot callers:
- Client:
  - each entity's `serverStateBuffer`, on every server state received once the buffer is full
  - each entity's `tickResimCounter`, on every resimulated tick
  - `clientTickRTTBuffer.Remove()` (`ClientPredictionManager.cs:692`) and `missedTicksBuffer.Add/Remove` (l.676, l.680), on every server state
- Server: `inputQueue.Remove()` twice per tick for every entity (`ServerPredictedEntity.cs:114` and `:286`)

**Suggested fix:** ticks are consecutive integers, so back the buffer with an array indexed by `tick % capacity` and store the tick in each slot to detect stale entries. `Add`, `Get`, `Contains` and `Remove` all become O(1), and the `Dictionary` overhead goes away.

## 9. The server samples inputs every tick and discards them

`ServerPredictedEntity.SamplePhysicsState()` (l.268) calls `SampleInput(serverStateRecord.input)` (l.280) for every entity on every tick, because `BROADCAST_INPUTS = true` (l.28). That asks every controllable component to sample input **on the server**. Immediately afterwards, `ServerPredictionManager.PostSimTick()` overwrites the field with `state.input = entity.GetLastInput();` (l.110). So:
- The sampling is wasted. In the demo, `PredictablePlayerController.SampleInput` reads the server's **keyboard** six times per entity per tick.
- After the first tick, `serverStateRecord.input` points to the previous tick's `lastAppliedInput` object, so the next tick's sampling **writes into that object**. For server-owned entities this is `serverAppliedInput`, which the next tick reuses.
- On a headless server, `Keyboard.current` is null. A component that reads input the way the demo does would throw here.

**Suggested fix:** remove the `SampleInput` call from `SamplePhysicsState` and keep `state.input = GetLastInput()` as the single source of the broadcast input. Alternatively, copy into the state's own record with `serverStateRecord.input.From(lastAppliedInput)` instead of sharing the reference.

## 10. Rigidbody state is read and written twice

- **Every tick on the client:** `RewindablePhysicsController._SimStep()` → `SampleWorldState()` (l.124) copies position, rotation, velocity and angular velocity of every tracked rigidbody into its own history. `ClientPredictedEntity.SamplePhysicsState()` (l.272) then reads the same four properties again into `localStateBuffer`. Each read is a native call into physics. The same happens in every resimulation step.
- **Every rewind:** `Rewind()` (l.101) → `ApplyWorldState()` (l.134) writes every tracked body. `ClientPredictionManager.Resimulate()` then calls `SnapToServer(startTick)` on every entity (`ClientPredictionManager.cs:452-454`, since `correctWholeWorldWhenResimulating = true`), overwriting the bodies it just restored.

**Suggested fix:** have one owner of the per-tick state history. Either the physics controller exposes its record for an entity and tick, or the entities' `localStateBuffer` becomes the history the controller restores from. When the whole world is about to be snapped to the server state, skip `ApplyWorldState` for those bodies.

## 11. The resimulation check repeats on an unchanged server state; the gap event never fires

`ClientPredictedEntity.GetPredictionDecision()` (l.317) runs every tick for every entity, from `ComputePredictionDecision()`. Its input is the newest server state, `serverStateBuffer.GetEnd()`, and nothing returns early when that state was already checked on an earlier tick. The checker therefore re-runs on the same pair of states on every tick between server updates. Each check is cheap, but it adds up across entities and ticks.

In the same method, `lastCheckedServerTickId` is assigned (l.350) **before** the gap check reads it (l.359):

```csharp
lastCheckedServerTickId = serverState.tickId;
fromTick = serverState.tickId;
...
if (serverState.tickId > lastCheckedServerTickId && (serverState.tickId - lastCheckedServerTickId) > 1)
```

The condition is always false, so `DesyncReason.GAP_IN_SERVER_STREAM` is never dispatched.

**Suggested fix:**
- Move the assignment after the gap check.
- Return `NOOP` early when `serverState.tickId == lastCheckedServerTickId` and the entity's state hasn't changed since that check. A resimulation or snap that rewrote local history would need to reset `lastCheckedServerTickId`.

## 12. Small per-frame waste in `PredictedEntityVisuals.Update`

- `serverGhost.SetActive(SHOW_DBG)` and `clientGhost.SetActive(SHOW_DBG)` (l.108, l.110) run every frame, even when nothing changed. Only call them when `SHOW_DBG` changes.
- The jump check reads `rotDiff.eulerAngles` three times (l.137 onwards). Each read converts the quaternion to Euler angles. Read it once, or compare with `Quaternion.Angle(rotBefore, rotAfter)`.

## 13. The tick RTT timestamp is taken one tick early

`ClientPredictionManager.Tick()` (l.90-114) records the send time for the RTT measurement **after** `base.Tick()`:

```csharp
base.Tick();                                  // sends the input for tick T, then tickId++ (now T+1)

if (clientTickRTTBuffer.GetCapacity() > 0)
{
    TickRttRecord tickRttRecord = new TickRttRecord();
    tickRttRecord.tickId = tickId;            // T+1
    tickRttRecord.sentTime = Time.realtimeSinceStartupAsDouble;   // end of tick T
    clientTickRTTBuffer.Add(tickId, tickRttRecord);
```

The record for tick T+1 is stamped at the end of tick T, but T+1's input is only sampled and sent during the next tick. The server echoes back the client tick of the input it applied, which arrives with the state. So `OnServerStateReceived` (l.673) measures from about one tick **before** that input was sent, and `onTickRttDuration` and `lastClientTickRTT` read about one tick too high (~17 ms at 60 Hz, ~8 ms at 120 Hz). The transport adds its own wait on top, between the send call and the actual flush to the network.

**Suggested fix:** stamp the tick in the same place its input is sent. For example, record it in `ClientPreSimTick()` just before `unreliableClientStateSender` is invoked, or before `base.Tick()` using the pre-increment `tickId`. The spectator heartbeat path needs the same change.

**What the value means:** even with this fixed, `onTickRttDuration` is not a network ping. It covers the full round trip of an input:
- the network both ways
- the server's input buffer (`ServerPredictedEntity.BUFFER_FULL_THRESHOLD`, about 4–5 ticks in the demo)
- the wait for the transport's next send

That's useful to know, but it isn't comparable with the ping-based RTT other libraries report (FishNet `TimeManager.RoundTripTime`, PurrNet `TickManager.rtt`, Mirror `NetworkTime.rtt`). Consider exposing both: a transport or ping RTT, and this "input round trip". Document which is which.

---

## Workaround applied in the demo

`PredictionDemo-Ursitoare` now turns off every logging flag that can be set from outside the package, in `NetworkPredictionManagerAdapter.ApplyConfig()`. The new `PredictionDemoConfig.library_logging` setting (default `false`) controls this. The adapter also no longer forces `PredictionManager.DEBUG = true`. The logs with no flag listed in section 2 still run until the demo picks up the fixed library.

Separately, the demo's own per-tick debug log in `PredictablePlayerController.ApplyForces` was removed. The other three demos have no equivalent log, so it was skewing the CPU comparison.
