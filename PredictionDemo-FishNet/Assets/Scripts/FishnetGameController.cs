using DefaultNamespace;
using FishNet;
using FishNet.Object;
using UnityEngine;

public class FishnetGameController : NetworkBehaviour
{
    [SerializeField] private GameObject ballPrefab;
    [Tooltip("Spawned without an owner so the server drives it as a bot. The player prefab works.")]
    [SerializeField] private GameObject botPrefab;
    [SerializeField] private BotGrid botGrid = new BotGrid();

    void Awake()
    {
        botGrid.LoadBotCountFromFile(nameof(FishnetGameController));
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsServerInitialized)
            return;

        if (UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame)
        {
            SpawnBall();
        }

        if (UnityEngine.InputSystem.Keyboard.current.xKey.wasPressedThisFrame)
        {
            SpawnBots();
        }
    }

    void SpawnBall()
    {
        var ball = Instantiate(ballPrefab);
        InstanceFinder.ServerManager.Spawn(ball);
    }

    void SpawnBots()
    {
        int count = botGrid.botCount;
        for (int i = 0; i < count; i++)
        {
            GameObject bot = Instantiate(botPrefab, botGrid.GetPosition(i, count), Quaternion.identity);
            bot.name = BotGrid.GetName(i, count);
            // No owner: the server is the controller and PredictedPlayerController feeds it bot inputs.
            InstanceFinder.ServerManager.Spawn(bot);
        }
    }
}
