using Unity.Entities;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    public enum SessionStatus : byte
    {
        Running,
        Won,
        Lost
    }

    public struct GameSession : IComponentData
    {
        public const float Duration = 180f;

        public SessionStatus Status;
        public float Elapsed;
        public bool RestartRequested;
        public bool RestartedThisFrame;
        public bool Initialized;
        public LocalTransform InitialPlayerTransform;
        public Health InitialPlayerHealth;

        public bool CanSimulate => Initialized && Status == SessionStatus.Running && !RestartedThisFrame;
    }
}
