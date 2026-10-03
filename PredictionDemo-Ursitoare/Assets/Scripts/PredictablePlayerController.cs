using Prediction.Components;
using Prediction.Data;
using UnityEngine;

namespace DefaultNamespace
{
    public class PredictablePlayerController : MonoBehaviour, PredictableControllableComponent, PredictableComponent
    {
        [SerializeField] private float RotationPower = 10;
        [SerializeField] private float ThrottlePower = 10;
        [SerializeField] private float BoostPower = 50;
        [SerializeField] private Rigidbody rigidbody;
        
        [SerializeField] private float throttle;
        [SerializeField] private float steer;
        [SerializeField] private bool boost;
        float ReadKeyboardThrottle()
        {
            float up = UnityEngine.InputSystem.Keyboard.current.upArrowKey.isPressed ? 1 : 0;
            float down = UnityEngine.InputSystem.Keyboard.current.downArrowKey.isPressed ? 1 : 0;
            return -down + up;
        }

        float ReadKeyboardRotate()
        {
            float left = UnityEngine.InputSystem.Keyboard.current.leftArrowKey.isPressed ? 1 : 0;
            float right = UnityEngine.InputSystem.Keyboard.current.rightArrowKey.isPressed ? 1 : 0;
            return -left + right;
        }

        bool ReadKeyboardBoost()
        {
            return UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed;
        }
        
        public int GetFloatInputCount()
        {
            return 2;
        }

        public int GetBinaryInputCount()
        {
            return 1;
        }

        public void SampleInput(PredictionInputRecord input)
        {
            input.WriteNextScalar(ReadKeyboardThrottle());
            input.WriteNextScalar(ReadKeyboardRotate());
            input.WriteNextBinary(ReadKeyboardBoost());
        }

        public bool ValidateInput(float deltaTime, PredictionInputRecord input)
        {
            return true;
        }

        public void LoadInput(PredictionInputRecord input)
        {
            throttle = input.ReadNextScalar();
            steer = input.ReadNextScalar();
            boost = input.ReadNextBool();
        }

        public void ClearInput()
        {
            throttle = 0;
            steer = 0;
            boost = false;
        }

        public void ApplyForces()
        {
            float rotToque = RotationPower * steer;
            float throttleForce = boost ? Mathf.Sign(throttle) * BoostPower : ThrottlePower * throttle;
            
            Debug.Log($"[PredictionPlayerController] ApplyForces rotT:{rotToque} throttleForce:{throttleForce}");
            rigidbody.AddRelativeTorque(Vector3.up * rotToque);
            rigidbody.AddRelativeForce(Vector3.forward * throttleForce);
        }

        public bool HasState()
        {
            return false;
        }

        public void SampleComponentState(PhysicsStateRecord physicsStateRecord)
        {
        }

        public void LoadComponentState(PhysicsStateRecord physicsStateRecord)
        {
        }

        public int GetStateFloatCount()
        {
            return 0;
        }

        public int GetStateBoolCount()
        {
            return 0;
        }
    }
}