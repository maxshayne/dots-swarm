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
        public void History_ReportsNearestRankPercentilesOfLatestFrames()
        {
            var history = new FrameTimeHistory(20);
            Assert.That(history.Summarize().Frames, Is.Zero);
            // 1..20 ms in shuffled order: nearest rank is independent of arrival order.
            for (var i = 0; i < 20; i++) history.Add((i * 7 % 20 + 1) / 1000d);

            var summary = history.Summarize();
            Assert.That(summary.Frames, Is.EqualTo(20));
            Assert.That(summary.AverageMilliseconds, Is.EqualTo(10.5f).Within(1e-4f));
            Assert.That(summary.P50Milliseconds, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(summary.P95Milliseconds, Is.EqualTo(19f).Within(1e-4f));
            Assert.That(summary.P99Milliseconds, Is.EqualTo(20f).Within(1e-4f));
            Assert.That(summary.MaxMilliseconds, Is.EqualTo(20f).Within(1e-4f));

            // A full ring drops the oldest frames first.
            var ring = new FrameTimeHistory(4);
            foreach (var milliseconds in new[] { 1, 2, 3, 4, 10, 10 }) ring.Add(milliseconds / 1000d);
            summary = ring.Summarize();
            Assert.That(summary.Frames, Is.EqualTo(4));
            Assert.That(summary.AverageMilliseconds, Is.EqualTo(6.75f).Within(1e-4f), "1 ms and 2 ms were overwritten.");
            Assert.That(summary.P50Milliseconds, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(summary.P95Milliseconds, Is.EqualTo(10f).Within(1e-4f));

            history.Clear();
            Assert.That(history.Add(0d), Is.False);
            Assert.That(history.Add(double.NaN), Is.False);
            Assert.That(history.Add(double.PositiveInfinity), Is.False);
            Assert.That(history.Add(0.004d), Is.True);
            summary = history.Summarize();
            Assert.That(summary.Frames, Is.EqualTo(1));
            Assert.That(summary.P95Milliseconds, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(new FrameTimeHistory(0).Capacity, Is.EqualTo(1));
        }

        [Test]
        public void Text_FormatsCountsTimeAndPresetWithinFixedCapacity()
        {
            var buffer = new char[BenchmarkHudText.Capacity];
            var session = new GameSession { Elapsed = 120.2f };
            var length = BenchmarkHudText.Write(buffer, 10000, 42, 16.67f, 21.04f, 12, session, default);
            var text = new string(buffer, 0, length);
            StringAssert.Contains("SURVIVAL  |  1:00  |  HP 12", text);
            StringAssert.Contains("Enemies: 10000   Projectiles: 42", text);
            StringAssert.Contains("Frame avg: 16.7 ms   p95: 21.0 ms", text);
            var benchmark = new BenchmarkState { ActivePreset = BenchmarkPreset.Swarm50K };
            length = BenchmarkHudText.Write(buffer, int.MaxValue, int.MaxValue, float.MaxValue, float.MaxValue,
                int.MaxValue, session, benchmark);
            text = new string(buffer, 0, length);
            StringAssert.Contains("STRESS 50000  |  Seed 1", text);
            StringAssert.Contains("2147483647", text);
            StringAssert.Contains("p95: 99999.0 ms", text);
            Assert.That(length, Is.LessThanOrEqualTo(buffer.Length));
        }

        [Test]
        public void SamplingAndFormatting_DoNotAllocateAfterWarmup()
        {
            var buffer = new char[BenchmarkHudText.Capacity];
            var sample = new BenchmarkFrameTime();
            var session = new GameSession();
            var benchmark = new BenchmarkState();
            var history = new FrameTimeHistory(GameSessionBridge.PercentileFrames);
            BenchmarkHudText.Write(buffer, 0, 0, 0f, 0f, 12, session, benchmark);
            sample.AddFrame(1d / 60d);
            history.Add(1d / 60d);
            history.Summarize();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                sample.AddFrame(1d / 60d);
                history.Add((i % 7 + 10) / 1000d);
                var p95 = history.Summarize().P95Milliseconds;
                BenchmarkHudText.Write(buffer, i, i, sample.Milliseconds, p95, i, session, benchmark);
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
            hud.Refresh(50000, 1, 16.6f, 18f, 12, session, new BenchmarkState { ActivePreset = selected });
            Assert.That(root.transform.Find("Result").gameObject.activeSelf, Is.True);
            root.transform.Find("Result/Restart (R)").GetComponent<Button>().onClick.Invoke();
            Assert.That(restarts, Is.EqualTo(1));
            session.Status = SessionStatus.Running;
            hud.Refresh(50000, 1, 16.6f, 18f, 12, session, default);
            Assert.That(root.transform.Find("Result").gameObject.activeSelf, Is.False);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) hud.Refresh(50000 + i, i, 16.6f, 18f + i, 12 - i % 13, session, default);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Refreshing existing TMP text must not construct strings.");
        }
    }
}
