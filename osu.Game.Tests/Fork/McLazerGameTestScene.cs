// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Fork;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK.Graphics;
using IntroSequence = osu.Game.Configuration.IntroSequence;

namespace osu.Game.Tests.Fork
{
    /// <summary>
    /// Runs the whole of <see cref="McLazerGame"/>, the same game class the desktop build derives from.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="OsuGameTestScene"/>, which is tied to a game deriving from <see cref="OsuGame"/> with a dummy online API.
    /// </remarks>
    public abstract partial class McLazerGameTestScene : OsuManualInputManagerTestScene
    {
        protected TestMcLazerGame Game = null!;

        protected override bool UseFreshStoragePerRun => true;

        protected override bool CreateNestedActionContainer => false;

        protected override bool DisplayCursorForManualInput => false;

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Black,
            };
        }

        [SetUpSteps]
        public virtual void SetUpSteps()
        {
            AddStep("create game", () =>
            {
                if (Game?.Parent != null)
                    Remove(Game, true);

                RecycleLocalStorage(false);

                AddGame(Game = CreateTestGame());
            });

            AddUntilStep("wait for load", () => Game.IsLoaded);
            AddUntilStep("wait for main menu", () => Game.ScreenStack.CurrentScreen is MainMenu menu && menu.IsLoaded);
        }

        [TearDownSteps]
        public virtual void TearDownSteps()
        {
            if (DebugUtils.IsNUnitRunning)
            {
                AddStep("exit game", () => Game.Exit());
                AddUntilStep("wait for game exit", () => Game.Parent == null);
                AddStep("dispose game", () => Game.Dispose());
            }
        }

        protected virtual TestMcLazerGame CreateTestGame() => new TestMcLazerGame(LocalStorage);

        protected void PushAndConfirm(Func<Screen> newScreen)
        {
            Screen screen = null!;

            AddStep("push screen", () => Game.ScreenStack.Push(screen = newScreen()));
            AddUntilStep("wait for screen", () => screen.IsLoaded && Game.ScreenStack.CurrentScreen == screen);
        }

        public partial class TestMcLazerGame : McLazerGame
        {
            public new IAPIProvider API => base.API;

            public RealmAccess Realm => Dependencies.Get<RealmAccess>();

            public new BeatmapManager BeatmapManager => base.BeatmapManager;

            public new OsuConfigManager LocalConfig => base.LocalConfig;

            public new Storage Storage => base.Storage;

            public new SkinManager SkinManager => base.SkinManager;

            public new Bindable<WorkingBeatmap> Beatmap => base.Beatmap;

            public new NotificationOverlay Notifications => base.Notifications;

            public new Bindable<RulesetInfo> Ruleset => base.Ruleset;

            // Under NUnit, these would otherwise be NUnit's own version.
            public override Version AssemblyVersion => new Version(0, 0);
            public override string Version => "test game";

            protected override Loader CreateLoader() => new OsuGameTestScene.TestLoader();

            // A real lazer may be running on the machine that runs the tests.
            protected override bool IsLazerRunning() => false;

            public TestMcLazerGame(Storage storage)
            {
                base.Storage = storage;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                LocalConfig.SetValue(OsuSetting.IntroSequence, IntroSequence.Circles);
                LocalConfig.SetValue(OsuSetting.ShowFirstRunSetup, false);

                Dependencies.Get<SessionStatics>().SetValue(Static.MutedAudioNotificationShownOnce, true);
            }

            protected override void Update()
            {
                base.Update();

                // A headless window never has focus, and the game pauses and mutes without it.
                ((Bindable<bool>)IsActive).Value = true;
            }
        }
    }
}
