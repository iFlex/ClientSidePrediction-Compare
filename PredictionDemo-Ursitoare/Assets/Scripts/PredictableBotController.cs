using UnityEngine;

namespace DefaultNamespace
{
    public class PredictableBotController : CustomPredictablePlayerController
    {
        [SerializeField] RandomBotBrain botBrain;

        protected override void Start()
        {
            base.Start();
            if (!botBrain)
            {
                botBrain = gameObject.AddComponent<RandomBotBrain>();
            }
        }
        
        protected override PlayerMovementInput ReadInput()
        {
            return botBrain.GetInput();
        }
    }
}
