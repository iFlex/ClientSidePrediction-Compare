# Correctness harness: implementation plan

How to measure and compare **how correct** each library's client-side prediction is. ResimGraph already shows how hard each library works (resims, costs, bandwidth). This harness measures how closely the result matches the server:

1. **Path fidelity:** how closely what a client simulates and shows follows the server's authoritative path, for the local player, remote players and balls.
2. **Visual overlap:** how often and how deeply rendered objects overlap each other or the level when the server's objects didn't (and the opposite: visible gaps when the server's objects touched).
3. **Collision agreement:** whether clients show the collisions the server had, and only those.

The plan covers the four demos (Ursitoare, Mirror, FishNet, PurrNet), using the same pattern as ResimGraph: a shared core that is byte-identical in every project, plus a small adapter per library.

---

## 1. Principles

- **The server is the ground truth.** Every metric compares what a client simulated or rendered with what the server simulated.
- **Record during the run, analyse after it.** Recording is cheap and identical for every library. All comparison logic runs offline, so the same analyser and the same maths score all four. Live overlays come later, if ever.
- **One clock for every process.** Server and clients run on one Windows machine, and every sample is stamped with `Stopwatch.GetTimestamp()`. On Windows that reads QueryPerformanceCounter, which is shared by every process on the machine, so no clock sync is needed. Running across machines is out of scope.
- **Public API only**, as in ResimGraph. Where a library hides something, the adapter derives it, and the docs say so.
- **Don't penalise deliberate design choices.** Libraries deliberately show remote objects slightly in the past (interpolation) or slightly ahead (extrapolation). The analysis keeps **delay** (when) separate from **error** (where), and reports both.
- **Comparable runs.** Same scripted inputs, same network conditions from one OS-level tool, same physics config and tick rates, a dedicated server (not a host), and several runs per case.

---

## 2. Architecture

```
 ┌──────────── per demo project ────────────┐
 │  Gameplay scene                          │
 │   ├─ CorrectnessRecorder (adapter)       │   CSV + meta.json
 │   │    └─ CorrectnessRecorderCore ───────┼──► runs/<runId>/<library>/<role>-<pid>/
 │   ├─ CollisionProbe (added at spawn)     │
 │   ├─ VisualProxies (client only)         │
 │   └─ DemoInput  (keyboard | bot)         │
 └──────────────────────────────────────────┘
                                                        │
 Run-Correctness.ps1  ── launches server + N clients,   │
                          applies clumsy profile,       │
                          collects the run folder       │
                                                        ▼
 CorrectnessAnalyzer (Unity Editor window, shared code) ──► report.md, metrics.csv, plots (optional HTML)
```

### Planned file layout

| Path (in every project unless noted) | What it is |
|---|---|
| `Assets/Scripts/Correctness/CorrectnessRecorderCore.cs` | Shared: sampling loop, buffered writers, file format, clock, metadata, proxies. Byte-identical in all four. |
| `Assets/Scripts/Correctness/CorrectnessRecorder.cs` | Per-library adapter: entity discovery, ids, sim/rendered transforms, server tick hook, replay flag. |
| `Assets/Scripts/Correctness/CollisionProbe.cs` | Shared: reports `OnCollisionEnter/Exit` to the recorder. |
| `Assets/Scripts/Correctness/DemoInput.cs` | Shared: one input source for the player controllers, either keyboard or a scenario timeline. |
| `Assets/Scripts/Correctness/ScenarioRunner.cs` | Shared core plus a small per-library spawn hook: runs a scenario (bot input on clients, ball spawns on the server). |
| `Assets/Scripts/Correctness/Editor/CorrectnessAnalyzer*.cs` | Shared: the analysis and report window. |
| `Correctness/Scenarios/*.json` (repo root) | Scenario definitions, shared by all four. |
| `Correctness/Run-Correctness.ps1` (repo root) | Launcher. |
| `Correctness/NetworkProfiles/*.json` (repo root) | clumsy presets: latency, jitter, loss. |
| `Correctness.md` (repo root) | User-facing docs, in the same style as `ResimGraph.md`. |

---

## 3. Data format

One folder per process: `runs/<runId>/<library>/<role>-<pid>/`. All files are UTF-8 CSV with a header row. Times are integer Stopwatch ticks; `meta.json` holds `Stopwatch.Frequency`. Positions are in metres, rotations are quaternions (x, y, z, w), and velocities are in metres per second.

### meta.json
`runId`, `library`, `role` (server/client), `pid`, `machine`, `stopwatchFrequency`, `startTimestamp`, `unityVersion`, `gitCommit`, `scenario`, `networkProfile`, `simulationHz`, `networkHz`, `renderHz`, the physics config values (copied from the `PhysicsBodyConfig` assets), the library's prediction settings (resim thresholds, interpolation settings), and `recorderOverheadMs` (p50/p95, measured by the recorder itself).

### server_ticks.csv
One row per entity per server tick, sampled after the tick's physics step:
`t, serverTick, entityId, kind, ownerId, px, py, pz, rx, ry, rz, rw, vx, vy, vz, avx, avy, avz`

### client_frames.csv
One row per entity per rendered frame, sampled as the frame is rendered (see §4.2):
`t, frame, localTick, entityId, kind, isLocal, simPx, simPy, simPz, simRx, simRy, simRz, simRw, renPx, renPy, renPz, renRx, renRy, renRz, renRw`

### client_ticks.csv
One row per entity per **forward** client tick (replays excluded), sampled after the tick's physics step. Used for "simulated state vs server state for the same tick":
`t, localTick, entityId, kind, isLocal, px, py, pz, rx, ry, rz, rw, vx, vy, vz`

### contacts.csv
Collisions, on both server and clients:
`t, tick, side, phase (enter/exit), entityA, entityB (or level:<name>), isReplay, pointX, pointY, pointZ, normalX, normalY, normalZ, impulse`

### visual_overlap.csv
Client only. One row per overlapping pair per frame (see §6):
`t, frame, a, b, depth, dirX, dirY, dirZ, kindA, kindB`

### inputs.csv
Client only. One row per sampled input per tick, used to verify the bot drove every demo identically:
`t, localTick, throttle, steer, boost, strafeLeft, strafeRight, spin`

### Notes
- **`entityId` must mean the same entity in every process of a run.** Ursitoare and Mirror use `netId`, FishNet uses `NetworkObject.ObjectId`, and PurrNet's id is to be verified in phase 0 (see §5).
- **`kind`** is `player` or `ball`, read from the prefab (`PhysicsBodyConfigApplier.config` name, or the controller component).
- **Volume:** roughly 12 entities × 120 fps ≈ 1,400 rows/s per client, about 0.3 MB/s, or about 20 MB per 60 s run. That's fine as CSV, so a binary format is optional.

---

## 4. Recorder (runtime)

### 4.1 Core responsibilities
- **Turning it on:** off unless enabled with command-line `-correctness-record <dir>` or the component's inspector toggle. It needs no scene changes beyond adding the component, which can auto-spawn the same way ResimGraph does.
- **No per-sample allocations:** samples go into preallocated struct arrays. A background thread formats and writes them about once per second. File I/O never runs on the main thread except on shutdown.
- **Overhead:** the recorder measures its own main-thread time per frame and writes it to `meta.json`. Phase 6 checks that it doesn't distort results (see §10).
- **Clean shutdown:** flush on `OnApplicationQuit` and on scenario end, and write `meta.json` last as a "run complete" marker. The analyser ignores folders without it.
- **Teleports and respawns:** a `respawn` row in `contacts.csv` (phase `respawn`) when an entity is spawned or teleported, so the analyser can break the path there.

### 4.2 When to sample
| Sample | When | Why |
|---|---|---|
| Server state | After each server tick's physics step (library hook, §5) | The authoritative state for that tick |
| Client simulated state | After each forward client tick's physics step, skipping replays | Compared with the server for the same tick |
| Client rendered state | `RenderPipelineManager.beginContextRendering` (the projects use URP; confirm in phase 0) | Exactly what is drawn, after every library's smoothing in `Update`/`LateUpdate`, whatever their execution order |

### 4.3 Replays and collisions
`OnCollisionEnter` also fires during client replays (resimulation, reconcile, rollback). Each adapter exposes `IsReplaying` (§5), so `CollisionProbe` can tag its rows with `isReplay`. Collision agreement uses forward-simulated client contacts only. Replayed contacts are kept as a separate statistic: "contacts recomputed during replays".

---

## 5. Per-library adapters

Each adapter implements one small interface:

```csharp
interface ICorrectnessSource
{
    bool IsServer { get; }  bool IsClientOnly { get; }
    bool IsReplaying { get; }                       // true while resimulating / reconciling / rolling back
    event Action ServerTickSimulated;               // after the server's physics step
    event Action ClientForwardTickSimulated;        // after a forward (non-replay) client step
    uint CurrentTick { get; }
    void CollectEntities(List<EntityRef> into);     // id, kind, isLocal, sim Rigidbody, rendered Transform, colliders
}
```

| | Ursitoare | Mirror | FishNet | PurrNet |
|---|---|---|---|---|
| Entity id | `NetworkIdentity.netId` | `NetworkIdentity.netId` | `NetworkObject.ObjectId` | **Verify:** `NetworkIdentity` id, or `PredictedIdentity`'s id, as long as it's stable across processes |
| Simulated body | Root `Rigidbody` | `PredictedRigidbody.predictedRigidbody` (the **physics ghost** while moving, the root while idle) | Root `Rigidbody` | `PredictedRigidbody.rb` |
| Rendered transform | Detached visuals (`PredictedEntityVisuals`) | Root transform (smoothly follows the ghost) | `NetworkObject.GetGraphicalObject()` | `PredictedTransform.graphics`, or the renderer's transform (unassigned in the demo prefab, so it's the body) |
| Server tick hook | `PredictionManager.onPostTick` on the server | End of the `FixedUpdate` player-loop phase (Mirror simulates in `FixedUpdate`) | `TimeManager.OnPostTick` (server) | `NetworkManager.onPostTick(asServer: true)` |
| Forward client tick | `onPostTick` on the client (resims run before it, inside `Tick()`) | End of `FixedUpdate` | `TimeManager.OnPostTick` (client) | `onAfterPhysicsPass` while `!isReplaying` |
| `IsReplaying` | Between `PredictionManager.resimulation(true)` and `(false)` | Always false (no replays) | `PredictionManager.IsReconciling` | `PredictionManager.isReplaying` |
| Local player | `ClientPredictionManager.GetLocalEntities()` | `isLocalPlayer` | `IsOwner` | `IsOwner()` |

