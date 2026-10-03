# Prediction library comparison

A static code analysis of the client-side prediction layers of three Unity networking stacks. All three were reviewed in the same `PredictionCompare` project, a force-driven Rigidbody player with free physics balls. Each one is ranked against the other two across 17 categories.

- **Ursitoare** `f2168cd` (Mirror adapter)
- **FishNet** 4.7.3 built-in prediction
- **PurrDiction** 1.3.3 on PurrNet 1.23.0-beta.51
- Reviewed 2026-10-03

> Markdown copy of `prediction-library-comparison.html`, with the same content.

## Verdict

There's no single winner. Each library leads in a different area:

- **PurrDiction** is the most complete and robust design.
- **FishNet** is the most mature and readable codebase.
- **Ursitoare** is the leanest and most portable. It has the cheapest expected CPU profile and is the only one with a test suite. It loses on resilience, robustness and ergonomics.

### 1st overall: PurrDiction (average rank 1.88, first in 7 of 17 categories)
- Delta-compressed, bit-packed frames with input acknowledgement
- Predicted spawning, deterministic math, desync detection
- Highest memory footprint and the most per-tick work

### 2nd overall: FishNet (average rank 2.00, first in 4 of 17 categories)
- Best inline documentation, pooled and profiled throughout
- Collider rollback (lag compensation) included
- Replays on every reconcile; prediction is fused into `NetworkBehaviour`

### 3rd overall: Ursitoare (average rank 2.12, first in 6 of 17 categories)
- Only resimulates when an entity's error exceeds a threshold; independent of any networking library
- 106 unit tests; pluggable physics, resim deciders and interpolators
- The broadest prediction metrics (22/28 coverage), though seven items never work
- No input redundancy, no compression, 13 conflicting config switches

## Ranking matrix

1 = best of the three. A tie shares the average rank. The overall order is the mean rank, with every category weighted equally. If your priorities differ, re-weight the rows that matter to you.

| Category | Ursitoare | FishNet | PurrDiction |
|---|:-:|:-:|:-:|
| Performance (expected CPU) | **1** | 2 | 3 |
| Computational complexity | **1** | 2 | 3 |
| Memory overhead | 2 | **1** | 3 |
| Bandwidth efficiency | 3 | 2 | **1** |
| Architecture | 2 | 3 | **1** |
| Developer ergonomics | 3 | 2 | **1** |
| Readability | 2 | **1** | 3 |
| Comprehensiveness | 3 | 2 | **1** |
| Extensibility | **1** | 3 | 2 |
| Resilience (bad networks) | 3 | 2 | **1** |
| Robustness (errors, misconfiguration) | 3 | 2 | **1** |
| Code quality | 3 | **1** | 2 |
| Test coverage | **1** | 2.5 | 2.5 |
| Observability & debugging | **1** | 3 | 2 |
| Correctness & determinism model | 3 | 2 | **1** |
| Portability (no lock-in) | **1** | 2.5 | 2.5 |
| Maturity & ecosystem | 3 | **1** | 2 |
| **Mean rank** | **2.12** | **2.00** | **1.88** |

## Measured indicators

Counted from source. For FishNet, "prediction core" means `NetworkBehaviour.Prediction.cs`, `NetworkObject.Prediction.cs`, `Managing/Prediction` and `Object/Prediction`; FishNet's whole runtime is about 96.6k lines. For PurrDiction, the counts cover `Core`, `Hierarchy` and `UnityPhysics`, except the first row, which covers its whole runtime.

