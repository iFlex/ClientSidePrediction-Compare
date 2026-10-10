# Client-side prediction in Unity: re-run of the library comparison (2026-10-06)

A re-run of the two reviews from 2026-10-03 (`prediction-library-comparison.md` and `prediction-library-comparison-independent.md`),
against the code as it is today, with five added categories. Read from source only: nothing was run or profiled, and no tests were executed.

| Library | Version reviewed | Since the last review |
|---|---|---|
| **Ursitoare** | `63ca5b1` (local `main`, one commit ahead of `origin/main`; the demo is pinned to `5117080`) | 3 commits: config flags removed, a new follower test file, all logging made opt-in |
| **FishNet** | 4.7.3 | unchanged |
| **PurrDiction** | 1.3.3 on PurrNet 1.23.0-beta.51 | unchanged |

Mirror's own `PredictedRigidbody` is still out of scope, as in both earlier reviews.

**Path prefixes:** `U/` = `C:\Development\Ursitoare\Runtime\src\`, `UT/` = `C:\Development\Ursitoare\Tests\Runtime\`,
`UD/` = `PredictionDemo-Ursitoare\Assets\Scripts\`, `F/` = `PredictionDemo-FishNet\Assets\FishNet\Runtime\`,
`FD/` = `PredictionDemo-FishNet\Assets\Scripts\`, `P/` = `PredictionDemo-PurrNet\Assets\PurrDiction\Runtime\`,
`PD/` = `PredictionDemo-PurrNet\Assets\Scripts\`. Line numbers are for the versions above.

## 1. What changed since 2026-10-03

### Ursitoare: three commits

| Commit | Change | Effect on the review |
|---|---|---|
| `0fcef54` Remove some confusing config flags | `IGNORE_NON_AUTH_RESIM_DECISIONS` and `IGNORE_CONTROLLABLE_FOLLOWER_DECISIONS` removed; `PREDICT_FOLLOWERS` added and folded into `ConfigureFollowerResimulation` (`U/ClientPredictionManager.cs:261-309`). `tickResimCounter` is now a bounded `TickIndexedBuffer` (`U/Components/Controllers/ClientPredictedEntity.cs:83,140-141`) | Fixes conflicts #1 and #2 from the library's own `docs/configuration-conflicts.md` (controllable followers ignored and never snapped). Fixes the unbounded per-entity dictionary |
| `5117080` New test file | `UT/PredictionManagerFollowerTest.cs`, 6 tests pinning follower correction | The file appears to have been present, uncommitted, at the last review (the count then was 106, with 100 committed), so the count is still 106 |
| `63ca5b1` Guard all logs behind config bools (not pushed) | Every `Debug.Log*` call now sits behind a switch; `LOG_EVENTS` and `LOG_ERRORS` added; every logging default is now `false`, including `LOG_ADDED_SERVER_STATES`, `LOG_RESIMULATION_STEPS`, `MovingAverageInterpolator.DEBUG/LOG_POS` and the per-frame `PosAnalyser` calls (`U/PredictionManager.cs:17-22`, `U/Interpolation/MovingAverageInterpolator.cs:16-17,109-110,157-158`) | Removes the default logging cost that held Ursitoare's CPU rank at 1.5 |

**Still open in Ursitoare** (re-verified at `63ca5b1`):

| Issue | Evidence | Status |
|---|---|---|
| Follower distance is always 0 (conflict #5) | `U/ClientPredictionManager.cs:279` subtracts the local entity's position from itself | open |
| Followers without input (balls) are never corrected when not predicted (conflict #3) | `ShouldIgnoreResimulationDecision` turns their decision into NOOP (`:261-264`); only controllable followers snap (`U/Components/Controllers/ClientPredictedEntity.cs:206-231`). With the new `PREDICT_FOLLOWERS = false` this now applies on every client, not only spectators | open, now easier to hit |
| Server input aliasing (conflict #4) | `state.input = entity.GetLastInput()` stores a reference (`U/ServerPredictionManager.cs:110`); the next `SamplePhysicsState` samples the server machine's input into that same object (`ServerPredictedEntity.cs:275-283`) | open |
| No fallback when a resimulation is skipped (conflict #9) | `Resimulate` returns on `!DO_RESIM`; a blocked resim becomes NOOP (`U/ClientPredictionManager.cs:384-390,412-413`) | open |
| `GAP_IN_SERVER_STREAM` can never fire | `lastCheckedServerTickId` is assigned at `ClientPredictedEntity.cs:352` before the comparison at `:361` | open |
| Dead metrics | `totalDesyncToSnapCount`, `totalResimulationsTriggeredBy*`, `catchupBufferWipes`, `TRACK_TIMING_STATS` have one reference each (the declaration); `totalResimulationsDueToBoth` is unreachable (`U/ClientPredictionManager.cs:355-373`) | open |
| Ownership leak on removal | `RemovePredictedEntity` calls `SetEntityOwner(entity, invalidConnectionId)` (`U/ServerPredictionManager.cs:251`), which returns at `:150` | open |
| Per-tick allocations in the interpolators | `PhysicsStateRecord.Alloc()` per added state and per averaged window (`U/Interpolation/MovingAverageInterpolator.cs:274,353`), arrays in `QuaternionAverage` (`:68`) | open |
| New: errors are silent by default | `LOG_ERRORS = false` also silences `NULL_PREDICTED_GAME_OBJECT` and `POTENTIAL_EXPLOIT_ATTEMPT` (`U/ServerPredictionManager.cs:322`) | new trade-off |

**Demo note, not scored:** the Ursitoare demo still runs `5117080`, so it still logs every received state and makes two unconditional per-frame logs per entity. Its `library_logging` switch cannot turn those per-frame calls off. Updating the package lock to `63ca5b1` (once pushed) brings the fix into the demo.

### FishNet and PurrDiction

No code changes. A sample of the 2026-10-03 citations was re-read and still matches, for example:
- FishNet: reconcile-always (`F/Managing/Prediction/PredictionManager.cs:549-734`) and the client-only `OnUpdate` subscription leak (`F/Object/NetworkObject/NetworkObject.Prediction.cs:389-410`).
- PurrDiction: rollback on every queued frame (`P/Core/PredictionManager.cs:2523-2624`).

Their findings are carried forward. Where this re-run read more of their code (for the new categories), that is cited below.

### The demos

The four demos now share a common package (movement maths, keyboard input, bots, settings panel), spawn server-driven bots, and show their effective prediction settings on screen (F4).
- **Ergonomics:** the code-size evidence was recounted (section 3).
- **New categories:** the bot and settings-panel work fed two new categories (scalability, and runtime configuration and inspectability).

### Rank changes

| Category | 2026-10-03 (U / F / P) | Now (U / F / P) | Why |
|---|---|---|---|
| Performance (expected CPU) | 1.5 / 1.5 / 3 | **1 / 2 / 3** | Ursitoare's default logging is gone; conditional resimulation now decides the rank, as the earlier sensitivity note predicted |
| Memory overhead | 3 / 1 / 2 | **2.5 / 1 / 2.5** | The unbounded dictionary is fixed; per-tick allocations and the ownership leak remain |

Every other one of the original 20 ranks is unchanged. Correctness improved for Ursitoare, but it is still the only library where an entity can be left with no correction, so it stays third.

## 2. Verdict

| Place | Library | Mean, 25 categories | Mean, original 20 | Mean, original 17 | 2026-10-03 (20) |
|---|---|---|---|---|---|
| 1 | **PurrDiction** | **1.72** | 1.73 | 1.82 | 1.70 |
| 2 | **FishNet** | **2.10** | 2.08 | 2.06 | 2.05 |
| 3 | **Ursitoare** | **2.18** | 2.20 | 2.12 | 2.25 |

The order is the same on every set of categories. The gap between FishNet and Ursitoare has closed to 0.08 over all 25, from 0.20 last time.

**PurrDiction**
- The most complete and most resilient. It has the best bandwidth, interest management and lifecycle handling, and is now also first on complex vehicles: every child piece of a prefab is its own predicted identity with typed state and its own id.
- It still rolls back and replays every identity on every server frame, and nothing can skip that, so a custom correction threshold is only possible as convergent soft correction.
- Its timing constants are private and its history length is hard-coded, which puts it last on runtime configuration.

**FishNet**
- The most mature and the most consistent code. Interest management through observers, lag compensation, good documentation.
- It always reconciles and replays the whole scene. There is no hook to skip that, so a custom threshold cannot save CPU and has to be emulated in user code.
- Most of the NetworkObject prediction settings are private.

**Ursitoare**
- Now the cheapest expected client CPU, with logging off by default. It is still the only library with a pluggable correction decision: global, per entity, or over the whole history.
- It also has the most tests, the most built-in metrics and the most open configuration.
- It sends full state for every entity to every connection with no interest management, and one entity's misprediction resimulates the whole world. That makes it the weakest at scale.
- Correctness bugs remain in the follower paths. It networks one rigidbody per entity, which limits multi-body vehicles.

## 3. Ranking matrix

1 = best. Ties share the average rank. Rows 18–20 were added in the second 2026-10-03 review; rows 21–25 are new in this re-run.

| # | Category | Ursitoare | FishNet | PurrDiction |
|---|---|:-:|:-:|:-:|
| 1 | Performance (expected CPU) | **1** | 2 | 3 |
| 2 | Computational complexity | **1** | 2 | 3 |
| 3 | Memory overhead | 2.5 | **1** | 2.5 |
| 4 | Bandwidth efficiency | 3 | 2 | **1** |
| 5 | Architecture | **1.5** | 3 | **1.5** |
| 6 | Developer ergonomics | 2.5 | 2.5 | **1** |
| 7 | Readability | 2.5 | **1** | 2.5 |
| 8 | Comprehensiveness | 3 | 2 | **1** |
| 9 | Extensibility | **1** | 3 | 2 |
| 10 | Resilience | 3 | 2 | **1** |
| 11 | Robustness | 3 | 2 | **1** |
| 12 | Code quality | 3 | **1.5** | **1.5** |
| 13 | Test coverage | **1** | 2.5 | 2.5 |
| 14 | Observability and debugging | **1** | 3 | 2 |
| 15 | Correctness and determinism model | 3 | 2 | **1** |
| 16 | Portability and lock-in | **1** | 2.5 | 2.5 |
| 17 | Maturity and ecosystem | 3 | **1** | 2 |
| 18 | Security against client cheating | 3 | **1.5** | **1.5** |
| 19 | Visual interpolation and smoothing | 3 | 2 | **1** |
| 20 | Multi-component movement with separate inputs | 2 | 3 | **1** |
| 21 | **Custom correction threshold** (new) | **1** | 3 | 2 |
| 22 | **Complex vehicles with many components** (new) | 2.5 | 2.5 | **1** |
| 23 | **Scalability with many entities** (new) | 3 | **1.5** | **1.5** |
| 24 | **Runtime configuration and inspectability** (new) | **1** | 2 | 3 |
| 25 | **Physics coverage** (new) | 3 | 2 | **1** |
| | **Mean (25)** | **2.18** | **2.10** | **1.72** |
| | Mean (1–20) | 2.20 | 2.08 | 1.73 |
| | Mean (1–17) | 2.12 | 2.06 | 1.82 |

**Overlap to keep in mind when weighting:** row 21 is a narrower view of something row 9 (extensibility) already credits, and row 22 builds on row 20. Both were requested explicitly, so they are scored, but anyone re-weighting should treat 9 and 21, and 20 and 22, as related.

## 4. Measured indicators

Recounted for this re-run with one method for all three: non-blank lines, `///` lines, keyword matches. The scopes are the same as in the second 2026-10-03 review. Counts are relative signals; they differ a little from the earlier tables where the earlier method counted differently (noted).

