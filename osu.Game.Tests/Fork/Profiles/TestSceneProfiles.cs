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
using osu.Game.Fork.Lazer;
using osu.Game.Fork.Profiles;
using osu.Game.Fork.Stats;
using osu.Game.Models;
using osu.Game.Rulesets.Scoring;
using osu.Framework.Graphics.Containers;
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

            AddStep("show all profiles", () => leaderboard.ShowAllProfiles.Value = true);
            AddUntilStep("all three scores", () => leaderboard.Scores.Value?.TopScores.Count, () => Is.EqualTo(3));
            AddStep("show this profile only", () => leaderboard.ShowAllProfiles.Value = false);
            AddUntilStep("one score again", () => leaderboard.Scores.Value?.TopScores.Count, () => Is.EqualTo(1));

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
        public void TestTotalPPMatchesAHandCalculation()
        {
            BeatmapInfo third = null!;

            AddStep("set scores on three ranked maps", () => Game.Realm.Write(r =>
            {
                var beatmaps = r.All<BeatmapInfo>().ToList().Where(b => !b.BeatmapSet!.Protected && b.Ruleset.ShortName == "osu").Take(3).ToList();
                Assert.That(beatmaps, Has.Count.EqualTo(3));

                foreach (var b in beatmaps)
                    b.Status = BeatmapOnlineStatus.Ranked;
                third = beatmaps[2];

                void add(BeatmapInfo beatmap, double pp, int userId, ScoreRank rank = ScoreRank.S, string name = "")
                {
                    var score = new ScoreInfo(beatmap, beatmap.Ruleset) { BeatmapHash = beatmap.Hash, PP = pp, Accuracy = pp / 1000, Rank = rank };
                    score.RealmUser = new RealmUser { OnlineID = userId, Username = name };
                    r.Add(score);
                }

                int me = profiles.Active.Id;

                add(beatmaps[0], 100, me);
                add(beatmaps[1], 300, me);
                add(beatmaps[1], 50, me); // a worse score on the same map does not count
                add(beatmaps[2], 200, me);
                add(beatmaps[2], 1000, me, ScoreRank.F); // failed
                add(beatmaps[0], 1000, 123, name: "someone" + LazerSync.LAZER_SCORE_SUFFIX); // lazer's
            }));

            AddAssert("total is 300 + 200 × 0.95 + 100 × 0.95²", () => LocalStats.For(Game.Realm, profiles.Active.Id).TotalPP, () => Is.EqualTo(300 + 200 * 0.95 + 100 * 0.9025).Within(1e-9));
            AddAssert("accuracy weighted the same way", () => LocalStats.For(Game.Realm, profiles.Active.Id).Accuracy,
                () => Is.EqualTo((0.3 + 0.2 * 0.95 + 0.1 * 0.9025) / (1 + 0.95 + 0.9025)).Within(1e-9));
            AddAssert("every saved score is a play", () => LocalStats.For(Game.Realm, profiles.Active.Id).PlayCount, () => Is.EqualTo(5));
            AddAssert("top plays in pp order", () => LocalStats.For(Game.Realm, profiles.Active.Id).TopPlays.Select(s => s.PP), () => Is.EqualTo(new double?[] { 300, 200, 100 }));

            AddStep("the third map is no longer ranked", () => Game.Realm.Write(r => r.Find<BeatmapInfo>(third.ID)!.Status = BeatmapOnlineStatus.Pending));
            AddAssert("its score no longer counts", () => LocalStats.For(Game.Realm, profiles.Active.Id).TotalPP, () => Is.EqualTo(300 + 100 * 0.95).Within(1e-9));
        }

        [Test]
        public void TestNewScoresGetPP()
        {
            Guid ranked = Guid.Empty, unranked = Guid.Empty;

            AddStep("save a score on a ranked map and one on an unranked map", () => Game.Realm.Write(r =>
            {
                var beatmaps = r.All<BeatmapInfo>().ToList().Where(b => !b.BeatmapSet!.Protected && b.Ruleset.ShortName == "osu").Take(2).ToList();
                beatmaps[0].Status = BeatmapOnlineStatus.Ranked;
                beatmaps[1].Status = BeatmapOnlineStatus.Graveyard;

                foreach (var beatmap in beatmaps)
                {
                    var score = new ScoreInfo(beatmap, beatmap.Ruleset)
                    {
                        BeatmapHash = beatmap.Hash,
                        Rank = ScoreRank.X,
                        Accuracy = 1,
                        MaxCombo = beatmap.TotalObjectCount,
                        Statistics = { [HitResult.Great] = beatmap.TotalObjectCount },
                        MaximumStatistics = { [HitResult.Great] = beatmap.TotalObjectCount },
                    };
                    score.User = profiles.Active.ToAPIUser();
                    r.Add(score);

                    if (beatmap == beatmaps[0])
                        ranked = score.ID;
                    else
                        unranked = score.ID;
                }
            }));

            AddUntilStep("ranked map score has pp", () => Game.Realm.Run(r => r.Find<ScoreInfo>(ranked)!.PP ?? 0), () => Is.GreaterThan(0));
            AddUntilStep("calculation finished", () => Game.PPTask.IsCompleted);
            AddAssert("unranked map score has none", () => Game.Realm.Run(r => r.Find<ScoreInfo>(unranked)!.PP), () => Is.Null);
        }

        [Test]
        public void TestClickingAProfileOpensIt()
        {
            AddStep("show the active profile", () => Game.ShowUser(profiles.Active.ToAPIUser()));
            AddUntilStep("profile overlay shown", () => Game.ProfileOverlay.State.Value, () => Is.EqualTo(Visibility.Visible));
            AddAssert("for the active profile", () => Game.ProfileOverlay.ProfileId, () => Is.EqualTo(profiles.Active.Id));
            AddStep("close it", () => Game.ProfileOverlay.Hide());

            AddStep("show a lazer player", () => Game.ShowUser(new APIUser { Id = 123, Username = "someone" }));
            AddAssert("nothing opens", () => Game.ProfileOverlay.State.Value, () => Is.EqualTo(Visibility.Hidden));
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