| Indicator | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Runtime size | 5.7k lines / 33 files | ~5.0k lines prediction core (+ codegen) | 27.8k lines / 123 files (6.1k is fixed-point & soft-float math) |
| Shipped unit tests | 106 tests / 4.3k lines | 0 | 0 |
| XML doc-comment lines | 0 (but a 4.4k-line markdown manual) | 999 (~20% of the core) | 769 (~4%) |
| TODO / FIXME markers | 97 | 0 | 0 |
| Exception guards (`catch`) | 12 | 0 | 29 |
| Pooling / cache references | 0 | 37 | 219 |
| Distinct profiler markers | 0 | 12 | 17 |
| Prediction counters exposed | ~50 public fields (7 never work) | 0 | ~40 read-only properties |
| When the client corrects itself | Only when an entity's error exceeds a threshold | Every reconcile (optionally throttled by frame rate) | Every verified server frame |
| State on the wire | Full-precision record per entity, per connection, every tick, plus re-sampled input | Full serialization (delta path present but forced to `FullSerialize`) | Bit-packed delta against an acknowledged baseline; periodic full frames |
| Input loss handling | None (one unreliable send per tick) | Up to 5 past inputs resent | Acknowledged redundancy window of up to 32 ticks |
| Client clock / lead control | None; the server buffers and catches up | TimeManager timing adjustment | Target / min / max lead, slack controller, starvation jump |

## Prediction metrics inventory

Every counter, timing value, event, hook and profiler marker each library exposes for prediction, sorted by the operational question it answers. **Yes** means a built-in runtime value or event answers it directly. **Partial** means you can derive it from a hook or tick numbers, or it's only visible in the Unity Profiler. **No** means you'd have to build it yourself.

| Question | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| How often does the client correct? | **Yes**: `totalResimulations`, `TickStat.didResimulate` | **Partial**: count `OnPreReconcile` yourself (fires on every state) | **Yes**: `framesReceivedTotal`, `onStartingToRollback` |
| How far back does it replay? | **Yes**: `TickStat.resimTicks`, `maxRewindDistance`, `totalRewindDistance` | **Partial**: `LocalTick − ClientStateTick` inside `OnPreReconcile` | **Partial**: `localTick` against the confirmed tick; lead counters |
| How long does a correction take? | **Yes**: `TickStat.resimDuration`, `duration`, `lastTickDuration` | **Partial**: Profiler only (12 reconcile/replay markers) | **Partial**: Profiler only (17 markers, including a per-type `Simulate`) |
| How large was the error? | **Yes**: decider `_avg/_Max` distance, rotation, velocity and angular velocity | **No**: compare the reconcile data yourself | **Partial**: `OnVerifiedStateReceived(tick, predicted, verified)` hook |
| Which entity caused it? | **Yes**: `totalResimulationsDueToAuthority / Followers / Both` | **No** | **No** |
| Did players see a jump? | **Yes**: `onLargeTransformJump` (distance and angle of the jump) | **No** | **Partial**: `viewBufferTrimsTotal`, `viewBufferStarvedFramesTotal` |
| What is the latency? | **Yes**: per-tick input→state RTT: `lastClientTickRTT`, `onTickRttDuration` | **Yes**: `RoundTripTime`, `OnRoundTripTimeUpdated` | **Partial**: `lastMaxAckLagTicks`, `lastInputSlackMs` (ping is in PurrNet core) |
| Are server states being lost? | **Partial**: `onPacketLoss`, `countMissingServerHistory` (the gap event never fires) | **No** | **Partial**: `starvationJumpsTotal`, acknowledgement lag, view-buffer starvation |
| Is the server getting good input? | **Yes**: `ticksWithoutInput`, `lateTickCount`, `totalBufferingTicks`, `catchupTicks`, `maxClientDelay`, `invalidInputs` + 8 reason codes | **No**: excess replicates are dropped silently | **Partial**: client-side starvation and slack; no per-entity server view |
| Is the client's lead or clock right? | **No**: no clock control to report on | **No**: `_adjustedTickDelta` is private | **Yes**: `currentTickPacingScale`, `leadJumpsTotal`, `leadPausesTotal`, `minLeadSnapsTotal` |
| How much bandwidth does prediction use? | **No** | **Yes**: traffic statistics by packet type (Replicate, Reconcile, StateUpdate, TimingUpdate) and GameObject | **Yes**: `TickBandwidthProfiler` bits per type per tick, plus frame and delta-section totals |
| Has the simulation desynced? | **Partial**: `potentialDesync` reports stream anomalies, not state divergence | **No** | **Yes**: hash reports → `onDesyncDetected`, `onLocalDesync` with a policy |
| Hooks for custom instrumentation | **Yes**: pre/post tick, pre/post resim tick, resim start/step, new state reached | **Yes**: 7 public reconcile/replay/transform-sync events, `ReplicateState` flags | **Yes**: rollback start/finish, before/after physics pass, per-identity hooks |
| Per-entity breakdown | **Yes**: about 12 counters on each client entity and 11 on each server entity | **Partial**: `IsBehaviourReconciling`, traffic per GameObject | **Partial**: per-type markers and bandwidth, but no per-instance counters |
| **Coverage score (Yes = 2, Partial = 1)** | **22 / 28** | **10 / 28** | **18 / 28** |

