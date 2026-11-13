// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// A normal run plays and saves like lazer's. A practice run never fails and is never saved.
    /// </summary>
    [HeadlessTest]
    public partial class TestScenePracticeRules : PracticePlayerTestScene
    {
        private BeatmapManager beatmaps = null!;
        private BeatmapSetInfo importedSet = null!;

        [BackgroundDependencyLoader]
        private void load(GameHost host, AudioManager audio)
        {
            RulesetStore rulesets;

            Dependencies.Cache(rulesets = new RealmRulesetStore(Realm));
            Dependencies.Cache(beatmaps = new BeatmapManager(LocalStorage, Realm, null, audio, Resources, host, Beatmap.Default));
            Dependencies.Cache(new ScoreManager(rulesets, () => beatmaps, LocalStorage, Realm, API));
            Dependencies.Cache(Realm);
        }

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("import beatmap", () =>
            {
                beatmaps.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely();
                importedSet = beatmaps.GetAllUsableBeatmapSets().First();
            });

            AddStep("clear scores", () => Realm.Write(r => r.RemoveAll<ScoreInfo>()));
        }

        // Scores can only be saved for a beatmap in the database.
        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => beatmaps.GetWorkingBeatmap(importedSet.Beatmaps.First()).Beatmap;

        private int savedScores => Realm.Run(r => r.All<ScoreInfo>().Count());

        private double lastObjectTime => Player.GameplayState.Beatmap.GetLastObjectTime();

        [Test]
        public void TestNormalRunFails()
        {
            LoadPlayer();

            AddUntilStep("run failed", () => Player.GameplayState.HasFailed);
            AddAssert("run is not practice", () => !Player.IsPractice);
        }

        [Test]
        public void TestPracticeRunNeverFails()
        {
            LoadPlayer();

            AddStep("seek to become practice", () => Player.PracticeSeek(Player.GameplayClockContainer.StartTime, true));
            WaitForResume();

            AddUntilStep("health hit zero", () => Player.HealthProcessor.Health.Value, () => Is.Zero);
            AddUntilStep("run completed", () => Player.ScoreProcessor.HasCompleted.Value);
            AddAssert("run did not fail", () => !Player.GameplayState.HasFailed);
        }

        [Test]
        public void TestSeekBlockedAfterFail()
        {
            LoadPlayer();

            AddUntilStep("run failed", () => Player.GameplayState.HasFailed);
            AddStep("try to seek", () => Player.PracticeSeek(0));
            AddAssert("run is not practice", () => !Player.IsPractice);
            AddAssert("no seek started", () => !Player.SeekSession.IsActive);
        }

        [Test]
        public void TestNormalRunIsSaved()
        {
            DateTimeOffset started = default;

            AddStep("note time", () => started = DateTimeOffset.Now);
            LoadPlayer(new OsuModNoFail());

            AddUntilStep("results shown", () => Player.GetChildScreen() is ResultsScreen);
            AddUntilStep("score saved", () => savedScores, () => Is.EqualTo(1));

            AddAssert("score dated at the end of the run", () => Realm.Run(r => r.All<ScoreInfo>().Single().Date), () => Is.GreaterThanOrEqualTo(started));
            AddAssert("score belongs to the local user", () => Realm.Run(r => r.All<ScoreInfo>().Single().User.OnlineID), () => Is.EqualTo(API.LocalUser.Value.OnlineID));
            AddAssert("beatmap marked as played", () => Realm.Run(r => r.Find<BeatmapInfo>(Beatmap.Value.BeatmapInfo.ID)!.LastPlayed), () => Is.GreaterThanOrEqualTo(started));
        }

        [Test]
        public void TestPracticeRunIsNotSaved()
        {
            LoadPlayer(new OsuModNoFail());

            AddStep("skip to the last object", () => Player.PracticeSeek(lastObjectTime, true));
            WaitForResume();

            AddUntilStep("run completed", () => Player.ScoreProcessor.HasCompleted.Value);

            // A normal run would show results one second after completing.
            AddWaitStep("wait for results delay", 10);
            AddAssert("still in gameplay", () => Player.IsCurrentScreen());

            AddStep("exit", () => Player.Exit());
            AddUntilStep("player exited", () => !Player.IsCurrentScreen());
            AddAssert("no score saved", () => savedScores, () => Is.Zero);
        }

        [Test]
        public void TestEndPrompt()
        {
            LoadPlayer(new OsuModNoFail());

            AddStep("skip to the last object", () => Player.PracticeSeek(lastObjectTime, true));
            WaitForResume();
            AddUntilStep("run completed", () => Player.ScoreProcessor.HasCompleted.Value);
            AddUntilStep("end prompt shown", () => Player.EndPrompt.State.Value, () => Is.EqualTo(Visibility.Visible));
            AddAssert("no checkpoint to load", () => Player.EndPrompt.Buttons.Count, () => Is.EqualTo(2));

            AddStep("play from the start", () => Player.EndPrompt.Buttons[0].TriggerClick());
            AddUntilStep("end prompt hidden", () => Player.EndPrompt.State.Value, () => Is.EqualTo(Visibility.Hidden));
            AddUntilStep("completion undone", () => !Player.ScoreProcessor.HasCompleted.Value);
            WaitForResume();

            AddStep("skip to the last object", () => Player.PracticeSeek(lastObjectTime, true));
            WaitForResume();
            AddUntilStep("end prompt shown", () => Player.EndPrompt.State.Value, () => Is.EqualTo(Visibility.Visible));

            AddStep("results", () => Player.EndPrompt.Buttons[^1].TriggerClick());
            AddUntilStep("results shown", () => Player.GetChildScreen() is ResultsScreen);
            AddAssert("no score saved", () => savedScores, () => Is.Zero);
        }
    }
}
