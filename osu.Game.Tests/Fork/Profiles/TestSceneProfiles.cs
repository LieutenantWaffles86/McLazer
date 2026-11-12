// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Fork.Profiles;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Rulesets.Osu;
using osu.Game.Scoring;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Tests.Fork.Lazer;

namespace osu.Game.Tests.Fork.Profiles
{
    /// <summary>
    /// The whole game, next to a lazer folder with one beatmap set, so there is something to set scores on.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneProfiles : McLazerGameTestScene
    {
        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private FakeLazerFolder lazer = null!;

        private ProfileStore profiles => Game.Profiles;

        public override void SetUpSteps()
        {
            AddStep("create lazer folder with one beatmap set", () =>
            {
                lazer?.Dispose();
                lazer = new FakeLazerFolder(host, audio, Resources, Beatmap.Default);
                lazer.ImportBeatmap();
            });

            base.SetUpSteps();

            AddUntilStep("wait for sync", () => Game.LazerSyncTask.IsCompleted);
        }

        protected override TestMcLazerGame CreateTestGame() => new TestMcLazerGame(LocalStorage) { LazerPath = lazer.Path };

        [Test]
        public void TestFirstProfileIsCreated()
        {
            AddAssert("one profile", () => profiles.Profiles.Count, () => Is.EqualTo(1));
            AddAssert("its ID is in the local range", () => ProfileStore.IsLocal(profiles.Active.Id));
            AddAssert("it is the local user", () => Game.API.LocalUser.Value.Id, () => Is.EqualTo(profiles.Active.Id));
            AddUntilStep("the picker replaced the login form", () => Game.ChildrenOfType<LoginOverlay>().SingleOrDefault()?.ChildrenOfType<ProfilePanel>().Count(), () => Is.EqualTo(1));
        }

        [Test]
        public void TestGuestScoresGoToTheFirstProfile()
        {
            ProfileStore fresh = null!;

            AddStep("set a guest score", () => Game.Realm.Write(r =>
            {
                var beatmap = r.All<BeatmapInfo>().ToList().First(b => !b.BeatmapSet!.Protected);
                r.Add(new ScoreInfo(beatmap, beatmap.Ruleset) { BeatmapHash = beatmap.Hash, User = new APIUser { Id = APIUser.SYSTEM_USER_ID, Username = "Guest" } });
            }));
            AddStep("create profiles in an empty folder", () => fresh = new ProfileStore(LocalStorage.GetStorageForDirectory("fresh"), Game.Realm, "first"));
            AddAssert("the first profile has the score", () => Game.Realm.Run(r => r.All<ScoreInfo>().ToList().Count(s => s.UserID == fresh.Active.Id && s.User.Username == "first")),
                () => Is.EqualTo(1));
            AddAssert("no guest scores left", () => Game.Realm.Run(r => r.All<ScoreInfo>().ToList().Count(s => s.UserID == APIUser.SYSTEM_USER_ID)), () => Is.Zero);
        }

        [Test]
        public void TestEachProfileSeesOnlyItsOwnScores()
        {
            Profile first = null!, second = null!;
            BeatmapInfo beatmap = null!;
            LeaderboardManager leaderboard = null!;

            AddStep("create two profiles", () =>
            {
                first = profiles.Active;
                second = profiles.Create("second")!;
            });
            AddUntilStep("second is the local user", () => Game.API.LocalUser.Value.Username, () => Is.EqualTo("second"));

            AddStep("each sets a score", () => Game.Realm.Write(r =>
            {
                beatmap = r.All<BeatmapInfo>().ToList().First(b => !b.BeatmapSet!.Protected && b.Ruleset.ShortName == "osu");

                foreach (var profile in new[] { first, second, second })
                {
                    var score = new ScoreInfo(beatmap, beatmap.Ruleset) { BeatmapHash = beatmap.Hash, TotalScore = 1000 };
                    score.User = profile.ToAPIUser();
                    r.Add(score);
                }
            }));

            AddStep("fetch the local leaderboard", () =>
            {
                leaderboard = Game.ChildrenOfType<LeaderboardManager>().Single();
                leaderboard.FetchWithCriteria(new LeaderboardCriteria(beatmap, new OsuRuleset().RulesetInfo, BeatmapLeaderboardScope.Local, null));
            });
            AddUntilStep("second sees its two scores", () => leaderboard.Scores.Value?.TopScores.Select(s => s.User.Username), () => Is.EqualTo(new[] { "second", "second" }));

            AddStep("switch to the first profile", () => profiles.Switch(first));
            AddUntilStep("first sees its one score", () => leaderboard.Scores.Value?.TopScores.Select(s => s.User.Id), () => Is.EqualTo(new[] { first.Id }));

            AddStep("rename the first profile", () => profiles.Rename(first, "renamed"));
            AddUntilStep("its score shows the new name", () => leaderboard.Scores.Value?.TopScores.Select(s => s.User.Username), () => Is.EqualTo(new[] { "renamed" }));

            AddStep("delete the second profile, keep its scores", () => profiles.Delete(second, false));
            AddAssert("its scores are kept under another name", () => Game.Realm.Run(r => r.All<ScoreInfo>().ToList().Count(s => s.User.Username == ProfileStore.DELETED_PLAYER && !s.DeletePending)),
                () => Is.EqualTo(2));

            AddStep("create a third profile", () => profiles.Create("third"));
            AddAssert("the deleted profile's ID was not reused", () => profiles.Active.Id, () => Is.GreaterThan(second.Id));
            AddStep("delete it with its scores", () => profiles.Delete(profiles.Active, true));
            AddAssert("first is active again", () => profiles.Active, () => Is.EqualTo(first));
            AddAssert("the last profile cannot be deleted", () => profiles.Delete(first, true), () => Is.False);
        }

        [Test]
        public void TestNamesMustBeUnique()
        {
            AddAssert("empty name refused", () => profiles.Create(" "), () => Is.Null);
            AddAssert("taken name refused", () => profiles.Create(profiles.Active.Username.ToUpperInvariant()), () => Is.Null);
            AddAssert("one profile", () => profiles.Profiles.Count, () => Is.EqualTo(1));
        }

        [Test]
        public void TestProfilesSurviveARestartAndACorruptFile()
        {
            string file = null!;

            AddStep("create a second profile", () => profiles.Create("second"));
            AddStep("reload from disk", () => file = LocalStorage.GetFullPath("profiles.json"));
            AddAssert("both profiles read back, second active", () => new ProfileStore(LocalStorage, Game.Realm, "x").Active.Username, () => Is.EqualTo("second"));
            AddAssert("no temporary file left", () => !File.Exists(file + ".tmp"));

            AddStep("corrupt the file", () => File.WriteAllText(file, "{ not json"));
            AddAssert("a fresh profile is created", () => new ProfileStore(LocalStorage, Game.Realm, "fresh").Profiles.Single().Username, () => Is.EqualTo("fresh"));
            AddAssert("the corrupt file is kept aside", () => File.Exists(file + ".corrupt"));
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            lazer?.Dispose();
        }
    }
}
