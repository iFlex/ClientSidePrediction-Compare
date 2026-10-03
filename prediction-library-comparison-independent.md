# Client-side prediction in Unity: Ursitoare vs FishNet 4.7.3 vs PurrDiction 1.3.3

Independent review, written 2026-10-03, read from source only. Nothing was run. Ursitoare is at commit `f2168cd`. FishNet is at 4.7.3. PurrDiction is at 1.3.3, on PurrNet 1.23.0-beta.51. Mirror's own `PredictedRigidbody` is out of scope.

**Path prefixes used in citations**

- `U/` = `C:\Development\Ursitoare\Runtime\src\`
- `UT/` = `C:\Development\Ursitoare\Tests\Runtime\`
- `UD/` = `D:\gamedev\PredictionCompare\PredictionDemo-Ursitoare\Assets\Scripts\`
- `F/` = `PredictionDemo-FishNet\Assets\FishNet\Runtime\`
- `FD/` = `PredictionDemo-FishNet\Assets\Scripts\`
- `P/` = `PredictionDemo-PurrNet\Assets\PurrDiction\Runtime\`
- `PD/` = `PredictionDemo-PurrNet\Assets\Scripts\`

Ursitoare line numbers refer to the files at `f2168cd`.

**Revision 2 (2026-10-03).** Two ranks were re-scored after review:

- **Architecture** is now Ursitoare 1.5, FishNet 3, PurrDiction 1.5 (was 3 / 2 / 1). Coupling to the network layer is now scored here, not only under portability. Before, Ursitoare's static configuration counted against it while the others' coupling to their own stacks did not.
- **Developer ergonomics** is now Ursitoare 2.5, FishNet 2.5, PurrDiction 1 (was 3 / 2 / 1). One-time glue code is now counted apart from the code each prediction feature needs. Ursitoare also gets credit for using the real `Rigidbody` rather than a wrapper.

The order of the three libraries is unchanged. See sections 5.5 and 5.6.

## 1. Verdict

Overall ranking, mean rank over 20 categories with equal weights (1 = best):

| Place | Library | Mean rank (20 categories) | Mean rank (17 required categories only) |
|---|---|---|---|
| 1 | **PurrDiction 1.3.3** | **1.68** | 1.76 |
| 2 | **FishNet 4.7.3** | **2.08** | 2.06 |
| 3 | **Ursitoare f2168cd** | **2.25** | 2.18 |

The order is the same whether or not the three extra categories are counted. Those three are security, visual interpolation, and movement from several components with separate inputs.

**How sensitive this is to cheap fixes (inferred):** suppose Ursitoare's per-entity `tickResimCounter` (`ClientPredictedEntity.cs:84`, a plain `Dictionary`) became a `TickIndexedBuffer`, and its logging were removed, including the unconditional calls in `MovingAverageInterpolator.cs:87,154,231,262`. Then:

- Performance becomes Ursitoare 1, FishNet 2, PurrDiction 3.
- Memory becomes Ursitoare 2.5, FishNet 1, PurrDiction 2.5.
- The 20-category means become PurrDiction 1.70, FishNet 2.10, Ursitoare 2.20.

The order still holds. The remaining gap comes from bandwidth, resilience and correctness.

**PurrDiction**

- It is the most complete and most resilient of the three. It sends delta-compressed state against a baseline the client has acknowledged, packs input redundancy at the bit level, and adapts the client's lead from server-reported input slack. It also has predicted spawning and a hierarchy system, prediction policies per identity, soft correction, and determinism tooling: fixed point, soft float, and desync hashing.
- That comes at a cost. It rolls back and replays every identity on every server frame. It saves full state for every identity every tick, and keeps histories 10 s long. That makes it the heaviest on CPU and memory.
- It is the easiest to integrate. The demo player is 99 lines and the balls need no code at all. It ships no tests, and its coordinator is a single 3,419-line partial class.

**FishNet**

- It is the most mature and has the most consistent quality. Allocations are pooled, it has 33 profiler markers in the prediction code, dense XML documentation, input redundancy, send-on-change reconciles, clock-drift correction, and lag compensation.
- It reconciles and replays on every received state, without comparing anything. It never measures prediction error, which makes it the weakest on observability: 10 of 26 points.
- Prediction is built into FishNet's core `NetworkBehaviour` and `NetworkObject` types and its codegen. The core algorithm can't be replaced, which puts it last on architecture. One subscription leak was found (section 5.11).

**Ursitoare**

- It is the only one that reconciles conditionally. It compares states against thresholds and resimulates only when they diverge, so it has the best expected client CPU when predictions are right. It is transport-agnostic, has the most plug-in points, ships the most tests (106), and has the largest set of built-in prediction metrics (21 of 26).
- It works with the real `Rigidbody`, so existing physics code needs no wrapper. Each new predicted feature needs little code. The cost is a one-time transport adapter, because none ships with the library.
- Many of those metrics are dead or unreliable. Bandwidth is the worst of the three: full uncompressed state per entity per connection per tick, and no input redundancy. Several configuration combinations leave entities with no correction at all.
- Global static configuration, verbose logging on by default (including one log per entity per frame that can't be switched off), per-tick allocations, and an unbounded per-entity dictionary make it the least production-ready in its current state.

## 2. Ranking matrix

Ranks go from 1 (best) to 3. Ties share the average rank. Rows 18–20 are categories this review added.

| # | Category | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|---|
| 1 | Performance (expected CPU) | 1.5 | 1.5 | 3 |
| 2 | Computational complexity | 1 | 2 | 3 |
| 3 | Memory overhead | 3 | 1 | 2 |
| 4 | Bandwidth efficiency | 3 | 2 | 1 |
| 5 | Architecture | 1.5 | 3 | 1.5 |
| 6 | Developer ergonomics | 2.5 | 2.5 | 1 |
| 7 | Readability | 2.5 | 1 | 2.5 |
| 8 | Comprehensiveness | 3 | 2 | 1 |
| 9 | Extensibility | 1 | 3 | 2 |
| 10 | Resilience | 3 | 2 | 1 |
| 11 | Robustness | 3 | 2 | 1 |
| 12 | Code quality | 3 | 2 | 1 |
| 13 | Test coverage | 1 | 2.5 | 2.5 |
| 14 | Observability and debugging | 1 | 3 | 2 |
| 15 | Correctness and determinism model | 3 | 2 | 1 |
| 16 | Portability and lock-in | 1 | 2.5 | 2.5 |
| 17 | Maturity and ecosystem | 3 | 1 | 2 |
| 18 | Security against client cheating | 3 | 1.5 | 1.5 |
| 19 | Visual interpolation and smoothing | 3 | 2 | 1 |
| 20 | Multi-component movement with separate inputs | 2 | 3 | 1 |
| | **Mean rank (20)** | **2.25** | **2.08** | **1.68** |
| | Mean rank (1–17 only) | 2.18 | 2.06 | 1.76 |

## 3. Measured indicators

All values are counted from the files. "Prediction scope" means:

- **Ursitoare:** all of `Runtime/`.
- **FishNet:** `Managing/Prediction`, `Object/Prediction`, `Managing/Timing`, `NetworkBehaviour.Prediction.cs`, `NetworkObject.Prediction.cs`, and the codegen `Processing/Prediction`.
- **PurrDiction:** all of `Runtime/`. The core-only figure leaves out FixedPoint, SoftFloat, Hierarchy and similar folders.

| Indicator | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Runtime files / lines (prediction scope) | 33 / 5,688 | 23 / 7,880 (plus 8,568 lines of tick smoothing and prediction components) | 123 / 27,800 (core 65 / 16,383; FixedPoint and SoftFloat 6,078) |
| Codegen lines | 0 (no codegen) | 1,090 | 716 |
| Shipped tests | **106** test methods, 14 files, 4,272 lines | 0 | 0 (the `Test*` files are example scripts) |
| XML doc lines (`///`) | 0 | 1,529 (19% of lines) | 1,027 (3.7%); 720 in core |
| External docs | 1,493 lines of Markdown (manual, tutorial, API, self-review) | Online GitBook (link in `DOCUMENTATION.txt`) | 3 PDFs plus online docs |
| TODO / FIXME / FUDO / FODO | 98 | 0 | 1 (in vendored `libm`) |
| try/catch blocks | 12 | 0 | 30 |
| Profiler markers | 0 | 33 | 22, plus one marker per identity type and per state type |
| Pooling references | 0 | 65 (`Pool`, `Caches<`) | 178 |
| `Debug.Log` call sites | 94 | 34 (`NetworkManager.Log*`) | 63 |
| Logging on by default | `LOG_ADDED_SERVER_STATES`, `LOG_RESIMULATION_STEPS`, `DEBUG_OWNERSHIP`, `MovingAverageInterpolator.DEBUG`, `LOG_POS`, and one log per frame per entity that can't be switched off | none | none |
| Demo code, one-time glue (written once per project) | 612: 460 transport adapter + 107 generic entity wrapper + 45 config | 0 | 0 |
| Demo code, per predicted player | 139 (about half of it empty state stubs) | 179 + 49 for two data structs | 99 |
| Demo code, per free physics object (balls) | 0 (reuses the generic wrapper) | 96 (`RigidbodySync`) | 0 (only the `PredictedRigidbody` component) |
| Rigidbody access | Real `Rigidbody` | `PredictionRigidbody` wrapper, plus a manual `.Simulate()` | `PredictedRigidbody` component |
| Dead or unreachable metrics found | 13 (section 4.4) | 1 (`ReduceClientTiming`) | 0 found |
| Package version | 1.0.0, with placeholder author email and keywords | 4.7.3 | 1.3.3, on a beta PurrNet |

