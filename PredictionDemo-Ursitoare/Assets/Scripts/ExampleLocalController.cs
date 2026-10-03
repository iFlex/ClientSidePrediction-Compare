using UnityEngine;

public class ExampleLocalController : MonoBehaviour
{
    [SerializeField] private Rigidbody rigidbody;
    [SerializeField] private float RotationPower = 1;
    [SerializeField] private float ThrottlePower = 1;
    [SerializeField] private float BoostPower = 5;
    
    [SerializeField] float rotate;
    [SerializeField] float throttle;
    [SerializeField] bool boost;
    [SerializeField] private bool groundTest = false;
    [SerializeField] private GameObject ground;
    
    void Awake()
    {
        if (!rigidbody)
            rigidbody = GetComponent<Rigidbody>();
    }
    
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
        rotate = ReadKeyboardRotate();
        throttle = ReadKeyboardThrottle();
        boost = ReadKeyboardBoost();
        
        float rotToque = RotationPower * rotate;
        float throttleForce = boost ? Mathf.Sign(throttle) * BoostPower : ThrottlePower * throttle;
        
        rigidbody.AddRelativeTorque(Vector3.up * rotToque);
        rigidbody.AddRelativeForce(Vector3.forward * throttleForce);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            ground = collision.gameObject;
        }
        groundTest = ground;
    }

    void OnCollisionStay(Collision collision)
    {
        //stuff?
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject == ground)
        {
            ground = null;
        }
        groundTest = ground;
    }
}
