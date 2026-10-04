using System.Collections.Generic;
using System.Threading;
using Mirror;
using UnityEngine;
using Prediction;
using Prediction.Components.Controllers;
using Prediction.Data;
using Prediction.Interpolation;
using Prediction.Resimulation.Detection;

namespace DefaultNamespace
{
    public class NetworkPredictionManagerAdapter : NetworkBehaviour
    {
        public static NetworkPredictionManagerAdapter instance;
        public static bool DEBUG = false;
        public static bool MSG_DEBUG = false;
        
        PredictionManager predictionManager;
        //TODO: remove the need for these 2 instances and use closures for handling messages from the server
        ClientPredictionManager _clientPredictionManager;
        ServerPredictionManager _serverPredictionManager;
        public bool hasClientPredManager;
        public bool hasServerPredManager;
        
        public LatencySimulation latencySimulation;
        public bool useUpdateLoop = false;
        public bool useGameTime;

        [SerializeField] private int InvalidConnectionId = -1;
        [SerializeField] private int ServerConnectionId = 0;
        [SerializeField] private PredictionDemoConfig config = new PredictionDemoConfig();

        public PredictionManager GetPredictionManager()
        {
            return predictionManager;
        }

        public void SetSendRateMultiplier(int frequency)
        {
            NetworkManager.singleton.sendRate = frequency;
        }
        
        public override void OnStartServer()
        {
            Setup();
        }
        
        public override void OnStartClient()
        {
            //TODO: will this run before all other OnStartClient calls from GameObjects?
            Setup();
        }

        void Setup()
        {
            if (instance)
                return;
            
            Debug.Log($"[NetworkPredictionManagerAdapter][Setup] isServer:{isServer} isClient:{isClient}");
            Debug.Log($"[NetworkPredictionManagerAdapter] AppPath:{Application.dataPath}");
            Debug.Log($"[NetworkPredictionManagerAdapter] PdPath:{Application.persistentDataPath}");

            instance = this;
            latencySimulation = GameObject.FindAnyObjectByType<LatencySimulation>();
            if (!isServer)
            {
                Debug.Log($"[PredictionMirrorBridge][clientStateSender] SETUP CLIENT SENDER CALLBACK");
                _clientPredictionManager = new ClientPredictionManager((tickId) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log(
                            $"[PredictionMirrorBridge][clientHeartbeadSender] SEND client_heartbeat tickId:{tickId}");
                    ReportHeartbeat(tickId);
                },(tickId, entityId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][clientStateSender] SEND client_report: tickId:{tickId} entityId:{entityId} data:{data}");
                    
                    ReportToServerUnreliable(tickId, entityId, data);
                });
                _clientPredictionManager.onTickStat.AddEventListener(OnTickStat);
                _clientPredictionManager.onPacketLoss.AddEventListener(OnPacketLoss);
                PredictedEntityVisuals.onLargeTransformJumpGlobal.AddEventListener(OnLargeTransformJump);
                predictionManager = _clientPredictionManager;
                hasClientPredManager = true;
            }
            else
            {
                Debug.Log($"[PredictionMirrorBridge][clientStateSender] SETUP SERVER SEND CALLBACK");
                _serverPredictionManager = new ServerPredictionManager(InvalidConnectionId, ServerConnectionId, (connId, entityId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][clientStateSender] SEND server_report: netId:{entityId} tickId:{data.tickId} data:{data}");

                    NetworkConnectionToClient netconn = GetNetConn(connId);
                    if (netconn != null)
                    {
                        TargetedReportFromServerUnreliable(netconn, entityId, data);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                }, (connId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][serverWorldStateSender] SEND server_world_report: connId:{connId} data:{data}");
                    
                    NetworkConnectionToClient netconn = GetNetConn(connId);
                    if (netconn != null)
                    {
                        TargetedWorldReportFromServerUnreliable(netconn, data);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                }, (connId, entityId, owned) =>
                {
                    NetworkConnectionToClient netconn = GetNetConn(connId);
                    Debug.Log($"[NetworkPredictionManagerAdapter][reliableServerSetControlledLocally] connId:{connId} entityId:{entityId} owned:{owned} owned:{owned} netconn:{netconn}");
                    if (netconn != null)
                    {
                        UpdateLocalOwnership(netconn, entityId, owned);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                }, () => NetworkServer.connections.Keys);
                predictionManager = _serverPredictionManager;
                hasServerPredManager = true;
            }
            
            predictionManager.SetPhysicsController(new RewindablePhysicsController2(120));
            ApplyConfig();
        }