### How reliable the metrics are

**Ursitoare**
- **Seven items never work.** `totalResimulationsTriggeredByLocalAuthority/Followers/Both` are never written; they duplicate the `DueTo*` counters, which do work. `totalDesyncToSnapCount`, `catchupBufferWipes` and the `inputUsed` event are never written either. `GAP_IN_SERVER_STREAM` can never fire (see the conflicts report).
- **Noisy reason codes.** `CATCHUP` fires every tick while catch-up is on, even when nothing is caught up. `INPUT_BUFFERED` fires on every buffering tick.
- **Error stats are global.** The decider's averages and maximums live on one shared checker instance, so they combine every entity and can't be broken down by entity.
- **No API.** The counters are writable public fields with no reset or snapshot method, and there are no profiler markers.

**FishNet**
- **Excellent timing hooks** on every reconcile phase, with matching profiler markers.
- **No counters at all** for prediction. Queue sizes, dropped replicates, reconcile throttling and clock adjustment are internal.
- **Traffic statistics** are the strongest per-packet-type bandwidth breakdown of the three.

**PurrDiction**
- **A clean API:** about 40 read-only counters, named consistently (`…Total`, `max…`, `last…`) and documented as "diagnostic only".
- **Static tools** with explicit start and stop: `PredictionHistoryTelemetry.Begin/End` snapshots and `TickBandwidthProfiler`.
- **Gaps:** no correction-size or cause metric; you have to build them from `OnVerifiedStateReceived`.

### Full catalogue per library

#### Ursitoare
- **PredictionManager (client):** `totalResimulations`, `totalResimulationSteps`, `totalResimulationsSkipped`, `totalResimulationsDueToAuthority/Followers/Both`, `totalTickFreezes`, `resimSkipNotEnoughHistory`, `maxRewindDistance`, `totalRewindDistance`, `clientStatesReceived`, `clientSendErrors`, `lastTickDuration`, `lastInterTickDuration`, `lastServerRecvIntervalDuration`, `lastClientTickRTT`, `reportedServerTickId`.
- **Events:** `onTickStat` (tick, duration, didResimulate, resimDuration, resimTicks), `onTickRttDuration`, `onPacketLoss`, `onSnapToServer`, `resimulation`, `resimulationStep`, `onPreTick`, `onPostTick`, `onPreResimTick`, `onPostResimTick`, `onServerStateSendError`, `onClientStateSendError`.
- **ClientPredictedEntity:**
  - **Counters:** `totalTicks`, `ticksAsFollower`, `ticksAsLocalAuthority`, `resimTicks`, `resimTicksAsAuthority`, `resimTicksAsFollower`, `maxServerDelay`, `resimChecksSkippedDueToLackOfServerData`, `resimChecksSkippedDueToServerAheadOfClient`, `oldServerTickCount`, `countMissingServerHistory`, `lastSvTickId`.
  - **`potentialDesync` reasons:** MISSING_SERVER_COMPARISON, GAP_IN_SERVER_STREAM, SERVER_AHEAD_OF_CLIENT, SNAP_TO_SERVER_NO_DATA.
  - **Events:** `newStateReached`, `newAuthoritativeStateReached`.
- **ServerPredictedEntity:**
  - **Counters:** `ticksWithoutInput`, `totalMissingInputTicks`, `totalBufferingTicks`, `lateTickCount`, `inputJumps`, `invalidInputs`, `catchupTicks`, `maxClientDelay`, `totalSnapAheadCounter`, `clUpdateCount`, `clAddedUpdateCount`.
  - **`potentialDesync` reasons:** NO_INPUT_FOR_SERVER_TICK, INPUT_BUFFERED, INPUT_JUMP, MULTIPLE_INPUTS_PER_FRAME, INVALID_INPUT, LATE_TICK, TICK_OVERFLOW, CATCHUP.
  - **Events:** `inputReceived`, `firstTickArrived`, `stateSampled`.
