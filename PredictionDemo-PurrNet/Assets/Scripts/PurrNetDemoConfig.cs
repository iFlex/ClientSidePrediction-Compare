using PurrNet;
using UnityEngine;

namespace DefaultNamespace
{
    // Counterpart of the Ursitoare demo's PredictionDemoConfig/ApplyConfig: puts the timing on the same values
    // as AntShipWars' MasterConfig.json (120 Hz simulation and rendering).
    //
    // PurrNet has no separate send rate: state and input go out once per tick, so tickRate covers both
    // SimulationHz and NetworkHz. PurrDiction derives Time.fixedDeltaTime from the tick rate itself.
    //
    // Runs before NetworkManager.Awake (-999) because the tick rate cannot change once a tick manager exists.
    [DefaultExecutionOrder(-1000)]
    public class PurrNetDemoConfig : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;

        //TIMING
        [SerializeField] private int tickRate = 120;
        [SerializeField] private int renderingHz = 120;
        [SerializeField] private int vSyncCount = 0;

        //APPLICATION
        [SerializeField] private bool runInBackground = true;

        private void Awake()
        {
            if (!networkManager)
                networkManager = FindAnyObjectByType<NetworkManager>();

            if (!networkManager)
            {
                Debug.LogError("[PurrNetDemoConfig][Awake] No NetworkManager found, config not applied.", this);
                return;
            }

            ApplyConfig();
        }

        void ApplyConfig()
        {
            networkManager.tickRate = tickRate;
            QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = renderingHz;
            Application.runInBackground = runInBackground;

            Debug.Log($"[PurrNetDemoConfig][ApplyConfig] tick:{networkManager.tickRate}Hz render:{renderingHz}Hz vSync:{vSyncCount} runInBackground:{runInBackground}");
        }
    }
}
