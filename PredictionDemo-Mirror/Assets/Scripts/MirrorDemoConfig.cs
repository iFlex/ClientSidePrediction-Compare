using Mirror;
using UnityEngine;

// Counterpart of the Ursitoare demo's PredictionDemoConfig/ApplyConfig: puts the timing on the same values
// as AntShipWars' MasterConfig.json (120 Hz physics and rendering, 60 Hz network).
//
// Mirror's PredictedRigidbody records and corrects in FixedUpdate with Unity stepping physics itself,
// so the simulation rate is Time.fixedDeltaTime. Mirror's sendRate is the network rate (state broadcasts).
//
// Runs before NetworkManager.Awake so the sendRate is in place when Mirror applies its configuration.
[DefaultExecutionOrder(-1000)]
public class MirrorDemoConfig : MonoBehaviour
{
    [SerializeField] private NetworkManager networkManager;

    //TIMING
    [SerializeField] private int simulationHz = 120;
    [SerializeField] private int renderingHz = 120;
    [SerializeField] private int networkHz = 60;
    [SerializeField] private int vSyncCount = 0;

    //APPLICATION
    [SerializeField] private bool runInBackground = true;

    private void Awake()
    {
        if (!networkManager)
            networkManager = FindAnyObjectByType<NetworkManager>();

        if (!networkManager)
        {
            Debug.LogError("[MirrorDemoConfig][Awake] No NetworkManager found, config not applied.", this);
            return;
        }

        ApplyConfig();
    }

    void ApplyConfig()
    {
        Time.fixedDeltaTime = 1f / simulationHz;
        networkManager.sendRate = networkHz;

        QualitySettings.vSyncCount = vSyncCount;
        Application.targetFrameRate = renderingHz;
        Application.runInBackground = runInBackground;
        networkManager.runInBackground = runInBackground;

        Debug.Log($"[MirrorDemoConfig][ApplyConfig] physics:{simulationHz}Hz render:{renderingHz}Hz net:{networkManager.sendRate}Hz vSync:{vSyncCount} runInBackground:{runInBackground}");
    }
}