- **Resim decider:** `_checkCount`, `_avgDistD/_avgRotD/_avgVeloD/_avgAVeloD`, `_MaxDistD/_MaxRotD/_MaxVeloD/_MaxAVeloD`.
- **Visuals:** `onLargeTransformJump`, `onLargeTransformJumpGlobal` (thresholds 0.35 m and 2.5°).

#### FishNet
- **PredictionManager:**
  - **Events:** `OnPreReconcile`, `OnReconcile`, `OnPostReconcile`, `OnPrePhysicsTransformSync`, `OnPostPhysicsTransformSync`, `OnPreReplicateReplay`, `OnPostReplicateReplay`.
  - **State:** `IsReconciling`, `ClientStateTick`, `ServerStateTick`, `ClientReplayTick`, `ServerReplayTick`, `GetReconcileStateTick()`.
- **Objects:** `NetworkObject.IsObjectReconciling`, `NetworkBehaviour.IsBehaviourReconciling`; `ReplicateState` flags on every replicate call (Ticked / Replayed / Created, `IsFuture()`).
- **TimeManager:** `RoundTripTime`, `HalfRoundTripTime`, `OnRoundTripTimeUpdated`, `LastPacketTick`, `Tick`, `LocalTick`, `TickDelta`, `OnPrePhysicsSimulation`, `OnPostPhysicsSimulation`.
- **Statistics:** `NetworkTrafficStatistics.OnNetworkTraffic` with inbound and outbound bytes per `PacketId` (Replicate, Reconcile, TimingUpdate, StateUpdate…) and per GameObject.
- **Profiler markers:** `PredictionManager.OnPreReconcile`, `OnReconcile`, `Physics.SyncTransforms`, `Physics2D.SyncTransforms`, `OnPre/PostPhysicsTransformSync`, `OnPostReconcileSyncTransforms`, `OnPre/Post/ReplicateReplay`, `OnPostReconcile`, `TimeManager_OnLateUpdate`.

#### PurrDiction
- **Input upload:** `inputSendsTotal`, `inputBytesSentTotal`, `inputTicksSentTotal`, `lastMaxAckLagTicks`, `lastInputSlackMs`.
- **Frames and bandwidth:** `framesReceivedTotal`, `fullFramesReceivedTotal`, `reliableFramesSentTotal`, `fullFramesSentTotal`, `fullFrameBytesTotal`, `deltaFramesWrittenTotal`, `deltaFrameBytesTotal`, `maxDeltaFrameBytes`, `deltaSectionDelete/Hierarchy/Input/StateBitsTotal`, `suppressedTicksTotal`, `latchCyclesTotal`, `latchTicksTotal`, `maxLatchTicks`.
- **Timing and lead:** `currentTickPacingScale`, `leadJumpsTotal`, `leadPausesTotal`, `minLeadSnapsTotal`, `starvationJumpsTotal`, `localTick`, `localTickInContext`, `tickRate`, `tickDelta`.
- **Apply and view:** `renderPhaseFrameAppliesTotal`, `tickPhaseFrameAppliesTotal`, `maxFrameApplyAgeFrames`, `viewBufferTrimsTotal`, `viewBufferStarvedFramesTotal`; flags `isReplaying`, `isVerified`, `isCatchingUpFrames`.
- **Events and hooks:** `onStartingToRollback`, `onRollbackFinished`, `onBeforePhysicsPass`, `onAfterPhysicsPass`, `onDesyncDetected`, `onLocalDesync`, `OnVerifiedStateReceived(tick, predicted, verified)`.
- **Tools:** `TickBandwidthProfiler`, `PredictionHistoryTelemetry`.
- **Profiler markers:** 17, including `RollbackToFrame`, `ReplayToLatestTick`, `WriteStateDeltas` and a per-type `{type}.Simulate`.

