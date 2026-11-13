// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// Times long seeks on a dense three-minute map. Run it in a Release build: <c>dotnet test -c Release --filter TestSceneSeekBenchmark</c>.
    /// </summary>
    /// <remarks>
    /// Headless runs skip drawing, so the game takes longer. In the game, each frame spends at most 10 ms walking the clock, then draws.
    /// </remarks>
    [HeadlessTest]
    [Explicit("Benchmark")]
    public partial class TestSceneSeekBenchmark : PracticePlayerTestScene
    {
        private const double map_length = 180_000;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new Beatmap { BeatmapInfo = { Ruleset = ruleset } };

            // A circle and a short slider every 250 ms each, back and forth across the playfield.
            for (double time = 1000; time < map_length; time += 500)
            {
                beatmap.HitObjects.Add(new HitCircle { StartTime = time, Position = new Vector2(100, 100) });
                beatmap.HitObjects.Add(new Slider
                {
                    StartTime = time + 250,
                    Position = new Vector2(400, 300),
                    Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(-100, 0) }),
                });
            }

            return beatmap;
        }

        [Test]
        public void TestLongSeeks()
        {
            var stopwatch = new Stopwatch();
            int frames = 0;
            ScoreState before = null!;

            LoadPlayer();

            // Between the bot's key presses: a press in the same frame as the capture would be judged after it, but replayed before it.
            AtTime(3_300, "capture state", () => before = CaptureState());

            timedSeek("forward 2.5 min", () => 150_000);
            AddAssert("seek arrived", () => CurrentTime, () => Is.EqualTo(150_000).Within(1e-6));

            timedSeek("back 2.5 min", () => before.Time);
            AddAssert("state restored", CaptureState, () => Is.EqualTo(before));

            void timedSeek(string description, Func<double> target)
            {
                AddStep(description, () =>
                {
                    frames = 0;
                    stopwatch.Restart();
                    Player.PracticeSeek(target());
                });
                AddUntilStep("wait for seek", () =>
                {
                    frames++;
                    return !Player.SeekSession.IsSeeking;
                });
                AddStep("log time", () => TestContext.Progress.WriteLine($@"{description}: {stopwatch.ElapsedMilliseconds} ms, {frames} frames"));
            }
        }
    }
}
