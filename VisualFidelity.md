# Visual fidelity: how closely the visuals follow the server

`VisualFidelityProbe` measures, on a client, how far what is **drawn** for an entity is from where the **server** had it. It works for the local player, remote players, bots and balls, and splits the result by what the entity was doing: moving freely, colliding, or standing still.

It complements ResimGraph. ResimGraph shows how hard a library works and how wrong the predicted simulation was (PREDICTION ERROR). This shows what the player actually ends up seeing after all the smoothing.

## Wiring it up

**Ursitoare, FishNet, PurrNet: nothing to do.** Press Play, or run a build, and connect a client-only instance. Each project's `VisualFidelityFeeder` creates itself after the first scene loads. It adds a probe to every predicted entity and feeds it the server states the client receives. The error is graphed live at the bottom of ResimGraph (F3, see [Live graphs](#live-graphs)), and keys export it to CSV. To turn the feeder off, add `VISUAL_FIDELITY_NO_AUTOSPAWN` to the scripting define symbols.

**Mirror: not wired.** `PredictedRigidbody` keeps the server state it receives private (`OnReceivedState`), so there is nothing to compare against through the public API. Getting it would mean subclassing `PredictedRigidbody` in the prefabs or sending the server pose separately.

The feeders use **public API only**. Where a library keeps its visual delay private, the feeder reports none and only the raw error is measured (see [Where each feeder gets its data](#where-each-feeder-gets-its-data)). The one exception is opt-in: FishNet's feeder can read its smoother's delay by reflection, toggled with F10 or *Read Smoother Delay By Reflection* on the feeder. It is off by default.

- **Mounting it yourself:** add *Prediction Debug → Visual Fidelity Probe* to a predicted prefab, for example to change its settings in the inspector. The feeder uses the probe it finds. With *Attach To All Entities* turned off on the feeder, only prefabs that carry a probe are measured.
- **Keys** (each logs to the Console, with the file path):
  - **F7:** reset the statistics, so the next summary covers only what follows.
  - **F8:** write the summary CSV.
  - **F9:** start or stop recording every compared frame to CSV.
  - **F10 (FishNet only):** turn reading the smoother delay by reflection on or off.
- **Clients only.** The host draws the server's own objects, so there is nothing to compare there.

| File | What it is |
|---|---|
| `Shared/com.predictioncompare.common/Runtime/VisualFidelity/VisualFidelityCore.cs` | The maths: server path, raw and residual errors, phases, statistics. No components, no networking library. |
| `Shared/.../VisualFidelity/VisualFidelityProbe.cs` | The component on each entity. Samples the visual every frame. |
| `Shared/.../VisualFidelity/VisualFidelity.cs` | What all probes share: the clock, the impact list, sampling, CSV output. |
| `Shared/.../VisualFidelity/VisualFidelityExport.cs` | The export keys. Created by the first probe. |
| `PredictionDemo-<library>/Assets/Scripts/VisualFidelity/VisualFidelityFeeder.cs` | The feeder for Ursitoare, FishNet and PurrNet: server states, clock and, where public, visual delay. |

## How it works

### One timeline for frames and server states

Server states and rendered frames are placed on one timeline, in seconds:

- **Server states.** The state after tick T sits at T × tick length. Between two states the server path is a straight line. That isn't a simplification: a physics step moves a body in a straight line at its new velocity, so there's nothing more between two ticks to recover.
- **Rendered frames.** A frame drawn a fraction f of the way into the next tick sits at (T + f) × tick length. The visual is read in `Application.onBeforeRender`, after every `Update` and `LateUpdate`, so every library's smoothing has already moved it.

All three libraries advance their tick counter after the tick's physics step, so in each the newest simulated tick is the counter minus one. Each library puts its server states on the client's tick numbering in its own way: see the table below.

### Comparing a frame later

The server state for a tick the client predicted arrives about a round trip after the client drew it. So each frame is kept until the server path covers its moment and is compared then. The numbers trail the game by a few hundred milliseconds. That is expected.

### Two errors per frame: raw and residual

Libraries draw entities behind (interpolation, smoothing) or ahead (extrapolation) on purpose. Each frame gets two errors so that this choice is not counted as being wrong:

- **Raw:** the visual vs the server **at the same moment**. This is what the player sees against the truth, delay included.
- **Residual:** the visual vs the server at the moment **the library says the visual shows**: the frame's time minus the visual delay the library reported for that frame.

The residual is split into two parts:

- **Along track:** along the server's direction of travel. This is timing error the reported delay doesn't account for. A library that reports 30 ms while drawing 50 ms behind shows 20 cm here at 10 m/s.
- **Cross track:** across the direction of travel. This is how far the visual is off the server's path.

### The visual delay comes from the library

Each feeder reads the library's current visual delay at runtime and reports it with `probe.ReportVisualDelay(seconds)`, every tick or every frame. Each frame the probe samples is stamped with the value last reported. So a delay that changes, such as an adaptive buffer or the fraction of a tick at render time, is followed exactly.

- **Meaning:** how far behind the clock the visual shows the server, in seconds. Negative means drawn ahead.
- **Not reported (NaN):** the residual and everything derived from it are left out, and only the raw error is measured. Idle frames are the exception, because a still object looks the same at any delay.

### Where each feeder gets its data

| | Ursitoare | FishNet | PurrNet |
|---|---|---|---|
| **Server states** | `ClientPredictedEntity.serverStateBuffer`. The server stamps every state, followers' too, with this client's tick. | The rigidbody pose right after each reconcile is applied (`OnPostPhysicsTransformSync`), for the objects with `IsObjectReconciling` set, at the reconcile's client tick | The rigidbody pose at `onBeforePhysicsPass` of each verified replay tick S (`isVerified`). The server's frame S is the state entering S, so it is reported at tick S − 1. |
| **Clock** | `GetTickId() - 1` plus the fraction of the fixed step | `LocalTick - 1` plus `GetTickPercentAsDouble()` | `localTick - 1` plus `TickManager.floatingPoint` |
| **Drawn object** | The detached `PredictedEntityVisuals` object | `GetGraphicalObject()`, moved by FishNet's tick smoother | The rigidbody itself (`PredictedTransform.graphics` is unassigned in the demo) |
| **Visual delay** | `PredictedEntityVisuals.GetVisualDelay()` (how far the visuals trail the newest simulated state, averaging window included) plus the time since that tick. Needs an Ursitoare version that has `GetVisualDelay()`. | **None** for smoothed objects by default: the smoother's state is private. **With the reflection toggle on**, it reads the smoother's queue of tick poses and its move rates. The graphical object moves to the oldest queued pose over one tick, so it shows `first queued tick − TimeRemaining / tick length`, adaptive interpolation and speed-up included. An object without a graphical object draws its rigidbody: the tick fraction. | **The tick fraction:** the rigidbody shows the newest tick, and the frame is drawn that far into the next one. With `graphics` assigned, view interpolation (internal) takes over and no delay is reported. |

So in this demo **Ursitoare and PurrNet get a residual**. FishNet does too with the reflection toggle on, and shows the raw error only without it.

**FishNet reflection toggle.** The reads are `TransformTickSmoother._transformProperties` (count and first tick) and `_moveRates.TimeRemaining`. They are compiled into getters the first time the toggle is switched on, so each frame's read costs about as much as ordinary field access and allocates nothing. With the toggle off, no reflection happens at all. If FishNet renames the fields, the feeder logs a warning and stays on raw error. `TransformTickSmoother` is marked obsolete for FishNet 5, but in 4.x it is what NetworkObject's graphical object setting, the one the demo prefabs use, runs on.

**FishNet truth caveat.** FishNet reconciles an object to the server's state when the packet carried one. Otherwise it uses the client's own stored state for that tick, and which one it used is internal (`IsReconcileRemote`). The server keeps sending an object's state while its transform changes and stops once it rests. So the stored state stands in only for objects at rest on the server, and that is also what FishNet itself corrects towards.

**Ursitoare's delay.** `GetVisualDelay()` is part of `VisualsInterpolationsProvider`, so every interpolator implements it. It records the moment each `Update` actually draws. The interpolator gets one state per forward tick (`newStateReached` never fires during a resimulation), so its newest entry is the newest simulated tick. The value is the gap between that tick and the drawn moment, plus half the averaging window for position. It isn't a whole number of ticks: the playback time runs on `deltaTime`, and a window of 4 adds 1.5 ticks.

### Phases

Every frame is classed by what the server had at the moment the visual shows:

| Phase | When |
|---|---|
| **collision** | Within 50 ms before to 500 ms after an impact on this entity, or on an entity it was touching |
| **idle** | Server speed below 5 cm/s, and not in a collision |
| **moving** | Everything else |

An **impact** is a velocity change of more than 2 m/s between consecutive server states. Impacts are shared between probes, because a collision often barely changes one of the two bodies, for example a player hitting a light ball. The other entity's frames count as collision when the two server positions were within both radii plus 0.5 m. Radii come from the entities' colliders.

A flip jump changes velocity by 6 m/s in one tick, so it counts as an impact too. A server step faster than 150 m/s is treated as a teleport or respawn. Nothing is interpolated across it, and frames whose comparison would straddle one are counted as unmatched.

### Collision episodes

An episode is a run of consecutive collision frames. Each episode records:

- **peak:** the highest residual error;
- **settle time:** time from the latest impact until the residual error last exceeded 5 cm;
- **unsettled:** whether the error was still above 5 cm when the window closed;
- the error integrated over the episode, in m·s (CSV only).

## Live graphs

Four rows at the bottom of ResimGraph (F3) show the error live, in all four demos:

| Row | What it plots |
|---|---|
| VISUAL vs SERVER | Local player, raw error |
| VISUAL vs SERVER resid | Local player, residual error |
| OTHERS vs SERVER resid | The worst residual among remote players, bots and balls in each frame |
| VISUAL DELAY | Local player, the delay the library reports, in ms. One grid line per tick. |

On the three error rows, as on PREDICTION ERROR, a bar's height is the position error and its colour is the worse of the position and rotation errors against the row's scales. So a visual that is in the right place but turned the wrong way shows a short bar in a hot colour. Collisions aren't marked in the graph; the summary CSV breaks them out.

**The error rows fill in about a round trip late.** A frame can only be compared once the server state for it arrives. Each error is then drawn in the column of the frame it describes, not the current one, so a spike lines up with the CORRECTION, RESIM and VISUAL JUMP rows of the same frame. The newest stretch of these rows, roughly one round trip wide, is empty until the server catches up.

The rows show `(no visual fidelity feeder running)` in libraries without a feeder, and on the host.

## CSV export

Files go to `<Application.persistentDataPath>/VisualFidelity/<time>-<library>-pid<pid>-<kind>/`, and the path is logged to the Console. On Windows that is `%USERPROFILE%\AppData\LocalLow\<company>\<product>\VisualFidelity`.

**Summary (F8):** `summary.csv`, in long format with columns `library,scope,subject,label,phase,metric,unit,count,mean,rms,p50,p95,p99,max`.

- **Scope:** one block per kind of entity (`scope=subject`: local player, remote players, bots, balls) and one per entity (`scope=entity`).
- **Phase:** `All`, `Moving`, `Collision` or `Idle`.
- **What it covers:** everything since the last reset (F7).

| Metric | Unit | Meaning |
|---|---|---|
| `raw` | m | Visual vs server at the same moment |
| `residual` | m | Visual vs server, with the reported delay taken out. **The main quality number.** |
| `along_track` | m | Residual along the direction of travel |
| `cross_track` | m | Residual across the direction of travel: off the path |
| `rotation_raw`, `rotation_residual` | deg | Rotation error, without and with the delay taken out |
| `velocity_error` | m/s | Visual velocity from frame to frame vs server velocity. Shows judder (a visual that only moves on tick frames) and rubber-banding. |
| `reported_delay` | s | Mean visual delay the library reported. Positive means behind the server. Empty when none was reported. |
| `extra_path` | ratio | How much further the visual travelled than the server over the same frames. Back-and-forth corrections add path. |
| `episode_peak`, `episode_settle` | m, s | Per collision episode: highest residual, and time until it last exceeded 5 cm |
| `episode_unsettled`, `episode_error_integral` | count, m·s | Episodes still above 5 cm when the window closed, and the mean residual integrated over an episode |
| `unmatched_frames` | count | Frames that had no server state around them, or that straddled a teleport |

**Recording (F9):** one CSV per entity with every compared frame: time, reported delay, phase, every error, server speed, and the visual, server and delayed-server positions.
  - Use it to plot a collision.
  - It writes about 150 KB/s per entity at 144 fps.

## Writing a feeder

A feeder is a small component in the demo project, the counterpart of `ResimGraph.cs`. It needs to do five things:

1. **Set the clock.** Set `VisualFidelity.Clock` to a function that returns the present time on the timeline the server states use: for a tick-based library, `(latest simulated tick + fraction into the next) × tick length`. Set `VisualFidelity.LibraryName`, and call `VisualFidelity.ResetSession()` when a session starts.
2. **Attach probes.** Add a `VisualFidelityProbe` to each entity, or use the one on the prefab. Set `Visual` to the transform that is actually drawn, `Subject` to the kind of entity, and `Label` to a name.
3. **Report server states.** Call `probe.ReportServerTick(tick, tickSeconds, position, rotation)`, or `ReportServerState(time, ...)`, for every server state the client receives. Order doesn't matter, and repeats are ignored.
4. **Report the visual delay.** Call `probe.ReportVisualDelay(seconds)` every tick, or every frame when the delay changes within a tick. It's measured against the same clock: how far behind `Clock()` the drawn object is. Report NaN when the library can't tell.
5. **Use the same numbering.** The tick a state is reported at must be on the same numbering as the clock, and mean the state *after* that tick. Check this per library, because what a "tick" means for a server state differs: compare FishNet's reconcile client tick with PurrNet's "state entering S" in the table above.

## Status

- **Built and tested offline, not yet run in the editor.**
  - **Compiled:** the shared files compile in all four projects with Unity 6000.3.7f1's Roslyn compiler, and each feeder compiles with its project's `Assembly-CSharp`.
  - **Tested:** the maths passed synthetic scenarios with known answers: a constant delay and side offset, extrapolation with a rotation offset, a wall bounce with overshoot, an idle object, impact sharing between nearby and distant entities, a delay that changes every frame with 15 % state loss, a teleport, a wrongly reported delay, and no reported delay.
  - **Unity's own Slerp untested:** those scenarios used a stand-in for `Vector3` and `Quaternion`, so Unity's `Quaternion.Slerp` isn't covered.
  - **Cost:** about 16 µs per rendered frame for 12 entities under .NET, measured offline. Expect 2–4× that under Unity's Mono.
- **The first run should confirm, per library:**
  - the graph rows fill on a client-only instance, and after pushing a ball the summary CSV has `Collision` rows for both the ball and the player;
  - **PurrNet:** the local player's residual stays near zero while driving straight; a large along-track part means the tick numbering is off;
  - **Ursitoare:** the local player's reported delay is a few ticks, and the residual stays small while driving straight; a large along-track part means the delay is off;
  - **FishNet:** the local player's raw error while driving straight is about speed × the smoothing delay, a few ticks' worth. With F10 on, VISUAL DELAY shows that delay and the residual drops close to zero; a large along-track part means the smoother reading is off.
- **Send rate below tick rate:** when the server sends less often than it ticks (Ursitoare's `NetworkHz` below `SimulationHz`), the server path is interpolated across the missing ticks, so impacts land up to one send interval off.
