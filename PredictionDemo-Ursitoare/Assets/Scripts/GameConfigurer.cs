using Mirror;
using Sector0.Ursitoare;
using Sector0.Ursitoare.Components;
using Sector0.Ursitoare.Interpolation;
using Sector0.Ursitoare.Resimulation.Detection;
using Sector0.UrsitoareMirror;
using Telepathy;
using UnityEngine;

namespace DefaultNamespace
{
    public class GameConfigurer : NetworkBehaviour
    {
        public static bool DEBUG = false;
        public static GameConfigurer instance;
      
        public LatencySimulation latencySimulation;

        [SerializeField] private NetworkPredictionManagerAdapter predictionManager;
        [SerializeField] private PredictionDemoConfig config = new PredictionDemoConfig();
        public PredictionDemoConfig Config => config;
            
        void Awake()
        {
            predictionManager.onReady.AddEventListener(OnReady);
        }

        void OnDestroy()
        {
            predictionManager.onReady.RemoveEventListener(OnReady);
        }

        void OnReady(bool ignore)
        {
            ApplyConfig();
        }
        
        public void SetSendRateMultiplier(int frequency)
        {
            NetworkManager.singleton.sendRate = frequency;
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

            ClientPredictionManager.DO_SNAP = config.snap;
            PredictionManager.Instance.protectFromOversimulation = config.oversim_protect;
            PredictionManager.Instance.oversimProtectWithTickInterval = config.oversim_protect_with_tick_interval;
            PredictionManager.Instance.minTicksBetweenResims = config.oversim_min_ticks_between;
            PredictionManager.Instance.maxTickResimulationCount = config.max_tick_resim_count;
            PredictionManager.SNAPSHOT_INSTANCE_RESIM_CHECKER = new SimpleConfigurableResimulationDecider(
                config.dist_tres, config.rot_tres, config.velo_tres, config.avelo_tres);
            PredictionManager.ROUND_TRIP_GETTER = () => NetworkTime.rtt;
            ClientPredictionManager.PREDICT_FOLLOWERS = config.predict_followers;
            //FOLLOWERS
            //NOTE: entities pick up FOLLOWER_INSTANCE_RESIM_CHECKER when they register, so entities registered before this keep the old one.
            ClientPredictedEntity.APPLY_SERVER_INPUT_TO_FOLLOWERS = config.client_apply_server_input_to_followers;
            ClientPredictionManager.RESIMULATE_FOLLOWERS_SQR_DISTANCE_THRESHOLD = Mathf.Pow(config.resim_followers_distance_treshold, 2);
            ClientPredictionManager.RESIMULATE_PRECISE_FOLLOWERS_SQR_DISTANCE_THRESHOLD = Mathf.Pow(config.precise_resim_followers_distance_treshold, 2);
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
            CustomVisualInterpolator.DEBUG = config.library_logging;
            CustomVisualInterpolator.LOG_POS = config.library_logging;

            Debug.Log($"[NetworkPredictionManagerAdapter][ApplyConfig] sim:{config.SimulationHz}Hz render:{config.RenderingHz}Hz net:{config.NetworkHz}Hz buffer:{config.server_buffer_size} catchupSections:{config.server_catchup_sections} resimChecker:{PredictionManager.SNAPSHOT_INSTANCE_RESIM_CHECKER} predict_followers:{ClientPredictionManager.PREDICT_FOLLOWERS}");
        }
    }
}