## 4. Prediction metrics inventory

### 4.1 Question coverage

Scoring: Yes = 2, Partial = 1, No = 0.

| Question | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| How often does the client correct, and how far back does it replay? | Yes: `totalResimulations`, `totalResimulationSteps`, `maxRewindDistance`, `totalRewindDistance`, `TickStat.resimTicks` | Partial: `OnPreReconcile(clientTick, serverTick)` against `LocalTick`. There is no counter, and it reconciles every state | Partial: `onStartingToRollback` / `onRollbackFinished` and `tickPhaseFrameAppliesTotal`. Replay depth isn't exposed |
| How long does a correction take? | Yes: `TickStat.resimDuration`, `lastResimDuration` | Partial: profiler markers only | Partial: the `RollbackToFrame` and `ReplayToLatestTick` markers, and the rollback events can be timed |
| How large was the error (position, rotation, velocity)? | Yes: the decider's `_MaxDistD`, `_MaxRotD`, `_MaxVeloD`, `_MaxAVeloD` and the running sums. They are global and misnamed (4.4) | No: nothing is ever compared | Partial: the per-identity hook `OnVerifiedStateReceived(tick, predicted, verified)` |
| Which entity caused the correction? | Partial: the decider gets the entity id and `LOG_RESIMULATIONS` logs it, but no event carries it | No | Partial: identities are named only for deterministic desyncs (`onDesyncDetected`, `onLocalDesync`) |
| Did players see a visible jump or snap? | Yes: `onLargeTransformJump` and `onLargeTransformJumpGlobal` | No: the teleport threshold exists, but there is no event | Partial: `viewBufferTrimsTotal`, `viewBufferStarvedFramesTotal` |
| What is the latency or round-trip time? | Yes: `lastClientTickRTT`, `onTickRttDuration`. It is unreliable above about 166 ms (4.4) | Yes: `RoundTripTime`, `OnRoundTripTimeUpdated` | Yes: `TickManager.rtt` in PurrNet core, and `lastInputSlackMs` |
| Are server states being lost or arriving out of order? | Partial: `onPacketLoss` (which mixes up upstream and downstream loss) and `oldServerTickCount`. The gap event never fires | No: old states are discarded silently | Partial: `framesReceivedTotal`, `fullFramesReceivedTotal`, `maxFrameApplyAgeFrames`, and reliable or full frame counts on the server |
| Is the server getting good input (late, missing, buffered, invalid)? | Yes: per entity `invalidInputs`, `ticksWithoutInput`, `lateTickCount`, `inputJumps`, `catchupTicks`, `maxClientDelay`, `totalBufferingTicks`, `totalMissingInputTicks`, plus `potentialDesync` with a reason | No: the only response is a kick for too many replicates | Partial: input margin and slack, echoed per client. Rejected input is only logged |
| Is the client's clock or lead right? | Partial: `maxServerDelay` and `GetServerDelay()` per entity. There is no lead control | Partial: `Tick` vs `LocalTick`. The adjusted delta is private | Yes: `leadJumpsTotal`, `leadPausesTotal`, `minLeadSnapsTotal`, `starvationJumpsTotal`, `currentTickPacingScale`, `smoothedInputSlackMs`, `currentSlackTargetMs` |
| How much bandwidth does prediction use? | No | Yes: `NetworkTrafficStatistics` per packet id and object, in development builds only | Yes: bits per delta section, frame byte totals, `inputBytesSentTotal`, `TickBandwidthProfiler` per type, and an editor profiler |
| Has the simulation desynced? | Yes: the threshold decision itself, plus `potentialDesync` reasons on the client and server | No | Partial: hash detection for deterministic identities only. Nothing for Unity-physics identities |
| Are there hooks for custom instrumentation? | Yes: 27 `SafeEventDispatcher`s | Yes: 8 reconcile and replay events, plus tick events | Yes: rollback and physics-pass events, desync events, virtual hooks |
| Is there a per-entity breakdown? | Yes: counters on every `ClientPredictedEntity` and `ServerPredictedEntity` | Partial: traffic per object, development builds only | Partial: bandwidth per type and reference |
| **Score (out of 26)** | **21** | **10** | **17** |

### 4.2 How reliable each library's metrics are

- **Ursitoare** has the widest coverage and the lowest reliability: 13 dead, unreachable or misleading items (4.4). Its RTT turns into "seconds since startup" once the server echo is more than 20 ticks old. Packet-loss counting can't tell lost inputs going up from lost states coming down. The error statistics are global across all entities, and the fields named `_avg*` hold sums.
- **FishNet** has few metrics, and the ones it has are correct. Traffic statistics are compiled out of server builds and release builds (`F/Managing/Statistic/NetworkTrafficStatistics.cs:1,275-283`). Reconciles dropped by the frame-rate throttle aren't counted (`F/Managing/Prediction/PredictionManager.cs:640-641`).
- **PurrDiction**: every `*Total` property and event checked was written somewhere (4.3). The counters are cumulative only, with no rate or window. `TickBandwidthProfiler` records on every write in every build, which costs a little at runtime (`P/Profiling/TickBandwidthProfiler.cs:20-38`). Of the three, its faults are the least likely to have been found (section 7).

### 4.3 Full catalogue

**Ursitoare: counters, timings and state (all public fields)**

- `PredictionManager` (`U/PredictionManager.cs:52-111`): `reportedServerTickId`, `totalResimulationsDueToAuthority`, `totalResimulationsDueToFollowers`, `totalResimulationsDueToBoth`, `totalResimulations`, `totalTickFreezes`, `totalResimulationSteps`, `totalDesyncToSnapCount`, `totalResimulationsTriggeredByLocalAuthority`, `totalResimulationsTriggeredByFollowers`, `totalResimulationsTriggeredByBoth`, `totalResimulationsSkipped`, `lastServerRecvIntervalDuration`, `lastClientTickRTT`, `lastInterTickDuration`, `lastTickDuration`, `resimSkipNotEnoughHistory`, `maxRewindDistance`, `totalRewindDistance`, `shouldResimThisTick`, `clientSendErrors`, `clientStatesReceived`, `GetAverageResimPerTick()`, `GetTotalTicks()`.
- `ClientPredictedEntity` (`U/Components/Controllers/ClientPredictedEntity.cs:90-102`): `totalTicks`, `ticksAsFollower`, `ticksAsLocalAuthority`, `resimTicks`, `resimTicksAsAuthority`, `resimTicksAsFollower`, `maxServerDelay`, `resimChecksSkippedDueToLackOfServerData`, `resimChecksSkippedDueToServerAheadOfClient`, `lastSvTickId`, `oldServerTickCount`, `countMissingServerHistory`, `GetServerDelay()`.
- `ServerPredictedEntity` (`U/Components/Controllers/ServerPredictedEntity.cs:49-58,315-316`): `invalidInputs`, `ticksWithoutInput`, `lateTickCount`, `totalSnapAheadCounter`, `inputJumps`, `catchupTicks`, `catchupBufferWipes`, `maxClientDelay`, `totalBufferingTicks`, `totalMissingInputTicks`, `clUpdateCount`, `clAddedUpdateCount`, `BufferFill()`, `BufferSize()`.
- `SimpleConfigurableResimulationDecider` (`U/Resimulation/Detection/SimpleConfigurableResimulationDecider.cs:16-25`): `_avgDistD`, `_avgRotD`, `_avgVeloD`, `_avgAVeloD`, `_checkCount`, `_MaxDistD`, `_MaxRotD`, `_MaxVeloD`, `_MaxAVeloD`.
- `PredictedEntityVisuals`: `GetInterpolationDistance()` (`:180`).
- `PredictionBudgetTracker` (`U/Stats/PredictionBudgetTracker.cs`): tick and resimulation budget statistics, a rolling window, and `onResimulationSpike`. **Nothing in the library, the demo or AntShipWars creates it.**

**Ursitoare: events**

