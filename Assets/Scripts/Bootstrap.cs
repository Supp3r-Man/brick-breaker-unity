using UnityEngine;

namespace BrickBurst
{
    /// <summary>
    /// The game builds itself at runtime, so the scene can be empty (or not exist yet): this runs
    /// after whatever scene is loaded and creates the controller, which creates everything else.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (Object.FindFirstObjectByType<GameController>() != null) return;
            new GameObject("BrickBurst").AddComponent<GameController>();
        }
    }
}