| Indicator | Ursitoare `63ca5b1` | FishNet 4.7.3 (prediction scope) | PurrDiction 1.3.3 |
|---|---|---|---|
| Files / lines | 33 / 5,725 | 23 / 7,880 | 123 / 27,800 (`Core` 50 / 13,205) |
| Code lines (no blanks or `//` comments) | 4,659 | 5,114 | 21,973 |
| XML doc lines (`///`) | 0 | 1,526 | 1,027 |
| TODO / FIXME / FUDO / FODO | 98 | 0 | 1 (vendored `libm`) |
| `catch` blocks | 5 | 0 | 30 |
| Profiler markers declared | 0 | 31 | 19 (plus one per identity type) |
| Pooling references | 0 | 75 | 248 |
| `Debug.Log` call sites | 94, **all behind switches that default to off** | 18 | 62 |
| Public static configuration fields | 75 | 0 | 1 |
| Shipped tests | **106** `[Test]` (+3 `[TestCase]`), 11 test files, 4,262 lines | 0 | 0 |
| Dead or unreachable metrics found | 13 (unchanged) | 1 | 0 found |
| Settings readable only through reflection | 0 (all public) | 16 | 13, plus 2 hard-coded (`LibrarySettingsAccess.md`) |

### Demo code (ergonomics evidence)

