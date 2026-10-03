using FishNet;
using FishNet.Object;
using UnityEngine;

public class FishnetGameController : NetworkBehaviour
{
    [SerializeField] private GameObject ballPrefab;

    // Update is called once per frame
    void Update()
    {
        if (IsServerInitialized && UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame)
        {
            SpawnBall();
        }
    }

    void SpawnBall()
    {
        var ball = Instantiate(ballPrefab);
        InstanceFinder.ServerManager.Spawn(ball);
    }
}
