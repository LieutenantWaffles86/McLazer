// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Screens.Play.HUD;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// Seeks come from keys. The progress bar is optional (skins can leave it out), so it never seeks.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneSeekControls : PracticePlayerTestScene
    {
        [Test]
        public void TestSeekKeys()
        {
            int reverted = 0;

            LoadPlayer();

            AtTime(GapAfter(8), "press the seek back key", () =>
            {
                Player.ScoreProcessor.JudgementReverted += _ => reverted++;
                InputManager.Key(Key.Left);
            });
            AddUntilStep("run is practice", () => Player.IsPractice);
            WaitForResume();

            // The key went back 5 s from 4,850 ms, to before the first circle. Live play goes on from there, so count undone judgements instead of judged ones.
            AddAssert("back before the first circle", () => reverted, () => Is.EqualTo(9));

            AtTime(GapAfter(0), "press the seek forward key", () => InputManager.Key(Key.Right));

            // From 850 ms, 5 s forward is past the last circle, so the seek stops at it. Circles 1 to 8 were skipped.
            AddUntilStep("run completed", () => Player.ScoreProcessor.HasCompleted.Value);
            AddAssert("skipped circles are misses", () => CaptureState().Statistics, () => Is.EqualTo("Miss: 8, Great: 2"));
        }

        [Test]
        public void TestProgressBarNeverSeeks()
        {
            LoadPlayer();

            AddAssert("progress bar not interactive in a normal run", () => progressBar.Interactive, () => Is.False);

            AtTime(GapAfter(3), "rewind", () => Player.PracticeSeek(GapAfter(0)));
            WaitForSeek();

            // Upstream makes it interactive whenever a replay is loaded, as it is during a seek session.
            AddAssert("replay attached", () => Player.DrawableRuleset.HasReplayLoaded.Value);
            AddAssert("progress bar not interactive while seeking", () => progressBar.Interactive, () => Is.False);
        }

        private SongProgressBar progressBar => Player.HUDOverlay.ChildrenOfType<SongProgressBar>().First();
    }
}
