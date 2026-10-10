// Keyboard shortcuts that export the visual fidelity data. Identical in all four demo projects.
// The error itself is graphed live by ResimGraph. See VisualFidelity.md in the repository root.

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PredictionDebug
{
    /// <summary>
    /// Created by the first probe that registers. F7 resets the statistics, F8 writes them to a CSV file, and F9
    /// starts or stops writing every compared frame to CSV. Each action logs to the Console, with the file path.
    /// </summary>
    public class VisualFidelityExport : MonoBehaviour
    {
#if ENABLE_INPUT_SYSTEM
        [SerializeField] Key resetKey = Key.F7;
        [SerializeField] Key saveKey = Key.F8;
        [SerializeField] Key recordKey = Key.F9;
#else
        [SerializeField] KeyCode resetKey = KeyCode.F7;
        [SerializeField] KeyCode saveKey = KeyCode.F8;
        [SerializeField] KeyCode recordKey = KeyCode.F9;
#endif

        static VisualFidelityExport _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        internal static void EnsureExists()
        {
            if (_instance != null)
                return;
            _instance = FindAnyObjectByType<VisualFidelityExport>(FindObjectsInactive.Include);
            if (_instance != null)
                return;
            var go = new GameObject("VisualFidelity (auto)");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<VisualFidelityExport>();
        }

        void Awake()
        {
            if (_instance == null)
                _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        // Closes the per-frame files so the last rows aren't lost.
        void OnApplicationQuit() => VisualFidelity.StopRecording();

        void Update()
        {
            if (WasPressed(resetKey))
            {
                VisualFidelity.ResetStats();
                Debug.Log("[VisualFidelity] Statistics reset.");
            }
            if (WasPressed(saveKey))
                VisualFidelity.WriteSummary();
            if (WasPressed(recordKey))
            {
                if (VisualFidelity.IsRecording)
                    VisualFidelity.StopRecording();
                else
                    VisualFidelity.StartRecording();
            }
        }

#if ENABLE_INPUT_SYSTEM
        static bool WasPressed(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }
#else
        static bool WasPressed(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);
#endif
    }
}
