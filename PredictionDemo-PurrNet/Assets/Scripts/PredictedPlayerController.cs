using PurrNet.Prediction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DefaultNamespace
{
    // Same box controller as the Ursitoare and FishNet demos: arrows throttle/steer, space boosts.
    [RequireComponent(typeof(PredictedRigidbody))]
    public class PredictedPlayerController : PredictedIdentity<PredictedPlayerController.PlayerInput, PredictedPlayerController.PlayerState>
    {
        [SerializeField] private float RotationPower = 10;
        [SerializeField] private float ThrottlePower = 10;
        [SerializeField] private float BoostPower = 50;

        private PredictedRigidbody _predictedRigidbody;

        private void Awake()
        {
            _predictedRigidbody = GetComponent<PredictedRigidbody>();
        }

        float ReadKeyboardThrottle(Keyboard keyboard)
        {
            float up = keyboard.upArrowKey.isPressed ? 1 : 0;
            float down = keyboard.downArrowKey.isPressed ? 1 : 0;
            return -down + up;
        }

        float ReadKeyboardRotate(Keyboard keyboard)
        {
            float left = keyboard.leftArrowKey.isPressed ? 1 : 0;
            float right = keyboard.rightArrowKey.isPressed ? 1 : 0;
            return -left + right;
        }

        // Called once per tick on the controlling side; inputs here are held keys so sampling at the tick is enough.
        protected override void GetFinalInput(ref PlayerInput input)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            input.throttle = ReadKeyboardThrottle(keyboard);
            input.steer = ReadKeyboardRotate(keyboard);
            input.boost = keyboard.spaceKey.isPressed;
        }

        protected override void Simulate(PlayerInput input, ref PlayerState state, float delta)
        {
            // Forces must go through PredictedRigidbody (not the Rigidbody) so they are part of the rolled back state.
            float rotTorque = RotationPower * input.steer;
            float throttleForce = input.boost ? Mathf.Sign(input.throttle) * BoostPower : ThrottlePower * input.throttle;

            _predictedRigidbody.AddRelativeTorque(Vector3.up * rotTorque);
            _predictedRigidbody.AddRelativeForce(Vector3.forward * throttleForce);
        }

        public struct PlayerInput : IPredictedData
        {
            public float throttle;
            public float steer;
            public bool boost;

            public void Dispose() { }
        }

        // All physical state lives in PredictedRigidbody, the controller itself has none.
        public struct PlayerState : IPredictedData<PlayerState>
        {
            public void Dispose() { }
        }
    }
}