Non-blank, non-comment lines, counted the same way for every file, including `using` lines and braces. The force maths, keyboard reading and bot brain now live in the shared package, so the per-player figures below are lower than in the earlier review for all three libraries.

| | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| One-time glue | Transport adapter 282 (about 190 of it transport; the rest is config application, diagnostics and test helpers), entity wrapper 101, component-merge helper 52, physics controller 15 | `RigidbodySync` 68 for free bodies | none |
| Per player controller, total | 78 | 102 + two data structs (31 + 16) | 110 |
| Of which prediction integration | about 78, of which 20 are empty state stubs | about 52, plus the two structs | about 59, of which 14 convert to the shared input struct |
| Server-driven bot on top | 19-line subclass overriding `ReadInput` | About 14 lines: the server builds the replicate when there is no owner | About 11 lines in `GetFinalInput`: the server is the controller of unowned identities |
| Free physics objects (balls) | 0 | 0 (after `RigidbodySync`) | 0 |

Adding server-controlled entities was equally easy in all three (10–20 lines); it doesn't change a rank.

## 5. Metrics inventory

The 13-question coverage table from the 2026-10-03 review still applies: Ursitoare 21, PurrDiction 17, FishNet 10 out of 26. FishNet and PurrDiction are unchanged, and none of Ursitoare's counters or events were added or removed. Two notes on reliability:

