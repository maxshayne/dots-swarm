using Unity.Entities;

namespace DotsSwarm.Gameplay
{
    public enum BenchmarkPreset : int
    {
        Survival = 0,
        Swarm1K = 1000,
        Swarm10K = 10000,
        Swarm20K = 20000,
        Swarm50K = 50000
    }

    public struct BenchmarkState : IComponentData
    {
        public const uint RandomSeed = 1u;
        public BenchmarkPreset ActivePreset;
        public BenchmarkPreset RequestedPreset;
        public bool ChangeRequested;

        public bool IsStress => ActivePreset != BenchmarkPreset.Survival;
        public int TargetCount => (int)ActivePreset;

        public static bool IsValid(BenchmarkPreset preset) => preset == BenchmarkPreset.Survival
            || preset == BenchmarkPreset.Swarm1K || preset == BenchmarkPreset.Swarm10K
            || preset == BenchmarkPreset.Swarm20K || preset == BenchmarkPreset.Swarm50K;
    }
}
