using FishNet.Managing;
using UnityEngine;

namespace DefaultNamespace
{
    // Counterpart of the Ursitoare demo's PredictionDemoConfig/ApplyConfig: puts the timing on the same values
    // as AntShipWars' MasterConfig.json (120 Hz simulation and rendering).
    //
    // FishNet has no separate send rate: replicates and reconciles go out once per tick, so tickRate covers both
    // SimulationHz and NetworkHz.
    //
    // Runs right after NetworkManager.Awake (short.MinValue), which initializes the TimeManager from the inspector
    // value; SetTickRate here overrides it before any connection starts. The tick rate is not synchronized,
    // so every build (server and clients) needs this component.
    [DefaultExecutionOrder(short.MinValue + 1)]
    public class FishNetDemoConfig : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;

        //TIMING
        [SerializeField] private ushort tickRate = 120;
        [SerializeField] private int renderingHz = 120;
        [SerializeField] private int vSyncCount = 0;

        //APPLICATION
        [SerializeField] private bool runInBackground = true;

        private void Awake()
        {
            if (!networkManager)
                networkManager = FindAnyObjectByType<NetworkManager>();

            if (!networkManager || !networkManager.TimeManager)
            {
                Debug.LogError("[FishNetDemoConfig][Awake] No initialized NetworkManager found, config not applied.", this);
                return;
            }

            ApplyConfig();
        }

        void ApplyConfig()
        {
            networkManager.TimeManager.SetTickRate(tickRate);
            //NOTE: TimeManager only copies TickDelta into fixedDeltaTime during its own init, keep them in step.
            Time.fixedDeltaTime = 1f / tickRate;

            QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = renderingHz;
            Application.runInBackground = runInBackground;

            Debug.Log($"[FishNetDemoConfig][ApplyConfig] tick:{networkManager.TimeManager.TickRate}Hz physics:{networkManager.TimeManager.PhysicsMode} render:{renderingHz}Hz vSync:{vSyncCount} runInBackground:{runInBackground}");
        }
    }
}