- Manager (`U/PredictionManager.cs:282-294`): `onPreTick`, `onPreResimTick`, `onPostTick`, `onPostResimTick`, `onTickStat`, `onTickRttDuration`, `onPacketLoss`, `onServerStateSendError`, `onClientStateSendError`, `resimulation`, `resimulationStep`, `onSnapToServer`.
- Client entity (`:609-620`): `newStateReached`, `newAuthoritativeStateReached`, `preSampleState`, `onReset`, `resimulation`, `resimulationStep`, `potentialDesync` (reasons MISSING_SERVER_COMPARISON, GAP_IN_SERVER_STREAM, SERVER_AHEAD_OF_CLIENT, SNAP_TO_SERVER_NO_DATA), `inputUsed`.
- Server entity (`:455-459`): `preSampleState`, `firstTickArrived`, `potentialDesync` (NO_INPUT_FOR_SERVER_TICK, INPUT_BUFFERED, INPUT_JUMP, MULTIPLE_INPUTS_PER_FRAME, INVALID_INPUT, LATE_TICK, TICK_OVERFLOW, CATCHUP), `stateSampled`, `inputReceived`.
- Visuals: `onLargeTransformJump`, `onLargeTransformJumpGlobal` (`U/Components/Controllers/PredictedEntityVisuals.cs:197-198`).
- Per-entity hooks: the four eligibility-check `Func`s (`ClientPredictedEntity.cs:31-58`).
- Logging switches: `DEBUG`, `LOG_TIMING`, `LOG_PRE_SIM_STATE`, `LOG_RESIMULATIONS`, `LOG_ALL_CHECKS`, `LOG_VELOCITIES`, `LOG_USED_INPUTS`, `SERVER_LOG_VELOCITIES`, `LOG_INPUT_QUEUE_SIZE`, `RewindablePhysicsController.LOG_STEP`.
- Profiler markers: none.

**FishNet**

- Events on `PredictionManager` (`F/Managing/Prediction/PredictionManager.cs:168-224`): `OnPreReconcile`, `OnReconcile`, `OnPostReconcile`, `OnPrePhysicsTransformSync`, `OnPostPhysicsTransformSync`, `OnPreReplicateReplay`, `OnPostReplicateReplay`, and `OnReplicateReplay` (internal).
- State on `PredictionManager` (`:233-251`): `IsReconciling`, `ClientReplayTick`, `ServerReplayTick`, `ClientStateTick`, `ServerStateTick`.
- Per behaviour and per object: `IsBehaviourReconciling` (`F/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs:168`), `IsObjectReconciling` (`F/Object/NetworkObject/NetworkObject.Prediction.cs:78`).
- Per call: `ReplicateState` flags Ticked, Replayed and Created, with `IsFuture()` (`F/Object/Prediction/ReplicateState.cs:16-79`).
- `TimeManager` (`F/Managing/Timing/TimeManager.cs:62-123`): `OnRoundTripTimeUpdated`, `OnPreTick`, `OnTick`, `OnPrePhysicsSimulation`, `OnPostPhysicsSimulation`, `OnPostTick`, `OnUpdate`, `OnLateUpdate`, `OnFixedUpdate`, `RoundTripTime`, `HalfRoundTripTime`, `LastPacketTick`.
- `NetworkTrafficStatistics.OnNetworkTraffic`, per packet id: Replicate, Reconcile, StateUpdate, PingPong, TimingUpdate. Development builds only.
- 33 profiler markers: 12 in `PredictionManager.cs:395-406`, 7 in `NetworkObject.Prediction.cs:229-235`, 11 in `TimeManager.cs:295-305`, and 3 in other files in scope.
- Kick reason `ExploitAttempt` for too many past replicates (`NetworkBehaviour.Prediction.cs:1027-1031`).

**PurrDiction**

- Counters and values on `PredictionManager` (`P/Core/PredictionManager.cs`):
  - Lead and pacing (2253-2302): `lastInputSlackMs`, `smoothedInputSlackMs`, `currentSlackTargetMs`, `currentTickPacingScale`, `hasInputSlackFeedback`, `leadJumpsTotal`, `leadPausesTotal`, `minLeadSnapsTotal`, `starvationJumpsTotal`.
  - Input and frames sent (1323-1365): `lastMaxAckLagTicks`, `reliableFramesSentTotal`, `fullFramesSentTotal`, `suppressedTicksTotal`, `latchCyclesTotal`, `latchTicksTotal`, `maxLatchTicks`, `inputSendsTotal`, `inputBytesSentTotal`, `inputTicksSentTotal`, `deltaSection{Delete,Hierarchy,Input,State}BitsTotal`, `deltaFramesWrittenTotal`, `deltaFrameBytesTotal`, `maxDeltaFrameBytes`, `fullFrameBytesTotal`.
  - Frames received and view (2056-2057, 2492-2521): `framesReceivedTotal`, `fullFramesReceivedTotal`, `viewBufferTrimsTotal`, `viewBufferStarvedFramesTotal`, `renderPhaseFrameAppliesTotal`, `tickPhaseFrameAppliesTotal`, `maxFrameApplyAgeFrames`.
  - Other: `guaranteedInputHistorySystems` (959), `inputRedundancyTickCount` (965).
  - State flags: `isReplaying`, `isVerified`, `isVerifiedAndReplaying`, `isCatchingUpFrames`, `isVerifiedView`, `isSimulating`, `isInPhysicsPass` (1893-1943).
- Events:
  - `onBeforePhysicsPass`, `onAfterPhysicsPass` (1957, 1971), `onStartingToRollback`, `onRollbackFinished` (2193-2194), `OnInstanceAdded` (32).
  - `onDesyncDetected`, `onLocalDesync` (`P/Core/PredictionManager.Desync.cs:18,26`).
  - `TickBandwidthProfiler.onTickEnded` with per-type `wroteStates`, `readStates`, `wroteInputs`, `readInputs` (`P/Profiling/TickBandwidthProfiler.cs`).
  - `PredictionHistoryTelemetry` with save counts (`P/Core/PredictionHistoryTelemetry.cs`).
  - Physics callbacks on `PredictedRigidbody` (`P/UnityPhysics/PredictedRigidbody.cs:50-56`).
- Per-identity hook: `OnVerifiedStateReceived(tick, predicted, verified)` (`P/Core/PredictedIdentityStatefull.cs:548`).
- Profiler markers: 14 in the manager (68-81), 5 for visibility, `{Type}.Simulate` per identity type (`P/Core/PredictedIdentity.cs:20-29`), and `DeepCopy.*` per state type.
- Editor tools: an editor profiler window (`Editor/Profiler/PurrdictionProfiler.cs`).

### 4.4 Metrics that are declared but never written, and reasons that can't fire

**Ursitoare**

| Item | Evidence | Status |
|---|---|---|
| `totalDesyncToSnapCount` | `U/PredictionManager.cs:71`, the only reference | never written |
| `totalResimulationsTriggeredByLocalAuthority`, `...ByFollowers`, `...ByBoth` | `U/PredictionManager.cs:73-75`, only references | never written |
| `totalResimulationsDueToBoth` | `U/ClientPredictionManager.cs:361-371`: `totalResimulationDecisions` is only ever 0 or 1, so `> 1` can't be true | unreachable |
| `GAP_IN_SERVER_STREAM` | `ClientPredictedEntity.cs:348` sets `lastCheckedServerTickId = serverState.tickId` before `:357` compares the two | can never fire |
| `MULTIPLE_INPUTS_PER_FRAME` | `ServerPredictedEntity.cs:146-156` counts stale inputs that were skipped, not inputs that were applied (the applied path breaks at `:133` before counting) | mislabeled |
| `CATCHUP` reason | `ServerPredictedEntity.cs:219-223` fires on every server tick per entity, even when the catch-up count is 0 | noise |
| `catchupBufferWipes` | `ServerPredictedEntity.cs:55`, only reference | never written |
| `totalSnapAheadCounter` | written only in `SnapToLatest()` (`:410-414`), which nothing calls | unreachable |
| `ClientPredictedEntity.resimulation` event | declared at `:614`; the only `resimulation.Dispatch` calls are on the manager (`ClientPredictionManager.cs:444,506`) | never dispatched |
| `inputUsed` event | `ClientPredictedEntity.cs:619-620` (with a "TODO: fire this") | never dispatched |
| `inputReceived` event | dispatched only when `LOG_CLIENT_INUPTS` is set (`ServerPredictedEntity.cs:353-358`) | off by default |
| `TRACK_TIMING_STATS` | `U/PredictionManager.cs:30`, never read | dead switch |
| `PredictionBudgetTracker` | not created anywhere | unwired |
| Decider `_avg*` fields | `SimpleConfigurableResimulationDecider.cs:55-58` keep sums and never divide | misnamed |
| `resimDesyncComparator.Check(...)` | result thrown away (`ClientPredictedEntity.cs:550`) | no effect |
| RTT measurement | `ClientPredictionManager.cs:695-701`: `TickIndexedBuffer.Remove` returns `emptyValue` (sentTime 0) once the record has been evicted. The buffer holds 20 ticks (`PredictionManager.cs:35`), so at 120 Hz an echo more than about 166 ms old reports the time since startup | wrong values |
| Packet-loss count | `ClientPredictionManager.cs:673-684` counts gaps in the *echoed client tick*. Those gaps come from lost upstream inputs, not lost downstream states | misattributed |

**FishNet**

- `PredictionManager.ReduceClientTiming` (`F/Managing/Prediction/PredictionManager.cs:229`) is never read or written.
- `ClientReconcileThrottler` compares `Time.fixedUnscaledTime` (`:72`) with a value it stored from `Time.unscaledTime` (`:80`). The two time sources differ by up to one fixed step. This is minor.

