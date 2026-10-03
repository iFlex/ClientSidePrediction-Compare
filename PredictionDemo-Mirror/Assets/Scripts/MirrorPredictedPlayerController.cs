using Mirror;
using UnityEngine;

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
    [SerializeField] private float BoostRotationPower = 30;
    [SerializeField] private float ThrottlePower = 1;
    [SerializeField] private float BoostPower = 5;
    
    [SerializeField] float rotate;
    [SerializeField] float throttle;
    [SerializeField] bool boost;
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
    
    private void FixedUpdate()
    {
        if (!isServer && isLocalPlayer)
        {
            rotate = ReadKeyboardRotate();
            throttle = ReadKeyboardThrottle();
            boost = ReadKeyboardBoost();
            
            ComputeForces();
            predictedRigidbody.predictedRigidbody.AddRelativeTorque(torqueVector);
            if (!LimitSpeed || rigidbody.linearVelocity.magnitude < GetMaxSpeed(boost))
            {
                predictedRigidbody.predictedRigidbody.AddRelativeForce(throttleVector);
            }
            CmdApplyServerForce(rotate, throttle, boost);
        }
        
        //TODO: a better host mode check?
        if (isServer && isClient && isLocalPlayer)
        {
            rotate = ReadKeyboardRotate();
            throttle = ReadKeyboardThrottle();
            boost = ReadKeyboardBoost();
            LocalApplyForces(rotate, throttle, boost);
        }
    }
    
    void ComputeForces()
    {
        torqueVector = Vector3.zero;
        if (Mathf.Abs(rotate) > 0.05f)
        {
            torqueVector = Vector3.up * (boost ? BoostRotationPower : RotationPower) * rotate;
        }
        throttleVector = Vector3.zero;
        if (Mathf.Abs(throttle) > 0.05f)
        {
            throttleVector = Vector3.forward * ( boost ? Mathf.Sign(throttle) * BoostPower : ThrottlePower * throttle);
        }
    }
    
    [Command]
    void CmdApplyServerForce(float crotate, float cthrottle, bool cboost)
    {
        LocalApplyForces(crotate, cthrottle, cboost);
    }

    void LocalApplyForces(float crotate, float cthrottle, bool cboost)
    {
        rotate = crotate;
        throttle = cthrottle;
        boost = cboost;
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
