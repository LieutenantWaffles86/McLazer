// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Fork.Offline;
using osu.Game.Fork.Practice;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Overlays.Toolbar;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Tests.Beatmaps.IO;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Offline
{
    [HeadlessTest]
    public partial class TestSceneOfflineGame : McLazerGameTestScene
    {
        [Test]
        public void TestAPIIsOffline()
        {
            AddAssert("API is the offline API", () => Game.API, Is.InstanceOf<OfflineAPIAccess>);
            AddAssert("state is offline", () => Game.API.State.Value, () => Is.EqualTo(APIState.Offline));
            AddAssert("not logged in", () => !Game.API.IsLoggedIn);
            AddAssert("endpoints cannot resolve", () => new Uri(Game.API.Endpoints.APIUrl).Host, () => Does.EndWith(".invalid"));
            AddAssert("game endpoints match", () => Game.CreateEndpoints().APIUrl, () => Is.EqualTo(Game.API.Endpoints.APIUrl));
        }

        [Test]
        public void TestRequestsFailAtOnce()
        {
            GetUsersRequest queued = null!;
            GetUsersRequest performed = null!;
            Exception? failure = null;

            AddStep("queue a request", () =>
            {
                queued = new GetUsersRequest(new[] { 2 });
                queued.Failure += e => failure = e;
                Game.API.Queue(queued);
            });
            AddAssert("failed before any frame passed", () => queued.CompletionState, () => Is.EqualTo(APIRequestCompletionState.Failed));

            // The callback runs on the API's scheduler, so this also proves the API is in the scene graph.
            AddUntilStep("failure callback ran", () => failure, () => Is.Not.Null);

            AddStep("perform a request", () => Game.API.Perform(performed = new GetUsersRequest(new[] { 2 })));
            AddAssert("failed", () => performed.CompletionState, () => Is.EqualTo(APIRequestCompletionState.Failed));

            AddStep("perform a request async", () => Game.API.PerformAsync(performed = new GetUsersRequest(new[] { 2 })).WaitSafely());
            AddAssert("failed", () => performed.CompletionState, () => Is.EqualTo(APIRequestCompletionState.Failed));
        }

        [Test]
        public void TestLoginDoesNothing()
        {
            AddStep("try to log in", () => Game.API.Login("someone", "password"));
            AddAssert("still offline", () => Game.API.State.Value, () => Is.EqualTo(APIState.Offline));
            AddAssert("still not logged in", () => !Game.API.IsLoggedIn);
        }

        [Test]
        public void TestToolbarHasNoOnlineButtons()
        {
            AddAssert("no news button", () => !Game.ChildrenOfType<ToolbarNewsButton>().Any());
            AddAssert("no changelog button", () => !Game.ChildrenOfType<ToolbarChangelogButton>().Any());
            AddAssert("no wiki button", () => !Game.ChildrenOfType<ToolbarWikiButton>().Any());
            AddAssert("no rankings button", () => !Game.ChildrenOfType<ToolbarRankingsButton>().Any());
            AddAssert("no beatmap listing button", () => !Game.ChildrenOfType<ToolbarBeatmapListingButton>().Any());
            AddAssert("no chat button", () => !Game.ChildrenOfType<ToolbarChatButton>().Any());
            AddAssert("no social button", () => !Game.ChildrenOfType<ToolbarSocialButton>().Any());

            AddAssert("settings button kept", () => Game.ChildrenOfType<ToolbarSettingsButton>().Count(), () => Is.EqualTo(1));
            AddAssert("music button kept", () => Game.ChildrenOfType<ToolbarMusicButton>().Count(), () => Is.EqualTo(1));
        }

        [Test]
        public void TestMainMenuHasNoOnlineButtons()
        {
            AddAssert("no daily challenge button", () => !Game.ChildrenOfType<DailyChallengeButton>().Any());

            // Upstream has five buttons from the play menu up: back, solo, multi, playlists and daily challenge.
            AddAssert("play menu is solo and back", () => Game.ChildrenOfType<MainMenuButton>().Count(b => b.VisibleStateMin == ButtonSystemState.Play), () => Is.EqualTo(2));
            AddAssert("top level is settings, play and maybe exit", () => Game.ChildrenOfType<MainMenuButton>().Count(b => b.VisibleStateMin == ButtonSystemState.TopLevel),
                () => Is.InRange(2, 3));
        }

        [Test]
        public void TestNoEditors()
        {
            SoloSongSelect songSelect = null!;

            AddStep("press skin editor hotkey", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.Key(Key.S);
                InputManager.ReleaseKey(Key.ShiftLeft);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("skin editor stays closed", () => Game.ChildrenOfType<SkinEditorOverlay>().All(e => e.State.Value == Visibility.Hidden));

            PushAndConfirm(() => songSelect = new SoloSongSelect());
            AddUntilStep("wait for song select", () => songSelect.CarouselItemsPresented);
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddUntilStep("wait for selection", () => !Game.Beatmap.IsDefault);

            AddAssert("no edit action on beatmaps", () => songSelect.GetForwardActions(Game.Beatmap.Value.BeatmapInfo).Count(i => i.Text.Value.ToString() == "Edit"), () => Is.Zero);
            AddAssert("play action kept", () => songSelect.GetForwardActions(Game.Beatmap.Value.BeatmapInfo).Count(i => i.Text.Value.ToString() == "Play"), () => Is.EqualTo(1));

            AddStep("ask song select to edit", () => songSelect.Edit(Game.Beatmap.Value.BeatmapInfo));
            AddWaitStep("wait", 5);
            AddAssert("still at song select", () => Game.ScreenStack.CurrentScreen, () => Is.SameAs(songSelect));
        }

        [Test]
        public void TestSongSelectOnlyHasLocalLeaderboard()
        {
            PushAndConfirm(() => new SoloSongSelect());

            AddUntilStep("scope dropdown loaded", () => Game.ChildrenOfType<ShearedDropdown<BeatmapLeaderboardScope>>().SingleOrDefault()?.IsLoaded == true);
            AddAssert("only the local scope is offered", () => Game.ChildrenOfType<ShearedDropdown<BeatmapLeaderboardScope>>().Single().Items, () => Is.EqualTo(new[] { BeatmapLeaderboardScope.Local }));
            AddAssert("local scope is selected", () => Game.ChildrenOfType<ShearedDropdown<BeatmapLeaderboardScope>>().Single().Current.Value, () => Is.EqualTo(BeatmapLeaderboardScope.Local));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestPlayFromSongSelect(bool practice)
        {
            Player? player = null;
            SoloSongSelect songSelect = null!;

            PushAndConfirm(() => songSelect = new SoloSongSelect());
            AddUntilStep("wait for song select", () => songSelect.CarouselItemsPresented);

            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddUntilStep("wait for selection", () => !Game.Beatmap.IsDefault);

            if (practice)
                AddStep("turn on practice", () => Game.ChildrenOfType<FooterButtonPractice>().Single().TriggerClick());

            AddStep("press enter", () => InputManager.Key(Key.Enter));
            AddUntilStep("wait for player", () =>
            {
                Game.Notifications.State.Value = Visibility.Hidden;
                return (player = Game.ScreenStack.CurrentScreen as Player)?.IsLoaded == true;
            });

            AddAssert("player is the practice player", () => player, Is.InstanceOf<PracticePlayer>);
            AddAssert($"run is {(practice ? "practice" : "normal")}", () => ((PracticePlayer)player!).IsPractice, () => Is.EqualTo(practice));
            AddUntilStep("gameplay clock running", () => player.ChildrenOfType<GameplayClockContainer>().Single().IsRunning);
            AddAssert("still offline", () => Game.API.State.Value, () => Is.EqualTo(APIState.Offline));
        }
    }
}
