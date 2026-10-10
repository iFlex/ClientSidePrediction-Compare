using DefaultNamespace;
using Mirror;
using UnityEngine;

public class GameController : NetworkBehaviour
{
    [SerializeField] GameObject ballPrefab;
    [SerializeField] GameObject botPrefab;
    [SerializeField] BotGrid botGrid = new BotGrid();

    void Awake()
    {
        botGrid.LoadBotCountFromFile(nameof(GameController));
    }
    
    void SpawnBots()
    {
        int count = botGrid.botCount;
        for (int i = 0; i < count; i++)
        {
            GameObject bot = Instantiate(botPrefab, botGrid.GetPosition(i, count), transform.rotation);
            NetworkServer.Spawn(bot);
            // Optional: Rename them in the hierarchy for easy organization
            bot.name = BotGrid.GetName(i, count);
        }
    }
    
    // Update is called once per frame
    void Update()
    {
        if (!isServer)
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
}
