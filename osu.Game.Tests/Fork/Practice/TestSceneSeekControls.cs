// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Fork.Practice;
using osu.Game.Screens.Play.HUD;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// The ways a player starts a seek: the seek keys and the progress bar.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneSeekControls : PracticePlayerTestScene
    {
        [Test]
        public void TestSeekKeys()
        {
            LoadPlayer();

            AtTime(GapAfter(8), "press the seek back key", () => InputManager.Key(Key.Left));
            AddUntilStep("run is practice", () => Player.IsPractice);
            WaitForResume();

            // The key went back 5 s from 4,850 ms, to before the first circle.
            AddAssert("back before the first circle", () => CaptureState().JudgedHits, () => Is.Zero);

            AtTime(GapAfter(0), "press the seek forward key", () => InputManager.Key(Key.Right));

            // From 850 ms, 5 s forward is past the last circle, so the seek stops at it. Circles 1 to 8 were skipped.
            AddUntilStep("run completed", () => Player.ScoreProcessor.HasCompleted.Value);
            AddAssert("skipped circles are misses", () => CaptureState().Statistics, () => Is.EqualTo("Miss: 8, Great: 2"));
        }

        [Test]
        public void TestProgressBarSeeksInPracticeOnly()
        {
            bool sessionStarted = false;

            LoadPlayer();

            AddAssert("progress bar not interactive in a normal run", () => progressBar.Interactive, () => Is.False);

            AtTime(GapAfter(3), "rewind", () => Player.PracticeSeek(GapAfter(0), true));
            WaitForResume();

            AddAssert("progress bar interactive in practice", () => progressBar.Interactive);

            AtTime(GapAfter(2), "seek from the progress bar", () =>
            {
                Player.Seek(GapAfter(0));
                sessionStarted = Player.SeekSession.IsActive;
            });
            AddAssert("seek went through the seek session", () => sessionStarted);
            WaitForResume();
        }

        [Test]
        public void TestDraggingProgressBarHolds()
        {
            int frameCount = 0;

            LoadPlayer();

            AtTime(GapAfter(3), "rewind", () => Player.PracticeSeek(GapAfter(0), true));
            WaitForResume();

            // The bot moves the mouse every frame. Stop it between circles, when it holds no key.
            AtTime(GapAfter(2), "stop the bot", () => Bot.Player = null);

            AddStep("drag along the progress bar", () =>
            {
                frameCount = Player.Score.Replay.Frames.Count;
                InputManager.MoveMouseTo(progressBar.ToScreenSpace(progressBar.LayoutRectangle.Centre));
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(progressBar.ToScreenSpace(progressBar.LayoutRectangle.Centre + new osuTK.Vector2(-progressBar.DrawWidth / 4, 0)));
            });

            AddUntilStep("seek started", () => Player.SeekSession.IsActive);
            AddUntilStep("seek arrived", () => !Player.SeekSession.IsSeeking);
            AddWaitStep("hold the drag", 5);
            AddAssert("still in the seek session", () => Player.SeekSession.IsActive);
            AddAssert("gameplay still paused", () => Player.GameplayClockContainer.IsPaused.Value);

            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            WaitForResume();
            AddAssert("recording was cut at the drag target", () => Player.Score.Replay.Frames.Count, () => Is.LessThan(frameCount));
        }

        private SongProgressBar progressBar => Player.HUDOverlay.ChildrenOfType<SongProgressBar>().First();
    }
}
