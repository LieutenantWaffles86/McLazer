// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.Osu.UI;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Practice
{
    [HeadlessTest]
    public partial class TestSceneSeekSession : PracticePlayerTestScene
    {
        [Test]
        public void TestRewindRestoresState()
        {
            ScoreState atTarget = null!;
            ScoreState later = null!;
            int judgedDuringRewind = 0;

            LoadPlayer();

            AtTime(GapAfter(3), "capture state", () => atTarget = CaptureState());
            AddAssert("four circles hit", () => atTarget.Statistics, () => Is.EqualTo("Great: 4"));

            AtTime(GapAfter(7), "capture state and rewind", () =>
            {
                later = CaptureState();
                Player.ScoreProcessor.NewJudgement += _ => judgedDuringRewind++;
                Player.PracticeSeek(atTarget.Time);
            });
            AddAssert("state had moved on", () => later.Statistics, () => Is.EqualTo("Great: 8"));
            AddAssert("replay attached for the jump", () => Player.DrawableRuleset.HasReplayLoaded.Value);

            WaitForSeek();

            AddAssert("state restored", CaptureState, () => Is.EqualTo(atTarget));

            // The clock walks straight back. It does not jump to the start and replay the run up to the target.
            AddAssert("nothing was judged again", () => judgedDuringRewind, () => Is.Zero);
            AddAssert("score info restored", () => (Player.Score.ScoreInfo.TotalScore, Player.Score.ScoreInfo.MaxCombo, Player.Score.ScoreInfo.Accuracy),
                () => Is.EqualTo((atTarget.TotalScore, atTarget.HighestCombo, atTarget.Accuracy)));
            AddAssert("run is practice", () => Player.IsPractice);
        }

        [Test]
        public void TestRewindThenPlayOn()
        {
            ScoreState first = null!;
            ScoreState second = null!;

            LoadPlayer();
            AddStep("bot misses circle 2", () => Bot.Skip.Add(2));

            AtTime(GapAfter(4), "capture state and rewind", () =>
            {
                first = CaptureState();
                Bot.Skip.Clear();
                Player.PracticeSeek(GapAfter(1), true);
            });
            AddAssert("first attempt has a miss", () => first.Statistics, () => Is.EqualTo("Miss: 1, Great: 4"));

            WaitForResume();

            AddAssert("replay detached", () => !Player.DrawableRuleset.HasReplayLoaded.Value);

            AtTime(GapAfter(4), "capture state", () => second = CaptureState());
            AddAssert("second attempt has no miss", () => second.Statistics, () => Is.EqualTo("Great: 5"));
            AddAssert("one continuous recording", () => Player.Score.Replay.Frames.Select(f => f.Time), () => Is.Ordered);
            AddAssert("input is recorded again", () => Player.Score.Replay.Frames.Last().Time, () => Is.GreaterThan(GapAfter(3)));
        }

        [Test]
        public void TestSkippedTimeCountsAsMisses()
        {
            double recordedUntil = 0;
            ScoreState afterGap = null!;

            LoadPlayer();

            AtTime(GapAfter(1), "skip forward", () =>
            {
                recordedUntil = Player.Score.Replay.Frames.Last().Time;
                Player.PracticeSeek(GapAfter(5));
            });
            WaitForSeek();

            AddAssert("skipped circles are misses", () => CaptureState().Statistics, () => Is.EqualTo("Miss: 4, Great: 2"));
            AddAssert("nothing skipped until resume", () => Player.SeekSession.SkippedRanges, () => Is.Empty);

            AddStep("resume", () => Player.ResumeFromSeek());

            AddAssert("gap stored as skipped", () => Player.SeekSession.SkippedRanges.Single().Start, () => Is.EqualTo(recordedUntil));
            AddAssert("gap ends at resume point", () => Player.SeekSession.SkippedRanges.Single().End, () => Is.EqualTo(GapAfter(5)).Within(1e-6));

            AtTime(GapAfter(7), "capture state and rewind", () =>
            {
                afterGap = CaptureState();
                Player.PracticeSeek(GapAfter(0), true);
            });
            AddAssert("live play continued after the gap", () => afterGap.Statistics, () => Is.EqualTo("Miss: 4, Great: 4"));

            // Resuming before the gap discards it along with the rest of that timeline.
            WaitForResume();
            AddAssert("skipped range discarded", () => Player.SeekSession.SkippedRanges, () => Is.Empty);
        }

        [Test]
        public void TestScrubbingIsStable()
        {
            ScoreState early = null!;
            ScoreState late = null!;
            int frameCount = 0;

            LoadPlayer();
            AddStep("bot misses circle 2", () => Bot.Skip.Add(2));

            AtTime(GapAfter(7), "rewind", () =>
            {
                frameCount = Player.Score.Replay.Frames.Count;
                Player.PracticeSeek(GapAfter(1));
            });
            WaitForSeek();

            AddStep("capture early state", () => early = CaptureState());
            AddAssert("two circles judged", () => early.Statistics, () => Is.EqualTo("Great: 2"));

            SeekTo(GapAfter(5));
            AddStep("capture late state", () => late = CaptureState());
            AddAssert("six circles judged", () => late.Statistics, () => Is.EqualTo("Miss: 1, Great: 5"));

            for (int i = 0; i < 2; i++)
            {
                SeekTo(GapAfter(1));
                AddAssert("early state repeats", CaptureState, () => Is.EqualTo(early));

                SeekTo(GapAfter(5));
                AddAssert("late state repeats", CaptureState, () => Is.EqualTo(late));
            }

            AddAssert("replay still attached", () => Player.DrawableRuleset.HasReplayLoaded.Value);
            AddAssert("gameplay still paused", () => Player.GameplayClockContainer.IsPaused.Value);
            AddAssert("no frames recorded or cut while scrubbing", () => Player.Score.Replay.Frames.Count, () => Is.EqualTo(frameCount));
        }

        [Test]
        public void TestSeekIntoIntroAndPastEnd()
        {
            LoadPlayer();

            AtTime(GapAfter(1), "seek far before the start", () => Player.PracticeSeek(-100000));
            WaitForSeek();

            AddAssert("clamped to start of map", () => CurrentTime, () => Is.EqualTo(Player.GameplayClockContainer.StartTime).Within(1e-6));
            AddAssert("nothing judged", () => CaptureState().JudgedHits, () => Is.Zero);

            SeekTo(100000);
            AddAssert("clamped to last object", () => CurrentTime, () => Is.EqualTo(ObjectTime(OBJECT_COUNT - 1)).Within(1e-6));

            // Circles 0 and 1 replay as hits. The rest were never played, and the last one is not yet judged at its own start time.
            AddAssert("all but the last judged", () => CaptureState().Statistics, () => Is.EqualTo($"Miss: {OBJECT_COUNT - 3}, Great: 2"));
        }

        [Test]
        public void TestSeekWhileResuming()
        {
            ScoreState afterResume = null!;

            LoadPlayer();
            AddStep("enable resume overlay", () => Player.DrawableRuleset.UseResumeOverlay = true);

            AtTime(GapAfter(3), "rewind", () => Player.PracticeSeek(GapAfter(0)));
            WaitForSeek();

            AddStep("resume", () => Player.ResumeFromSeek());
            AddUntilStep("resume overlay shown", () => Player.DrawableRuleset.ResumeOverlay.State.Value, () => Is.EqualTo(Visibility.Visible));
            AddAssert("gameplay still paused", () => Player.GameplayClockContainer.IsPaused.Value);

            // A seek cancels the resume in progress.
            SeekTo(GapAfter(1));
            AddAssert("resume overlay hidden", () => Player.DrawableRuleset.ResumeOverlay.State.Value, () => Is.EqualTo(Visibility.Hidden));
            AddAssert("not resuming", () => !Player.IsResuming);

            AddStep("resume", () => Player.ResumeFromSeek());
            AddUntilStep("resume overlay shown", () => Player.DrawableRuleset.ResumeOverlay.State.Value, () => Is.EqualTo(Visibility.Visible));

            AddStep("click the resume cursor", () =>
            {
                InputManager.MoveMouseTo(Player.ChildrenOfType<OsuResumeOverlay.OsuClickToResumeCursor>().Single());
                InputManager.Key(Key.Z);
            });

            WaitForResume();

            AtTime(GapAfter(4), "capture state", () => afterResume = CaptureState());

            // The first resume cut the recording after circle 0, so the seek over circle 1 was skipped time.
            AddAssert("live play continues", () => afterResume.Statistics, () => Is.EqualTo("Miss: 1, Great: 4"));
        }
    }
}
