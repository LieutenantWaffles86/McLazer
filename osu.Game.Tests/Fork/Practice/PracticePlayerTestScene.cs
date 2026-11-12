// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Testing.Input;
using osu.Game.Beatmaps;
using osu.Game.Fork.Practice;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Fork.Practice
{
    /// <summary>
    /// Runs a live <see cref="PracticePlayer"/> on a short osu! map, played by a bot through real key and mouse input.
    /// </summary>
    public abstract partial class PracticePlayerTestScene : RateAdjustedBeatmapTestScene
    {
        /// <summary>
        /// Time between hit circles. Circle <c>n</c> (from zero) starts at <c>(n + 1) * OBJECT_SPACING</c>.
        /// </summary>
        protected const double OBJECT_SPACING = 500;

        protected const int OBJECT_COUNT = 10;

        protected TestPracticePlayer Player = null!;

        protected InputBot Bot = null!;

        protected sealed override Ruleset CreateRuleset() => new OsuRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            // Wide hit windows (a miss is judged 200 ms after a circle), because headless tests step 33 ms at a time.
            var beatmap = new Beatmap
            {
                BeatmapInfo = { Ruleset = ruleset },
                Difficulty = { OverallDifficulty = 0 },
            };

            for (int i = 0; i < OBJECT_COUNT; i++)
            {
                beatmap.HitObjects.Add(new HitCircle
                {
                    StartTime = ObjectTime(i),
                    Position = i % 2 == 0 ? new Vector2(120, 100) : new Vector2(380, 280),
                });
            }

            return beatmap;
        }

        protected static double ObjectTime(int index) => (index + 1) * OBJECT_SPACING;

        /// <summary>
        /// A time between circle <paramref name="index"/> and the next one. No judgement or bot input can happen within 100 ms of it.
        /// </summary>
        protected static double GapAfter(int index) => ObjectTime(index) + 350;

        protected double CurrentTime => Player.DrawableRuleset.FrameStableClock.CurrentTime;

        private string? unguardedJump;
        private bool gameplayStarted;

        private readonly List<(double time, Action action)> timedActions = new List<(double, Action)>();

        protected override void Update()
        {
            base.Update();

            if (gameplayStarted && !Player.SeekSession.IsActive)
            {
                foreach (var timed in timedActions.Where(t => CurrentTime >= t.time).ToArray())
                {
                    timedActions.Remove(timed);
                    timed.action();
                }
            }

            // In a Release build, FrameStabilityContainer ignores a running clock that is over 500 ms away unless a replay is attached,
            // and gameplay freezes. That guard is off under NUnit, so check here instead that live play never sees such a jump.
            if (gameplayStarted && !Player.GameplayClockContainer.IsPaused.Value
                                && !Player.DrawableRuleset.HasReplayLoaded.Value
                                && Math.Abs(CurrentTime - Player.GameplayClockContainer.CurrentTime) > 500)
                unguardedJump ??= $"frame-stable clock at {CurrentTime:N0}, gameplay clock at {Player.GameplayClockContainer.CurrentTime:N0}";
        }

        [TearDownSteps]
        public override void TearDownSteps()
        {
            AddAssert("every clock jump had a replay attached", () => unguardedJump, () => Is.Null);
            base.TearDownSteps();
        }

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("add bot", () =>
            {
                unguardedJump = null;
                gameplayStarted = false;
                timedActions.Clear();

                if (Bot != null)
                    Remove(Bot, true);

                Add(Bot = new InputBot(InputManager));
            });
        }

        protected void LoadPlayer(params Mod[] mods)
        {
            AddStep("load player", () =>
            {
                if (Stack.CurrentScreen is Player)
                    Stack.Exit();

                var ruleset = CreateRuleset();
                Ruleset.Value = ruleset.RulesetInfo;
                Beatmap.Value = CreateWorkingBeatmap(CreateBeatmap(ruleset.RulesetInfo));
                SelectedMods.Value = mods;

                LoadScreen(Player = CreatePlayer());
                Bot.Player = Player;
            });

            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);

            // Clicking the resume cursor is covered by its own test.
            AddStep("disable resume overlay", () =>
            {
                Player.DrawableRuleset.UseResumeOverlay = false;
                gameplayStarted = true;
            });
        }

        protected virtual TestPracticePlayer CreatePlayer() => new TestPracticePlayer();

        /// <summary>
        /// Run an action during live play, on the first frame at or after a gameplay time.
        /// </summary>
        /// <remarks>
        /// Test steps run about 200 ms of gameplay apart, which is too coarse to read state between two circles.
        /// </remarks>
        protected void AtTime(double time, string description, Action action)
        {
            bool done = false;

            AddStep($"at {time}: {description}", () =>
            {
                Assert.That(CurrentTime, Is.LessThan(time), "Scheduled too late: gameplay is already past this time.");
                timedActions.Add((time, () =>
                {
                    action();
                    done = true;
                }));
            });

            AddUntilStep($"wait for {time}", () => done);
        }

        /// <summary>
        /// Seek while paused in a seek session.
        /// </summary>
        protected void SeekTo(double time)
        {
            AddStep($"seek to {time}", () => Player.PracticeSeek(time));
            WaitForSeek();
        }

        protected void WaitForSeek() => AddUntilStep("wait for seek to finish", () => Player.SeekSession.IsActive && !Player.SeekSession.IsSeeking);

        protected void WaitForResume() => AddUntilStep("wait for resume", () => !Player.SeekSession.IsActive && !Player.GameplayClockContainer.IsPaused.Value);

        /// <summary>
        /// Everything about the run's score at the current frame.
        /// </summary>
        protected ScoreState CaptureState()
        {
            var processor = Player.ScoreProcessor;

            return new ScoreState(
                CurrentTime,
                processor.TotalScore.Value,
                processor.Combo.Value,
                processor.HighestCombo.Value,
                processor.Accuracy.Value,
                Player.HealthProcessor.Health.Value,
                processor.JudgedHits,
                string.Join(", ", processor.Statistics.Where(kvp => kvp.Value > 0).OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}: {kvp.Value}")));
        }

        public record ScoreState(double Time, long TotalScore, int Combo, int HighestCombo, double Accuracy, double Health, int JudgedHits, string Statistics)
        {
            // Health is restored from the next judgement and drained back one frame at a time, so it can be off by a frame of drain.
            public virtual bool Equals(ScoreState? other) =>
                other != null
                && Math.Abs(Time - other.Time) < 1e-6
                && TotalScore == other.TotalScore
                && Combo == other.Combo
                && HighestCombo == other.HighestCombo
                && Accuracy == other.Accuracy
                && Math.Abs(Health - other.Health) < 0.01
                && JudgedHits == other.JudgedHits
                && Statistics == other.Statistics;

            public override int GetHashCode() => HashCode.Combine(TotalScore, Combo, HighestCombo, Accuracy, JudgedHits, Statistics);
        }

        public partial class TestPracticePlayer : PracticePlayer
        {
            protected override bool PauseOnFocusLost => false;

            public new DrawableRuleset DrawableRuleset => base.DrawableRuleset;

            public new GameplayClockContainer GameplayClockContainer => base.GameplayClockContainer;

            public new ScoreProcessor ScoreProcessor => base.ScoreProcessor;

            public new HealthProcessor HealthProcessor => base.HealthProcessor;

            public new SeekSession SeekSession => base.SeekSession;

            public new HUDOverlay HUDOverlay => base.HUDOverlay;

            public TestPracticePlayer(PlayerConfiguration? configuration = null)
                : base(configuration)
            {
            }
        }

        /// <summary>
        /// Plays the map with real mouse and key input: moves to each circle and holds Z around its start time.
        /// </summary>
        public partial class InputBot : Component
        {
            public TestPracticePlayer? Player;

            /// <summary>
            /// Indices of circles the bot leaves alone.
            /// </summary>
            public readonly HashSet<int> Skip = new HashSet<int>();

            // Input takes a frame or two to reach gameplay, so press early. Held long enough that a press is never lost between frames.
            private const double press_lead = 40;
            private const double press_duration = 150;

            private readonly ManualInputManager input;

            private bool pressed;

            public InputBot(ManualInputManager input)
            {
                this.input = input;
            }

            protected override void Update()
            {
                base.Update();

                if (Player?.IsLoaded != true || Player.DrawableRuleset == null)
                    return;

                HitCircle? target = null;

                // Like a player, the bot does nothing while the game is paused or seeking.
                if (!Player.GameplayClockContainer.IsPaused.Value)
                {
                    double time = Player.DrawableRuleset.FrameStableClock.CurrentTime;

                    target = Player.DrawableRuleset.Objects.OfType<HitCircle>()
                                   .Where((_, index) => !Skip.Contains(index))
                                   .FirstOrDefault(h => time <= h.StartTime + press_duration);

                    if (target != null)
                        input.MoveMouseTo(Player.DrawableRuleset.Playfield.GamefieldToScreenSpace(target.StackedPosition));

                    if (target != null && time < target.StartTime - press_lead)
                        target = null;
                }

                if (target != null == pressed)
                    return;

                pressed = target != null;

                if (pressed)
                    input.PressKey(Key.Z);
                else
                    input.ReleaseKey(Key.Z);
            }

            protected override void Dispose(bool isDisposing)
            {
                if (pressed)
                    input.ReleaseKey(Key.Z);

                base.Dispose(isDisposing);
            }
        }
    }
}
