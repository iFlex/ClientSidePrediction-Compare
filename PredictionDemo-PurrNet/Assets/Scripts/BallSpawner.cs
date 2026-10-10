using PurrNet.Prediction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DefaultNamespace
{
    // Equivalent of FishnetGameController: the server presses B to spawn a ball and X to spawn a grid of bots.
    // Spawning goes through hierarchy.Create inside Simulate so the spawned objects are part of the predicted world.
    // Has no owner, so the server is its controller and the only side that reads the keys.
    public class BallSpawner : PredictedIdentity<BallSpawner.SpawnInput, BallSpawner.SpawnState>
    {
        [SerializeField] private GameObject ballPrefab;
        [SerializeField] private Transform spawnPoint;

        [Header("Bots")]
        [Tooltip("Created without an owner so the server drives it as a bot. The player prefab works.")]
        [SerializeField] private GameObject botPrefab;
        [SerializeField] private BotGrid botGrid = new BotGrid();

        private void Awake()
        {
            botGrid.LoadBotCountFromFile(nameof(BallSpawner));
        }

        // Update runs every frame on the controller; accumulate the key presses until the next tick consumes them.
        protected override void UpdateInput(ref SpawnInput input)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            input.spawn |= keyboard.bKey.wasPressedThisFrame;
            // The count rides in the input so clients replaying the tick spawn the same grid as the server.
            if (keyboard.xKey.wasPressedThisFrame)
                input.spawnBots = botGrid.botCount;
        }

        // A key press is a one-off, don't repeat it when inputs are extrapolated.
        protected override void ModifyExtrapolatedInput(ref SpawnInput input)
        {
            input.spawn = false;
            input.spawnBots = 0;
        }

        protected override void Simulate(SpawnInput input, ref SpawnState state, float delta)
        {
            if (input.spawn)
            {
                Transform at = spawnPoint ? spawnPoint : transform;
                hierarchy.Create(ballPrefab, at.position, at.rotation);
            }

            if (input.spawnBots > 0)
            {
                SpawnBots(input.spawnBots);
            }
        }

        void SpawnBots(int count)
        {
            for (int i = 0; i < count; i++)
            {
                // No owner: the server is the controller and PredictedPlayerController feeds it bot inputs.
                hierarchy.Create(botPrefab, botGrid.GetPosition(i, count), Quaternion.identity);
            }
        }

        public struct SpawnInput : IPredictedData
        {
            public bool spawn;
            public int spawnBots;

            public void Dispose() { }
        }

        public struct SpawnState : IPredictedData<SpawnState>
        {
            public void Dispose() { }
        }
    }
}
