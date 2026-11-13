// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Fork.Practice;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// Quick save and quick load, the section HUD, and pausing during a jump.
    /// </summary>
    [HeadlessTest]
    public partial class TestScenePracticeTools : PracticePlayerTestScene
    {
        [Resolved]
        private Storage storage { get; set; } = null!;

        private string checkpointFile => storage.GetFullPath(Path.Combine(@"practice", API.LocalUser.Value.OnlineID.ToString(), $@"{TEST_MD5}.json"));

        public override void SetUpSteps()
        {
            base.SetUpSteps();
            AddStep("delete checkpoint file", () =>
            {
                if (File.Exists(checkpointFile))
                    File.Delete(checkpointFile);
            });
        }

        [Test]
        public void TestQuickSaveAndLoad()
        {
            int reverted = 0;

            LoadPlayer();

            AtTime(GapAfter(1), "quick save", () => InputManager.Key(Key.F6));
            AddUntilStep("checkpoint set", () => Player.Checkpoint, () => Is.Not.Null);
            AddAssert("checkpoint between circles 1 and 2", () => Player.Checkpoint, () => Is.InRange(GapAfter(1), GapAfter(1) + 100));
            AddAssert("saving does not make it practice", () => !Player.IsPractice);
            AddAssert("checkpoint file written", () => File.Exists(checkpointFile));

            AddStep("bot misses circle 3", () => Bot.Skip.Add(3));
            AtTime(GapAfter(4), "quick load", () =>
            {
                Player.ScoreProcessor.JudgementReverted += _ => reverted++;
                InputManager.Key(Key.F7);
            });
            AcceptPracticePrompt();
            WaitForResume();
            AddAssert("first attempt", () => Player.Attempts, () => Is.EqualTo(1));

            // Live play goes on after the resume, so count undone judgements: circles 2 to 4.
            AddAssert("back at the checkpoint", () => reverted, () => Is.EqualTo(3));
            // The HUD shows the section: everything but circles 0 and 1, from before the checkpoint.
            AddAssert("section starts at the checkpoint", () => Player.ScoreProcessor.JudgedHits - Player.SectionScore.Processor.JudgedHits, () => Is.EqualTo(2));

            AddStep("bot hits everything", () => Bot.Skip.Clear());
            AtTime(GapAfter(4), "quick load again", () => InputManager.Key(Key.F7));
            AddUntilStep("second attempt, no question asked", () => Player.Attempts, () => Is.EqualTo(2));
            AddAssert("no prompt in a practice run", () => Player.PracticePrompt.State.Value, () => Is.EqualTo(Visibility.Hidden));
            WaitForResume();

            // A new player for the same map and profile finds the checkpoint.
            double saved = 0;
            AddStep("note checkpoint", () => saved = Player.Checkpoint!.Value);
            LoadPlayer();
            AddAssert("checkpoint loaded from file", () => Player.Checkpoint, () => Is.EqualTo(saved));
            AddAssert("attempts start again", () => Player.Attempts, () => Is.Zero);
        }

        [Test]
        public void TestCorruptCheckpointFile()
        {
            AddStep("write a corrupt checkpoint file", () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(checkpointFile)!);
                File.WriteAllText(checkpointFile, @"{ not json");
            });

            LoadPlayer();
            AddAssert("no checkpoint", () => Player.Checkpoint, () => Is.Null);

            AtTime(GapAfter(1), "quick load without a checkpoint", () => InputManager.Key(Key.F7));
            AddAssert("nothing asked", () => Player.PracticePrompt.State.Value, () => Is.EqualTo(Visibility.Hidden));
            AddAssert("still a normal run", () => !Player.IsPractice);

            AtTime(GapAfter(5), "quick save", () => InputManager.Key(Key.F6));
            AddUntilStep("file replaced", () => File.ReadAllText(checkpointFile), () => Does.Contain(@"QuickSave"));
        }

        [Test]
        public void TestKeepPlaying()
        {
            int reverted = 0;

            LoadPlayer();

            AtTime(GapAfter(1), "seek back", () =>
            {
                Player.ScoreProcessor.JudgementReverted += _ => reverted++;
                InputManager.Key(Key.Left);
            });
            AddUntilStep("practice prompt shown", () => Player.PracticePrompt.State.Value, () => Is.EqualTo(Visibility.Visible));
            AddStep("keep playing", () => Player.PracticePrompt.Buttons[1].TriggerClick());

            WaitForResume();
            AddAssert("still a normal run", () => !Player.IsPractice);
            AddAssert("nothing undone", () => reverted, () => Is.Zero);
        }

        [Test]
        public void TestEscapeDuringSeekSessionPauses()
        {
            LoadPlayer();

            AtTime(GapAfter(3), "rewind and hold", () => Player.PracticeSeek(GapAfter(0)));
            WaitForSeek();

            AddStep("press escape", () => InputManager.Key(Key.Escape));
            AddUntilStep("pause menu shown", () => Player.PauseOverlay.State.Value, () => Is.EqualTo(Visibility.Visible));
            AddAssert("still in gameplay", () => Player.IsCurrentScreen());
            AddAssert("seek session ended", () => !Player.SeekSession.IsActive);

            AddStep("continue", () => Player.PauseOverlay.Buttons[0].TriggerClick());
            WaitForResume();
            AddAssert("still in gameplay", () => Player.IsCurrentScreen());
        }

        [Test]
        public void TestStartAsPractice()
        {
            LoadPlayer(() => new TestPracticePlayer { StartAsPractice = true });

            AddAssert("practice from the start", () => Player.IsPractice);

            AtTime(GapAfter(0), "seek back", () => InputManager.Key(Key.Left));
            AddAssert("nothing asked", () => Player.PracticePrompt.State.Value, () => Is.EqualTo(Visibility.Hidden));
        }
    }
}
