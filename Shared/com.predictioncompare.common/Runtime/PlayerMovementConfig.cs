using UnityEngine;

namespace DefaultNamespace
{
    // Movement tuning and force maths shared by every demo's player controller, so all four libraries push the box the same way.
    // The forces are relative to the body; each controller applies them through its own library's rigidbody wrapper.
    // Pure functions of the input and config, so they are safe to run again during resimulation.
    [CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "PredictionCompare/Player Movement Config")]
    public class PlayerMovementConfig : ScriptableObject
    {
        public bool LimitSpeed = true;
        public float MaxTravelSpeed = 15f;
        public float MaxBoostTravelSpeed = 30f;

        public float RotationPower = 10;
        public float SpinRotationPower = 30;
        public float ThrottlePower = 10;
        public float BoostPower = 50;

        [Tooltip("Steer and throttle below this magnitude are ignored.")]
        public float DeadZone = 0.05f;

        [Header("Flip upright (R)")]
        [Tooltip("Upward velocity change (m/s) applied on the tick the flip starts, so the box jumps clear of the ground.")]
        public float FlipJumpVelocity = 6f;
        [Tooltip("Proportional gain: angular acceleration (rad/s²) per radian the box's up is away from world up.")]
        public float FlipTorque = 20f;
        [Tooltip("Derivative gain: angular acceleration (rad/s²) opposing each rad/s of spin, so the box settles instead of overshooting.")]
        public float FlipDamping = 8f;
        [Tooltip("A flip can only start when dot(box up, world up) is below this. 0.5 = tilted more than 60°.")]
        public float FlipStartBelowDot = 0.5f;
        [Tooltip("The corrective torque stops once dot(box up, world up) reaches this. 0.98 = within about 11°.")]
        public float FlipUprightDot = 0.98f;
        [Tooltip("Safety cap on how many ticks the corrective torque may run.")]
        public int FlipMaxTicks = 240;
        [Tooltip("Ticks after a flip starts before another one can.")]
        public int FlipCooldownTicks = 120;

        public Vector3 ComputeTorque(in PlayerMovementInput input)
        {
            if (Mathf.Abs(input.steer) <= DeadZone)
                return Vector3.zero;
            return Vector3.up * ((input.spin ? SpinRotationPower : RotationPower) * input.steer);
        }

        public Vector3 ComputeForce(in PlayerMovementInput input)
        {
            Vector3 force = Vector3.zero;
            if (Mathf.Abs(input.throttle) > DeadZone)
            {
                force = Vector3.forward * (input.boost ? Mathf.Sign(input.throttle) * BoostPower : ThrottlePower * input.throttle);
            }
            if (input.strafeLeft)
            {
                force += Vector3.left * BoostPower;
            }
            if (input.strafeRight)
            {
                force += Vector3.right * BoostPower;
            }
            return force;
        }

        // The force from ComputeForce is only applied below the speed cap; torque always is.
        public bool CanAccelerate(float currentSpeed, bool boosting)
        {
            return !LimitSpeed || currentSpeed < GetMaxSpeed(boosting);
        }

        public float GetMaxSpeed(bool boosting)
        {
            return boosting ? MaxBoostTravelSpeed : MaxTravelSpeed;
        }

        /// <summary>
        /// One simulation tick of the flip: on the tick it starts, an upward jump; from that tick on, a corrective torque
        /// until the box is upright. Call it exactly once per simulated tick, inside the library's predicted step
        /// (including replays), with the body's state at the start of the tick and the predicted flip state.
        /// Both outputs are in world space: apply jumpVelocity with ForceMode.VelocityChange and
        /// torqueAcceleration with ForceMode.Acceleration, so neither depends on mass or inertia.
        /// </summary>
        public void ComputeFlip(in PlayerMovementInput input, Quaternion rotation, Vector3 angularVelocity, ref PlayerFlipState state,
            out Vector3 jumpVelocity, out Vector3 torqueAcceleration)
        {
            jumpVelocity = Vector3.zero;
            torqueAcceleration = Vector3.zero;

            if (state.cooldownTicks > 0)
                state.cooldownTicks--;

            Vector3 up = rotation * Vector3.up;
            float uprightDot = Vector3.Dot(up, Vector3.up);

            if (state.activeTicks == 0)
            {
                if (!input.flip || state.cooldownTicks > 0 || uprightDot > FlipStartBelowDot)
                    return;

                jumpVelocity = Vector3.up * FlipJumpVelocity;
                state.activeTicks = 1;
                state.cooldownTicks = FlipCooldownTicks;
            }
            else if (uprightDot >= FlipUprightDot || state.activeTicks >= FlipMaxTicks)
            {
                state.activeTicks = 0;
                return;
            }
            else
            {
                state.activeTicks++;
            }

            // PD controller turning the box's up towards world up.
            Vector3 axis = Vector3.Cross(up, Vector3.up);
            if (axis.sqrMagnitude < 1e-6f)
            {
                // Exactly upside down: any horizontal axis works, roll around the box's own forward.
                axis = rotation * Vector3.forward;
            }
            float angle = Vector3.Angle(up, Vector3.up) * Mathf.Deg2Rad;
            torqueAcceleration = axis.normalized * (angle * FlipTorque) - angularVelocity * FlipDamping;
        }
    }
}
