using System;
using DotsSwarm.Gameplay;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DotsSwarm.Tests.Gameplay
{
    public sealed class BenchmarkHudTests
    {
        [Test]
        public void FrameTime_UsesMeanDurationAndResetsWindow()
        {
            var sample = new BenchmarkFrameTime();
            Assert.That(sample.AddFrame(0.1d), Is.False);
            Assert.That(sample.AddFrame(0.2d), Is.True);
            Assert.That(sample.Milliseconds, Is.EqualTo(150f).Within(0.001f));
            Assert.That(sample.AddFrame(0.25d), Is.True);
            Assert.That(sample.Milliseconds, Is.EqualTo(250f));
            sample = default;
            Assert.That(sample.Milliseconds, Is.Zero);
            Assert.That(sample.AddFrame(0.1d), Is.False);
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void FrameTime_IgnoresInvalidSamples(double seconds)
        {
            var sample = new BenchmarkFrameTime();
            Assert.That(sample.AddFrame(seconds), Is.False);
            Assert.That(sample.AddFrame(0.25d), Is.True);
            Assert.That(sample.Milliseconds, Is.EqualTo(250f));
        }

        [Test]
        public void Text_FormatsCountsTimeAndPresetWithinFixedCapacity()
        {
            var buffer = new char[BenchmarkHudText.Capacity];
            var session = new GameSession { Elapsed = 120.2f };
            var length = BenchmarkHudText.Write(buffer, 10000, 42, 16.67f, session, default);
            var text = new string(buffer, 0, length);
            StringAssert.Contains("SURVIVAL  |  1:00", text);
            StringAssert.Contains("Enemies: 10000   Projectiles: 42", text);
            StringAssert.Contains("16.7 ms", text);
            var benchmark = new BenchmarkState { ActivePreset = BenchmarkPreset.Swarm50K };
            length = BenchmarkHudText.Write(buffer, int.MaxValue, int.MaxValue, float.MaxValue, session, benchmark);
            text = new string(buffer, 0, length);
            StringAssert.Contains("STRESS 50000  |  Seed 1", text);
            StringAssert.Contains("2147483647", text);
            Assert.That(length, Is.LessThanOrEqualTo(buffer.Length));
        }

        [Test]
        public void SamplingAndFormatting_DoNotAllocateAfterWarmup()
        {
            var buffer = new char[BenchmarkHudText.Capacity];
            var sample = new BenchmarkFrameTime();
            var session = new GameSession();
            var benchmark = new BenchmarkState();
            BenchmarkHudText.Write(buffer, 0, 0, 0f, session, benchmark);
            sample.AddFrame(1d / 60d);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                sample.AddFrame(1d / 60d);
                BenchmarkHudText.Write(buffer, i, i, sample.Milliseconds, session, benchmark);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Hud_LoadsFontRoutesButtonsAndReusesTextStorage()
        {
            var selected = BenchmarkPreset.Survival;
            var restarts = 0;
            using var hud = new GameSessionHud(preset => selected = preset, () => restarts++);
            hud.SetVisible(true);
            var root = GameObject.Find("Session HUD");
            var counters = root.transform.Find("Benchmark/Counters").GetComponent<TextMeshProUGUI>();
            Assert.That(counters.font, Is.Not.Null);
            Assert.That(counters.font.material.shader.name, Does.StartWith("TextMeshPro/"));
            root.transform.Find("Benchmark/4: 50k").GetComponent<Button>().onClick.Invoke();
            Assert.That(selected, Is.EqualTo(BenchmarkPreset.Swarm50K));
            var session = new GameSession { Status = SessionStatus.Lost };
            hud.Refresh(50000, 1, 16.6f, session, new BenchmarkState { ActivePreset = selected });
            Assert.That(root.transform.Find("Result").gameObject.activeSelf, Is.True);
            root.transform.Find("Result/Restart (R)").GetComponent<Button>().onClick.Invoke();
            Assert.That(restarts, Is.EqualTo(1));
            session.Status = SessionStatus.Running;
            hud.Refresh(50000, 1, 16.6f, session, default);
            Assert.That(root.transform.Find("Result").gameObject.activeSelf, Is.False);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) hud.Refresh(50000 + i, i, 16.6f, session, default);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Refreshing existing TMP text must not construct strings.");
        }
    }
}
