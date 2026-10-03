using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

public class MirrorPredictedPlayerController : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnColorUpdated))]
    private Color color;
    [SerializeField] private bool LimitSpeed = true;
    [SerializeField] private float MaxTravelSpeed = 15f;
    [SerializeField] private float MaxBoostTravelSpeed = 30f;

    [SerializeField] private Renderer renderer;
    [SerializeField] private PredictedRigidbody predictedRigidbody;
    [SerializeField] private Rigidbody rigidbody;
    
    [SerializeField] private float RotationPower = 1;
    [FormerlySerializedAs("BoostRotationPower")] [SerializeField] private float SpinRotationPower = 30;
    [SerializeField] private float ThrottlePower = 1;
    [SerializeField] private float BoostPower = 5;
    
    [SerializeField] float rotate;
    [SerializeField] float throttle;
    [SerializeField] bool boost;
    [SerializeField] bool strafeLeft;
    [SerializeField] bool strafeRight;
    [SerializeField] bool spin;
    [SerializeField] private bool groundTest = false;
    [SerializeField] private GameObject ground;
    
    [SerializeField] private Vector3 torqueVector;
    [SerializeField] private Vector3 throttleVector;

    private void Start()
    {
        if (isServer)
        {
            color = new Color(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f));
            OnColorUpdated(Color.white, color);
        }
    }

    //TODO: pick colors for playables :D
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
    
    private void FixedUpdate()
    {
        if (!isServer && isLocalPlayer)
        {
            rotate = ReadKeyboardRotate();
            throttle = ReadKeyboardThrottle();
            boost = ReadKeyboardBoost();
            strafeLeft = ReadKeyboardStrafeLeft();
            strafeRight = ReadKeyboardStrafeRight();
            spin = ReadKeyboardSpin();

            ComputeForces();
            predictedRigidbody.predictedRigidbody.AddRelativeTorque(torqueVector);
            if (!LimitSpeed || rigidbody.linearVelocity.magnitude < GetMaxSpeed(boost))
            {
                predictedRigidbody.predictedRigidbody.AddRelativeForce(throttleVector);
            }
            CmdApplyServerForce(rotate, throttle, boost, strafeLeft, strafeRight, spin);
        }

        //TODO: a better host mode check?
        if (isServer && isClient && isLocalPlayer)
        {
            rotate = ReadKeyboardRotate();
            throttle = ReadKeyboardThrottle();
            boost = ReadKeyboardBoost();
            strafeLeft = ReadKeyboardStrafeLeft();
            strafeRight = ReadKeyboardStrafeRight();
            spin = ReadKeyboardSpin();
            LocalApplyForces(rotate, throttle, boost, strafeLeft, strafeRight, spin);
        }
    }
    
    void ComputeForces()
    {
        torqueVector = Vector3.zero;
        if (Mathf.Abs(rotate) > 0.05f)
        {
            torqueVector = Vector3.up * (spin ? SpinRotationPower : RotationPower) * rotate;
        }
        throttleVector = Vector3.zero;
        if (Mathf.Abs(throttle) > 0.05f)
        {
            throttleVector = Vector3.forward * ( boost ? Mathf.Sign(throttle) * BoostPower : ThrottlePower * throttle);
        }
        if (strafeLeft)
        {
            throttleVector += Vector3.left * BoostPower;
        }
        if (strafeRight)
        {
            throttleVector += Vector3.right * BoostPower;
        }
    }

    [Command]
    void CmdApplyServerForce(float crotate, float cthrottle, bool cboost, bool cstrafeLeft, bool cstrafeRight, bool cspin)
    {
        LocalApplyForces(crotate, cthrottle, cboost, cstrafeLeft, cstrafeRight, cspin);
    }

    void LocalApplyForces(float crotate, float cthrottle, bool cboost, bool cstrafeLeft, bool cstrafeRight, bool cspin)
    {
        rotate = crotate;
        throttle = cthrottle;
        boost = cboost;
        strafeLeft = cstrafeLeft;
        strafeRight = cstrafeRight;
        spin = cspin;
        ComputeForces();
        
        rigidbody.AddRelativeTorque(torqueVector);
        if (!LimitSpeed || rigidbody.linearVelocity.magnitude < GetMaxSpeed(boost))
        {
            rigidbody.AddRelativeForce(throttleVector);
        }
    }
    
    void OnColorUpdated(Color oldC, Color newC)
    {
        renderer.material.color = newC;
    }
    
    float GetMaxSpeed(bool boosting) 
    {
        return boosting ? MaxBoostTravelSpeed : MaxTravelSpeed;
    }
    
    //TODO: ground test
    //TODO: other finesse aspects?
}