        void ApplyConfig()
        {
            Time.fixedDeltaTime = 1f / config.SimulationHz;
            Application.targetFrameRate = config.RenderingHz;
            SetSendRateMultiplier(config.NetworkHz);
            QualitySettings.vSyncCount = config.vSync;
            
            //NOTE: ServerPredictedEntity reads CATCHUP_SECTIONS in its constructor, so entities created before this keep the old value.
            ServerPredictedEntity.USE_BUFFERING = config.server_use_buffering;
            ServerPredictedEntity.BUFFER_FULL_THRESHOLD = config.server_buffer_size;
            ServerPredictedEntity.CATCHUP = config.server_catchup;
            ServerPredictedEntity.CATCHUP_SECTIONS = config.server_catchup_sections;
            ServerPredictedEntity.INCREMENT_TICK_WHEN_NO_INPUT = config.server_increment_ticks;
            ServerPredictedEntity.APPLY_FORCES_TO_EACH_CATCHUP_INPUT = false;

            PredictionManager.DO_RESIM = config.resimulate;
            PredictionManager.DO_SNAP = config.snap;
            PredictionManager.Instance.protectFromOversimulation = config.oversim_protect;
            PredictionManager.Instance.oversimProtectWithTickInterval = config.oversim_protect_with_tick_interval;
            PredictionManager.Instance.minTicksBetweenResims = config.oversim_min_ticks_between;
            PredictionManager.Instance.maxTickResimulationCount = config.max_tick_resim_count;
            PredictionManager.SNAPSHOT_INSTANCE_RESIM_CHECKER = new SimpleConfigurableResimulationDecider(
                config.dist_tres, config.rot_tres, config.velo_tres, config.avelo_tres);
            PredictionManager.ROUND_TRIP_GETTER = () => NetworkTime.rtt;
            PredictionManager.PREDICT_FOLLOWERS = config.predict_followers;
            //FOLLOWERS
            //NOTE: entities pick up FOLLOWER_INSTANCE_RESIM_CHECKER when they register, so entities registered before this keep the old one.
            ClientPredictedEntity.APPLY_SERVER_INPUT_TO_FOLLOWERS = config.client_apply_server_input_to_followers;
            PredictionManager.RESIMULATE_FOLLOWERS_SQR_DISTANCE_THRESHOLD = Mathf.Pow(config.resim_followers_distance_treshold, 2);
            PredictionManager.RESIMULATE_PRECISE_FOLLOWERS_SQR_DISTANCE_THRESHOLD = Mathf.Pow(config.precise_resim_followers_distance_treshold, 2);
            PredictionManager.FOLLOWER_INSTANCE_RESIM_CHECKER = new SimpleConfigurableResimulationDecider(
                config.follower_dist_tres, config.follower_rot_tres, config.follower_velo_tres, config.follower_avelo_tres);

            //LOGGING
            //NOTE: PosAnalyser (every visual frame), LATE_ADD and TIME_PAST_END_OF_BFR in the interpolators log without a flag and can't be turned off here.
            PredictionManager.DEBUG = config.library_logging;
            PredictionManager.DEBUG_OWNERSHIP = config.library_logging;
            ClientPredictedEntity.LOG_ADDED_SERVER_STATES = config.library_logging;
            ClientPredictedEntity.LOG_RESIMULATION_STEPS = config.library_logging;
            MovingAverageInterpolator.DEBUG = config.library_logging;
            MovingAverageInterpolator.LOG_POS = config.library_logging;
            Adapters.Prediction.CustomVisualInterpolator.DEBUG = config.library_logging;
            Adapters.Prediction.CustomVisualInterpolator.LOG_POS = config.library_logging;

            Debug.Log($"[NetworkPredictionManagerAdapter][ApplyConfig] sim:{config.SimulationHz}Hz render:{config.RenderingHz}Hz net:{config.NetworkHz}Hz buffer:{config.server_buffer_size} catchupSections:{config.server_catchup_sections} resimChecker:{PredictionManager.SNAPSHOT_INSTANCE_RESIM_CHECKER} predict_followers:{PredictionManager.PREDICT_FOLLOWERS}");
        }

        private NetworkConnectionToClient GetNetConn(int connId)
        {
            if (connId == 0)
                return null;
            return NetworkServer.connections.GetValueOrDefault(connId, null);
        }

