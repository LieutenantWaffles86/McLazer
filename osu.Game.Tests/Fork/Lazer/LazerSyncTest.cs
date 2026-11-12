// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Input.Bindings;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Fork.Lazer;
using osu.Game.Input.Bindings;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using Realms;

namespace osu.Game.Tests.Fork.Lazer
{
    [HeadlessTest]
    public partial class LazerSyncTest : OsuTestScene
    {
        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private FakeLazerFolder lazer = null!;
        private Guid setId;

        [BackgroundDependencyLoader]
        private void load()
        {
            // Registers rulesets in McLazer's own database.
            Dependencies.Cache(new RealmRulesetStore(Realm));
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create lazer folder with one beatmap set", () =>
            {
                lazer?.Dispose();
                lazer = new FakeLazerFolder(host, audio, Resources, Beatmap.Default);
                setId = lazer.ImportBeatmap();
            });

            AddStep("empty own database", () => Realm.Write(r =>
            {
                r.RemoveAll<ScoreInfo>();
                r.RemoveAll<BeatmapInfo>();
                r.RemoveAll<BeatmapMetadata>();
                r.RemoveAll<BeatmapSetInfo>();
                r.RemoveAll<RealmFile>();
                r.RemoveAll<SkinInfo>();
                r.RemoveAll<BeatmapCollection>();
                r.RemoveAll<RealmKeyBinding>();

                foreach (var ruleset in r.All<RulesetInfo>())
                    ruleset.Available = true;
            }));
        }

        private LazerSyncResult sync(bool lazerRunning = false) => LazerSync.Run(Realm, lazer.Path, LocalStorage, () => lazerRunning);

        private int ownSets => Realm.Run(r => r.All<BeatmapSetInfo>().Count());

