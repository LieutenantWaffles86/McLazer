// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Fork.Lazer;
using osu.Game.Overlays;

namespace osu.Game.Tests.Fork.Lazer
{
    /// <summary>
    /// The whole game, started with no lazer folder to be found.
    /// </summary>
    [HeadlessTest]
    public partial class TestSceneLazerFolderPicker : McLazerGameTestScene
    {
        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private FakeLazerFolder lazer = null!;

        [Test]
        public void TestChosenFolderIsUsedFromNextStart()
        {
            FirstRunSetupOverlay overlay = null!;

            AddStep("create lazer folder", () => lazer = new FakeLazerFolder(host, audio, Resources, Beatmap.Default));
            AddAssert("no lazer folder linked", () => Game.LinkedLazerFolder, () => Is.Null);

            AddStep("show first-run setup", () => (overlay = Game.ChildrenOfType<FirstRunSetupOverlay>().Single()).Show());
            AddUntilStep("step to the lazer folder screen", () =>
            {
                if (overlay.CurrentScreen is ScreenLazerFolder { IsLoaded: true })
                    return true;

                if (overlay.CurrentScreen?.IsLoaded == true)
                    overlay.NextButton.AsNonNull().TriggerClick();

                return false;
            });

            AddStep("choose a folder that is not lazer's", () => ((ScreenLazerFolder)overlay.CurrentScreen!).Choose(Path.GetTempPath()));
            AddAssert("nothing saved", () => LazerFolder.ReadSavedPath(LocalStorage.GetFullPath(string.Empty)), () => Is.Null);

            AddStep("choose the lazer folder", () => ((ScreenLazerFolder)overlay.CurrentScreen!).Choose(lazer.Path));

            AddStep("restart game", () =>
            {
                Remove(Game, true);
                AddGame(Game = new TestMcLazerGame(LocalStorage));
            });
            AddUntilStep("wait for load", () => Game.IsLoaded);
            AddAssert("linked to the chosen folder", () => Game.LinkedLazerFolder, () => Is.EqualTo(lazer.Path));
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            lazer?.Dispose();
        }
    }
}
