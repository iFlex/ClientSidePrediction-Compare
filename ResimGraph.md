# ResimGraph: live prediction metrics for the four demos

Each demo project now has a `ResimGraph` component. It draws scrolling graphs of resimulations and related prediction metrics in real time, modelled on `ResimGraph.cs` from AntShipWars_URP. All four use the same set of graphs, in the same order and colours, so you can run the demos side by side and compare them row by row.

The graphs read **only the public API** of each library. Nothing in Ursitoare, Mirror, FishNet or PurrNet was modified. Where a library has no such metric, the row is either filled with the closest metric the library does have (marked `~`) or left as `n/a`.

## Wiring it up

**Nothing to do: just press Play, or run a build.** Once the first scene has loaded, each project creates a `ResimGraph (auto)` GameObject, kept across scene loads, unless the scene already has a `ResimGraph`.

To place it yourself, for example to change its settings in the inspector, add the `ResimGraph` component to any GameObject in `Assets/Scenes/Gameplay.unity`. In the Add Component menu it's under *Resim Graph*; the class is `PredictionDebug.ResimGraph`. The automatic one then won't be created. To turn the automatic graph off completely, add `RESIM_GRAPH_NO_AUTOSPAWN` to *Project Settings → Player → Scripting Define Symbols*.

- It creates its own screen-space overlay canvas. To draw into an existing canvas instead, assign it to *Parent Canvas*.
- **F3** shows and hides the graphs. You can change the key under *Toggle Key*.
- **Watch it on a client-only instance.** In all four demos the host does not predict its own objects, so most graphs show `(client only)` or `(no prediction on server/host)` when hosting. The server-side input graph is the exception: it shows `(server only)` on clients.
- It starts graphing once networking is up. It reconnects to a new session by itself if you stop and start again.
- When it wakes up, it logs a support table to the Console: `[ResimGraph][<library>] graph support: ...`. The table lists every row with its source and the reason for any substitute.
- **FishNet only:** NET OUT needs the `CountingTugboat` transport, which isn't in the scene yet. Until it is, the row shows `(needs CountingTugboat transport, see ResimGraph.md)`. To turn it on, add the `CountingTugboat` component to the NetworkManager GameObject in the Gameplay scene. FishNet's `TransportManager` picks up the transport already on that object and only adds a stock `Tugboat` at runtime when there's none. `CountingTugboat` is a `Tugboat` subclass that reports each segment it's asked to send and otherwise behaves exactly like `Tugboat`.
- **PurrNet only:** jitter, packet loss and bandwidth come from PurrNet's `StatisticsManager`, and the Gameplay scene doesn't have one. The graph adds one to the NetworkManager GameObject at startup. You can turn this off with *Add Statistics Manager If Missing*. It sends about 20 small ping packets a second, which show up in NET OUT.

Files, the same in every project:

| File | What it is |
|---|---|
| `Assets/Scripts/ResimGraph/ResimGraphCore.cs` | Shared drawing and layout code. Byte-identical in all four projects. |
| `Assets/Scripts/ResimGraph/ResimGraph.cs` | Library-specific collector. This is the component you add. |
| `Assets/Scripts/ResimGraph/CountingTugboat.cs` | FishNet only. A `Tugboat` subclass that reports outgoing bytes for NET OUT. Not wired into the scene yet; see the FishNet note above. |

### Reading the graphs

- One column per rendered frame, scrolling left to right. The dim white column is the write head.
- Graphs are stacked top-down from the top-right corner. When the screen runs out of height they continue in another column to the left.
- Each label shows the latest value and the peak (`pk`) currently on screen. Event graphs show their rate per second and how many events are on screen. Bipolar graphs show the latest value and the range.
- Label prefixes:
  - none: the value comes straight from a library counter, event or property.
  - `*`: computed by the graph from public library state, e.g. comparing two poses.
  - `~`: a substitute. The library has no such metric, so the graph shows the closest one it has, under a title that says what it actually is.
  - `n/a`: no public data comes close.
- Every scale can be changed on the component: resim depth, costs, error, latency, tick lead, snapshot age, input buffer, bandwidth and smoothing. The visual-jump thresholds default to Ursitoare's values, 0.35 m and 2.5°, so all four libraries are judged by the same test.

## The graphs

These 18 rows appear in every project, in this order.

