# Network send rate vs simulation rate

Can each library send over the network less often than it simulates, for example simulating at 120 Hz and sending at 60 Hz?

| Library | Separate send rate built in? | How the demo sets it |
|---|---|---|
| Ursitoare | **Yes** | `SimulationHz` sets `Time.fixedDeltaTime`; `NetworkHz` sets Mirror's `sendRate` ([NetworkPredictionManagerAdapter.cs#L142-L144](PredictionDemo-Ursitoare/Assets/Scripts/NetworkPredictionManagerAdapter.cs#L142-L144)) |
| Mirror | **Yes** | `simulationHz` sets `Time.fixedDeltaTime`; `networkHz` sets `NetworkManager.sendRate` ([MirrorDemoConfig.cs#L41-L42](PredictionDemo-Mirror/Assets/Scripts/MirrorDemoConfig.cs#L41-L42)) |
| FishNet 4.7.3 | **No** | One `tickRate` covers both ([FishNetDemoConfig.cs#L44-L46](PredictionDemo-FishNet/Assets/Scripts/FishNetDemoConfig.cs#L44-L46)) |
| PurrNet 1.23.0-beta.51 + PurrDiction 1.3.3 | **No** | One `tickRate` covers both ([PurrNetDemoConfig.cs#L42](PredictionDemo-PurrNet/Assets/Scripts/PurrNetDemoConfig.cs#L42)) |

In FishNet and PurrNet, one tick is one simulation step and one network send. Neither has a setting that lowers the send rate in both directions, client to server and server to client, while keeping the simulation rate. Getting there needs a change to the networking layer, either a transport wrapper or an edit to the library (see [Workaround](#workaround-gating-the-transport-flush)).

The links below point at the copies of each library in this repository. The line numbers match the versions listed above.

## FishNet 4.7.3

### The simulation rate is the tick rate

- `TimeManager` has a single `_tickRate` ([TimeManager.cs#L184-L191](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs#L184-L191)).
- With `PhysicsMode.TimeManager`, physics steps once per tick with `tickDelta` ([TimeManager.cs#L743-L748](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs#L743-L748)).

### Every tick ends with a send, in both directions

Each tick of the `TimeManager` loop runs, in order ([TimeManager.cs#L722-L767](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs#L722-L767)):

1. `OnTick`, where user code runs replicates.
2. `OnPostTick`, where user code creates reconciles.
3. `PredictionManager.SendStateUpdate()`, which writes the reconcile state for each client ([TimeManager.cs#L754](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs#L754), [PredictionManager.cs#L739](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Prediction/PredictionManager.cs#L739)).
4. `TryIterateData(false)`, under the comment `// Send out data.` ([TimeManager.cs#L765-L767](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs#L765-L767)).

`TryIterateData(false)` flushes outgoing data for the server and the client, every time it is called ([TimeManager.cs#L1118-L1122](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs#L1118-L1122)). Its comment says outgoing "may iterate multiple times per frame due to there possibly being multiple ticks per frame" ([TimeManager.cs#L1102-L1109](PredictionDemo-FishNet/Assets/FishNet/Runtime/Managing/Timing/TimeManager.cs#L1102-L1109)).

On the client, the owner writes its input to the server in the same tick it is created ([NetworkBehaviour.Prediction.cs#L578-L586](PredictionDemo-FishNet/Assets/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs#L578-L586)). There is no option to delay it or send it every Nth tick. The only thing that stops the sends is when the input hasn't changed and the resend count has run out.

### The partial controls that do exist

These lower the rate for single features. None of them lowers the overall send rate.

- **Sending reconciles less often (server to client only).** The server only sends a reconcile on ticks where user code calls it ([NetworkBehaviour.Prediction.cs#L1248-L1253](PredictionDemo-FishNet/Assets/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs#L1248-L1253)). The client fills the gaps from local history, and FishNet names this use case: "if the player is throttling reconciles" ([NetworkBehaviour.Prediction.cs#L1278-L1281](PredictionDemo-FishNet/Assets/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs#L1278-L1281)). Client inputs and the per-tick flush are not affected.
- **`NetworkTransform` Send Interval.** "How often in ticks to synchronize", per component ([NetworkTransform.cs#L440-L444](PredictionDemo-FishNet/Assets/FishNet/Runtime/Generated/Component/NetworkTransform/NetworkTransform.cs#L440-L444), [NetworkTransform.cs#L939-L950](PredictionDemo-FishNet/Assets/FishNet/Runtime/Generated/Component/NetworkTransform/NetworkTransform.cs#L939-L950)). This applies only to that component's transform sync, not to prediction.

## PurrNet 1.23.0-beta.51 + PurrDiction 1.3.3

### The simulation rate is the tick rate

- `NetworkManager` has a single `_tickRate` ([NetworkManager.cs#L82](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Managers/NetworkManager.cs#L82), [NetworkManager.cs#L125-L142](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Managers/NetworkManager.cs#L125-L142)).
- PurrDiction takes its step size from it, `tickDelta = 1f / tickRate` ([PredictionManager.cs#L244-L245](PredictionDemo-PurrNet/Assets/PurrDiction/Runtime/Core/PredictionManager.cs#L244-L245)). It also writes that into `Time.fixedDeltaTime` when Unity physics is used ([PredictionManager.cs#L280-L284](PredictionDemo-PurrNet/Assets/PurrDiction/Runtime/Core/PredictionManager.cs#L280-L284)).

### Every tick ends with a send, in both directions

- **Transport flush.** `NetworkManager.OnTick` receives messages, runs the module callbacks, and then always calls `SendMessagesNow(delta)` ([NetworkManager.cs#L1802-L1862](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Managers/NetworkManager.cs#L1802-L1862)). That calls `ITransport.SendMessages` ([NetworkManager.cs#L1790-L1800](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Managers/NetworkManager.cs#L1790-L1800)), which is where the UDP transport pushes everything queued onto the wire ([UDPTransport.cs#L354-L363](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Transports/UDPTransport.cs#L354-L363)).
- **Server state.** At the end of every simulated tick, the server calls `SendFrameToOthers()` with no condition ([PredictionManager.cs#L868-L877](PredictionDemo-PurrNet/Assets/PurrDiction/Runtime/Core/PredictionManager.cs#L868-L877)). That sends the prepared frame for that tick to every client ([PredictionManager.cs#L1754-L1816](PredictionDemo-PurrNet/Assets/PurrDiction/Runtime/Core/PredictionManager.cs#L1754-L1816)).
- **Client input.** The client's only timing setting for input is a hard-coded minimum gap between resends of the cached input, `InputResendIntervalSeconds = 0.02` ([PredictionManager.cs#L938](PredictionDemo-PurrNet/Assets/PurrDiction/Runtime/Core/PredictionManager.cs#L938), [PredictionManager.cs#L993-L1001](PredictionDemo-PurrNet/Assets/PurrDiction/Runtime/Core/PredictionManager.cs#L993-L1001)). It is a `const`, not a send-rate setting.

### The partial controls that do exist

PurrNet's per-feature intervals don't apply to PurrDiction's prediction traffic:

- `SyncVar`/`SyncList`/`SyncArray`/`SyncDictionary` `sendIntervalInSeconds` ([SyncVar.cs#L26-L35](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Components/NetworkModule/SyncVar.cs#L26-L35))
- `NetworkTransform` `maxSendInterval` ([NetworkTransformSyncStrategy.cs#L48](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Components/NetworkBehaviour/NetworkTransformSyncStrategy.cs#L48))
- Network LOD `sendIntervalTicks` ([NetworkLODProfile.cs#L16](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Components/NetworkLOD/NetworkLODProfile.cs#L16))

## Workaround: gating the transport flush

This needs no edit to either library. In both, network writes made during a tick sit in the transport's queue until the per-tick flush call above:

- **FishNet / Tugboat.** `SendToServer`/`SendToClient` add the data to a queue ([ClientSocket.cs#L261-L267](PredictionDemo-FishNet/Assets/FishNet/Runtime/Transporting/Transports/Tugboat/Core/ClientSocket.cs#L261-L267)). Only `IterateOutgoing` sends it ([Tugboat.cs#L232-L238](PredictionDemo-FishNet/Assets/FishNet/Runtime/Transporting/Transports/Tugboat/Tugboat.cs#L232-L238), [ClientSocket.cs#L219-L222](PredictionDemo-FishNet/Assets/FishNet/Runtime/Transporting/Transports/Tugboat/Core/ClientSocket.cs#L219-L222), [ServerSocket.cs#L421](PredictionDemo-FishNet/Assets/FishNet/Runtime/Transporting/Transports/Tugboat/Core/ServerSocket.cs#L421)).
- **PurrNet / UDPTransport.** LiteNetLib runs in manual mode ([UDPTransport.cs#L380](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Transports/UDPTransport.cs#L380), [UDPTransport.cs#L409](PredictionDemo-PurrNet/Assets/PurrNet/Runtime/Transports/UDPTransport.cs#L409)). `peer.Send` adds the packet to a pending list ([LiteNetPeer.cs#L394-L398](PredictionDemo-PurrNet/Assets/PurrNet/Externals/LiteNetLib/LiteNetPeer.cs#L394-L398)), which goes out on `ManualUpdate` from `SendMessages`.

A pass-through transport that wraps Tugboat or UDPTransport could forward every call but let the flush through only every Nth tick. With the tick rate at 120 and N = 2, the server and every client would send at 60 Hz. Caveats:

- **Bytes per second barely drop.** Both libraries still write one input or state per tick, so each send carries N of them. Packet count falls; the amount of data doesn't.
- **The library's timing systems will react.** Data arrives in groups of N ticks, which FishNet's tick adjustment and PurrDiction's input-lead controller will see as jitter. They will probably grow their buffers to absorb it.
- **Not verified.** This workaround has been checked by reading the code only, not run.
