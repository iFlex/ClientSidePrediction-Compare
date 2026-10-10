using FishNet.Managing;
using UnityEngine;

public class Configurer : MonoBehaviour
{
    [SerializeField] NetworkManager networkManager;

    // Update is called once per frame
    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame)
        {
            networkManager.ClientManager.StartConnection("5.12.112.44");
            //networkManager.ClientManager.StartConnection("127.0.0.1");
        }
    }
}