**PurrDiction**

- Every counter listed in 4.3 was traced to a write site, for example `ReportViewBufferTrim` at `DeterministicIdentity.cs:383` and `PredictedIdentityStatefull.cs:586`.
- No dead reason codes were found. Coverage of PurrDiction was lower than for the other two (section 7).

## 5. Per-category analysis

### 5.0 The core loop, end to end

| Stage | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Input sampling | `PredictableControllableComponent.SampleInput` writes ordered float and bool slices (`U/Components/Controllers/AbstractPredictedEntity.cs:138-150`) | User `[Replicate]` method, data built in `OnTick` (`FD/PredictedPlayerController.cs:110-113`) | `UpdateInput` every frame and `GetFinalInput` per tick (`PD/PredictedPlayerController.cs:38-59`) |
| Sending | One unreliable message per tick per entity, with no redundancy (`U/ClientPredictionManager.cs:581-605`, `UD/NetworkPredictionManagerAdapter.cs:202-208`) | The last `RedundancyCount = StateInterpolation + 1` (3) replicates, sent only while input isn't default or the transform changes (`F/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs:565-586,878-911`) | All unacked ticks, capped at the redundancy window. Repeated inputs cost 1 bit, and resends run on a 20 ms timer (`P/Core/PredictionManager.cs:1016-1140,993-1014`) |
| Server application | `TickIndexedBuffer` per entity, startup buffering of 3, and catch-up that consumes extra inputs. A missing input re-applies the last loaded one (`U/Components/Controllers/ServerPredictedEntity.cs:93-166,206-259`) | Queue of at most 15 per object, start delay of `StateInterpolation`, default data when the queue is empty (`NetworkBehaviour.Prediction.cs:598-712,1141-1145`) | One input per tick, keyed by tick, window `[localTick, localTick+64]`. Missing inputs are extrapolated from the last one (`P/Core/PredictionManager.cs:901-915,3027-3039`, `:48`) |
| State sending | Full state of every entity to every connection every tick, stamped with that connection's last applied client tick (`U/ServerPredictionManager.cs:98-128,327-348`) | Reconcile only while changing, written into a per-connection batch, one `StateUpdate` packet per tick (`NetworkBehaviour.Prediction.cs:374-459`, `F/Managing/Prediction/PredictionManager.cs:739-808`) | A delta frame per client against the frame that client acknowledged, bit-packed, with Fast compression, and reliable when it is a full frame or over the MTU (`P/Core/PredictionManager.cs:1160-1317,1754-1853,2041-2051`) |
| Client decision to correct | Compares the latest server state with the local history at the same tick against thresholds, and takes the strongest decision across all entities (`U/ClientPredictionManager.cs:314-376`, `ClientPredictedEntity.cs:315-371`) | **Always**, for the oldest queued state, unless the frame rate is low (`F/Managing/Prediction/PredictionManager.cs:549-734`) | **Always**, for every queued server frame (`P/Core/PredictionManager.cs:2523-2624`) |
| Rollback | Restores tracked bodies from the physics controller history, then snaps entities to server state, either all of them or only those that diverged (`U/ClientPredictionManager.cs:410-462`, `U/Simulation/RewindablePhysicsController.cs:101-111`) | The user's reconcile method applies the state. Objects without data are paused, made kinematic through `RigidbodyPauser` (`F/Object/NetworkObject/NetworkObject.Prediction.cs:448-471`) | Every identity reads its state record. Soft-correction identities are frozen instead (`P/Core/PredictionManager.cs:2111-2179,2747-2763`) |
| Replay | Per tick: `PreResimulationStep` for every entity, a whole-scene `Physics.Simulate`, and an optional snap to server state again (`U/ClientPredictionManager.cs:465-503`) | `OnReplicateReplay`, then `Physics.Simulate` for the whole scene, up to `LocalTick` (`F/Managing/Prediction/PredictionManager.cs:702-721`) | `SimulateFrame` for the verified tick and then for each tick up to `localTick`, saving state again (`P/Core/PredictionManager.cs:2622-2623,2841-2952`) |
| Visual smoothing | Detached visuals, a moving average of the last 4 states, then a lerp (section 5.19) | `TransformTickSmoother` with interpolation for owner and spectators, adaptive interpolation, and a teleport threshold | A view buffer of at most 0.1 s, accumulated error correction with snap thresholds, and a soft-correction policy |

### 5.1 Performance (expected CPU)

This section is inferred from data structures and control flow.

**Ursitoare: rank 1.5**

- Replay happens only when a threshold is exceeded (`U/ClientPredictionManager.cs:380-408`). When predictions are right, the client does no replay work, which neither competitor offers.
- Every replay step is still a whole-scene `Physics.Simulate` (`U/Simulation/RewindablePhysicsController.cs:83-88`).
- As shipped it is slowed down by logging:
  - `LOG_ADDED_SERVER_STATES = true` logs every received state for every entity (`ClientPredictedEntity.cs:18,401-402`).
  - `LOG_RESIMULATION_STEPS = true` formats a 10-decimal state string per entity per replay step (`:26,554-557`).
  - `MovingAverageInterpolator.DEBUG` and `LOG_POS` default to true (`U/Interpolation/MovingAverageInterpolator.cs:16-17`).
  - `ApplyState` calls `PosAnalyser.LogAndPrintPosRot` with no condition, every frame, for every entity (`:154`).
  - The demo also turns on `PredictionManager.DEBUG` (`UD/NetworkPredictionManagerAdapter.cs:62`).
- With logging off, it should be the cheapest on the client.

**FishNet: rank 1.5**

- It reconciles and replays R ticks on *every* received state (`F/Managing/Prediction/PredictionManager.cs:549-734`), so client physics cost is about (R+1)× that of a non-predicted client.
- Below 50 FPS it throttles reconciles to about 4 per second (`:59-84`).
- The rest is lean: pooled readers and writers (`:655-660`), profiler markers, and threaded tick smoothing (`F/Generated/Component/TickSmoothing/*.Threaded.cs`).

**PurrDiction: rank 3**

- It rolls back and replays on every server frame, like FishNet. On top of that it runs `RunSaveState` for every identity at every replayed tick (`P/Core/PredictionManager.cs:2852-2867`), with deep copies (`P/Core/FULL_STATE.cs:13`).
- On the server it encodes a delta and a visibility projection per client and per identity every tick (`:1160-1317`).
- `TickBandwidthProfiler` records on every write in every build (`P/Profiling/TickBandwidthProfiler.cs:20-38`).

### 5.2 Computational complexity

Variables:

- E = predicted entities
- L = locally controlled entities
- P = bodies in the physics scene, including non-predicted ones
- S(P) = cost of one physics step
- R = replay length in ticks (about RTT plus buffering)
- C = connections
- H = history capacity per entity

| | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Client per tick, no correction | O(E·L + E·H)\* + S(P) | O(R·(E + S(P))), always | O(R·(E·s + S(P))), always (s = state copy size) |
| Client per tick, with correction | O(R·(E + S(P)) + P) | same | same, plus a gap catch-up O(G·(E + S(P))) |
| Server per tick | O(E) + S(P) + O(E·C) separate sends | O(E) + S(P) + O(E·C) buffer copies | O(E·s) + S(P) + O(E·C) delta encode and visibility |
| Lookup structures | `TickIndexedBuffer` is O(H) per eviction (`U/Utils/TickIndexedBuffer.cs:180-204`) | Replicate history O(1), O(H) brute force (`NetworkBehaviour.Prediction.cs:62-155`) | Ring buffers O(1) (`P/Core/History.cs`) |

\* `ConfigureFollowerResimulation` loops over local entities for every entity (`U/ClientPredictionManager.cs:268-310`). When an entity's server buffer is full, every new state evicts with a scan of all keys.

The expected client cost is ordered Ursitoare < FishNet < PurrDiction. Ursitoare is the only one where the R factor doesn't apply every tick.

### 5.3 Memory overhead

**FishNet: rank 1**

- Ring buffers of 60 (`F/Plugins/GameKit/Dependencies/Utilities/Types/RingBuffer.cs:155`; wired in codegen `CodeGenerating/Processing/Prediction/PredictionProcessor.cs:512`).
- Server queue of at most 15 (`F/Managing/Prediction/PredictionManager.cs:343`).
- Pooled writers, readers, `StatePacket` objects and collections (`:118-161,851`).
- The only per-tick allocation in the reconcile path is a `LocalReconcile` struct holding a pooled writer (`NetworkBehaviour.Prediction.cs:1294-1297`).

**PurrDiction: rank 2**

- Everything is bounded and pooled (`BitPackerPool`, `DisposableList`, `ListPool`), but large:
  - State history of `tickRate*10`: 1,200 entries at 120 Hz (`P/Core/PredictedIdentityStatefull.cs:147`).
  - A verified store of the same size (`P/Core/PredictionManager.cs:222`).
  - Input history of `tickRate*5` (`P/Core/PredictedIdentityWithInput.cs:48`).
  - A rigidbody verified history of `tickRate*10` (`P/UnityPhysics/PredictedRigidbody.cs:336`).

