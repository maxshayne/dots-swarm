using Unity.Entities;

namespace DotsSwarm.Gameplay
{
    [InternalBufferCapacity(4)]
    public struct DamageEvent : IBufferElementData
    {
        public int Amount;
    }
}