## Category analysis

### Performance (expected CPU): Ursitoare 1 · FishNet 2 · PurrDiction 3
The dominant client cost in all three is re-running physics after a correction. How often that happens decides the ranking.
- **Ursitoare:** it only rewinds when a decider reports an error above the threshold (`ComputePredictionDecision`), so on a good link most ticks cost one simulation step. When it does resimulate, it re-runs the whole scene with `Physics.Simulate` once per rewound tick. Two things count against it: there's no pooling, and logging is gated by static flags but common.
- **FishNet:** every received state replays from the server tick up to the local tick, calling `SimulatePhysics` for the whole scene each replay tick (`PredictionManager.cs:697-721`). The surrounding code is well optimized (pooled readers, profiler markers), and `_reduceReconcilesWithFramerate` can drop reconciles on slow clients.
- **PurrDiction:** every verified frame rolls back *every* predicted system (hierarchy, players, time, random, physics), decodes a delta, simulates the verified tick and replays to the head (`ProcessQueuedFrames`). It's the most work per frame, partly offset by heavy pooling.

### Computational complexity: Ursitoare 1 · FishNet 2 · PurrDiction 3
E = predicted entities, W = bodies in the physics scene, R = replay length in ticks (about round-trip time ÷ tick length), S = registered prediction systems, C = connections.
- **Ursitoare:** O(E) checks per tick, and O(R·W) only on ticks that resimulate. The server does O(E·C) sends per tick.
- **FishNet:** O(R·W) for **every** state received, plus per-behaviour replicate history work.
- **PurrDiction:** O(R·(W + S)) for every verified frame, plus delta decoding that's linear in the changed fields.

### Memory overhead: Ursitoare 2 · FishNet 1 · PurrDiction 3
- **Ursitoare:** small, preallocated ring buffers for each entity, and a 120-tick history of tracked bodies. But every incoming state record is a fresh allocation, per-entity `tickResimCounter` is a `Dictionary` that's never trimmed (it grows for the whole session), and both entity classes have finalizers that log.
- **FishNet:** replicate and reconcile history in pooled `RingBuffer`s of structs, sized by `_maximumServerReplicates` (default 15) and the reconcile window. The leanest of the three.
- **PurrDiction:** input history of `tickRate × 5` per identity, plus full state history for every system and buffered delta frames. The largest footprint by design, mitigated by `DisposableList` and packer pooling.

### Bandwidth efficiency: Ursitoare 3 · FishNet 2 · PurrDiction 1
- **Ursitoare:** every tick (`ServerPredictionManager.PostSimTick`) it sends each connection full-float position, rotation, velocity, angular velocity and component state for every entity, plus an input record. At 120 Hz that's E·C records × 120/s with no compression. There's an optional world-state packet that batches them, but it isn't delta-encoded either.
- **FishNet:** a compact writer, but the delta path is disabled (`GetDeltaSerializeOption` returns `FullSerialize`). Up to 5 redundant past inputs travel upstream.
- **PurrDiction:** a `BitPacker` delta against the last acknowledged baseline, with full frames only when needed. Input redundancy is sized to measured loss bursts.

### Architecture: Ursitoare 2 · FishNet 3 · PurrDiction 1
- **Ursitoare:** a clean client/server split (`ClientPredictionManager` / `ServerPredictionManager`), with transport hidden behind callbacks and a pluggable `PhysicsController`. Weakened by global static configuration and singletons, and by entity behaviour spread across several static flags.
- **FishNet:** prediction is woven into `NetworkBehaviour` and `NetworkObject` through partial classes and codegen-generated replicate and reconcile methods. Powerful, but tightly coupled and spread across the framework.
- **PurrDiction:** a coherent "simulated world": every piece of state is a `PredictedIdentity` with typed input and state, and built-in systems (hierarchy, players, time, random) use the same rollback path. Optional modules and a deterministic layer sit cleanly on top.

