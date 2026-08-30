using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    public struct PlayerInput : IComponentData
    {
        public float2 Move;
    }
}
