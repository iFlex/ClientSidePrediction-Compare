using UnityEngine;

namespace DefaultNamespace
{
    // Swaps between the scene's main camera and the camera that rides on the player's box.
    // Plain class rather than a component so the controllers keep their serialized localCamera field;
    // each controller decides which object is the local player, because that check is library-specific.
    public class LocalCameraSwitch
    {
        private readonly Camera localCamera;
        private readonly Camera globalCamera;

        // Call from Awake: remembers the main camera and starts on it.
        public LocalCameraSwitch(Camera localCamera)
        {
            this.localCamera = localCamera;
            globalCamera = Camera.main;
            if (localCamera)
                localCamera.enabled = false;
        }

        // Call every frame on the local player only.
        public void Update()
        {
            if (DemoInput.SwapCameraPressed())
                Swap();
        }

        public void Swap()
        {
            if (!localCamera || !globalCamera)
                return;
            bool toLocal = !localCamera.enabled;
            localCamera.enabled = toLocal;
            globalCamera.enabled = !toLocal;
        }
    }
}
