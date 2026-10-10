using System;
using System.IO;
using UnityEngine;

namespace DefaultNamespace
{
    // Bot spawn settings shared by every demo: how many bots one X press spawns and how they are laid out.
    // The spawning itself stays in each project because it goes through that project's networking library.
    [Serializable]
    public class BotGrid
    {
        [Tooltip("Bots spawned per X press. Overridden by the config file when it holds a valid number.")]
        public int botCount = 1;
        [Tooltip("The distance between each bot on the grid.")]
        public float gridSpacing = 3f;
        [Tooltip("If true, the center of the grid will be at center, otherwise its first corner is.")]
        public bool centerGrid = true;
        [Tooltip("World position of the grid.")]
        public Vector3 center = Vector3.zero;
        [Tooltip("Text file holding a single integer, next to the executable (or the project root in the Editor).")]
        public string configFileName = "runtime_config.txt";

        public void LoadBotCountFromFile(string logTag)
        {
            // GetCurrentDirectory returns the project root in the Editor,
            // and the folder containing the executable in a built game.
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), configFileName);

            if (File.Exists(filePath))
            {
                string fileContent = File.ReadAllText(filePath).Trim();
                if (int.TryParse(fileContent, out int parsedBots))
                {
                    botCount = Mathf.Max(1, parsedBots);
                    Debug.Log($"[{logTag}] Loaded bot count: {botCount} from {filePath}");
                }
                else
                {
                    Debug.LogWarning($"[{logTag}] The file contained '{fileContent}', which is not a valid number. Using default: {botCount}");
                }
            }
            else
            {
                // Create it with the default so it is easy to find and edit later.
                File.WriteAllText(filePath, botCount.ToString());
                Debug.Log($"[{logTag}] Config file not found. Created {filePath} with default count: {botCount}");
            }
        }

        // Lays count bots out on a roughly square grid, row by row.
        public Vector3 GetPosition(int index, int count)
        {
            int gridDimension = Mathf.CeilToInt(Mathf.Sqrt(count));
            float offset = centerGrid ? (gridDimension - 1) * gridSpacing / 2f : 0f;
            int row = index / gridDimension;
            int col = index % gridDimension;
            return center + new Vector3(col * gridSpacing - offset, 0f, row * gridSpacing - offset);
        }

        public static string GetName(int index, int count)
        {
            int gridDimension = Mathf.CeilToInt(Mathf.Sqrt(count));
            return $"Bot_{index / gridDimension}_{index % gridDimension}";
        }
    }
}