- **Ursitoare** still has 13 dead, unreachable or misleading items (section 1 lists the re-checked ones).
  - New: with `LOG_ERRORS = false` by default, its error paths are silent unless you subscribe to the events or switch logging on. That includes `NULL_PREDICTED_GAME_OBJECT`, `POTENTIAL_EXPLOIT_ATTEMPT` and the misuse warnings.
  - It also gained a candid TODO: "fix these counters now that the client and server have been separated! they are not accurate" (`U/PredictionManager.cs:172`).
- **All three:** the new on-screen settings panel shows which configuration values each library exposes (row 24). That is about configuration, not runtime metrics, so it does not change this table.

## 6. The original 20 categories

Unchanged ranks are summarised briefly. The full evidence is in `prediction-library-comparison-independent.md`, section 5, and still applies.

1. **Performance (expected CPU): Ursitoare 1, FishNet 2, PurrDiction 3.**
   - **Changed.** Ursitoare replays only when its decider asks for it (`U/ClientPredictionManager.cs:380-408`), and since `63ca5b1` nothing logs by default.
   - FishNet replays the whole scene on every reconcile it accepts (`F/Managing/Prediction/PredictionManager.cs:549-734`).
   - PurrDiction rolls back every identity and saves state on every replayed tick (`P/Core/PredictionManager.cs:2523-2624`).
   - Caveat: Ursitoare's decision is global, so with many entities its replay rate rises (row 23).
2. **Computational complexity: 1 / 2 / 3.** Unchanged. Only Ursitoare avoids the replay-length factor R on ticks that predicted correctly.
3. **Memory overhead: Ursitoare 2.5, FishNet 1, PurrDiction 2.5.**
   - **Changed.** Ursitoare's unbounded `tickResimCounter` is gone.
   - It still allocates two records per entity per tick in the interpolator, uses no pooling, and leaks an owner map entry on removal.
   - PurrDiction is bounded and pooled but large: 10 s of full state per identity.
   - FishNet stays the leanest.
4. **Bandwidth: 3 / 2 / 1.** Unchanged. Ursitoare sends full uncompressed state per entity per connection per tick. FishNet sends on change, with full serialization. PurrDiction sends bit-packed deltas against acknowledged baselines.
5. **Architecture: 1.5 / 3 / 1.5.** Unchanged. Ursitoare's configuration surface shrank by two flags but is still 75 public statics.
6. **Developer ergonomics: 2.5 / 2.5 / 1.** Unchanged. The recount in section 4 keeps the same order per player: PurrDiction < Ursitoare < FishNet. Ursitoare still needs a one-time adapter of about 190 lines.
7. **Readability: 2.5 / 1 / 2.5.** Unchanged.
8. **Comprehensiveness: 3 / 2 / 1.** Unchanged.
9. **Extensibility: 1 / 3 / 2.** Unchanged. See row 21 for the correction-decision part in detail.
10. **Resilience: 3 / 2 / 1.** Unchanged. Ursitoare still has no input redundancy, no lead control, and gap detection that can't fire.
11. **Robustness: 3 / 2 / 1.**
    - Unchanged rank. Ursitoare still has no exception isolation in its entity loops.
    - New: its error logs are now off by default, so misconfiguration fails quietly.
    - FishNet's client-only `OnUpdate` subscription leak is still there.
