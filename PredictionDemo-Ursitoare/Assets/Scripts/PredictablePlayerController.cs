using Prediction.Components;
using Prediction.Data;
using UnityEngine;
using UnityEngine.Serialization;

namespace DefaultNamespace
{
    public class PredictablePlayerController : MonoBehaviour, PredictableControllableComponent, PredictableComponent
    {
        [SerializeField] private bool LimitSpeed = true;
        [SerializeField] private float MaxTravelSpeed = 15f;
        [SerializeField] private float MaxBoostTravelSpeed = 30f;
        
        [SerializeField] private float RotationPower = 10;
        [FormerlySerializedAs("BoostRotationPower")] [SerializeField] private float SpinRotationPower = 30;
        [SerializeField] private float ThrottlePower = 10;
        [SerializeField] private float BoostPower = 50;
        [SerializeField] private Rigidbody rigidbody;
        
        [SerializeField] private float throttle;
        [SerializeField] private float steer;
        [SerializeField] private bool boost;
        [SerializeField] private bool strafeLeft;
        [SerializeField] private bool strafeRight;
        [SerializeField] private bool spin;
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

        bool ReadKeyboardStrafeLeft()
        {
            return UnityEngine.InputSystem.Keyboard.current.qKey.isPressed;
        }

        bool ReadKeyboardStrafeRight()
        {
            return UnityEngine.InputSystem.Keyboard.current.eKey.isPressed;
        }

        bool ReadKeyboardSpin()
        {
            return UnityEngine.InputSystem.Keyboard.current.leftShiftKey.isPressed;
        }
        
        public int GetFloatInputCount()
        {
            return 2;
        }

        public int GetBinaryInputCount()
        {
            return 4;
        }

        public void SampleInput(PredictionInputRecord input)
        {
            input.WriteNextScalar(ReadKeyboardThrottle());
            input.WriteNextScalar(ReadKeyboardRotate());
            input.WriteNextBinary(ReadKeyboardBoost());
            input.WriteNextBinary(ReadKeyboardStrafeLeft());
            input.WriteNextBinary(ReadKeyboardStrafeRight());
            input.WriteNextBinary(ReadKeyboardSpin());
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
            strafeLeft = input.ReadNextBool();
            strafeRight = input.ReadNextBool();
            spin = input.ReadNextBool();
        }

        public void ClearInput()
        {
            throttle = 0;
            steer = 0;
            boost = false;
            strafeLeft = false;
            strafeRight = false;
            spin = false;
        }

        float GetMaxSpeed(bool boosting)
        {
            return boosting ? MaxBoostTravelSpeed : MaxTravelSpeed;
        }
        
        public void ApplyForces()
        {
            float rotToque = 0;
            if (Mathf.Abs(steer) > 0.05f)
            {
                rotToque = (spin ? SpinRotationPower : RotationPower) * steer;
            }
            float throttleForce = 0;
            if (Mathf.Abs(throttle) > 0.05f)
            {
                throttleForce = boost ? Mathf.Sign(throttle) * BoostPower : ThrottlePower * throttle;
            }
            Vector3 throttleVector = Vector3.forward * throttleForce;
            if (strafeLeft)
            {
                throttleVector += Vector3.left * BoostPower;
            }
            if (strafeRight)
            {
                throttleVector += Vector3.right * BoostPower;
            }

            rigidbody.AddRelativeTorque(Vector3.up * rotToque);
            if (!LimitSpeed || rigidbody.linearVelocity.magnitude < GetMaxSpeed(boost))
            {
                rigidbody.AddRelativeForce(throttleVector);
            }
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