| Graph | Unit | What it measures |
|---|---|---|
| RESIM | event | A rewind + replay (resimulation) happened this frame. |
| RESIM DEPTH | ticks | How many ticks the resimulation went back and replayed (deepest in the frame). |
| RESIM COST | ms | Wall time spent resimulating this frame. Full height = one tick by default. |
| RESIM ENTITIES | count | How many predicted bodies were resimulated or corrected. |
| PREDICTION ERROR | m | Local player: predicted pose vs the server's pose **for the same tick**. Height is position error; colour is the worse of position and rotation. This is the cause of a correction. |
| CORRECTION | m | Local player: how far the **present** pose moved because of the correction, measured before vs after it was applied. This is the effect of a correction, after the replay. |
| TICK COST | ms | Wall time of a normal simulation tick, excluding resimulation. |
| TICK GAP | ms | Wall time between tick starts. Grid line = one tick. Colour shows the distance from one tick. |
| CLOCK ADJUST | % (bipolar) | How much the library is currently speeding up (+) or slowing down (−) the client tick to stay in sync. |
| LATENCY RTT | ms | Network round trip time: the library's ping. |
| INPUT RTT | ms | Time from a client tick's input going out until a server state that echoes that tick comes back. Includes the network, how long the input waits on the server (input buffer or slack), and send batching. Same scale as LATENCY RTT, so the gap between the two rows is the server-side wait. |
| TICK LEAD | ticks | How far the client runs ahead of the newest server-confirmed tick. |
| SNAPSHOT AGE | ms | Time since the last server state arrived (sawtooth). Peaks show gaps in the stream. |
| SERVER INPUT BUFFER | ticks | Client inputs waiting on the server. 0 means the server is starved and has to guess, which often causes resims. |
| PACKET LOSS | event | Lost server updates. |
| NET IN / NET OUT | KB/s | Bytes received and sent, over a sliding 1 s window. |
| VISUAL JUMP | event | The rendered transform moved more than 0.35 m or 2.5° in one frame. |
| VISUAL vs SIM | m | Distance between the rendered object and the simulated rigidbody. This is how much the smoothing is hiding. |

Some projects add extra rows after these. They're listed per library below.

### Read these differences before comparing numbers

- **FishNet and PurrNet resimulate on every received state**, whether or not anything was wrong. Ursitoare resimulates only when its checker finds a mismatch. Mirror never replays physics at all. So the RESIM row runs nearly solid for FishNet and PurrNet, and that is expected. For comparing correctness across all four, PREDICTION ERROR and CORRECTION are the meaningful rows.
- **LATENCY RTT is a ping in all four; INPUT RTT is the full input loop.** Ursitoare's tick round trip used to be on the LATENCY row, which made it look far slower than the others. It now sits on INPUT RTT next to the FishNet and PurrNet equivalents. Ursitoare and FishNet time it from the input being sent. PurrNet times it from the forward simulation of the tick. Ursitoare reads it on packet arrival, while FishNet and PurrNet read it when the state is applied, which can be up to a tick later.
- **Mirror works in time, not ticks.** Its TICK COST and TICK GAP measure Unity's fixed step, and TICK LEAD is its time-based prediction horizon converted to fixed steps.
- **What the demo prefabs render differs.** Ursitoare draws detached, interpolated visuals. Mirror draws a renderer that smoothly follows a physics ghost while the body moves. FishNet draws a tick-smoothed graphical object. PurrNet's `PredictedTransform.graphics` is unassigned in the demo prefab, so it draws the raw rigidbody: VISUAL vs SIM stays 0 and every correction is visible.

## Support matrix

✓ = native, * = computed, ~ = substitute, ✗ = not available, — = no extra row.

| Graph | Ursitoare | Mirror | FishNet | PurrNet |
|---|---|---|---|---|
| RESIM | ✓ | ~ correction event | ✓ | ✓ |
| RESIM DEPTH | ✓ | ✗ | ✓ | * |
| RESIM COST | ✓ | ~ net receive cost | * | * |
| RESIM ENTITIES | * | * | * | * |
| PREDICTION ERROR | * | ✗ | * | * |
| CORRECTION | * | * | * | * |
| TICK COST | ✓ | * | * | * |
| TICK GAP | * | * | * | * |
| CLOCK ADJUST | ~ tick rate drift | * | ~ tick rate drift | ✓ |
| LATENCY RTT | ✓ | ✓ | ✓ | ✓ |
| INPUT RTT | ✓ | n/a | * | * |
| TICK LEAD | ✓ | * | * | ✓ |
| SNAPSHOT AGE | * | * | * | * |
| SERVER INPUT BUFFER | ✓ (server) | ~ commands/frame (server) | * (server) | ~ input slack (client) |
| PACKET LOSS | ✓ | ✗ | ~ tick gaps (est.) | ✓ (%) |
| NET IN | ✓ | ✓ | ✓ | ✓ |
| NET OUT | ✓ | ✓ | * (needs CountingTugboat) | ✓ |
| VISUAL JUMP | ✓ | * | * | * |
| VISUAL vs SIM | ✓ | * | * | * |
| Extra rows | FREEZE / RESET, RESIM SKIPPED, RTT JITTER | RTT JITTER | — | RTT JITTER, LEAD JUMP / PAUSE, VIEW STARVED |