12. **Code quality: 3 / 1.5 / 1.5.** Unchanged. Ursitoare: 98 TODO markers, dead switches (`APPLY_FORCES_TO_EACH_CATCHUP_INPUT`, `BUFFER_ONCE`, `TRACK_TIMING_STATS`), a null guard pasted into 9 loops, no XML docs.
13. **Test coverage: 1 / 2.5 / 2.5.**
    - Unchanged. Ursitoare has 106 tests. FishNet and PurrDiction ship none.
    - The new follower tests probably don't all pass yet (inferred, not run): for example, `TestFarFollowerIsNotPredictedWhenOutsideDistanceThreshold` can't succeed while the distance is always 0.
14. **Observability: 1 / 3 / 2.** Unchanged (section 5).
15. **Correctness and determinism model: 3 / 2 / 1.**
    - Unchanged rank, better than before. With the ignore flags removed, the library defaults no longer leave controllable followers uncorrected.
    - Three paths remain:
      - Non-controllable followers are never corrected when not predicted.
      - The server's input record is aliased.
      - Nothing falls back when a resimulation is skipped.
16. **Portability: 1 / 2.5 / 2.5.** Unchanged.
17. **Maturity: 3 / 1 / 2.** Unchanged. Ursitoare's `package.json` still has a placeholder email and keywords.
18. **Security: 3 / 1.5 / 1.5.** Unchanged. Ursitoare's exploit warning is now silent by default, but the owner check still rejects the input.
19. **Visual interpolation: 3 / 2 / 1.** Unchanged. Ursitoare's moving average adds about (window − 1) / 2 ticks of delay, also for the local player.
20. **Multi-component movement with separate inputs: 2 / 3 / 1.** Unchanged.

## 7. New categories

### 7.1 Custom correction threshold (row 21): Ursitoare 1, PurrDiction 2, FishNet 3

The question: how easily can a game decide for itself when a client's prediction is wrong enough to correct, for example a tighter threshold for the local player, a looser one for distant props, or one that compares gameplay fields as well as physics?

| | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Is there a decision step at all? | **Yes.** Every tick each entity compares local and server state and returns NOOP, SNAP, RESIMULATE or FREEZE (`U/Components/Controllers/ClientPredictedEntity.cs:319-375`) | **No.** Every reconcile that passes the tick checks is applied and replayed (`F/Managing/Prediction/PredictionManager.cs:549-734`) | **No.** Every queued server frame rolls back and replays (`P/Core/PredictionManager.cs:2539-2624`) |
| Plug-in points | 1. Global checker: `SNAPSHOT_INSTANCE_RESIM_CHECKER` / `FOLLOWER_INSTANCE_RESIM_CHECKER` implement `SingleSnapshotInstanceResimChecker.Check(entityId, tick, local, server)` (`U/PredictionManager.cs:43-44`). 2. Per entity: `SetSingleStateEligibilityCheckHandler`, `SetFollowerSingleStateEligibilityCheckHandler`. 3. Whole history: `SetCustomEligibilityCheckHandler(entityId, tick, localHistory, serverHistory)` (`ClientPredictedEntity.cs:144-166`). 4. `SimpleConfigurableResimulationDecider.Check` is `virtual`, so subclassing it is enough | None. The `[Reconcile]` method only receives the server's data; the client's stored state for that tick is used only when no server data arrived (`F/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs:1319-1366`) | `OnVerifiedStateReceived(tick, predicted, verified)` is `protected virtual` on `PredictedRigidbody`, `PredictedTransform`, `PredictedProjectile3D` and custom identities, but it runs only for identities on the SoftCorrection policy (`P/Core/PredictedIdentityStatefull.cs:489-504,548`) |
| How you'd do it | Assign a checker before entities register, or call the per-entity setter after registration | Keep your own per-tick history in the replicate method; in `[Reconcile]`, apply your own state instead of the server's when the error is small | Put the identity on `SoftCorrection` and override `OnVerifiedStateReceived` to decide how much of the error to correct; the identity is then excluded from rollback |
| Does it save CPU? | **Yes.** No replay when nothing asks for one | No. The whole-scene replay runs anyway | Partly. The soft identity skips replay, but the world still rolls back every frame |
| Is the result authoritative? | Yes: correction is a resimulation or snap to server state | Yes, if implemented carefully | No. Soft correction is "convergent rather than authoritative" (`P/Core/PredictionPolicy.cs:62`) |
| Can it see gameplay state? | Yes. Component floats and bools travel in the same record (`U/Data/PhysicsStateRecord.cs:17`), but the default decider ignores them (`SimpleConfigurableResimulationDecider.cs:48-131`) | Yes, whatever the reconcile struct carries | Yes, the typed `STATE` |
| Limits | The decisions are combined globally: one RESIMULATE resimulates the whole world from the earliest tick (`U/ClientPredictionManager.cs:313-375`). Checkers are global statics copied at registration (`:127-128`). Choosing the precise follower checker by distance is broken (distance always 0) | Per behaviour, hand-written, easy to get subtly wrong | The rollback itself can't be skipped; the lead and rollback algorithm are fixed |