**Ursitoare: rank 3**

- Its footprint is small: buffers of `bufferSize` (50 in the demo, `UD/PredictedNetworkBehaviour.cs:16`) plus 120 per tracked body.
- But it has **unbounded growth**: `tickResimCounter` is a `Dictionary<uint,uint>` with one entry per resimulated tick, cleared only on `Reset()` (`ClientPredictedEntity.cs:84,534-535,589`).
- It **leaks on the server**: `RemovePredictedEntity` calls `SetEntityOwner(entity, invalidConnectionId)`, which returns early (`U/ServerPredictionManager.cs:150,248`). The entity stays in `_connIdToEntity`.
- It allocates every tick:
  - 2 × `PhysicsStateRecord` per entity per tick in the interpolator (`MovingAverageInterpolator.cs:267,346`).
  - A new record and arrays for every deserialized message.
  - Interpolated log strings.
- There is no pooling.

### 5.4 Bandwidth efficiency

This section is inferred from what the code writes.

**PurrDiction: rank 1**

- Deltas against the frame the client acknowledged. An unchanged identity costs one `false` bit (`P/Core/PredictedIdentityStatefull.cs:424-446`).
- Bit packing and `CompressionLevel.Fast` (`P/Core/PredictionManager.cs:2041`).
- Full frames only when the client is distressed or the gap is longer than 8 s (`:1226-1242`).
- Input repeats cost 1 bit, and the redundancy window is sized from the input margin (`:945-951,1100-1117`).

**FishNet: rank 2**

- Reconciles are sent only while the input isn't default or the transform moved, plus a redundancy tail (`NetworkBehaviour.Prediction.cs:465-492,565-576`).
- Rotation is packed (`F/Generated/Component/Prediction/RigidbodyState.cs:76-89`), and states are batched into one packet per connection per tick.
- Delta serialization exists but is disabled. It sits behind `#if DO_NOT_USE`, and `GetDeltaSerializeOption()` always returns `FullSerialize` (`:851-873`).
- Inputs carry 3 redundant copies.

**Ursitoare: rank 3**

- It sends full `PhysicsStateRecord`s every tick, as separate RPCs, to every connection (`U/ServerPredictionManager.cs:327-348`).
- Each record is a tick plus three `Vector3`s and a `Quaternion`, about 56 bytes, plus the broadcast input and component-state arrays (`U/Data/PhysicsStateRecord.cs:10-17`), with no quantization.
- The batched world-state option is still full state (`U/Data/WorldStateRecord.cs`).
- Inputs are sent once, with no redundancy.
- At 120 Hz this is roughly 90–100 B × 120 per entity per client, about 11–12 KB/s. This is an order-of-magnitude estimate.

### 5.5 Architecture

Revised in revision 2. All three are now scored on the same five criteria, and coupling to the network layer counts here as well as under portability.

| Criterion | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Independence from the network layer | **Best.** The core never touches a network type; transport is 5 delegates (`U/ClientPredictionManager.cs:35-46`, `U/ServerPredictionManager.cs:35-49`) | Worst. Prediction is built into `NetworkBehaviour` and `NetworkObject` as partial classes (1,514 + 671 lines), driven by `TimeManager` (`F/Managing/Timing/TimeManager.cs:688-779`) and codegen RPCs | Coupled. `PredictionManager : NetworkIdentity`, with `[TargetRpc]` and `[ServerRpc]` in the coordinator itself (`P/Core/PredictionManager.cs:20,2041-2051`) |
| Swappable core strategies | **Best.** Interfaces for physics, the correction decider, the interpolator and the timer | Worst. `sealed` manager (`F/Managing/Prediction/PredictionManager.cs:26`), and the reconcile-always policy is fixed | Middle. Policies per identity, but the rollback algorithm and lead controller are fixed (`:2202-2222`) |
| Size and focus of the central class | Good. Separate client and server managers, each under 750 lines | Spread across core framework types | Worst. A 3,419-line coordinator handling input upload, frame encoding, rollback, lead control and visibility, plus a 2,092-line `PredictedHierarchy.cs` |
| Configuration and state management | **Worst.** More than 40 mutable `public static` fields in 6 classes (`U/PredictionManager.cs:15-44`, `ServerPredictedEntity.cs:13-28`); `Instance` singletons overwritten by every constructor (`U/PredictionManager.cs:115`, `ClientPredictionManager.cs:42`, `ServerPredictionManager.cs:48`); values copied at construction (`ServerPredictedEntity.cs:72`, `ClientPredictionManager.cs:126-127`) | Instance-based, through `NetworkManager` | Instance-based, with locked inspector settings |
| Design of the prediction model | Basic. One timeline plus a threshold decision | Replicate/reconcile with state order modes | **Best.** Verified and speculative timelines, prediction policies, modules, baselines acknowledged by the client (`:2328-2434`) |

**Ursitoare: rank 1.5.** It has the best layering and plug-in points, and the worst configuration and state handling. Moving the statics into an instance-level settings object would fix most of that.

**PurrDiction: rank 1.5.** It has the best prediction model, but it is coupled to PurrNet and concentrated in one very large class.

**FishNet: rank 3.** On these criteria it comes last, because prediction is part of the framework's core types and can't be swapped. That says nothing about how well its prediction works.

### 5.6 Developer ergonomics

Revised in revision 2. Code is now split into one-time glue and the cost per predicted feature (see section 3), and access to the real `Rigidbody` is scored.

| | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| One-time glue | 612 lines, because no transport adapter ships with the library | 0 | 0 |
| Per player | 139 lines: 2 interfaces, 12 methods, about half of them empty state stubs (`UD/PredictablePlayerController.cs:117-138`) | 179 lines plus 49 for data structs | 99 lines |
| Per free physics object | 0 | 96 lines (`FD/RigidbodySync.cs:69-73` sends a default replicate and reconcile every tick) | 0 |
| Rigidbody access | Real `Rigidbody` (`UD/PredictablePlayerController.cs:104-110`); state is restored straight onto it (`U/Data/PhysicsStateRecord.cs:86-92`) | `PredictionRigidbody` wrapper plus a manual `.Simulate()` (`FD/PredictedPlayerController.cs:135-151`) | `PredictedRigidbody` component (`PD/PredictedPlayerController.cs:70-79`) |
| Silent misuse | Any force applied outside `ApplyForces()` (collision scripts, other MonoBehaviours) isn't re-applied during resimulation. Input slices must be read in write order (`AbstractPredictedEntity.cs:548-555`). About a dozen switch combinations turn off correction (5.15; the library's own `docs/configuration-conflicts.md` lists 13) | Using the real `Rigidbody` drops forces from rollback. `IsFuture` handling. Codegen catches signature mistakes | Using the real `Rigidbody` drops forces from rollback. One-shot inputs need `ModifyExtrapolatedInput` |
| Concepts to learn | 2 interfaces, plus static switches | Replicate and Reconcile, `ReplicateState`, `CreateReconcile`, `PredictionRigidbody`, state forwarding, state order | Identity with `INPUT` and `STATE`, `Simulate`, optional overrides |

**PurrDiction: rank 1.** It needs the least code and has the fewest concepts; spawning is one call to `hierarchy.Create` (`PD/BallSpawner.cs:134`).

**Ursitoare: rank 2.5.** It needs less code per feature than FishNet, and existing physics code works unchanged on the real `Rigidbody`. The one-time adapter and its configuration pitfalls hold it back. If the adapter already exists, as it does in the Mirror projects, it edges ahead of FishNet.

**FishNet: rank 2.5.** It has more concepts and boilerplate, a wrapper, and per-object scripts for free bodies. Codegen catches some mistakes at compile time.

### 5.7 Readability

**FishNet: rank 1.** Comments explain the reasoning behind tricky scenarios, for example `F/Managing/Prediction/PredictionManager.cs:601-629`. 19% of lines are XML doc. The `#if DO_NOT_USE` blocks are noise.

**Ursitoare: rank 2.5**

- It is the smallest codebase, and the whole loop fits in 4 files.
- But it has 98 TODO, FUDO and FODO comments, commented-out code, spelling mistakes in identifiers (`CanResiumlate`, `LOG_CLIENT_INUPTS`, `artifficialDelay`), mixed tabs and spaces (`ServerPredictedEntity.cs:88-150`), and no XML docs.
- Its external Markdown docs are good.

**PurrDiction: rank 2.5.** Names are clear, and logic is factored into small pure `internal static` helpers such as `ComputeTickPacingScale` (`:2312-2321`). The very large files and the depth of the concepts make it slow to read.

### 5.8 Comprehensiveness

**PurrDiction: rank 1**

- Predicted spawning and despawning, and hierarchy (`P/Hierarchy/`).
- Determinism tooling: fixed point, soft float, desync hash policies (`P/Core/DesyncPolicy.cs`).
- Predicted random, time and state machine.
- Prediction policies (`P/Core/PredictionPolicy.cs`) and soft correction.
- Lead and pacing control, physics events, interest management and visibility, and a profiler.