Most of these hooks are already used and checked in each project's `ResimGraph.cs`. Reuse its lookups (local entity, rendered transform) instead of rewriting them.

---

## 6. Overlap measurement (client, runtime)

Overlap needs Unity's physics queries, so it's measured during the run, and the analyser only classifies the results.

1. **Proxies:** for every entity, a hidden proxy object on a dedicated `CorrectnessProxy` layer, set to collide with nothing in the layer collision matrix. It carries a copy of the entity's collider shape (player `BoxCollider` size and centre, ball `SphereCollider` radius, scaled the same way).
2. **Placement:** at the render sample (§4.2), move each proxy to its entity's rendered pose and call `Physics.SyncTransforms()`.
3. **Queries:**
   - **Pairs:** `Physics.ComputePenetration` for each proxy pair whose bounds overlap. With about 12 entities that's at most 66 pairs; a cheap bounds check first keeps it negligible.
   - **Level:** `Physics.OverlapBox`/`OverlapSphere` against the static level layers, then `ComputePenetration` on what they return.
   - Write every pair whose depth is above `contactTolerance` (default: the physics `defaultContactOffset` + 1 mm) to `visual_overlap.csv`.
4. **Gaps:** repeat the pair test with proxies **inflated by `g`** (default 5 cm) and record which pairs *don't* overlap. An inflated miss means the visuals are more than `g` apart, with no distance query needed. Only pairs the server had in contact need this check, but at runtime the recorder doesn't know which those are, so it records inflated misses for pairs within a coarse distance and the analyser filters them. Exact form to be decided in phase 4.
5. **Server truth:** the server runs the same queries against its real colliders after each tick and writes them to `server_contacts_depth.csv` (same columns as `visual_overlap.csv`). This shows where the server legitimately had overlap or contact (resting contact, solver penetration).

