// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Fork.Lazer;
using osu.Game.Models;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Skinning;
using osu.Game.Tests.Resources;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Lazer
{
    /// <summary>
    /// The whole game, started next to a lazer folder with one beatmap set in it.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneLazerLinkedGame : McLazerGameTestScene
    {
        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private FakeLazerFolder lazer = null!;
        private Dictionary<string, string> lazerBefore = null!;

        private static readonly Guid lazer_skin = Guid.NewGuid();

        public override void SetUpSteps()
        {
            AddStep("create lazer folder with one beatmap set", () =>
            {
                lazer?.Dispose();
                lazer = new FakeLazerFolder(host, audio, Resources, Beatmap.Default);
                lazer.ImportBeatmap();
                string skinIni = lazer.AddFile("[General]\nVersion: 2.3\n");
                lazer.Write(r =>
                {
                    var skin = new SkinInfo("lazer skin", "someone", typeof(LegacySkin).AssemblyQualifiedName) { ID = lazer_skin };
                    skin.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = skinIni }, "skin.ini"));
                    r.Add(skin);
                });
                File.WriteAllText(Path.Combine(lazer.Path, "game.ini"), $"Token = secret\nSkin = {lazer_skin}\nMenuVoice = False\n");
                lazerBefore = lazer.Fingerprint();
            });

            base.SetUpSteps();

            AddUntilStep("wait for sync", () => Game.LazerSyncTask.IsCompleted);
        }

        protected override TestMcLazerGame CreateTestGame() => new TestMcLazerGame(LocalStorage) { LazerPath = lazer.Path };

        // Not counting the game's own bundled intro track.
        private int librarySets => Game.Realm.Run(r => r.All<BeatmapSetInfo>().Count(s => !s.Protected && !s.DeletePending));

        [Test]
        public void TestLazerBeatmapsArePlayable()
        {
            Player? player = null;
            SoloSongSelect songSelect = null!;

            AddAssert("linked to the lazer folder", () => Game.LinkedLazerFolder, () => Is.EqualTo(lazer.Path));
            AddAssert("sync succeeded", () => Game.LazerSyncTask.GetResultSafely(), () => Is.EqualTo(LazerSyncResult.Synced));
            AddAssert("lazer's set is in the library", () => librarySets, () => Is.EqualTo(1));

            AddStep("pick the osu! ruleset", () => Game.Ruleset.Value = new OsuRuleset().RulesetInfo);
            PushAndConfirm(() => songSelect = new SoloSongSelect());
            AddUntilStep("wait for song select", () => songSelect.CarouselItemsPresented);
            AddUntilStep("a lazer beatmap is selected", () => !Game.Beatmap.IsDefault);

            AddAssert("beatmap file read from lazer's folder", () => Game.Beatmap.Value.Beatmap.HitObjects.Count, () => Is.GreaterThan(0));
            AddAssert("audio file read from lazer's folder", () => Game.Beatmap.Value.Track.Length, () => Is.GreaterThan(0));

            AddStep("press enter", () => InputManager.Key(Key.Enter));
            AddUntilStep("wait for player", () =>
            {
                Game.Notifications.State.Value = Visibility.Hidden;
                return (player = Game.ScreenStack.CurrentScreen as Player)?.IsLoaded == true;
            });
            AddUntilStep("gameplay clock running", () => player.ChildrenOfType<GameplayClockContainer>().Single().IsRunning);

            AddAssert("no file was copied into McLazer", () => Game.Storage.GetStorageForDirectory("files").GetDirectories(string.Empty)
                                                                 .SelectMany(d => Game.Storage.GetStorageForDirectory("files").GetFiles(d, "*")).Count(),
                () => Is.LessThan(lazerBefore.Count(f => f.Key.StartsWith("files", StringComparison.Ordinal))));
            AddAssert("lazer folder unchanged", () => lazer.Fingerprint(), () => Is.EqualTo(lazerBefore));
        }

        [Test]
        public void TestLazerSettingsAreBorrowed()
        {
            AddAssert("lazer's setting is in effect", () => Game.LocalConfig.Get<bool>(OsuSetting.MenuVoice), () => Is.False);
            AddUntilStep("lazer's skin is in use", () => Game.SkinManager.CurrentSkinInfo.Value.ID, () => Is.EqualTo(lazer_skin));
            AddAssert("the skin's files are read from lazer's folder", () => Game.SkinManager.CurrentSkin.Value.Configuration.LegacyVersion, () => Is.EqualTo(2.3m));
            AddAssert("lazer's login token was not copied", () => File.ReadAllText(Game.Storage.GetFullPath("game.ini")), () => Does.Not.Contain("secret").And.Contain("MenuVoice"));
            AddAssert("lazer folder unchanged", () => lazer.Fingerprint(), () => Is.EqualTo(lazerBefore));
        }

        [Test]
        public void TestImportIsRefused()
        {
            AddStep("try to import a beatmap", () => Game.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely());
            AddWaitStep("wait", 5);
            AddAssert("only lazer's set is in the library", () => librarySets, () => Is.EqualTo(1));
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            lazer?.Dispose();
        }
    }
}