        [Test]
        public void TestSyncCopiesRecordsNotFiles()
        {
            AddAssert("sync succeeds", () => sync(), () => Is.EqualTo(LazerSyncResult.Synced));

            AddAssert("set copied with lazer's ID", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)), () => Is.Not.Null);
            AddAssert("difficulties match lazer's", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.Select(b => b.MD5Hash).OrderBy(h => h).ToArray()),
                () => Is.EqualTo(lazerRun(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.Select(b => b.MD5Hash).OrderBy(h => h).ToArray())));
            AddAssert("file records match lazer's", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Files.Select(f => f.Filename + f.File.Hash).OrderBy(f => f).ToArray()),
                () => Is.EqualTo(lazerRun(r => r.Find<BeatmapSetInfo>(setId)!.Files.Select(f => f.Filename + f.File.Hash).OrderBy(f => f).ToArray())).And.Not.Empty);
            AddAssert("no files copied", () => !LocalStorage.ExistsDirectory("files") || !LocalStorage.GetStorageForDirectory("files").GetDirectories(string.Empty).Any());
            AddAssert("snapshot removed", () => !LocalStorage.ExistsDirectory("lazer-snapshot"));
        }

        [Test]
        public void TestLazerFolderIsNeverChanged()
        {
            System.Collections.Generic.Dictionary<string, string> before = null!;

            AddStep("fingerprint lazer folder", () => before = lazer.Fingerprint());

            AddStep("sync twice", () =>
            {
                sync();
                sync();
            });

            AddStep("read every beatmap file through the fallback", () =>
            {
                var fallback = new FallbackReadStorage(LocalStorage.GetStorageForDirectory("files"), host.GetStorage(Path.Combine(lazer.Path, "files")));

                string[] paths = Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Files.Select(f => f.File.GetStoragePath()).ToArray());
                Assert.That(paths, Is.Not.Empty);

                foreach (string path in paths)
                {
                    using (var stream = fallback.GetStream(path))
                        Assert.That(stream, Is.Not.Null, path);
                }
            });

            AddAssert("lazer folder unchanged", () => lazer.Fingerprint(), () => Is.EqualTo(before));
        }

        [Test]
        public void TestSecondSyncUpdatesInPlace()
        {
            AddStep("sync", () => sync());
            AddStep("set an offset in McLazer", () => Realm.Write(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.First().UserSettings.Offset = 12));

            AddStep("lazer learns the set is ranked", () => lazer.Write(r =>
            {
                var set = r.Find<BeatmapSetInfo>(setId)!;
                set.Status = BeatmapOnlineStatus.Ranked;
                foreach (var b in set.Beatmaps)
                    b.Status = BeatmapOnlineStatus.Ranked;
            }));

            AddAssert("sync succeeds", () => sync(), () => Is.EqualTo(LazerSyncResult.Synced));
            AddAssert("still one set", () => ownSets, () => Is.EqualTo(1));
            AddAssert("ranked status copied", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.All(b => b.Status == BeatmapOnlineStatus.Ranked)));
            AddAssert("unchanged set was not replaced", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.First().UserSettings.Offset), () => Is.EqualTo(12));
        }

        [Test]
        public void TestChangedSetIsReplacedAndKeepsMatchingScores()
        {
            Guid keptBeatmap = Guid.Empty;
            Guid changedBeatmap = Guid.Empty;

            AddStep("sync", () => sync());
            AddStep("set a score on two difficulties", () => Realm.Write(r =>
            {
                var beatmaps = r.Find<BeatmapSetInfo>(setId)!.Beatmaps;
                keptBeatmap = beatmaps[0].ID;
                changedBeatmap = beatmaps[1].ID;

                foreach (var beatmap in beatmaps.Take(2))
                    r.Add(new ScoreInfo(beatmap, beatmap.Ruleset) { BeatmapHash = beatmap.Hash });
            }));

            AddStep("lazer changes one difficulty", () => lazer.Write(r =>
            {
                var set = r.Find<BeatmapSetInfo>(setId)!;
                set.Hash = "changed";
                set.Beatmaps.First(b => b.ID == changedBeatmap).Hash = "changed";
            }));

            AddAssert("sync succeeds", () => sync(), () => Is.EqualTo(LazerSyncResult.Synced));
            AddAssert("set replaced", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Hash), () => Is.EqualTo("changed"));
            AddAssert("both scores kept", () => Realm.Run(r => r.All<ScoreInfo>().Count()), () => Is.EqualTo(2));
            AddAssert("score stays on the unchanged difficulty", () => Realm.Run(r => r.Find<BeatmapInfo>(keptBeatmap)!.Scores.Count()), () => Is.EqualTo(1));
            AddAssert("score leaves the changed difficulty", () => Realm.Run(r => r.Find<BeatmapInfo>(changedBeatmap)!.Scores.Count()), () => Is.Zero);
        }

        [Test]
        public void TestSetRemovedInLazer()
        {
            AddStep("sync", () => sync());
            AddStep("lazer deletes the set", () => lazer.Write(r => r.Find<BeatmapSetInfo>(setId)!.DeletePending = true));

            AddAssert("sync succeeds", () => sync(), () => Is.EqualTo(LazerSyncResult.Synced));
            AddAssert("set removed", () => ownSets, () => Is.Zero);
            AddAssert("no difficulties left behind", () => Realm.Run(r => r.All<BeatmapInfo>().Count() + r.All<BeatmapMetadata>().Count()), () => Is.Zero);
        }

        [Test]
        public void TestSetRemovedInLazerKeepsMcLazerScores()
        {
            AddStep("sync", () => sync());
            AddStep("set a score in McLazer", () => Realm.Write(r =>
            {
                var beatmap = r.Find<BeatmapSetInfo>(setId)!.Beatmaps.First();
                r.Add(new ScoreInfo(beatmap, beatmap.Ruleset) { BeatmapHash = beatmap.Hash });
            }));
            AddStep("lazer deletes the set", () => lazer.Write(r => r.Find<BeatmapSetInfo>(setId)!.DeletePending = true));

            AddAssert("sync succeeds", () => sync(), () => Is.EqualTo(LazerSyncResult.Synced));
            AddAssert("score kept", () => Realm.Run(r => r.All<ScoreInfo>().Count()), () => Is.EqualTo(1));
            AddAssert("set hidden from song select", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.All(b => b.Hidden)));
        }

        [Test]
        public void TestDifficultiesOfMissingRulesetsAreLeftOut()
        {
            AddStep("this build has no mania", () => Realm.Write(r => r.Find<RulesetInfo>("mania")!.Available = false));
            AddStep("one lazer difficulty is mania", () => lazer.Write(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.First().Ruleset = r.Find<RulesetInfo>("mania")!));

            AddAssert("sync succeeds", () => sync(), () => Is.EqualTo(LazerSyncResult.Synced));
            AddAssert("lazer's other difficulties copied", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.Count),
                () => Is.EqualTo(lazerRun(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.Count(b => b.Ruleset.ShortName != "mania"))).And.GreaterThan(0));
            AddAssert("no mania difficulty", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.All(b => b.Ruleset.ShortName != "mania")));
        }

        [Test]
        public void TestNoSyncWhileLazerRuns()
        {
            AddStep("sync", () => sync());
            AddStep("lazer deletes the set", () => lazer.Write(r => r.Find<BeatmapSetInfo>(setId)!.DeletePending = true));

            AddAssert("sync refused", () => sync(lazerRunning: true), () => Is.EqualTo(LazerSyncResult.LazerRunning));
            AddAssert("last sync's data kept", () => ownSets, () => Is.EqualTo(1));
        }

        [Test]
        public void TestNewerLazerDatabase()
        {
            AddStep("sync", () => sync());
            AddStep("lazer updates past McLazer's schema", () =>
            {
                using (Realms.Realm.GetInstance(new RealmConfiguration(Path.Combine(lazer.Path, OsuGameBase.CLIENT_DATABASE_FILENAME)) { SchemaVersion = 99999 }))
                {
                }
            });

            AddAssert("sync reports newer lazer", () => sync(), () => Is.EqualTo(LazerSyncResult.LazerNewer));
            AddAssert("last sync's data kept", () => ownSets, () => Is.EqualTo(1));
            AddAssert("snapshot removed", () => !LocalStorage.ExistsDirectory("lazer-snapshot"));
        }

        [Test]
        public void TestUnreadableLazerDatabase()
        {
            AddStep("sync", () => sync());
            AddStep("corrupt lazer's database", () => File.WriteAllBytes(Path.Combine(lazer.Path, OsuGameBase.CLIENT_DATABASE_FILENAME), new byte[4096]));

            AddAssert("sync reports unreadable", () => sync(), () => Is.EqualTo(LazerSyncResult.Unreadable));
            AddAssert("last sync's data kept", () => ownSets, () => Is.EqualTo(1));
        }

        [Test]
        public void TestUserTagsFollowLazer()
        {
            AddStep("sync", () => sync());
            AddStep("lazer learns user tags", () => lazer.Write(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.First().Metadata.UserTags.Add("tech")));
            AddStep("sync", () => sync());
            AddAssert("tags copied", () => Realm.Run(r => r.Find<BeatmapSetInfo>(setId)!.Beatmaps.SelectMany(b => b.Metadata.UserTags).Distinct().ToArray()), () => Is.EqualTo(new[] { "tech" }));
        }

        [Test]
        public void TestSkinsFollowLazer()
        {
            Guid skinId = Guid.Empty;

            AddStep("add a skin in lazer", () => lazer.Write(r =>
            {
                var skin = new SkinInfo("lazer skin", "someone", typeof(LegacySkin).AssemblyQualifiedName) { Hash = "1" };
                skin.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = "abc" }, "skin.ini"));
                skinId = r.Add(skin).ID;
            }));
            AddStep("add a built-in skin to own database", () => Realm.Write(r => r.Add(new SkinInfo("built-in") { Protected = true })));
            AddStep("sync", () => sync());

            AddAssert("skin copied with its file record", () => Realm.Run(r => r.Find<SkinInfo>(skinId)!.Files.Single().File.Hash), () => Is.EqualTo("abc"));

            AddStep("save the skin in lazer", () => lazer.Write(r =>
            {
                var skin = r.Find<SkinInfo>(skinId)!;
                skin.Name = "renamed";
                skin.Hash = "2";
            }));
            AddStep("sync", () => sync());
            AddAssert("skin updated", () => Realm.Run(r => r.Find<SkinInfo>(skinId)!.Name), () => Is.EqualTo("renamed"));

            AddStep("delete the skin in lazer", () => lazer.Write(r => r.Find<SkinInfo>(skinId)!.DeletePending = true));
            AddStep("sync", () => sync());
            AddAssert("only the built-in skin is left", () => Realm.Run(r => r.All<SkinInfo>().ToList().Select(s => s.Name).ToArray()), () => Is.EqualTo(new[] { "built-in" }));
        }

        [Test]
        public void TestCollectionsFollowLazer()
        {
            AddStep("add a collection in lazer", () => lazer.Write(r => r.Add(new BeatmapCollection("lazer's", new[] { "md5" }))));
            AddStep("add a collection in McLazer", () => Realm.Write(r => r.Add(new BeatmapCollection("own"))));
            AddStep("sync", () => sync());

            AddAssert("only lazer's collection is left", () => Realm.Run(r => r.All<BeatmapCollection>().ToList().Select(c => c.Name + c.BeatmapMD5Hashes.Single()).ToArray()),
                () => Is.EqualTo(new[] { "lazer'smd5" }));
        }

        [Test]
        public void TestKeyBindingsFollowLazer()
        {
            AddStep("bind actions in lazer", () => lazer.Write(r =>
            {
                r.Add(new RealmKeyBinding(1, new KeyCombination(InputKey.A)));
                r.Add(new RealmKeyBinding(1, new KeyCombination(InputKey.B)));
                r.Add(new RealmKeyBinding(1, new KeyCombination(InputKey.C), "osu"));
            }));
            AddStep("bind actions in McLazer", () => Realm.Write(r =>
            {
                r.Add(new RealmKeyBinding(1, new KeyCombination(InputKey.X)));
                r.Add(new RealmKeyBinding(10000, new KeyCombination(InputKey.Y)));
            }));
            AddStep("sync", () => sync());
            AddStep("sync again", () => sync());

            AddAssert("lazer's bindings replace McLazer's, and McLazer-only actions keep theirs",
                () => Realm.Run(r => r.All<RealmKeyBinding>().ToList().Select(b => $"{b.RulesetName}:{b.ActionInt}:{b.KeyCombinationString}").OrderBy(b => b).ToArray()),
                () => Is.EqualTo(new[] { $":1:{(int)InputKey.A}", $":1:{(int)InputKey.B}", $":10000:{(int)InputKey.Y}", $"osu:1:{(int)InputKey.C}" }));
        }

        [Test]
        public void TestHostSettingsAreBorrowed()
        {
            string pointer = null!;
            System.Collections.Generic.Dictionary<string, string> before = null!;

            AddStep("lazer's data was moved, leaving stale settings behind", () =>
            {
                pointer = Path.Combine(Path.GetTempPath(), $"mclazer-test-pointer-{Guid.NewGuid()}");
                Directory.CreateDirectory(pointer);
                File.WriteAllText(Path.Combine(pointer, "storage.ini"), $"FullPath = {lazer.Path}\n");
                File.WriteAllText(Path.Combine(pointer, "input.json"), "stale");
                File.WriteAllText(Path.Combine(lazer.Path, "input.json"), "lazer's input");
                File.WriteAllText(Path.Combine(pointer, "framework.ini"), "lazer's framework");
                before = lazer.Fingerprint();
            });
            AddStep("McLazer has its own input settings", () => File.WriteAllText(LocalStorage.GetFullPath("input.json"), "own"));

            AddStep("borrow", () => LazerFolder.BorrowHostSettings(LocalStorage.GetFullPath(string.Empty), pointer));

            AddAssert("input settings replaced by the moved folder's", () => File.ReadAllText(LocalStorage.GetFullPath("input.json")), () => Is.EqualTo("lazer's input"));
            AddAssert("framework settings found in the default folder", () => File.ReadAllText(LocalStorage.GetFullPath("framework.ini")), () => Is.EqualTo("lazer's framework"));
            AddAssert("lazer folder unchanged", () => lazer.Fingerprint(), () => Is.EqualTo(before));

            AddStep("clean up", () => Directory.Delete(pointer, true));
        }

        [Test]
        public void TestResolveFollowsStorageIni()
        {
            string pointer = null!;

            AddStep("create a folder that points at the lazer folder", () =>
            {
                pointer = Path.Combine(Path.GetTempPath(), $"mclazer-test-pointer-{Guid.NewGuid()}");
                Directory.CreateDirectory(pointer);
                File.WriteAllText(Path.Combine(pointer, "storage.ini"), $"FullPath = {lazer.Path}\n");
            });

            AddAssert("moved folder found", () => LazerFolder.Resolve(pointer), () => Is.EqualTo(lazer.Path));
            AddAssert("direct folder found", () => LazerFolder.Resolve(lazer.Path), () => Is.EqualTo(lazer.Path));
            AddAssert("folder without a database is not a lazer folder", () => LazerFolder.Resolve(Path.GetTempPath()), () => Is.Null);

            AddStep("clean up", () => Directory.Delete(pointer, true));
        }

        private T lazerRun<T>(Func<Realm, T> action)
        {
            T result = default!;
            lazer.Write(r => result = action(r));
            return result;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            lazer?.Dispose();
        }
    }
}