---

## 7. Scenarios, bot input and running

### 7.1 Input abstraction
Each demo's player controller reads `Keyboard.current` directly:
- Ursitoare: `PredictablePlayerController.cs`
- Mirror: `MirrorPredictedPlayerController.cs`
- FishNet: `PredictedPlayerController.cs`
- PurrNet: `PredictedPlayerController.cs`

Replace those reads with `DemoInput.Throttle`, `.Steer`, `.Boost`, `.StrafeLeft`, `.StrafeRight` and `.Spin`. `DemoInput` returns the keyboard by default, or the scenario timeline when a bot is active. That's the only gameplay code change, and keyboard play stays identical.

The ball-spawn key (B in `GameController` / `FishnetGameController` / `BallSpawner`) also goes through `DemoInput`. Scenarios spawn balls at **fixed** positions through a per-library spawn hook, not at random.

### 7.2 Scenario format (`Correctness/Scenarios/*.json`)
```json
{
  "name": "S4-push-ball",
  "durationSeconds": 30,
  "settleSeconds": 3,
  "balls": [ { "at": 0.0, "position": [0, 1, 6] } ],
  "players": [
    { "client": 0, "spawn": [0, 1, 0], "yawDeg": 0,
      "timeline": [ { "at": 0.0, "throttle": 1 }, { "at": 2.5, "throttle": 1, "boost": true }, { "at": 4.0, "throttle": 0 } ] }
  ]
}
```
- **Timing:** the timeline uses scenario time, which starts once all expected players exist and `settleSeconds` have passed. Each library samples input at its own tick, and `DemoInput` returns the value in force at that moment. `inputs.csv` records what was actually sampled, so the analyser can confirm every library got the same input stream (tolerance of one tick).
- **Spawn poses:** `spawn` and `yawDeg` need a spawn hook per library. Otherwise, use each demo's spawn points and record them as they are.