**Ursitoare 1:** the only library built around a correction decision, with four levels of hooks and a real CPU saving. **PurrDiction 2:** soft correction gives a supported, per-identity place for custom tolerance, but it isn't a threshold on rollback. **FishNet 3:** it can only be emulated, at full replay cost.

### 7.2 Complex vehicles with many components (row 22): PurrDiction 1, FishNet 2.5, Ursitoare 2.5

The question: a vehicle with several rigidbodies (chassis, wheels or tracks, a trailer), joints, gameplay state (engine rpm, gear, fuel, damage), subsystems with their own input (steering, throttle, turret, weapons), and seats that change owner.

| Sub-criterion | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Several bodies, each with networked state | One `Rigidbody` per entity (`AbstractPredictedEntity`). Extra bodies can be tracked for local rewind (`PhysicsController.Track`), but their state isn't sent. To network each body, register it as its own entity; the demo adapter maps one entity to one Mirror `netId`, so that needs adapter work | Manual: the user's reconcile struct can carry several `PredictionRigidbody` fields, or each part is a nested NetworkObject. `RigidbodyPauser` collects child bodies and pauses those without data (`F/Object/NetworkObject/NetworkObject.Prediction.cs:322-336`) | **Built in.** A prefab is split into pieces; every child GameObject with a `PredictedIdentity` (for example a `PredictedRigidbody` per wheel) gets its own object id and synced state (`P/Hierarchy/PredictedHierarchy.cs:802-834`) |
| Gameplay state of the parts | Float and bool slices per component, read and written in strict order; the default decider ignores them | Any serializable struct per behaviour | Typed `STATE` per identity, delta-packed; modules for finer composition |
| Many subsystems feeding prediction | Native: any number of `PredictableControllableComponent`s aggregate into one input and state record (`U/Components/Controllers/AbstractPredictedEntity.cs:36-40,126-150`) | Each behaviour has its own replicate RPC and redundancy; behaviours sharing a body must leave exactly one reconcile to it (section 5.20 of the earlier review) | One identity per subsystem, each with typed input; inputs routed per component id |
| Per-part ownership (a gunner seat) | Per entity only | Nested NetworkObjects with their own owner | `SetOwnership(root, player, cascade: false)` (`P/Core/PredictionManager.cs:3373-3392`) on a child piece |
| Attach and detach (trailers, dropped parts) | Not supported | Reparenting NetworkObjects, not predicted | Predicted attach and detach of pieces (`PredictedHierarchy.TryRestoreAttach`, `:1063`) |
| State the physics engine can't rewind: `WheelCollider` suspension and spin, joint solver caches | No library saves it (no references to `WheelCollider`, `Joint` or `ArticulationBody` in any of the three). **Least affected:** it only rewinds when a correction is needed | Disturbed on every reconcile | Disturbed on every applied frame |

Totalled per sub-criterion, Ursitoare and FishNet come out level, with PurrDiction clearly ahead. Ursitoare's native component aggregation and its tolerance of non-rewindable state offset its one-networked-body limit, and FishNet's multi-body support is offset by its per-behaviour overhead and the shared-body pitfall.

**For a WheelCollider-heavy vehicle** (inferred): all three restore the chassis but not the wheel colliders' internal state, so expect small mispredictions after every rewind. Fewer rewinds (Ursitoare) means fewer such errors, and raycast-suspension vehicles on a plain `Rigidbody` avoid the problem in all three.

### 7.3 Scalability with many entities (row 23): FishNet 1.5, PurrDiction 1.5, Ursitoare 3

The question: what grows, and how fast, as the number of predicted entities E and connections C grows (the bots scenario in the demos)?

| | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Interest management | **None.** `SendServerState` loops over every connection (`U/ServerPredictionManager.cs:332-352`) | Observers: reconciles and forwarded inputs go to `Observers` only (`F/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs:427-452,944-946`) | Per-player visibility timeline (`P/Core/PredictionManager.cs:1244`) |
| Server send cost | E·C separate full-state messages per tick | Batched per connection, only for objects with input or movement | One delta frame per client; an unchanged identity costs one bit |
| Idle entities | Still sent every tick | Not sent after the redundancy tail | About one bit |
| Effect of one misprediction | The decision is the maximum over all entities, so one entity resimulates the whole world. With N entities each mispredicting with probability p per tick, the resim rate is 1 − (1 − p)^N (inferred) | Each reconcile replays the scene, regardless | Every frame replays all identities, regardless |
| Client per-entity work per tick | A decider check and a follower simulation | Replicate replays for forwarded objects | State save on every replayed tick |

**FishNet and PurrDiction 1.5:**
- Both have interest management.
- PurrDiction is clearly ahead on bandwidth.
- FishNet is lighter per entity on the client, with no per-identity state copies on replay.

**Ursitoare 3:**
- It sends all-to-all, full state, with no interest management.
- Its CPU advantage shrinks as entity count rises, because the correction decision is global.

### 7.4 Runtime configuration and inspectability (row 24): Ursitoare 1, FishNet 2, PurrDiction 3

The question: can a tool or test harness read, and safely set, the prediction settings in effect? Evidence comes from building the F4 settings panel (`LibrarySettingsAccess.md`) and from the runtime-config study (`RuntimeConfigFile-Plan.md`).

| | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| Read the settings in effect | **Everything public.** The interpolator settings need a throwaway instance | 16 values only via reflection: most of the NetworkObject prediction section, plus `RedundancyCount`, `DropExcessiveReplicates` and others on `PredictionManager` | 13 via reflection (lead constants, view buffer, configured policy, `_repeatInputFactor`, ...). The state and input history lengths are hard-coded and can't be read at all |
| Change at runtime | Any value can be set, but several are read only when an entity is constructed (`CATCHUP_SECTIONS`, the follower checker), and they are process-wide | Supported setters: `SetStateOrder`, `SetMaximumServerReplicates`, spectator and adaptive interpolation on `PredictionSmoother`. Everything else only before the object or network starts | Policy setters and `extrapolateInput`. Lead and pacing are `const`. Some inspector settings are locked (`PurrLock`) |
| Guard rails | None. Nothing stops a mid-session change that has no effect | `OnValidate` clamping | Locks, so misconfiguration mid-session is prevented, not just undocumented |

**Ursitoare 1** for openness. Its lack of guard rails is already scored under architecture and robustness. **FishNet 2:** everything is configurable in the inspector, and it has a few supported runtime setters. **PurrDiction 3:** its key timing behaviour can't be configured, and some of it can't even be read.

### 7.5 Physics coverage (row 25): PurrDiction 1, FishNet 2, Ursitoare 3

| | Ursitoare | FishNet | PurrDiction |
|---|---|---|---|
| 3D rigidbodies | Yes | Yes (`PredictionRigidbody`) | Yes (`PredictedRigidbody`) |
| 2D rigidbodies | No (`PhysicsStateRecord` is 3D only) | Yes: `PredictionType.Rigidbody2D`; `Physics2D.Simulate` every tick (`F/Managing/Timing/TimeManager.cs:1086-1092`) | Yes: `PredictedRigidbody2D`, a 2D provider flag |
| Non-rigidbody movement (CharacterController, kinematic) | A kinematic physics controller (`U/Simulation/SimplePhysicsControllerKinematic.cs`); no character controller support | `PredictionType.Other` for custom movement | `PredictedTransform` with a CharacterController patch |
| Several physics scenes | Default scene only (`Physics.Simulate`); would need a custom `PhysicsController` | Default scenes only, unless `PhysicsMode.Disabled` and you step scenes yourself | **Per scene:** each `PredictionManager` steps its own scene's physics scene (`P/Core/PredictionManager.cs:1988-2008`) |
| Joints, wheels, articulations | Not handled (section 7.2) | Not handled | Not handled |

Physics events and callbacks are already scored under comprehensiveness, so they aren't counted again here.

## 8. Which to pick

