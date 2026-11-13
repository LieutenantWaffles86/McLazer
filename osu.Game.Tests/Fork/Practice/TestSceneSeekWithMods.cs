// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// A rewind restores the run's state exactly with mods that change timing, visibility, or read the clock or cursor every frame.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneSeekWithMods : PracticePlayerTestScene
    {
        private static IEnumerable<TestCaseData> mods => new Func<Mod>[]
        {
            () => new OsuModHardRock(),
            () => new OsuModDoubleTime(),
            () => new OsuModHalfTime(),
            () => new OsuModHidden(),
            () => new OsuModFlashlight(),
            () => new ModAdaptiveSpeed(),
            () => new ModWindUp(),
            () => new ModWindDown(),
            () => new OsuModMagnetised(),
            () => new OsuModRepel(),
        }.Select(create => new TestCaseData(create).SetName($"TestRewindRestoresState({create().Acronym})"));

        [TestCaseSource(nameof(mods))]
        public void TestRewindRestoresState(Func<Mod> createMod)
        {
            ScoreState atTarget = null!;

            // Practice from the start: a fun mod may make the bot miss, and a normal run would fail.
            LoadPlayer(() => new TestPracticePlayer { StartAsPractice = true }, createMod());

            // Well clear of the bot's next press: rate-changing mods move each frame further in map time.
            AtTime(ObjectTime(3) + 160, "capture state", () => atTarget = CaptureState());
            AtTime(GapAfter(7), "rewind", () => Player.PracticeSeek(atTarget.Time));
            WaitForSeek();

            AddAssert("state restored", CaptureState, () => Is.EqualTo(atTarget));

            // Play on from the rewind: live play must work after it.
            AddStep("resume", () => Player.ResumeFromSeek());
            WaitForResume();
            AddUntilStep("run completed", () => Player.ScoreProcessor.HasCompleted.Value);
        }
    }
}