### 7.3 Scenario set (first version)
| Id | What it stresses |
|---|---|
| S0 idle | No input: the baseline. Any error here is noise or drift. |
| S1 straight + stop | Acceleration and stopping, settle time |
| S2 slalom | Rapid steering changes: prediction around input changes |
| S3 boost into wall | Hard collisions with static geometry |
| S4 push ball | Player–ball contact, the most common overlap case |
| S5 head-on | Two bot clients colliding: remote vs local interaction |
| S6 crowd | 10 balls plus two players: many contacts at once |

### 7.4 Network conditions
- Use **clumsy** (Windows, WinDivert) on the loopback interface for all four libraries, rather than each library's own latency simulator, so conditions are identical. Profiles live in `Correctness/NetworkProfiles/`: `clean` (0 ms), `typical` (50 ms ±5, 0.5 % loss), `bad` (150 ms ±20, 2 % loss), `loss-burst` (scripted loss bursts).
- Disable each library's built-in simulator during runs: Mirror/Ursitoare `LatencySimulation`, FishNet `LatencySimulator`, PurrNet's equivalent if present. Record that in `meta.json`.
- clumsy's command-line options must be checked in phase 5. If it can't be scripted reliably, the launcher prompts for the profile and records which one was used.

### 7.5 Launcher (`Correctness/Run-Correctness.ps1`)
- **Inputs:** library, scenario, network profile, number of runs, build path.
- **Per run:**
  1. Start a dedicated server: `-role server -scenario … -correctness-record …`.
  2. Start N bot clients: `-role client -bot <index>`, windowed, with fixed resolution and frame cap.
  3. Wait for the scenario-complete marker, or time out.
  4. Close all processes, check that every folder has `meta.json`.
  5. Zip the run.
- **Prerequisite:** each demo needs the `-role` / `-scenario` / `-bot` command-line handling in a small shared `CommandLineLauncher`. Today, Ursitoare and PurrNet start sessions from code or a HUD, and Mirror and FishNet from their managers' HUDs.

---

## 8. Analysis (offline)

### 8.1 Loading and alignment
- **Loading:** load every process folder of a run. Convert times to seconds since the earliest `startTimestamp`.
- **Server trajectory:** for each entity, a continuous function built from `server_ticks.csv`. Position uses cubic Hermite interpolation with the recorded velocities, and rotation uses slerp. The path is split at respawns.
- **Groups:** results are grouped by **viewer** (which client) × **subject** (local player / remote player / ball).