**FishNet: rank 2**

- Lag compensation (`F/Plugins/ColliderRollback/Scripts/RollbackManager.cs`) and predicted spawn (`F/Generated/Component/TakeOwnership/PredictedSpawn.cs`).
- Clock synchronization (`TimeManager.cs:1142-1228`) and tick smoothing.
- State order modes, local reconciles, and pausing rigidbodies that have no data.
- No determinism tooling.

**Ursitoare: rank 3**

- Rigidbody prediction, server input buffering and catch-up, follower prediction, and visuals.
- No predicted spawning, lag compensation, lead control or determinism tooling.

### 5.9 Extensibility

**Ursitoare: rank 1.** It is the only one whose *core algorithm* can be swapped:

- the correction decider (`SingleSnapshotInstanceResimChecker`, or the whole-history hook through `SetCustomEligibilityCheckHandler`, `ClientPredictedEntity.cs:140-162`)
- the physics backend (`PhysicsController`, 9 methods)
- the interpolator (`INTERPOLATION_PROVIDER`)
- the timer (`TIMER_PROVIDER`)
- the transport (constructor delegates)
- component state (`PredictableComponent.SampleComponentState` and `LoadComponentState`)

The catch is that these plug-ins are global statics, copied when an entity registers.

**PurrDiction: rank 2.** Behaviour is extended through generic identities and modules with many virtual hooks. The rollback algorithm and the lead constants are fixed (`const`, `:2202-2222`).

**FishNet: rank 3.** The user owns the replicate and reconcile data and logic, and gets events. The manager is `sealed` and the reconcile policy is fixed.

The detailed table of extension points is in 5.21.

### 5.10 Resilience

**PurrDiction: rank 1**

- Inputs are acknowledged, sent redundantly over a window, and resent every 20 ms (`P/Core/PredictionManager.cs:993-1014`).
- Missing inputs are extrapolated (`:48`).
- Frames are acknowledged against a baseline, recovery uses reliable full frames (`:1184-1242`), and gaps are caught up by replaying the inputs they contained (`:2117-2161`).
- The lead controller adapts to the input slack the server measures, and it has a starvation rescue (`:2339-2416,2591-2601`).

**FishNet: rank 2**

- 3 redundant inputs, a server queue with a start delay, and clock correction every second, nudging the tick delta by ±1% (`TimeManager.cs:1130-1228`).
- Local reconciles fill in missing states (`NetworkBehaviour.Prediction.cs:1266-1298,1319-1382`).
- Reconciles are throttled when the frame rate is low.
- The 60-entry history is about 0.5 s at 120 Hz. Beyond that, replays find no input and skip the logic silently (`:759-775`). This is inferred.

**Ursitoare: rank 3**

- No input redundancy. A lost input makes the server re-apply the previous input, which guarantees a misprediction.
- No lead or clock control.
- RTT and gap detection are broken (4.4).
- Freeze-and-snap when local history is exceeded (`ClientPredictedEntity.cs:365-368`). But `ClientResimulationCheckPass` returns "skip the simulation", and `Tick` ignores that return value (`U/ClientPredictionManager.cs:95`).

### 5.11 Robustness

**PurrDiction: rank 1**

- Every simulation phase is wrapped in try/catch with `LogException` (`P/Core/PredictionManager.cs:815-887`). One throwing identity skips the rest of that phase for that tick.
- It validates payload lengths when reading (`:1622-1628`). Malformed input and desync reports are swallowed without a log (`:3175-3178`, `PredictionManager.Desync.cs:84-87`).

**FishNet: rank 2**

- Interpolation is clamped and warned about in `OnValidate` (`F/Managing/Prediction/PredictionManager.cs:479-497`), and too many replicates gets the client kicked.
- There is no exception isolation; user exceptions propagate through the tick loop.
- Subscription leak: `InvokeStartCallbacks_Prediction` subscribes `TimeManager.OnUpdate` when `!asServer`, but `InvokeStopCallbacks_Prediction` returns early when `!asServer` and only unsubscribes as server (`F/Object/NetworkObject/NetworkObject.Prediction.cs:389-410`). This is a static reading. The likely effect is that client-only objects never unsubscribe.

**Ursitoare: rank 3**

- Constructors validate themselves (`INVALID_CONFIG` exceptions, `U/ClientPredictionManager.cs:49-78`). But `useServerWorldStateMessage` is a field that can only be set after construction, so the check at `U/ServerPredictionManager.cs:61-70` can't see it.
- Entity loops have no exception isolation; only sends are wrapped. Some exceptions are swallowed with `//TODO: event` (`U/ServerPredictionManager.cs:190-197`).
- Finalizers call `Debug.Log` (`ClientPredictedEntity.cs:104-107`, `ServerPredictedEntity.cs:60-63`).
- `WrapperHelpers.GetComponents(controllable, predictable)` indexes the wrong array (`U/Wrappers/WrapperHelpers.cs:47-52`).

### 5.12 Code quality

**PurrDiction: rank 1.** Consistent style, pooled resources, and testable static helpers. Downsides are the very large files and catches that swallow errors silently.

**FishNet: rank 2.** Polished, but with dead `DO_NOT_USE` paths, a dead field, the inverted unsubscribe guard, and a time-source mix-up (4.4).

**Ursitoare: rank 3.** Dead switches (`APPLY_FORCES_TO_EACH_CATCHUP_INPUT`, `BUFFER_ONCE`, `TRACK_TIMING_STATS`), a distance calculation that always returns 0 (`(ent.position - ent.position)` at `U/ClientPredictionManager.cs:280`), reference aliasing of input records (5.15), and the issues in 4.4.

### 5.13 Test coverage

**Ursitoare: rank 1.**

- 106 tests covering:
  - buffers
  - server buffering, catch-up and loss
  - client resimulation and freezing
  - followers
  - ownership interop
  - physics-controller rewind
- Some tests describe behaviour that isn't fixed yet, so they very likely fail at `f2168cd`. Two examples:
  - `GapInServerStreamIsReported` (`UT/components/ClientPredictedEntityTest.cs:414`) contradicts the code at `ClientPredictedEntity.cs:348-357`.
  - `TestIgnoredControllableFollowerIsSnappedInstead` is marked "fails today" by the library's own doc.
- This is inferred; the tests were not run.

**FishNet, PurrDiction: rank 2.5 each.** Neither ships tests. PurrDiction's `internal static` seams (`ShouldJumpForLowMargin`, `RequiresReliableRecovery`) suggest tests exist outside the package.

### 5.14 Observability and debugging

**Ursitoare: rank 1** on coverage, with 21 of 26 points. Reliability is the lowest of the three (4.2).

**PurrDiction: rank 2**, with 17 of 26. It is the best on network, lead and bandwidth telemetry, and every metric checked was live.

**FishNet: rank 3**, with 10 of 26. It has events and profiler markers but almost no counters, and its traffic statistics are for development builds only.

### 5.15 Correctness and determinism model

**PurrDiction: rank 1**

- Every frame rolls back to the server-verified timeline. Every identity is either restored, relayed as `ServerRelay`, or converged by soft correction.
- Soft correction is explicitly "convergent rather than authoritative" (`P/Core/PredictionPolicy.cs:268-278`).
- Deterministic identities get desync detection with Report, Resync or Correct (`PredictionManager.Desync.cs:91-140`). It uses a 16-bit hash, so false negatives are possible.

**FishNet: rank 2**

- Every received state is reconciled. Objects without data are paused, so no predicted object goes uncorrected.
- Reconciles are skipped silently while the frame rate is low.
- Bodies without `NetworkObject` prediction still get stepped during replays.

**Ursitoare: rank 3.** Several paths leave an entity **with no correction at all**:

- Library defaults ignore controllable followers' requests (`IGNORE_CONTROLLABLE_FOLLOWER_DECISIONS = true`, `U/PredictionManager.cs:24`), while `predictAsFollower` is true, so those followers aren't snapped either (`ClientPredictedEntity.cs:218`, `ClientPredictionManager.cs:255-266,289-310`).
- Non-controllable followers (the balls) are never snapped for spectators (`ClientPredictedEntity.cs:210-250`).
- With `DO_RESIM = false`, or a resimulation blocked by the oversimulation guard, nothing replaces it: there is no fallback snap (`U/ClientPredictionManager.cs:384-391,410-413`).

Input broadcast to followers is aliased:

- `ServerPredictionManager.cs:110` replaces `state.input` with `GetLastInput()`.
- On the next tick, `SamplePhysicsState` re-samples the *server machine's* input into that same object (`ServerPredictedEntity.cs:275-280`).
- So on ticks without fresh client input, the server's own live input, read from the host keyboard in the demo (`UD/PredictablePlayerController.cs:64-73`), is sent out as the remote player's input.

Rewind restores only tracked bodies, but replay steps the whole scene.

### 5.16 Portability and lock-in

**Ursitoare: rank 1.** It depends only on Unity physics and `sector0.safe-event-dispatcher` (`package.json`). Transport is plain delegates.

