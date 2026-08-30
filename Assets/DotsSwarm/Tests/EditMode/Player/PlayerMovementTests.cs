using DotsSwarm.Gameplay;
using NUnit.Framework;
using Unity.Mathematics;

namespace DotsSwarm.Tests.Gameplay
{
    public sealed class PlayerMovementTests
    {
        [Test]
        public void CalculatePosition_NormalizesDiagonalInput()
        {
            var position = PlayerMovementSystem.CalculatePosition(
                float3.zero,
                new float2(1f, 1f),
                10f,
                1f,
                new float2(100f));

            Assert.That(math.length(position.xz), Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void CalculatePosition_ClampsToArenaBounds()
        {
            var position = PlayerMovementSystem.CalculatePosition(
                new float3(4f, 0.5f, -4f),
                new float2(1f, -1f),
                10f,
                1f,
                new float2(5f));

            Assert.That(position.x, Is.EqualTo(5f));
            Assert.That(position.y, Is.EqualTo(0.5f));
            Assert.That(position.z, Is.EqualTo(-5f));
        }
    }
}