### 8.2 Path metrics (per viewer × subject, over `client_frames` rendered poses unless noted)
| Metric | Definition | Answers |
|---|---|---|
| **Raw error** | `‖p_ren(t) − p_srv(t)‖`, and the rotation angle | What the player actually sees vs the truth at that moment. Includes deliberate delay. |
| **Display delay τ\*** | In sliding 2 s windows: `argmin_τ mean ‖p_ren(t) − p_srv(t − τ)‖`, τ ∈ [−0.5, 0.5] s | How far behind (+) or ahead (−) the client shows this subject |
| **Residual error** | `‖p_ren(t) − p_srv(t − τ*)‖`, and the rotation angle | Shape error once the delay is removed: the main quality number |
| **Path deviation** | Distance from `p_ren(t)` to the server path within ±1 s | Was it on the right path at all, whatever the timing |
| **Simulated-state error** | `client_ticks` vs `server_ticks` for the **same tick** (local player and predicted followers) | Prediction accuracy before smoothing. Cross-checks ResimGraph's PREDICTION ERROR. |
| **Wobble** | Rendered path length ÷ server path length over the same span; RMS jerk of the rendered path | Jitter, overshoot, back-and-forth corrections |
| **Visual jumps** | Frames moving more than 0.35 m or 2.5° (ResimGraph's thresholds) | Visible snaps |
| **Settle time** | After the scenario's "stop" markers: time until `‖p_ren − p_srv,final‖ < ε` | How quickly the client converges |
| **Cross-client agreement** | For subjects seen by two clients: `‖p_ren,A(t) − p_ren,B(t)‖` | Whether players see the same game |

Each metric is reported as p50 / p95 / p99 / max, plus time-weighted means where relevant.

### 8.3 Overlap and contact metrics
| Metric | Definition |
|---|---|
| **False visual overlap** | Client overlap rows for a pair where the server had no overlap deeper than `contactTolerance` within `[t − τmax, t + τmax]`. `τmax` is the larger of the two subjects' display delays plus one tick. Reported as count, total depth × time (m·s), and max depth. Split into entity–entity and entity–level. |
| **Legitimate overlap** | Client overlap also present on the server (resting contact, solver penetration). Reported for context and not penalised. |
| **Visible gap during contact** | The server had the pair in contact while the client's inflated proxies missed (more than `g` apart). Reported as duration and count. |
| **Collision recall** | Fraction of server `enter` contacts matched by a client **forward** contact for the same pair within `W = τ*(subject) ± 75 ms` |
| **Collision precision** | Fraction of client forward contacts matched by a server contact. Unmatched ones are "phantom collisions". |
| **Replayed contacts** | Count of contacts recomputed during replays, as a cost and instability signal |

### 8.4 Output
- **`report.md` per run set:** one table per scenario × network profile with the four libraries side by side. Each value is the median over runs, with the interquartile range.
- **`metrics.csv`:** the long-format table for further plotting.
- **Plots (optional):** server vs rendered path traces, and error over time. These can be PNGs written from the editor, or a single HTML page if needed later.

### 8.5 Robustness
- **Missing or extra rows:** dropped frames are handled by working in time, not frame index. Partial runs are rejected through the `meta.json` marker.
- **Outliers:** report p95/p99/max next to p50, so short spikes stay visible.
- **Edge cases:** when a subject is static (S0, or a parked ball), τ\* is undefined, so the window is skipped and residual falls back to raw error. Respawns split the path, and windows that cross a respawn are skipped.

---

## 9. Phases and acceptance criteria

The estimates are rough and assume one person familiar with the four projects.

| Phase | Work | Done when | Est. |
|---|---|---|---|
| **0. Prerequisites and fairness audit** | Align tick and send rates. The README states 120 Hz simulation, but the Ursitoare scene overrides `SimulationHz` to 60. Decide on dedicated-server runs. Verify PurrNet's stable entity id, URP rendering hooks, and that the `PhysicsBodyConfig` assets match. Add `CommandLineLauncher` to each project. | All four start as dedicated server + client from the command line with identical rates. The audit table is written to `Correctness.md`. | 1 d |
| **1. Recorder core + Ursitoare adapter** | Format, buffered writer, clock, meta, server/client sampling, `CollisionProbe`, replay flag | A manual Ursitoare run produces all files. Its simulated-state error matches ResimGraph's PREDICTION ERROR. Overhead is under 0.2 ms p95. | 2 d |
| **2. Mirror, FishNet and PurrNet adapters** | The three adapters, reusing the ResimGraph lookups | Each library produces the same file set, and the entity ids match between server and client. | 2 d |
| **3. Analyser: path metrics** | Loader, server trajectory, §8.2 metrics, `report.md` | Passes the validation tests in §9.1 (V1–V3). | 2–3 d |
| **4. Overlap and contacts** | Proxies, server depth queries, gap test, §8.3 metrics | V4 passes. Results are stable across repeated S4 runs (the IQR stays below an agreed threshold). | 2 d |
| **5. Bot, scenarios and launcher** | `DemoInput` swap in the four controllers, `ScenarioRunner`, spawn hooks, S0–S6, the launcher, clumsy profiles | One command runs a scenario × profile × N runs for one library unattended. `inputs.csv` matches across the four libraries within one tick. | 2–3 d |
| **6. Validation and first comparison** | V5–V6, a full matrix (S0–S6 × clean/typical/bad × 5 runs × 4 libraries), the first report | Report reviewed. Recorder overhead shown not to change the results. | 1–2 d |

### 9.1 Validation tests
- **V1, zero conditions:** on `clean` with S0 idle, the residual error and path deviation of every subject are close to 0, within an agreed noise floor.
- **V2, known offset:** a debug toggle adds a constant 0.25 m offset to one client's rendered transform. The analyser must report residual error of about 0.25 m.
- **V3, known delay:** a debug toggle renders one remote subject with an extra 100 ms delay. The analyser must report a τ\* about 100 ms higher, with unchanged residual error.
- **V4, known overlap:** move a ball's visual 10 cm into a wall on one client. That must show as false entity–level overlap of about 0.1 m, and nothing on the server.
- **V5, cross-check:** the simulated-state error per library matches ResimGraph's PREDICTION ERROR (where supported) within a tick.
- **V6, overhead:** compare runs with the recorder on and off. ResimGraph's TICK COST and RESIM counts should stay within run-to-run spread.

---

## 10. Risks and mitigations

| Risk | Mitigation |
|---|---|
| The recorder slows the frame and changes the results | Allocation-free sampling and background writes. Overhead is measured and reported, and V6 compares runs with and without it. |
| Comparing deliberate delay as if it were error | Delay (τ\*) is kept separate from residual error. Both are reported, and the report explains the trade-off. |
| Mirror's "simulated body" is a ghost object that exists only while moving | The adapter switches between ghost and root explicitly and records which one in `client_frames` (extra flag column). The analyser treats both as the simulated state. |
| Entity ids differ between processes (PurrNet unknown) | Verified in phase 0. Fallback: match by spawn order plus spawn position from the scenario. |
| Collisions from replays polluting the results | `IsReplaying` flag per adapter. Only forward contacts are scored. |
| clumsy on loopback is unreliable or hard to script | Phase 5 spike. Fallback: manual profile selection, recorded in meta. Last resort: each library's simulator with matched settings, flagged in the report. |
| Host-mode runs skew results (hosts don't predict their own objects) | Dedicated server only. The launcher refuses `-role host` for correctness runs. |
| Different library defaults (interpolation buffers, smoothing) dominate the results | Record each library's prediction and interpolation settings in meta, and report them next to the results. Optionally add a "tuned" run set later. |
| Tick-rate mismatch across demos | The phase 0 audit is a blocker. |
| Large disk use for long matrices | About 20 MB per client-minute. Zip per run, and optionally a compact binary format later. |

---

## 11. Decisions needed before starting

1. **Tick and send rates for comparison runs:** 120/120 everywhere, or each library's recommended setup? This also decides the Ursitoare 60 Hz scene override.
2. **Number of clients:** 2 bot clients (needed for S5 and cross-client agreement), or more.
3. **Network tool:** clumsy (preferred), or accept each library's simulator.
4. **Report format:** Markdown and CSV only, or also an HTML page with path traces.
5. **Library settings:** run each library on default settings only, or also a tuned set.
