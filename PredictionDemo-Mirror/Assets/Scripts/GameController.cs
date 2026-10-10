using DefaultNamespace;
using Mirror;
using UnityEngine;

public class GameController : MonoBehaviour
{
    [SerializeField] GameObject ballPrefab;
    [Tooltip("Spawned without an owner so the server drives it as a bot. The player prefab works.")]
    [SerializeField] GameObject botPrefab;
    [SerializeField] BotGrid botGrid = new BotGrid();

    void Awake()
    {
        botGrid.LoadBotCountFromFile(nameof(GameController));
    }

    // Update is called once per frame
    void Update()
    {
        if (!NetworkServer.active)
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
        var newBall = Instantiate(ballPrefab);
        NetworkServer.Spawn(newBall);
    }

    void SpawnBots()
    {
        int count = botGrid.botCount;
        for (int i = 0; i < count; i++)
        {
            GameObject bot = Instantiate(botPrefab, botGrid.GetPosition(i, count), Quaternion.identity);
            bot.name = BotGrid.GetName(i, count);
            // No owner connection: the server is the authority and MirrorPredictedPlayerController treats it as a bot.
            NetworkServer.Spawn(bot);
        }
    }
}