| Use case | Pick | Why |
|---|---|---|
| A new physics-heavy multiplayer game, open to PurrNet | **PurrDiction** | Best bandwidth, resilience, correctness model and lifecycle; least integration code. Budget CPU for rolling back every frame |
| Vehicles with several bodies, parts that detach, or seats with different owners | **PurrDiction** | Every piece is its own predicted identity with typed state, and its own owner if needed |
| Vehicles built on WheelColliders or many joints, where most ticks predict correctly | **Ursitoare** (after fixes) or a raycast-suspension design in any library | Ursitoare only rewinds on a real misprediction, so the state the engine can't rewind is disturbed less often (inferred) |
| You need your own correction policy (per-entity thresholds, gameplay-field comparisons, snap vs resim) | **Ursitoare** | The only library with a decision step and plug-in points for it |
| Many players and many AI entities in a large world | **FishNet** or **PurrDiction** | Interest management; Ursitoare sends everything to everyone and resimulates the world for one entity |
| A team already on FishNet, or one that needs lag compensation | **FishNet** | Mature, documented, observers. Add your own error metrics |
| You must stay on Mirror or a custom transport | **Ursitoare** | Transport-agnostic. Before shipping: input redundancy, the open items in section 1, and an interest-management filter in the adapter |
| Test harnesses that read or set the prediction settings | **Ursitoare** | Everything is public; FishNet and PurrDiction need reflection (`LibrarySettingsAccess.md`) |
| Deterministic, fixed-point simulation | **PurrDiction** | Fixed point, soft float, desync hashing |

## 9. Method and caveats

- **Re-verified vs carried forward:**
  - Every Ursitoare finding cited in section 1 was re-checked at `63ca5b1`.
  - FishNet and PurrDiction are byte-for-byte the versions reviewed on 2026-10-03, so their earlier findings are carried forward; a sample of their citations was re-read.
  - The new categories read further into both: FishNet's reconcile and local-reconcile paths, and PurrDiction's soft correction, hierarchy pieces, ownership and physics stepping.
- **Ursitoare version:**
  - `63ca5b1` is local and unpushed; the demos run `5117080`.
  - The scores use `63ca5b1` because it is the latest code. With `5117080`, Performance would revert to 1.5 / 1.5 / 3 and the means to U 2.20, F 2.08 (25 categories).
- **Nothing was run:**
  - Performance, memory, bandwidth, scalability and the WheelCollider remarks are inferred from code.
  - Ursitoare's tests were not executed (the project was open in the editor), so which of them pass is inferred.
  - The four demos are configured differently (for example PurrNet's demo runs a 60 Hz tick and vSync on, the others 120 Hz). That doesn't affect static scores, but any measurement should align them first; the F4 panel shows the values in effect.
- **Review depth:** as before, Ursitoare was read most closely, then FishNet, then PurrDiction. PurrDiction's faults remain the most likely to be under-found.
- **Scoring rules:** the same as section 8.1 of the second 2026-10-03 review. Ranks, not points; ties share the average; each finding scored in one category; shipped defaults count, demo-only settings don't.
  - New criteria were added only where they don't re-score an existing finding. Gameplay state is part of row 22 rather than its own row; spawning and lifecycle stay under comprehensiveness and robustness.

### Criteria for the new rubrics

| # | Rubric | What is judged | Not counted here |
|---|---|---|---|
| 21 | Custom correction threshold | Whether a correction decision exists; where user code can plug into it (global, per entity, history-wide); whether a custom decision saves CPU; whether the result stays authoritative; whether gameplay state can be compared | General extensibility (row 9) |
| 22 | Complex vehicles | Networked state for several bodies in one vehicle; per-part gameplay state; many subsystems feeding prediction; per-part ownership; attach and detach; sensitivity to state the physics engine can't rewind | Separate typed inputs per component (row 20) |
| 23 | Scalability with many entities | Interest management; how server sends grow with E and C; cost of idle entities; whether one entity's misprediction costs the whole world; per-entity client work | Per-entity encoding efficiency (row 4), per-tick big-O (row 2) |
| 24 | Runtime configuration and inspectability | Whether the settings in effect can be read without reflection; which can be changed at runtime through supported API; hard-coded values | Robustness of the configuration (row 11) |
| 25 | Physics coverage | 3D and 2D bodies, non-rigidbody movement, multiple physics scenes, joints and wheels | Physics events and callbacks (row 8) |

---
*Re-run from source analysis of the PredictionCompare repository and `C:\Development\Ursitoare`, 2026-10-06.*