---

## Ursitoare

Ursitoare runs on Mirror for transport. Its own API exposes almost everything AntShipWars' graph used, and more.

### What each graph reads

| Graph | Source |
|---|---|
| RESIM | `PredictionManager.resimulation` (true when a resimulation starts, false when it ends). `TickStat.didResimulate` is not used: the client resimulates before `base.Tick()` resets it, so it is always false. |
| RESIM DEPTH | `totalRewindDistance` delta at the resimulation start. |
| RESIM COST * | Wall time between the `resimulation` start and end events. |
| RESIM ENTITIES * | Client entities whose public `resimTicks` counter advanced this frame. Ursitoare replays every registered client entity. |
| PREDICTION ERROR * | Local entity's `localStateBuffer` vs `serverStateBuffer` at the newest server tick. Read by a player-loop hook at the start of the fixed step, before `PredictionManager.Tick()` checks for and runs the resimulation. |
| CORRECTION * | Local rigidbody pose in that hook vs in `onPreTick`, which fires after the resim or snap. |
| TICK COST | `TickStat.duration`. Ursitoare already excludes the resimulation from it. |
| TICK GAP * | Time between `onPreTick` events. |
| CLOCK ADJUST ~ **TICK RATE DRIFT** | Ursitoare ticks at a fixed rate from `FixedUpdate` and never adjusts its clock. Shown instead: the tick rate actually achieved over 2 s vs `1/fixedDeltaTime`. |
| LATENCY RTT | Mirror `NetworkTime.rtt` (Ursitoare runs on Mirror; the same ping the Mirror demo graphs), drawn when it updates |
| INPUT RTT | `ClientPredictionManager.onTickRttDuration`: the client tick echoed back with the server state of the input it applied. Commit `5117080` stamps the tick one tick early, so it reads about one tick high (see `Ursitoare-library-issues.md` #13) |
| TICK LEAD | `ClientPredictedEntity.GetServerDelay()` of the local entity |
| SNAPSHOT AGE * | Time since `onTickRttDuration` last fired. It fires whenever a newer server state arrives. |
| SERVER INPUT BUFFER | Server only: largest `ServerPredictedEntity.BufferFill()` |
| PACKET LOSS | `ClientPredictionManager.onPacketLoss` (server ticks missing from the state stream) |
| NET IN / OUT | Mirror `Transport.OnClient/ServerDataReceived/Sent` |
| VISUAL JUMP | `PredictedEntityVisuals.onLargeTransformJumpGlobal`, filtered to the local entity |
| VISUAL vs SIM | `PredictedEntityVisuals.GetInterpolationDistance()` |
| **FREEZE / RESET** (extra) | `PredictionManager.onSnapToServer`: history ran out and everything snapped to the latest server state |
| **RESIM SKIPPED** (extra) | Deltas of `totalResimulationsSkipped` (oversimulation protection) and `resimSkipNotEnoughHistory` |
| **RTT JITTER** (extra) | √ Mirror `NetworkTime.rttVariance`: the same ping statistics as LATENCY RTT, and the same row the Mirror demo has |

### Not available

- **Clock adjustment.** Ursitoare has none by design; the row shows the tick rate drift substitute.
- **Server input buffer, as seen from a client.** The server doesn't send its buffer fill back to clients, so it only shows on the server.

### Everything Ursitoare exposes publicly

- **PredictionManager counters:**
  - Resimulations: `totalResimulations`, `totalResimulationSteps`, and the breakdowns by cause, `totalResimulationsDueToAuthority/Followers/Both` and `totalResimulationsTriggeredByLocalAuthority/Followers/Both`.
  - Skips and freezes: `totalResimulationsSkipped`, `resimSkipNotEnoughHistory`, `totalTickFreezes`, `totalDesyncToSnapCount`.
  - Rewind distance: `maxRewindDistance`, `totalRewindDistance`.
  - Other: `clientSendErrors`, `clientStatesReceived`, `shouldResimThisTick`, `GetTotalTicks()`, `GetAverageResimPerTick()`.
- **Timing and ticks:** `lastTickDuration`, `lastInterTickDuration`, `lastClientTickRTT`, `lastServerRecvIntervalDuration`, `GetTickId()`, `GetServerTickId()`, `reportedServerTickId`.
- **Events:**
  - Tick: `onPreTick`, `onPostTick`, `onPreResimTick`, `onPostResimTick`.
  - Stats: `onTickStat` (tickId, duration, resimDuration, didResimulate, resimTicks), `onTickRttDuration`, `onPacketLoss`.
  - Resimulation: `onSnapToServer`, `resimulation`, `resimulationStep`.
  - Errors: `onServerStateSendError`, `onClientStateSendError`.
- **ClientPredictedEntity:**
  - Tick counts: `totalTicks`, `ticksAsFollower`, `ticksAsLocalAuthority`.
  - Resim counts: `resimTicks`, `resimTicksAsAuthority`, `resimTicksAsFollower`.
  - Server delay: `maxServerDelay`, `GetServerDelay()`.
  - Skipped checks and stale data: `resimChecksSkippedDueToLackOfServerData`, `resimChecksSkippedDueToServerAheadOfClient`, `oldServerTickCount`, `countMissingServerHistory`.
  - Tick markers: `lastSvTickId`, `lastCheckedServerTickId`.
  - Buffers: `localStateBuffer`, `serverStateBuffer`, `localInputBuffer`.
  - Events: `potentialDesync` (reasons: missing server comparison, server ahead of client, gap in server stream, snap with no data), `newStateReached`, `newAuthoritativeStateReached`, `inputUsed`, `onReset`, `resimulation`, `resimulationStep`.
- **ServerPredictedEntity:**
  - Input problems: `invalidInputs`, `ticksWithoutInput`, `totalMissingInputTicks`, `lateTickCount`, `inputJumps`.
  - Catch-up and buffering: `totalSnapAheadCounter`, `catchupTicks`, `catchupBufferWipes`, `totalBufferingTicks`, `maxClientDelay`.
  - Client updates: `clUpdateCount`, `clAddedUpdateCount`.
  - Buffer: `BufferFill()`, `BufferSize()`, `GetClientTickId()`, `inputQueue`.
  - Events: `potentialDesync`, `inputReceived`, `firstTickArrived`, `stateSampled`.
- **ServerPredictionManager:** per-connection latest tick (`_connIdToLatestTick`, public), ownership queries.
- **PredictedEntityVisuals:** `GetInterpolationDistance()`, `onLargeTransformJump` / `onLargeTransformJumpGlobal`, plus thresholds `LARGE_POS_JUMP` and `LARGE_ANGLE_JUMP`.
- **Stats/PredictionBudgetTracker:** a standalone helper you feed tick stats. It gives tick and resim budget fractions, window load and resimulation spikes (`onResimulationSpike`). The demo doesn't wire it up, so the graph doesn't use it.

---

## Mirror (PredictedRigidbody)

`PredictedRigidbody` has no prediction tick and no public correction event. It doesn't replay physics either. When a server state arrives, it samples its private state history at that timestamp. If the difference is over a threshold, it shifts the history by the delta and writes the corrected state straight into the Rigidbody. All of this happens inside Mirror's `NetworkEarlyUpdate` player-loop system. Nothing else moves a Rigidbody during EarlyUpdate, so the graph puts a hook before and after that system: any change in a body's position, rotation or velocity between the two is a correction.

### What each graph reads

| Graph | Source |
|---|---|
| RESIM ~ **CORRECTION EVENT** | Frames where at least one `PredictedRigidbody` was corrected, detected with the hook above |
| RESIM DEPTH | **n/a.** Mirror doesn't step physics again, and how many history entries a correction touched is internal. TICK LEAD shows how far back corrections reach. |
| RESIM COST ~ **NET RECV COST** | Wall time of Mirror's `NetworkEarlyUpdate`, which deserializes server state and runs every correction. Corrections have no separate timing. |
| RESIM ENTITIES * | Bodies corrected during this frame's network receive |
| PREDICTION ERROR | **n/a.** Mirror compares the server state with an interpolated sample of its private `stateHistory`, and neither value is exposed. |
| CORRECTION * | Local player body: pose before vs after `NetworkEarlyUpdate` |
| TICK COST * | Wall time of the whole `FixedUpdate` phase (scripts + physics), which is Mirror's simulation step |
| TICK GAP * | Time between fixed-step starts |
| CLOCK ADJUST * (ms) | Change of `NetworkTime.predictionErrorUnadjusted`, the offset Mirror adds to `predictedTime` from server ping feedback. Mirror's snapshot-interpolation timescale (`localTimescale`) is internal. |
| LATENCY RTT | `NetworkTime.rtt`, an EMA of pings, drawn when it updates |
| TICK LEAD * | `(NetworkTime.predictedTime − NetworkClient.connection.remoteTimeStamp) / fixedDeltaTime`: the prediction horizon |
| SNAPSHOT AGE * | Time since `connection.remoteTimeStamp` last changed |
| SERVER INPUT BUFFER ~ **CMDS RECEIVED / FRAME** | Mirror runs a `[Command]` as soon as it arrives, so the server keeps no input buffer. Shown instead (server only): `CommandMessage`s per frame, from `NetworkDiagnostics.InMessageEvent`. |
| PACKET LOSS | **n/a.** Neither Mirror nor its KCP transport exposes loss on the unreliable channel. |
| NET IN / OUT | `Transport.OnClient/ServerDataReceived/Sent` |
| VISUAL JUMP * | Ursitoare's jump test applied to the local player's rendered transform |
| VISUAL vs SIM * | Rendered transform vs `PredictedRigidbody.predictedRigidbody` (the physics ghost while moving; 0 while idle) |
| **RTT JITTER** (extra) | √`NetworkTime.rttVariance` |

### Not available

- Resim depth, prediction error and packet loss: as in the table above.
- **Input round trip.** The demo sends input as a `[Command]` with no tick, and the server applies it as soon as it arrives. `PredictedRigidbody` state carries no reference to an input, so an input can't be matched to the state that applied it. LATENCY RTT is the closest measure.
- Exact correction events. `PredictedRigidbody` has `OnCorrected`, `OnSnappedIntoPlace`, `OnBeforeApplyState`, `OnBeginPrediction` and `OnEndPrediction`, but they are `protected virtual`. Using them means subclassing `PredictedRigidbody` and swapping it into the prefabs, which is outside "public API only". With them, the pose-diff detection could be replaced by real events.

### Everything Mirror exposes publicly for this

- **NetworkTime:** `rtt`, `rttVariance`, `predictedTime`, `predictionErrorUnadjusted`, `predictionErrorAdjusted`, `time`, `localTime`, `offset`, `PingInterval`.
- **NetworkClient / connection:** `connection.remoteTimeStamp`, `bufferTime`, `initialBufferTime`, `bufferTimeMultiplier`, `localPlayer`.
- **NetworkDiagnostics:** `InMessageEvent` / `OutMessageEvent` (message, channel, bytes, count).
- **Transport:** `OnClientDataReceived/Sent`, `OnServerDataReceived/Sent`.
- **NetworkStatistics** component: client and server packets and bytes per second, sent and received.
- **PredictedRigidbody:** configuration (`mode`, `positionCorrectionThreshold`, `rotationCorrectionThreshold`, `snapThreshold`, `stateHistoryLimit`, `recordInterval`, interpolation speeds, …) and `predictedRigidbody`, the body currently simulated. The protected callbacks listed above are available to subclasses.

---

## FishNet

FishNet has public events around every reconcile and replay. That makes the resimulation graphs exact, even though it reconciles on every received state.

### What each graph reads

| Graph | Source |
|---|---|
| RESIM | `PredictionManager.OnPreReconcile` / `OnPostReconcile` |
| RESIM DEPTH | `OnPostReplicateReplay` calls between them |
| RESIM COST * | Time from `OnPreReconcile` to `OnPostReconcile` (state apply + all replays) |
| RESIM ENTITIES * | Spawned `NetworkObject`s with `EnablePrediction`. A replay runs every predicted object's replicate. |
| PREDICTION ERROR * | Local player rigidbody pose recorded in `TimeManager.OnPostTick`, keyed by `LocalTick`, vs the reconciled pose for the same tick, read in `OnPostPhysicsTransformSync`. That event only fires with TimeManager physics mode, which the demo uses. |
| CORRECTION * | Local rigidbody pose at `OnPreReconcile` vs `OnPostReconcile` |
| TICK COST * | `OnPreTick` → `OnPostTick`, minus the reconcile |
| TICK GAP * | Time between `OnPreTick` events |
| CLOCK ADJUST ~ **TICK RATE DRIFT** | FishNet does speed up and slow down the client tick, but `_adjustedTickDelta` is private. Shown instead: achieved tick rate vs `TimeManager.TickRate` over 2 s, which captures that adjustment. |
| LATENCY RTT | `TimeManager.OnRoundTripTimeUpdated` |
| INPUT RTT * | `TimeManager.OnPostTick` stamps `LocalTick` (its replicate is queued to send). `PredictionManager.OnPreReconcile(clientTick, …)` times it: the server sends each state with the last replicate tick it ran for that client. Read at reconcile, up to a tick after arrival, and throttled reconciles are not seen |
| TICK LEAD * | `TimeManager.LocalTick` − the client tick of the latest reconcile |
| SNAPSHOT AGE * | Time since `TimeManager.LastPacketTick.LastRemoteTick` advanced, checked on each `Transport.OnClientReceivedData` |
| SERVER INPUT BUFFER * | Server only, sampled after each server tick: the largest `NetworkConnection.PacketTick.RemoteTick − ReplicateTick.RemoteTick` across remote clients. That's the newest client tick received minus the client tick of the input the server last ran, i.e. inputs received but not yet simulated, the same meaning as Ursitoare's `BufferFill()`. `ReplicateTick` only advances for created replicates. FishNet stops resending unchanged input, so a client whose `ReplicateTick` hasn't moved for more than `GetMaximumServerReplicates()` server ticks counts as 0 rather than a growing false backlog. The queue itself is internal to `NetworkBehaviour`. |
| PACKET LOSS ~ **TICK GAPS (est.)** | No loss counter. Shown instead: server ticks skipped between consecutive packets. The server sends every tick while predicted objects move, so a gap usually means a lost or merged packet. |
| NET IN | `Transport.OnClientReceivedData` / `OnServerReceivedData` |
| NET OUT * | `CountingTugboat.OnDataSent`: the segments FishNet passes to `SendToServer` / `SendToClient`, as payload bytes without LiteNetLib/UDP headers. That's the same basis as NET IN (`OnClientReceivedData` / `OnServerReceivedData`). Inactive until `CountingTugboat` replaces the stock `Tugboat` on the NetworkManager (see "Wiring it up"). FishNet itself has no public way to read outgoing bytes: see "Not available". |
| VISUAL JUMP * | Ursitoare's jump test applied to `NetworkObject.GetGraphicalObject()` |
| VISUAL vs SIM * | Graphical object vs rigidbody: the tick smoother's offset |

### Not available

- **Outgoing bandwidth, from FishNet itself.** `NetworkTrafficStatistics.OnNetworkTraffic` is public, but the byte totals it carries (`BidirectionalNetworkTraffic.OutboundTraffic`) are `internal`. Tugboat's LiteNetLib `NetManager`, which counts sent bytes when `EnableStatistics` is on, is `internal` too. NET OUT therefore comes from the `CountingTugboat` subclass instead.
- The real clock adjustment value.
- The server replicate queue itself. Its depth is derived from the public per-connection ticks instead (see SERVER INPUT BUFFER).
- A real packet loss counter. `Transport.GetPacketLoss(bool)` is public and Tugboat implements it, but it reads LiteNetLib statistics that only count when `NetManager.EnableStatistics` is on. Tugboat never enables it, and the `NetManager` is `internal`, so it always returns 0 here. Don't wire it.

These exist inside FishNet but are `internal` or `private`. The substitutes above cover the clock adjustment and packet loss. The computed SERVER INPUT BUFFER covers the replicate queue, and `CountingTugboat` covers outgoing bandwidth.

### Everything FishNet exposes publicly for this

- **TimeManager:**
  - Events: `OnPreTick`, `OnTick`, `OnPostTick`, `OnPrePhysicsSimulation`, `OnPostPhysicsSimulation`, `OnUpdate`, `OnLateUpdate`, `OnFixedUpdate`, `OnRoundTripTimeUpdated`.
  - RTT: `RoundTripTime`, `HalfRoundTripTime`.
  - Ticks: `Tick`, `LocalTick`, `TickDelta`, `TickRate`, `FrameTicked`.
  - `LastPacketTick`: `LocalTick`, `RemoteTick`, `LastRemoteTick`, `LocalTickDifference()`, `IsLastRemoteTickOrdered`.
  - Other: `ServerUptime`, `ClientUptime`, `GetTickPercentAsDouble()`, `GetPhysicsTimeScale()`, tick/time conversion helpers.
- **PredictionManager** (namespace `FishNet.Managing.Predicting`):
  - Events: `OnPreReconcile`, `OnReconcile`, `OnPostReconcile`, `OnPrePhysicsTransformSync`, `OnPostPhysicsTransformSync`, `OnPreReplicateReplay`, `OnPostReplicateReplay`.
  - State: `IsReconciling`, `ClientStateTick`, `ServerStateTick`, `ClientReplayTick`, `ServerReplayTick`.
  - Settings: `StateInterpolation`, `StateOrder`, `GetMaximumServerReplicates()`.
- **Transport:** `OnClientReceivedData`, `OnServerReceivedData`, connection state events. `LatencySimulator` exposes its configured latency, loss and out-of-order settings, not measurements.
- **NetworkObject:** `EnablePrediction`, `GetGraphicalObject()`, `PredictionManager`.
- **NetworkConnection** (server side, via `ServerManager.Clients`): `PacketTick` (newest client tick received), `ReplicateTick` (client tick of the last replicate the server ran), `LocalTick`, `IsLocalClient`. Each is an `EstimatedTick` with `RemoteTick`, `LastRemoteTick`, `LocalTick` and `IsUnset`.
- **Inside your own `[Replicate]` method:** `ReplicateState` (created, replayed, future, …).
- **StatisticsManager / NetworkTrafficStatistics.OnNetworkTraffic:** fires per tick when enabled in the inspector (it's disabled by default), but the byte counts in its arguments are `internal`, and FishNet marks the event "for internal use and may change".
- **Tugboat:** `public class`, not sealed, with `public override` `SendToServer` and `SendToClient`. Subclassing it is how `CountingTugboat` observes outgoing data. `GetPacketLoss(bool)` is public, but its `NetManager` and statistics are not.

---

## PurrNet (PurrDiction)

PurrDiction exposes the most diagnostics of the four, including its own clock pacing and a set of lead and view-buffer counters. It rolls back and replays the whole predicted world on every verified server frame.

### What each graph reads

| Graph | Source |
|---|---|
| RESIM | `PredictionManager.onStartingToRollback` / `onRollbackFinished`, counted only when `tickPhaseFrameAppliesTotal + renderPhaseFrameAppliesTotal` advanced, i.e. a server frame was applied |
| RESIM DEPTH * | `onBeforePhysicsPass` calls while `isReplaying`, during the rollback |
| RESIM COST * | Time from `onStartingToRollback` to `onRollbackFinished` |
| RESIM ENTITIES * | `PredictedRigidbody` bodies in the scene, on frames that rolled back. The whole world replays. |
| PREDICTION ERROR * | Local rigidbody pose at `onBeforePhysicsPass` for each predicted tick (`localTickInContext`) vs the pose at the same point of that tick's verified pass (`isVerified`) |
| CORRECTION * | Local rigidbody pose at `onStartingToRollback` vs `onRollbackFinished` |
| TICK COST * | `NetworkManager.onPreTick` → `onPostTick`. The NetworkManager forwards both before PurrDiction's own handlers, so the forward simulation is included and the rollback is not. |
| TICK GAP * | Time between `NetworkManager.onPreTick` events |
| CLOCK ADJUST | `(PredictionManager.currentTickPacingScale − 1) × 100`. This is PurrDiction's input-slack controller, clamped to ±2%. |
| LATENCY RTT | `TickManager.rtt` |
| INPUT RTT * | The forward (non-replay) physics pass stamps `localTickInContext`. Timed when the local player's `PredictedIdentity.lastVerifiedTick` reaches that tick. PurrDiction uses one shared tick timeline, so the verified frame for tick T contains the server's simulation of the client's input for T. Read when the frame is applied, not on packet arrival |
| TICK LEAD | `PredictionManager.localTick` − the local player's `PredictedIdentity.lastVerifiedTick` |
| SNAPSHOT AGE * | Time since `PredictionManager.framesReceivedTotal` advanced |
| SERVER INPUT BUFFER ~ **INPUT SLACK** | The server's input queue is private, but the server echoes `lastInputSlackMs` back: how early the newest input arrived before it was needed. Shown in ticks on the client; late inputs are clamped to 0. |
| PACKET LOSS (%) | `StatisticsManager.packetLoss` |
| NET IN / OUT | `StatisticsManager.download` / `upload` |
| VISUAL JUMP * | Ursitoare's jump test applied to the rendered transform: `PredictedTransform.graphics`, or the first Renderer |
| VISUAL vs SIM * | Rendered transform vs rigidbody. Stays 0 in this demo because `graphics` is unassigned (see above). |
| **RTT JITTER** (extra) | `StatisticsManager.jitter` |
| **LEAD JUMP / PAUSE** (extra) | Deltas of `leadJumpsTotal + leadPausesTotal + minLeadSnapsTotal + starvationJumpsTotal`: hard corrections of the prediction head on top of the smooth pacing |
| **VIEW STARVED** (extra) | `viewBufferStarvedFramesTotal` delta: view updates that ran with an empty interpolation buffer |

### Not available

Every row has data. The only gap is the server's actual input queue depth: `_clientTicks` is private, so the row shows input slack instead. Jitter, packet loss and bandwidth need a `StatisticsManager`, which the graph adds when one is missing.

### Everything PurrDiction / PurrNet exposes publicly for this

- **PredictionManager:**
  - Ticks: `localTick`, `localTickInContext`, `tickRate`, `tickDelta`.
  - State flags: `isReplaying`, `isVerified`, `isCatchingUpFrames`, `isVerifiedView`, `isSimulating`, `isInPhysicsPass`.
  - Events: `onStartingToRollback`, `onRollbackFinished`, `onBeforePhysicsPass`, `onAfterPhysicsPass`, `onDesyncDetected`, `onLocalDesync`.
  - Frames received and sent: `framesReceivedTotal`, `fullFramesReceivedTotal`, `reliableFramesSentTotal`, `fullFramesSentTotal`, `suppressedTicksTotal`.
  - Latching: `latchCyclesTotal`, `latchTicksTotal`, `maxLatchTicks`.
  - Input sending: `inputSendsTotal`, `inputBytesSentTotal`, `inputTicksSentTotal`.
  - Delta frame sizes: `deltaSectionDeleteBits/HierarchyBits/InputBits/StateBitsTotal`, `deltaFramesWrittenTotal`, `deltaFrameBytesTotal`, `maxDeltaFrameBytes`, `fullFrameBytesTotal`.
  - Ack lag: `lastMaxAckLagTicks`.
  - Input slack and pacing: `lastInputSlackMs`, `smoothedInputSlackMs`, `currentSlackTargetMs`, `currentTickPacingScale`, `hasInputSlackFeedback`.
  - Lead corrections: `leadJumpsTotal`, `leadPausesTotal`, `minLeadSnapsTotal`, `starvationJumpsTotal`.
  - View buffer and frame application: `viewBufferTrimsTotal`, `viewBufferStarvedFramesTotal`, `renderPhaseFrameAppliesTotal`, `tickPhaseFrameAppliesTotal`, `maxFrameApplyAgeFrames`.
  - Settings: `inputRedundancyTickCount`, `desyncPolicy`.
- **PredictedIdentity:** `lastVerifiedTick`, `owner`, `isOwner`, `isController`, `currentState`, `viewState`, `verifiedState`.
- **PredictedTransform:** `graphics`, `GetViewWorldPose()`.
- **PredictedRigidbody:** `rb`, `position`, `rotation`, `linearVelocity`, `angularVelocity`, collision and trigger events.
- **TickManager:** `localTick`, `syncedTick`, `rtt`, `tickRate`, `tickDelta`, `tickPacingScale`, `lastTickTime`, `floatingPoint`, plus `onPreTick/onTick/onPostTick` and the reliable variants.
- **NetworkManager:** `onPreTick/onTick/onPostTick(asServer)`, `isServer`, `isClient`, `isHost`, `isClientOnly`.
- **StatisticsManager:**
  - Values: `ping`, `jitter`, `packetLoss`, `upload`, `download`.
  - Flags: `hasPingEstimate`, `isHighPing`, `isHighJitter`, `isHighPacketLoss`, `isConnectionStalled`.
  - Events: `onHighPingChanged`, `onHighJitterChanged`, `onHighPacketLossChanged`, `onConnectionStalledChanged`.
- **TickBandwidthProfiler:** per-tick lists of states and inputs written and read, with bit counts (`wroteStates`, `readStates`, `wroteInputs`, `readInputs`), and an `onTickEnded` event.

---

## Status

- All four `ResimGraph` scripts compile without errors against their own project's assemblies. They were compiled with Unity 6000.3.7f1's Roslyn compiler, using each project's `Assembly-CSharp.csproj` references and defines.
- They **have not been run in the editor yet.** The first run should confirm three things:
  - the graph appears and fills on a client-only instance;
  - the Console support table matches the matrix above;
  - in Mirror, the correction rows react when you bump into a ball.
- No scene or prefab was changed. The graph creates itself at startup (see *Wiring it up*).
