using Mirror;
using UnityEngine;

public class GameController : MonoBehaviour
{
    [SerializeField] GameObject ballPrefab;
    
    // Update is called once per frame
    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame)
        {
            SpawnBall();
        }
    }

    void SpawnBall()
    {
        var newBall = Instantiate(ballPrefab);
        NetworkServer.Spawn(newBall);
    }
}