        private void FixedUpdate()
        {
            if (DEBUG)
                Debug.Log($"[NetworkPredictionManagerAdapter][Tick] t:{predictionManager.GetTickId()} time:{Time.realtimeSinceStartup} uul:{useUpdateLoop} mdt:{Time.maximumDeltaTime}");
            if (!useUpdateLoop)
            {
                predictionManager.Tick();
            }
        }

        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportHeartbeat(uint tickId, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportHeartbeat] RECV client_heartbeat: tickId:{tickId} sender:{sender}");
            _serverPredictionManager.OnHeartbeatReceived(sender.connectionId, tickId);
        }
        
        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportToServerUnreliable(uint tickId, uint entityId, PredictionInputRecord data, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportToServerUnreliable] RECV client_report: tickId:{tickId} entityId:{entityId} sender:{sender} data:{data}");
            _serverPredictionManager.OnClientStateReceived(sender.connectionId, tickId, entityId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedReportFromServerUnreliable(NetworkConnectionToClient receiver, uint entityNetId, PhysicsStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedReportFromServerUnreliable] RECV serrver_report: netId:{entityNetId} tickId:{data.tickId} data:{data}");
            _clientPredictionManager.OnServerStateReceived(entityNetId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedWorldReportFromServerUnreliable(NetworkConnectionToClient receiver, WorldStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedWorldReportFromServerUnreliable] RECV server_report: data:{data}");
            _clientPredictionManager.OnServerWorldStateReceived(data);
        }
        
        [TargetRpc(channel = Channels.Reliable)]
        void UpdateLocalOwnership(NetworkConnectionToClient receiver, uint entityId, bool owned)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][UpdateLocalOwnership] Received server_ownership_report: entity:{entityId} owned:{owned}");
            _clientPredictionManager.OnEntityOwnershipChanged(entityId, owned);
        }
        

        void OnPacketLoss(int lostCount)
        {
            Debug.Log($"[NetworkPredictionManagerAdapter][WARNING][PACKET_LOSS] tickId:{PredictionManager.Instance.GetTickId()} lost:{lostCount}");        
        }
        
        void OnTickStat(PredictionManager.TickStat tickStat)
        {
            if (tickStat.duration >= Time.fixedDeltaTime * 0.75f)
            {
                Debug.Log($"[NetworkPredictionManagerAdapter][WARNING][HEAVY_TICK] tickId:{tickStat.tickId} tickTime:{tickStat.duration} FixedDTime:{Time.fixedDeltaTime} resimDuration:{tickStat.resimDuration} resimmedTicks:{tickStat.resimTicks}");        
            }
        }

        void OnLargeTransformJump(PredictedEntityVisuals.GlobalTransformJump transformJump)
        {
            Debug.Log($"[NetworkPredictionManagerAdapter][WARNING][LARGE_VISUAL_TRANSFORM_JUMP] tickId:{predictionManager.GetTickId()} pos:{transformJump.jump.positionDiff}|({transformJump.jump.positionDiff.magnitude}) rot:{transformJump.jump.rotationDiff.eulerAngles}");
        }

        private float timeSincePredTick = 0;
        private float lastWallClockUpdate = 0;
        void Update()
        {
            if (DEBUG) 
                Debug.Log($"[Prediction][Update] t:{Time.realtimeSinceStartup} uul:{useUpdateLoop} mdt:{Time.maximumDeltaTime}");

            if (useUpdateLoop)
            {
                float deltaWallClock = Time.realtimeSinceStartup - lastWallClockUpdate;
                if (useGameTime)
                {
                    timeSincePredTick += Time.deltaTime;
                }
                else
                {
                    timeSincePredTick += deltaWallClock;
                }
                
                if (timeSincePredTick >= Time.fixedDeltaTime)
                {
                    timeSincePredTick -= Time.fixedDeltaTime;
                    predictionManager.Tick();
                }
                
                lastWallClockUpdate = Time.realtimeSinceStartup;
            }
            
            /*
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.P))
            {
                PredictionManager.PREDICTION_ENABLED = !PredictionManager.PREDICTION_ENABLED;
            }

            //TODO: remove once the disconnect experiments are done
            if (isServer && Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.X))
            {
                TestForceClientDisconnectViaRpcException();
            }

            //TODO: remove this method once testing is done
            if (Input.GetKeyDown(KeyCode.Home))
            {
                PredictedEntityVisuals.SHOW_DBG = !PredictedEntityVisuals.SHOW_DBG;
            }
            
            if (latencySimulation)
            {
                //TODO: control more of the latency sim properties here.
                if (Input.GetKey(KeyCode.L) && Input.GetKeyDown(KeyCode.Alpha0))
                {
                    latencySimulation.latency = 0;
                }
                if (Input.GetKey(KeyCode.L) && Input.GetKeyDown(KeyCode.Alpha1))
                {
                    latencySimulation.latency = 20;
                }
                if (Input.GetKey(KeyCode.L) && Input.GetKeyDown(KeyCode.Alpha2))
                {
                    latencySimulation.latency = 35;
                }
                if (Input.GetKey(KeyCode.L) && Input.GetKeyDown(KeyCode.Alpha3))
                {
                    latencySimulation.latency = 52;
                }
                if (Input.GetKey(KeyCode.L) && Input.GetKeyDown(KeyCode.Alpha4))
                {
                    latencySimulation.latency = 100;
                }
                if (Input.GetKey(KeyCode.L) && Input.GetKeyDown(KeyCode.Alpha5))
                {
                    latencySimulation.latency = 150;
                }
            
                if (Input.GetKey(KeyCode.J) && Input.GetKeyDown(KeyCode.Alpha0))
                {
                    latencySimulation.jitter = 0;
                }
                if (Input.GetKey(KeyCode.J) && Input.GetKeyDown(KeyCode.Alpha1))
                {
                    latencySimulation.jitter = 0.01f;
                }
                if (Input.GetKey(KeyCode.J) && Input.GetKeyDown(KeyCode.Alpha2))
                {
                    latencySimulation.jitter = 0.02f;
                }
                if (Input.GetKey(KeyCode.J) && Input.GetKeyDown(KeyCode.Alpha3))
                {
                    latencySimulation.jitter = 0.1f;
                }
                if (Input.GetKey(KeyCode.J) && Input.GetKeyDown(KeyCode.Alpha4))
                {
                    latencySimulation.jitter = 0.2f;
                }
                
                if (Input.GetKey(KeyCode.D) && Input.GetKeyDown(KeyCode.Alpha0))
                {
                    latencySimulation.unreliableLoss = 0;
                    latencySimulation.unreliableScramble = 0;
                }
                if (Input.GetKey(KeyCode.D) && Input.GetKeyDown(KeyCode.Alpha1))
                {
                    latencySimulation.unreliableLoss = 1f;
                    latencySimulation.unreliableScramble = 1f;
                }
                if (Input.GetKey(KeyCode.D) && Input.GetKeyDown(KeyCode.Alpha2))
                {
                    latencySimulation.unreliableLoss = 2f;
                    latencySimulation.unreliableScramble = 2f;
                }
                if (Input.GetKey(KeyCode.D) && Input.GetKeyDown(KeyCode.Alpha3))
                {
                    latencySimulation.unreliableLoss = 5f;
                    latencySimulation.unreliableScramble = 5f;
                }
                if (Input.GetKey(KeyCode.D) && Input.GetKeyDown(KeyCode.Alpha4))
                {
                    latencySimulation.unreliableLoss = 10f;
                    latencySimulation.unreliableScramble = 10f;
                }
            }

            // Freeze main thread (F+digit): simulate a hard freeze for testing prediction recovery
            // F+1=100ms  F+2=250ms  F+3=500ms  F+4=1000ms
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha1))
            {
                FreezeMainThread(50);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha2))
            {
                FreezeMainThread(100);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha3))
            {
                FreezeMainThread(200);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha4))
            {
                FreezeMainThread(350);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha5))
            {
                FreezeMainThread(500);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha6))
            {
                FreezeMainThread(1500);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha7))
            {
                FreezeMainThread(5000);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha8))
            {
                FreezeMainThread(10000);
            }
            if (Input.GetKey(KeyCode.F) && Input.GetKeyDown(KeyCode.Alpha9))
            {
                FreezeMainThread(25000);
            }
            */
        }

        void FreezeMainThread(int milliseconds)
        {
            string role = isServer ? "Server" : "Client";
            Debug.Log($"[Freeze] {role} freezing for {milliseconds}ms");
            Thread.Sleep(milliseconds);
            Debug.Log($"[Freeze] {role} unfrozen after {milliseconds}ms");
        }

        void OnDestroy()
        {
            instance = null;
            if (predictionManager != null)
            {
                predictionManager.onTickStat.RemoveEventListener(OnTickStat);
                predictionManager.onPacketLoss.RemoveEventListener(OnPacketLoss);
            }
            PredictedEntityVisuals.onLargeTransformJumpGlobal.RemoveEventListener(OnLargeTransformJump);
        }

        /// <summary>
        /// TEST ONLY: sends an RPC that throws on every client, including the host's local client,
        /// to reproduce Mirror dropping a connection over an exception in an RPC handler.
        /// Run it with LeftCtrl+LeftShift+X, or from the component's context menu in the Inspector, while hosting.
        /// </summary>
        [ContextMenu("TEST: Throw In RPC On All Clients")]
        public void TestForceClientDisconnectViaRpcException()
        {
            if (!isServer)
            {
                Debug.LogWarning("[NetworkPredictionManagerAdapter][TestForceClientDisconnectViaRpcException] Only the server can send this RPC.");
                return;
            }

            Debug.LogWarning("[NetworkPredictionManagerAdapter][TestForceClientDisconnectViaRpcException] Sending throwing RPC to all clients.");
            RpcThrowForDisconnectTest();
        }

        [ClientRpc]
        void RpcThrowForDisconnectTest()
        {
            string role = isServer ? "Host" : "Client";
            Debug.LogWarning($"[NetworkPredictionManagerAdapter][RpcThrowForDisconnectTest] {role} throwing on purpose.");
            throw new System.InvalidOperationException($"[NetworkPredictionManagerAdapter][RpcThrowForDisconnectTest] Intentional test exception on {role}.");
        }
    }
}