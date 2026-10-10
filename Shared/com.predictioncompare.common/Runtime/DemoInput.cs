using UnityEngine.InputSystem;

namespace DefaultNamespace
{
    // The demo's keyboard layout, read the same way in every project:
    // arrows throttle/steer, space boosts, Q/E strafe, left shift spins faster, R flips upright, C swaps cameras.
    public static class DemoInput
    {
        public static float ReadThrottle()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return 0;
            return (keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.downArrowKey.isPressed ? 1 : 0);
        }

        public static float ReadSteer()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return 0;
            return (keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0);
        }

        public static bool ReadBoost() => Keyboard.current?.spaceKey.isPressed ?? false;
        public static bool ReadStrafeLeft() => Keyboard.current?.qKey.isPressed ?? false;
        public static bool ReadStrafeRight() => Keyboard.current?.eKey.isPressed ?? false;
        public static bool ReadSpin() => Keyboard.current?.leftShiftKey.isPressed ?? false;
        public static bool ReadFlip() => Keyboard.current?.rKey.isPressed ?? false;
        public static bool SwapCameraPressed() => Keyboard.current?.cKey.wasPressedThisFrame ?? false;

        public static PlayerMovementInput ReadMovement()
        {
            return new PlayerMovementInput(ReadThrottle(), ReadSteer(), ReadBoost(), ReadStrafeLeft(), ReadStrafeRight(), ReadSpin(), ReadFlip());
        }
    }
}