### Developer ergonomics: Ursitoare 3 · FishNet 2 · PurrDiction 1
Evidence from building the same demo in each.
- **Ursitoare:** you implement a 12-method interface. Input and state are read and written by position, so field order has to match exactly. You write your own network adapter (about 400 lines in the demo) and configure through static fields. In the demo, the library's default settings produced the follower-drift bug.
- **FishNet:** attributes and codegen keep the code short, but you must remember to call `CreateReconcile()` yourself, handle `ReplicateState` subtleties and manage the object caches. The demo shipped without reconciliation because one override was missing, and nothing warned about it.
- **PurrDiction:** subclass `PredictedIdentity<Input, State>`, override `GetFinalInput` and `Simulate`, and add `PredictedRigidbody`. Reconciliation, spawning and player handling are automatic. The fewest concepts to get right.

### Readability: Ursitoare 2 · FishNet 1 · PurrDiction 3
- **Ursitoare:** the smallest codebase and easy to trace end to end. It has no XML docs, though, along with 97 TODOs, commented-out code, mixed indentation and typos in API names (`CanResiumlate`).
- **FishNet:** about 20% of the prediction core is XML documentation, and the long comments explain the timing reasoning. Consistent style. The big partial classes are the main cost.
- **PurrDiction:** clean code, but very large files (`PredictionManager` partials total about 5.7k lines) and sparse inline docs (around 4%). It takes a while to understand.

### Comprehensiveness: Ursitoare 3 · FishNet 2 · PurrDiction 1
- **Ursitoare:** rigidbody prediction, selective resimulation, several visual interpolators and desync events. No predicted spawning, lag compensation or clock control; the host game has to build those, as AntShipWars does with `LagCompensationStateHistory`.
- **FishNet:** replicate and reconcile, `PredictionRigidbody`, state forwarding, `PredictedSpawn`, tick smoothing, time sync and collider rollback.
- **PurrDiction:** all of the above except collider rollback, plus predicted hierarchy and parenting, deterministic fixed-point and soft-float math, desync policies with hash reports, soft correction, predicted random, physics events, a state machine and prebuilt controllers.

### Extensibility: Ursitoare 1 · FishNet 3 · PurrDiction 2
- **Ursitoare:** nearly every policy is a pluggable object: the physics controller, the resim deciders (separate for owned entities and followers), the interpolation provider and the transport callbacks. The easiest to bend to unusual games.
- **FishNet:** extended through events (`OnPreReconcile`, `OnReplicateReplay`…) and your own replicate code. The core flow is fixed by codegen.
- **PurrDiction:** custom systems and modules, a physics provider and per-identity overrides (`ModifyRollbackViewState`, `ModifyExtrapolatedInput`). Rich, but inside its world model.

### Resilience (bad networks): Ursitoare 3 · FishNet 2 · PurrDiction 1
- **Ursitoare:** each input is sent once over an unreliable channel and never resent; late inputs are dropped (`IGNORE_OLD_INPUT`). Jitter is absorbed only by the server buffer and catch-up, which skip forces for the inputs they fold together. The client clock is never corrected.
- **FishNet:** past-input redundancy, state interpolation and dropping of excessive replicates, plus TimeManager timing adjustment.
- **PurrDiction:** an acknowledged input window, lead control with min and max limits, recovery from input starvation, and full-frame fallback when delta baselines are lost.

### Robustness (errors, misconfiguration): Ursitoare 3 · FishNet 2 · PurrDiction 1
- **Ursitoare:** an exception in one entity aborts the whole tick. There are 13 documented combinations of config switches that silently disable each other (see `Ursitoare/docs/configuration-conflicts.md`). The ownership leak on entity removal is known.
- **FishNet:** validates and clamps its settings (for example `_stateInterpolation` against `_maximumServerReplicates`). User replicate code isn't guarded, and a forgotten reconcile fails silently.
- **PurrDiction:** user callbacks are wrapped in `try/catch` and fall back to default input. Settings are locked once running (`PurrLock`), and desync detection is built in.

### Code quality: Ursitoare 3 · FishNet 1 · PurrDiction 2
- **Ursitoare:** sound core ideas and a test suite, but uneven hygiene: dead flags, finalizers that log, uncapped dictionaries, a gap check that can never fire, and inconsistent naming.
- **FishNet:** mature and consistent, with pooling and profiler markers throughout, and obsolete APIs marked with removal targets.
- **PurrDiction:** modern and careful (pooling, telemetry, guarded callbacks). Very large classes and a beta host framework hold it back slightly.

