// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Fork.Practice;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// Holding the scrub key shows the strain graph. Clicks on it jump and hold there until the key is released.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneScrub : PracticePlayerTestScene
    {
        [Test]
        public void TestScrub()
        {
            LoadPlayer();

            AddAssert("graph hidden", () => overlay.IsPresent, () => Is.False);
            AddAssert("graph has columns", () => overlay.ChildrenOfType<Box>().Count(b => b.Width < 0.01f && b.Height > 0), () => Is.GreaterThan(0));

            // The bot moves the mouse every frame. Stop it between circles, when it holds no key.
            AtTime(GapAfter(3), "stop the bot and hold the scrub key", () =>
            {
                Bot.Player = null;
                InputManager.PressKey(Key.LShift);
            });
            AddUntilStep("graph shown", () => overlay.IsPresent);
            AddAssert("normal run until a click", () => !Player.IsPractice);

            // The graph runs from the first circle (500 ms) to the last (5,000 ms).
            clickAt(0.2f); // 1,400 ms
            AddAssert("two circles judged", () => CaptureState().Statistics, () => Is.EqualTo("Great: 2"));

            clickAt(0.8f); // 4,100 ms: circles 4 to 6 were never played
            AddAssert("seven circles judged", () => CaptureState().Statistics, () => Is.EqualTo("Miss: 3, Great: 4"));

            clickAt(0.2f);
            AddAssert("own attempt again", () => CaptureState().Statistics, () => Is.EqualTo("Great: 2"));
            AddAssert("still paused", () => Player.GameplayClockContainer.IsPaused.Value);

            AddStep("release the scrub key", () => InputManager.ReleaseKey(Key.LShift));
            WaitForResume();
            AddUntilStep("graph hidden", () => overlay.IsPresent, () => Is.False);
            AddAssert("run is practice", () => Player.IsPractice);
        }

        private void clickAt(float x)
        {
            AddStep($"click the graph at {x:P0}", () =>
            {
                InputManager.MoveMouseTo(overlay.ToScreenSpace(new Vector2(overlay.DrawWidth * x, overlay.DrawHeight / 2)));
                InputManager.Click(MouseButton.Left);
            });

            double target = ObjectTime(0) + x * (ObjectTime(OBJECT_COUNT - 1) - ObjectTime(0));
            AddUntilStep("wait for the jump", () => Player.SeekSession.IsActive && !Player.SeekSession.IsSeeking
                                                    && System.Math.Abs(Player.GameplayClockContainer.CurrentTime - target) < 1);
        }

        private ScrubOverlay overlay => Player.ChildrenOfType<ScrubOverlay>().Single();
    }
}
