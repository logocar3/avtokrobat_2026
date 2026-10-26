// Makes the simulator "just work": press Play on any scene and, if it has no simulator yet,
// one is spawned. No manual scene wiring is needed (Avtokrobat > Create Play Scene saves a
// scene with it preset).
using UnityEngine;

namespace Avtokrobat
{
    public static class AvtokrobatBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<UnityPhysicsController>() != null) return;
            new GameObject("Avtokrobat").AddComponent<UnityPhysicsController>();
        }
    }
}