### Test coverage: Ursitoare 1 · FishNet 2.5 · PurrDiction 2.5
Ursitoare ships 106 NUnit tests covering the managers, entities, buffers, interpolation and physics rewind. Neither FishNet nor PurrDiction ships tests in its package; both rely on demos and their own internal processes.

### Observability & debugging: Ursitoare 1 · FishNet 3 · PurrDiction 2
Revised after the metrics inventory, from coverage 22 / 10 / 18 out of 28. An earlier draft ranked PurrDiction first from a quick read.
- **Ursitoare:** directly answers the most prediction-specific questions: correction rate, replay length and cost, error size, cause, visible jumps, per-tick RTT and server input health, all per entity. Held back by seven items that never work, noisy reason codes, error stats combined across all entities, and no profiler markers.
- **FishNet:** excellent reconcile and replay hooks with matching profiler markers, and the best traffic breakdown by packet type. But it has no prediction counters at all, so you instrument everything yourself.
- **PurrDiction:** the cleanest metrics API (about 40 read-only totals) and the only library that reports clock lead and desyncs, with a bandwidth profiler per type. It doesn't measure error size or the cause of a correction.

### Correctness & determinism model: Ursitoare 3 · FishNet 2 · PurrDiction 1
- **Ursitoare:** relies on PhysX determinism and a threshold check per entity. In some configurations followers are left with no way to be corrected (drift, then periodic jumps).
- **FishNet:** always reconciles to server state and replays, so it's simple and consistent, but bounded by PhysX determinism.
- **PurrDiction:** full-world rollback to a verified frame, plus optional fixed-point and soft-float math for cross-platform determinism, and server-side desync policies.

### Portability (no lock-in): Ursitoare 1 · FishNet 2.5 · PurrDiction 2.5
Ursitoare doesn't depend on any networking library; the demo runs it on Mirror, and any transport that can deliver its handful of message types (input, state, heartbeat, ownership) will do. FishNet prediction only works inside FishNet, and PurrDiction only inside PurrNet.

### Maturity & ecosystem: Ursitoare 3 · FishNet 1 · PurrDiction 2
FishNet is a long-established framework on its fourth major version, with a large user base and many deprecation cycles behind it. PurrDiction is actively developed on a host framework that's still in beta (`1.23.0-beta.51`). Ursitoare is an in-house library with one production consumer (AntShipWars) and has no external user base.

## Which to pick

- **PurrDiction:** for a new project that wants the most complete prediction model: predicted spawning, low bandwidth, determinism tooling. Accept the beta host framework and the higher per-frame cost.
- **FishNet:** when you want a proven framework, good documentation and built-in lag compensation, and can live with replaying on every reconcile and with the codegen conventions.
- **Ursitoare:** when you need to stay on Mirror or another transport, control the prediction policy yourself, or keep CPU low with selective resimulation. To close the gap, prioritise input redundancy, delta state, the configuration conflicts, and isolating each entity's exceptions.

## Method & caveats

- **No benchmarks.** Performance, complexity, memory and bandwidth rankings are **expected** behaviour, worked out from data structures and control flow. Nothing was measured in Unity. Profiling all three on the same scene and tick rate (the demos now share 120 Hz configs) would confirm or overturn those four rows.
- **Code versions:** FishNet and PurrDiction are the copies imported into `PredictionDemo-FishNet` and `PredictionDemo-PurrNet`. Ursitoare is `C:\Development\Ursitoare` at `f2168cd`.
- **Counting:** indicator counts are text matches (for example `catch`, `///`, pooling identifiers) and are meant as relative signals, not exact metrics.
- **Possible bias:** Ursitoare was reviewed in more depth than the other two (a full review, a configuration-conflict audit and new tests), so more of its faults are known. The other two may have issues this review didn't find.
- **Mirror excluded:** Mirror's own `PredictedRigidbody` was left out, as requested.

---
*Generated from source analysis of the PredictionCompare repository, 2026-10-03.*