**FishNet, PurrDiction: rank 2.5 each.** Each works only inside its own networking stack. PurrDiction's fixed-point and soft-float code could be reused elsewhere.

### 5.17 Maturity and ecosystem

**FishNet: rank 1.** Version 4.x, wide adoption, documentation site and Discord.

**PurrDiction: rank 2.** Version 1.3.3, on a beta PurrNet.

**Ursitoare: rank 3.** Version 1.0.0 with placeholder package metadata, one production user (AntShipWars), and 171 commits.

### 5.18 Security against client cheating (added category)

**FishNet: rank 1.5.** Owner check, a kick when more than `RedundancyCount` replicates arrive, a queue cap with `DropExcessiveReplicates = true`, and one input consumed per tick (`NetworkBehaviour.Prediction.cs:1017-1031,673-685`).

**PurrDiction: rank 1.5.** Owner check per input entry (`P/Core/PredictionManager.cs:3162`). Inputs are keyed by tick inside a window and consumed once per tick (`:3031-3034,905-910`), which rules out speed hacks. It never kicks.

**Ursitoare: rank 3.**

- It has an owner check (`U/ServerPredictionManager.cs:314-319`) and a `ValidateInput` hook, but the demo's hook returns `true`.
- Catch-up consumes several inputs per tick when the queue grows (`ServerPredictedEntity.cs:217-231`). A client sending ahead gets extra simulation, so a bounded speed hack is possible.
- It never kicks.

### 5.19 Visual interpolation and smoothing (added at the user's request)

**PurrDiction: rank 1**

- Each identity keeps a view interpolation buffer of at most `max(3, tickRate × 0.1 s)` entries. It trims when over and holds when starved, and both cases are counted (`P/Core/PredictionManager.cs:2476-2503`, `P/Core/PredictedIdentityStatefull.cs:572-604`).
- Correction errors are *accumulated* and bled off at a rate that varies between a minimum and maximum, with separate thresholds for position and rotation snaps (`P/Transform/PredictedTransform.cs:524-620`).
- `SoftCorrection` blends errors exponentially, with a snap threshold (`P/Core/PredictionPolicy.cs:221-241`, `PredictedTransform.cs:452-472`).
- `Interpolate` can be overridden per state (`PredictedIdentityStatefull.cs:617-622`), and `UpdateViewMode` chooses Update or LateUpdate.

**FishNet: rank 2**

- `TransformTickSmoother` on a graphical child object, with optional detach.
- Owner interpolation defaults to 1 tick and spectator interpolation to 2. Adaptive interpolation scales with latency.
- Smoothed properties can be chosen per axis, there is a teleport threshold, and a threaded implementation exists (`F/Object/NetworkObject/NetworkObject.Prediction.cs:119-205,354-361`, `F/Generated/Component/TickSmoothing/`).
- It smooths only between tick results; there is no separate error-decay model.

**Ursitoare: rank 3**

- Visuals are detached (`U/Components/Controllers/PredictedEntityVisuals.cs:40-45`). A moving average over the last 4 states (`FOLLOWER_SMOOTH_WINDOW`, `MovingAverageInterpolator.cs:20,344-374`) feeds a lerp. Owner and followers use the same window (`:387-402`).
- That adds about 2–3 ticks of visual delay even for the local player. Rotations are averaged with `QuaternionAverage`.
- The code says "NOTE: this is broken, interpolation time is somehow always ahead of the god damned buffer" (`:187`).
- `Reset()` doesn't clear `averagedBuffer` (`:381-385`).
- Logs that can't be turned off fire on starvation and late adds (`:87,231,262`).
- Its strong point is jump detection, through `onLargeTransformJump`. AntShipWars swaps in its own `VisualsSnapshotInterpolator` and an EMA variant, which shows the provider interface works.

### 5.20 Movement from several components, each with its own input (added at the user's request)

**PurrDiction: rank 1, easiest**

- Each component is its own `PredictedIdentity<INPUT, STATE>` with a typed input and state.
- `RegisterInstance` gives *every* `PredictedIdentity` on a GameObject its own `PredictedComponentID(objectId, index)` (`P/Core/PredictionManager.cs:473-504`).
- On upload, each owned identity writes its own input span, and unchanged spans cost a 1-bit repeat (`:1077-1118`). On the server, inputs are routed per component id with an ownership check (`:3151-3172`).
- Components share the body through `PredictedRigidbody`.
- `PredictedModule` gives finer-grained composition (`P/Core/PredictedModule.cs`, `PredictedIdentity.Module.cs`).
- Pitfall: execution order between components follows registration order.

**Ursitoare: rank 2, native but untyped**

- It is designed for this. An entity aggregates any number of `PredictableControllableComponent`s, sums their declared float and bool counts (`AbstractPredictedEntity.cs:36-40`), and samples, validates and loads each slice in array order (`:72-96,138-150`). Components can also carry state (`PredictableComponent.SampleComponentState` and `LoadComponentState`, `:114-136`).
- But there is a single record per entity, sent whole every tick. Only floats and bools are supported. Sample and load order must match exactly ("critical!", `:548`).
- There is no versioning ("TODO: what about adding & removing components?", `:553-554`). The helper that merges component lists has an indexing bug (`U/Wrappers/WrapperHelpers.cs:47-52`).

**FishNet: rank 3, possible but manual**

- Each `NetworkBehaviour` may declare its own `[Replicate]` and `[Reconcile]`. `NetworkObject` collects every behaviour that uses prediction (`F/Object/NetworkObject/NetworkObject.Prediction.cs:257-263,599-606`) and replays them all (`:587-590`).
- But each behaviour sends its own replicate RPC with its own redundancy counter (`NetworkBehaviour.Prediction.cs:195-199,578-586`), so there is more overhead per component.
- Order between behaviours depends on the order of `TimeManager.OnTick` subscriptions.
- Behaviours that share a body must share one `PredictionRigidbody`, and exactly one of them should reconcile it. Otherwise the rigidbody is restored twice and forces applied from other behaviours are lost. This is inferred from `PredictionRigidbody.Reconcile` and the serializer, which no longer writes pending forces (`F/Object/Prediction/PredictionRigidbody.cs:105-111`).
- The usual workaround is to merge all inputs into one replicate struct on one behaviour, which defeats separate inputs.

### 5.21 Extension options in detail (added at the user's request)

| Extension point | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Transport | Any: 5 delegates (`U/ClientPredictionManager.cs:35-46`, `U/ServerPredictionManager.cs:35-49`) | FishNet transports only | PurrNet transports only |
| Physics backend | `PhysicsController` interface: Rewindable, Simple, Kinematic, Scene stub (`U/Simulation/`) | `PhysicsMode` Unity, TimeManager or Disabled, plus pre/post physics events | `PredictionPhysicsProvider` flags (3D/2D) and `onBefore/AfterPhysicsPass`. A custom provider would need a deterministic identity |
| Correction decision | Pluggable globally and per entity (5.9) | Not pluggable; always reconciles | Not pluggable. Per-identity `PredictionPolicy` chooses the mode |
| Visual interpolation | `VisualsInterpolationsProvider` (5 methods), chosen through `INTERPOLATION_PROVIDER` | Tick smoother settings. You can provide your own graphical object or turn smoothing off | Override `Interpolate`, `UpdateView`, `LateUpdateView`, `ViewStart`, `ModifyRollbackViewState`; interpolation settings assets |
| Custom state | `PredictableComponent` float and bool slices | Any serializable reconcile struct, with custom serializers | Any `IPredictedData<T>` struct, with `ReadDeltaState`/`WriteDeltaState` overrides and custom packers |
| Input shaping | `ValidateInput` per component | User code in the replicate method | `GetFinalInput`, `UpdateInput`, `ModifyExtrapolatedInput`, input sanitizing (`DeterministicIdentityWithInput.cs:253`) |
| Lifecycle hooks | 27 events (4.3) | 8 reconcile and replay events, plus tick events | Rollback, physics and desync events, plus 42 virtual or abstract identity hooks |
| Limits | Plug-ins are global statics copied at registration | Sealed manager, internal replay event | Constants for lead and pacing, rollback algorithm fixed |

## 6. Which to pick

| Use case | Pick | Why |
|---|---|---|
| A new physics-heavy multiplayer game on PurrNet, or open to switching stacks | **PurrDiction** | Best bandwidth, resilience and correctness model, and least integration code. Budget CPU for rolling back every frame |
| A team already on FishNet, or one that needs lag compensation and a mature ecosystem | **FishNet** | Stable, pooled, documented. Add your own error and correction metrics, because the library has none |
| You must stay on Mirror or a custom transport, or you need your own correction policy | **Ursitoare**, after fixes | The only transport-agnostic option with pluggable correction. Before shipping, turn off the default logs, add input redundancy, and fix the items in 4.4 and 5.15 |
| Deterministic lockstep-like logic: RTS, fighting games, fixed-point simulation | **PurrDiction** | Fixed point, soft float and desync hashing |
| Many movement components per object, each with its own input | **PurrDiction** (or Ursitoare) | Typed input per component. Ursitoare supports it natively but untyped |
| Low CPU budget clients (mobile) with mostly correct predictions | **Ursitoare** (algorithm) or **FishNet** (as shipped) | Conditional replay versus replaying every tick. Ursitoare needs its logging removed first |
| Strong anti-cheat on input timing | **FishNet** or **PurrDiction** | Tick-keyed consumption, kicks or windows |

