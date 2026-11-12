// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.Fork.Practice
{
    [HeadlessTest]
    public partial class TestScenePracticeReplay : PracticePlayerTestScene
    {
        /// <summary>
        /// A run with a rewind, a second attempt and a forward skip leaves one continuous replay.
        /// Watching it back gives the same result as the run.
        /// </summary>
        [Test]
        public void TestFinalReplayMatchesRun()
        {
            ScoreState run = null!;
            Score replay = null!;
            TestReplayPlayer replayPlayer = null!;

            LoadPlayer();
            AddStep("bot misses circle 2", () => Bot.Skip.Add(2));

            AtTime(GapAfter(4), "rewind for a second attempt", () =>
            {
                Bot.Skip.Clear();
                Player.PracticeSeek(GapAfter(1), true);
            });
            WaitForResume();

            AtTime(GapAfter(3), "skip three circles", () => Player.PracticeSeek(GapAfter(6), true));
            WaitForResume();

            AddUntilStep("run completed", () => Player.ScoreProcessor.HasCompleted.Value);
            AddStep("keep the run's result and replay", () =>
            {
                run = CaptureState();
                replay = new Score { ScoreInfo = Player.Score.ScoreInfo.DeepClone(), Replay = Player.Score.Replay.DeepClone() };
            });

            AddAssert("second attempt and skip both count", () => run.Statistics, () => Is.EqualTo("Miss: 3, Great: 7"));
            AddAssert("replay frames are in order", () => replay.Replay.Frames.Select(f => f.Time), () => Is.Ordered);

            AddStep("watch the replay", () =>
            {
                Stack.Exit();
                LoadScreen(replayPlayer = new TestReplayPlayer(replay, showResults: false));
            });
            AddUntilStep("replay completed", () => replayPlayer.IsLoaded && replayPlayer.ScoreProcessor.HasCompleted.Value);

            AddAssert("replay gives the run's result",
                () => (replayPlayer.ScoreProcessor.TotalScore.Value, replayPlayer.ScoreProcessor.HighestCombo.Value, replayPlayer.ScoreProcessor.Accuracy.Value,
                    string.Join(", ", replayPlayer.ScoreProcessor.Statistics.Where(kvp => kvp.Value > 0).OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}: {kvp.Value}"))),
                () => Is.EqualTo((run.TotalScore, run.HighestCombo, run.Accuracy, run.Statistics)));
        }
    }
}