## 7. Method and caveats

**Measured vs inferred**

- *Measured* items were counted or read directly: line and file counts, `///` counts, TODOs, catches, markers, tests, whether each metric is written, the cited code paths.
- Everything about performance, memory footprint, bandwidth, resilience and runtime effects is *inferred* from data structures and control flow. Unity was not run, and no test was executed.

**Review bias**

- I traced **Ursitoare** almost line by line. It is the smallest codebase, at about 5.7k runtime lines.
- I read the **FishNet** prediction core in full: `PredictionManager`, `NetworkBehaviour.Prediction`, `NetworkObject.Prediction` and the relevant `TimeManager` sections. I sampled the tick smoother and codegen.
- For **PurrDiction** (about 16k core lines plus 11k for determinism and hierarchy) I read the manager's tick, upload, frame and rollback, and lead paths, plus selected identity, transform and telemetry code.
- Its faults are therefore **the most likely to be under-found**, and FishNet's next. The robustness, code-quality and correctness ranks for PurrDiction carry the most uncertainty.
- Ursitoare also ships a frank self-review (`docs/configuration-conflicts.md`). I checked every item I cite from it against the code.
- One of its claims, #4, is only partly right. The manager overwrites `state.input` with the applied input (`U/ServerPredictionManager.cs:110`). The leak happens through object aliasing on ticks with no new input, as described in 5.15.

**Other caveats**

- **Scope of line counts:** FishNet's prediction lives inside a larger framework, so its counts cover the listed prediction folders only. PurrDiction's counts include its determinism libraries.
- **Configuration sensitivity:** Ursitoare's CPU rank assumes its default logging is turned off. As shipped, its client cost is dominated by `Debug.Log`.
- **Tie rule:** ties share the average rank, and the overall verdict uses an unweighted mean. With weights that favor CPU, FishNet moves up. With weights that favor network quality, PurrDiction's lead grows.

## Appendix: evidence behind each ranking

Every row cites code as `file:line`, using the prefixes defined at the top. Code was read at the stated versions.

| Category | Key evidence |
|---|---|
| Performance | Conditional resimulation: `U/ClientPredictionManager.cs:380-408`. Always reconcile: `F/Managing/Prediction/PredictionManager.cs:549-734`, `P/Core/PredictionManager.cs:2523-2624`. Default logs: `U/Components/Controllers/ClientPredictedEntity.cs:18,26,401-402,554-557`, `U/Interpolation/MovingAverageInterpolator.cs:16-17,154`, `UD/NetworkPredictionManagerAdapter.cs:62`. Save state on every replay step: `P/Core/PredictionManager.cs:2852-2867`. Throttle: `F/Managing/Prediction/PredictionManager.cs:59-84,640` |
| Complexity | Follower loop: `U/ClientPredictionManager.cs:268-310,322-376`. O(H) eviction: `U/Utils/TickIndexedBuffer.cs:24-43,180-204`. Replay loops: `U/ClientPredictionManager.cs:465-503`, `F/Managing/Prediction/PredictionManager.cs:702-721`, `P/Core/PredictionManager.cs:2833-2839`. Server encode per client: `P/Core/PredictionManager.cs:1166-1314` |
| Memory | Unbounded dictionary: `ClientPredictedEntity.cs:84,534-535`. Ownership leak: `U/ServerPredictionManager.cs:150,248`. Interpolator allocations: `MovingAverageInterpolator.cs:267,346`. FishNet capacity 60: `RingBuffer.cs:155`, `PredictionProcessor.cs:512`. PurrDiction histories: `PredictedIdentityStatefull.cs:147`, `PredictionManager.cs:222`, `PredictedIdentityWithInput.cs:48` |
| Bandwidth | Full per-entity sends: `U/ServerPredictionManager.cs:98-128,327-348`, `U/Data/PhysicsStateRecord.cs:10-17`. FishNet send-on-change: `NetworkBehaviour.Prediction.cs:465-492,565-586`. Delta disabled: `:409-413,851-873`. Rotation packing: `RigidbodyState.cs:76-89`. PurrDiction delta and bit: `PredictedIdentityStatefull.cs:424-446`. Compression: `PredictionManager.cs:2041`. Input repeat: `:1100-1117` |
| Architecture | Section 5.5 table. Transport delegates: `U/ClientPredictionManager.cs:35-46`, `U/ServerPredictionManager.cs:35-49`. Statics and singletons: `U/PredictionManager.cs:15-44,115`, `ServerPredictedEntity.cs:13-28,72`. FishNet sealed manager: `F/Managing/Prediction/PredictionManager.cs:26`; prediction partials in `F/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs`, `F/Object/NetworkObject/NetworkObject.Prediction.cs`. PurrDiction coupling and controllers: `P/Core/PredictionManager.cs:20,2041-2051,2328-2434` |
| Ergonomics | Demo sizes in section 3. `PD/PredictedPlayerController.cs`, `PD/BallSpawner.cs:134`. `FD/PredictedPlayerController.cs`, `FD/RigidbodySync.cs:69-73`, `FD/PredictedPlayerController.cs:135-151`. `UD/NetworkPredictionManagerAdapter.cs`, `UD/PredictablePlayerController.cs:104-110,117-138`, `U/Data/PhysicsStateRecord.cs:86-92`, `AbstractPredictedEntity.cs:548-555` |
| Readability | XML and TODO counts in section 3. Typos: `U/ClientPredictionManager.cs:558`, `ServerPredictedEntity.cs:21`, `PredictedEntityVisuals.cs:36`. FishNet explanatory comments: `F/Managing/Prediction/PredictionManager.cs:601-629` |
| Comprehensiveness | `P/Hierarchy/`, `P/Core/DesyncPolicy.cs`, `P/Core/PredictionPolicy.cs`, `P/FixedPoint/`, `P/SoftFloat/`. `F/Plugins/ColliderRollback/Scripts/RollbackManager.cs:23`, `F/Generated/Component/TakeOwnership/PredictedSpawn.cs` |
| Extensibility | Table 5.21. `ClientPredictedEntity.cs:140-162`, `U/Simulation/PhysicsController.cs:9-20`, `U/PredictionManager.cs:39-42`. `P/Core/PredictionManager.cs:2202-2222` |
| Resilience | Upload and resend: `P/Core/PredictionManager.cs:993-1140`. Gap catch-up: `:2117-2161`. Lead: `:2339-2416,2591-2620`. FishNet redundancy and clock: `NetworkBehaviour.Prediction.cs:878-911`, `TimeManager.cs:1130-1228`. Ignored freeze result: `U/ClientPredictionManager.cs:95,380-408` |
| Robustness | Phase catches: `P/Core/PredictionManager.cs:815-887,3175-3178`. FishNet `OnValidate` and kick: `F/Managing/Prediction/PredictionManager.cs:479-497`, `NetworkBehaviour.Prediction.cs:1027-1031`. Leak: `NetworkObject.Prediction.cs:389-410`. Ursitoare: `U/ServerPredictionManager.cs:46,61-70,190-197`, `U/Wrappers/WrapperHelpers.cs:47-52` |
| Code quality | Zero distance: `U/ClientPredictionManager.cs:280`. Dead switches: `ServerPredictedEntity.cs:14,16`, `U/PredictionManager.cs:30`. FishNet dead field: `F/Managing/Prediction/PredictionManager.cs:229` |
| Tests | 106 `[Test]` attributes in `UT/`. No NUnit references in FishNet's or PurrDiction's prediction packages |
| Observability | Section 4 |
| Correctness | `U/PredictionManager.cs:24`, `ClientPredictedEntity.cs:210-250`, `U/ClientPredictionManager.cs:255-266,289-310,384-413`. Aliasing: `U/ServerPredictionManager.cs:109-110`, `ServerPredictedEntity.cs:183,275-280`, `UD/PredictablePlayerController.cs:64-73`. FishNet pauser: `NetworkObject.Prediction.cs:462-469`. PurrDiction policies: `PredictionPolicy.cs:252-298`, `PredictionManager.Desync.cs:91-140` |
| Portability | Ursitoare `package.json` dependencies. Delegates: `U/ClientPredictionManager.cs:35-46` |
| Maturity | `package.json` versions: FishNet 4.7.3, PurrDiction 1.3.3, Ursitoare 1.0.0 with `todo@example.com`. 171 commits in Ursitoare's git log |
| Security | `NetworkBehaviour.Prediction.cs:1017-1031,673-685`. `P/Core/PredictionManager.cs:3031-3034,3162`. `U/ServerPredictionManager.cs:314-319`, `ServerPredictedEntity.cs:217-231`, `UD/PredictablePlayerController.cs:75-78` |
| Visual interpolation | Section 5.19 citations |
| Multi-component input | Section 5.20 citations |
