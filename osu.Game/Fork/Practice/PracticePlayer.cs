// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// The player for all local play. A run is either normal or practice.
    /// </summary>
    /// <remarks>
    /// A normal run plays as in lazer's <see cref="SoloPlayer"/> and is saved locally. Nothing is submitted online.
    /// A practice run can jump in time (see <see cref="SeekSession"/>), never fails and is never saved as a score.
    /// Using a practice tool in a normal run asks first, then turns it into a practice run.
    /// </remarks>
    public partial class PracticePlayer : SoloPlayer, IKeyBindingHandler<GlobalAction>
    {
        /// <summary>
        /// How far the seek keys jump, as in replays.
        /// </summary>
        public const double SEEK_STEP = 5 * ReplayPlayer.BASE_SEEK_AMOUNT;

        /// <summary>
        /// How far the frame step keys jump: one frame at 60 fps.
        /// </summary>
        public const double FRAME_STEP = 1000 / 60.0;

        /// <summary>
        /// Whether the run starts as a practice run (the practice toggle at song select).
        /// </summary>
        public bool StartAsPractice { get; init; }

        /// <summary>
        /// Whether this run is a practice run. Once set, it stays set for the run.
        /// </summary>
        public bool IsPractice { get; private set; }

        /// <summary>
        /// The quick-save checkpoint, if any. Saved per profile and map.
        /// </summary>
        public double? Checkpoint { get; private set; }

        /// <summary>
        /// How many times the checkpoint has been loaded since it was set.
        /// </summary>
        public int Attempts { get; private set; }

        // The progress bar never seeks: it is optional and skins can leave it out. Seeks come from keys only.
        // Upstream turns it on while a replay is loaded, which a seek session is.
        public override IBindable<bool> SeekingAllowed { get; } = new BindableBool();

        protected SeekSession SeekSession { get; private set; } = null!;

        protected ScrubOverlay ScrubOverlay { get; private set; } = null!;

        /// <summary>
        /// What the HUD shows: the current section.
        /// </summary>
        protected SectionScore SectionScore { get; private set; } = null!;

        /// <summary>
        /// Asks whether a normal run should become a practice run.
        /// </summary>
        protected PracticeMenuOverlay PracticePrompt { get; private set; } = null!;

        /// <summary>
        /// Offers the next step when a practice run reaches the end of the map.
        /// </summary>
        protected PracticeMenuOverlay EndPrompt { get; private set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        /// <summary>
        /// Whether the scrub key is held. Seeks then stay at their target, so the player can scrub over their own attempt, and resume when it is released.
        /// </summary>
        private bool scrubbing;

        private bool resumeOnArrival;
        private OsuSpriteText seekingText = null!;
        private OsuSpriteText toast = null!;
        private double seekingFor;

        /// <summary>
        /// Times where health hit zero. A normal run would have failed there.
        /// </summary>
        private readonly List<double> failPoints = new List<double>();

        private bool endPromptShown;
        private bool resultsRequested;
        private int lastMarkerState;

        private int profileId;
        private string beatmapMD5 = string.Empty;

        public PracticePlayer(PlayerConfiguration? configuration = null)
            : base(configuration)
        {
        }

        // Called from the base load, before this class's own.
        protected override ScoreProcessor CreateHUDScoreProcessor() => (SectionScore = new SectionScore(GameplayState, ScoreProcessor)).Processor;

        [BackgroundDependencyLoader]
        private void load()
        {
            if (!LoadedBeatmapSuccessfully)
                return;

            AddInternal(SeekSession = new SeekSession(DrawableRuleset, GameplayClockContainer, GameplayState));
            AddInternal(seekingText = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = @"Seeking…",
                Font = OsuFont.GetFont(size: 30, weight: FontWeight.Bold),
                Alpha = 0,
            });
            AddInternal(toast = new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 120,
                Font = OsuFont.GetFont(size: 20, weight: FontWeight.SemiBold),
                Alpha = 0,
            });

            var hitObjects = GameplayState.Beatmap.HitObjects;
            AddInternal(ScrubOverlay = new ScrubOverlay(calculateDifficulties(), hitObjects.FirstOrDefault()?.StartTime ?? 0, GameplayState.Beatmap.GetLastObjectTime(),
                () => DrawableRuleset.FrameStableClock.CurrentTime,
                time =>
                {
                    // The overlay still takes clicks while it fades out. Without the key held, nothing would resume after the jump.
                    if (scrubbing)
                        usePracticeTool(() => PracticeSeek(time, !scrubbing));
                },
                time =>
                {
                    if (scrubbing)
                        saveCheckpoint(time);
                }));

            AddInternal(PracticePrompt = new PracticeMenuOverlay(@"practice this run?"));
            AddInternal(EndPrompt = new PracticeMenuOverlay(@"end of the map"));

            profileId = api.LocalUser.Value.OnlineID;
            beatmapMD5 = Beatmap.Value.BeatmapInfo.MD5Hash;
            Checkpoint = CheckpointStore.Load(storage, profileId, beatmapMD5);

            HealthProcessor.Health.BindValueChanged(health =>
            {
                double time = DrawableRuleset.FrameStableClock.CurrentTime;

                // Replayed jumps cross the same points again.
                if (health.NewValue <= 0 && health.OldValue > 0 && failPoints.All(t => Math.Abs(t - time) > 1))
                    failPoints.Add(time);
            });

            if (StartAsPractice)
                becomePractice();
        }

        private IReadOnlyList<(Type Skill, (double Time, double Difficulty)[] Objects)> calculateDifficulties()
        {
            try
            {
                return GameplayState.Ruleset.CreateDifficultyCalculator(Beatmap.Value).CalculateObjectDifficulties(GameplayState.Mods);
            }
            catch (Exception e)
            {
                // The graph is a guide. Scrubbing works without it.
                Logger.Error(e, @"Could not calculate the strain graph.");
                return Array.Empty<(Type, (double, double)[])>();
            }
        }

        // With no token, SubmittingPlayer skips score submission. Everything it does locally still runs.
        protected override APIRequest<APIScoreToken>? CreateTokenRequest() => null;

        #region Seeking

        /// <summary>
        /// Jump to a time in the map. This turns the run into a practice run without asking.
        /// </summary>
        /// <param name="time">The time to jump to. Clamped to the map.</param>
        /// <param name="resumeOnArrival">Whether to resume as soon as the jump finishes. If false, gameplay stays paused there until <see cref="ResumeFromSeek"/>.</param>
        public void PracticeSeek(double time, bool resumeOnArrival = false)
        {
            if (!LoadedBeatmapSuccessfully || GameplayState.HasFailed)
                return;

            becomePractice();

            if (IsResuming)
            {
                DrawableRuleset.CancelResume();
                IsResuming = false;
            }

            PauseOverlay.Hide();
            EndPrompt.Hide();

            this.resumeOnArrival = resumeOnArrival;
            SeekSession.SeekTo(Math.Clamp(time, GameplayClockContainer.StartTime, GameplayState.Beatmap.GetLastObjectTime()));
        }

        /// <summary>
        /// Resume live play from where the last jump ended. Recorded input after that point is dropped.
        /// </summary>
        public void ResumeFromSeek()
        {
            if (!SeekSession.IsActive || SeekSession.IsSeeking)
                return;

            commit();
            Resume();
        }

        /// <summary>
        /// End the seek session where it is. A new section starts here.
        /// </summary>
        private void commit()
        {
            resumeOnArrival = false;
            SeekSession.Commit();

            double time = DrawableRuleset.FrameStableClock.CurrentTime;
            failPoints.RemoveAll(t => t > time);
            SectionScore.Restart();
        }

        // A seek session plays the run's own input as a replay. Esc should pause it, not leave the map.
        protected override bool CanPauseWithReplayLoaded => SeekSession.IsActive;

        public override bool Pause()
        {
            if (LoadedBeatmapSuccessfully && SeekSession.IsActive)
            {
                // A jump cannot stop half way. Esc does nothing until it ends, which takes a few seconds at most.
                if (SeekSession.IsSeeking)
                    return false;

                commit();
            }

            return base.Pause();
        }

        // The cooldown stops pause buffering in scored runs. After a rewind it would block pausing until play passes the last pause again.
        protected override double PauseCooldownDuration => IsPractice ? double.NegativeInfinity : base.PauseCooldownDuration;

        #endregion

        #region Keys

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (!LoadedBeatmapSuccessfully)
                return false;

            switch (e.Action)
            {
                case GlobalAction.SeekReplayBackward:
                    usePracticeTool(() => PracticeSeek(GameplayClockContainer.CurrentTime - SEEK_STEP, !scrubbing));
                    return true;

                case GlobalAction.SeekReplayForward:
                    usePracticeTool(() => PracticeSeek(GameplayClockContainer.CurrentTime + SEEK_STEP, !scrubbing));
                    return true;

                case GlobalAction.PracticeScrub:
                    if (!e.Repeat)
                    {
                        scrubbing = true;
                        lastMarkerState = -1;
                        ScrubOverlay.Show();
                    }

                    return true;

                case GlobalAction.PracticeQuickSave:
                    if (!e.Repeat)
                        saveCheckpoint(GameplayClockContainer.CurrentTime);
                    return true;

                case GlobalAction.PracticeQuickLoad:
                    if (!e.Repeat)
                        loadCheckpoint();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
            if (e.Action != GlobalAction.PracticeScrub || !LoadedBeatmapSuccessfully)
                return;

            scrubbing = false;
            ScrubOverlay.Hide();

            if (SeekSession.IsActive)
                resumeOnArrival = true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // With Shift held for scrubbing, bindings without Shift (Left, Right, comma, period) do not match. Handle those keys here.
            if (scrubbing && e.ShiftPressed && LoadedBeatmapSuccessfully)
            {
                double? step = e.Key switch
                {
                    Key.Left => -SEEK_STEP,
                    Key.Right => SEEK_STEP,
                    Key.Comma => -FRAME_STEP,
                    Key.Period => FRAME_STEP,
                    _ => null,
                };

                if (step is double amount)
                {
                    usePracticeTool(() => PracticeSeek(GameplayClockContainer.CurrentTime + amount, !scrubbing));
                    return true;
                }
            }

            return base.OnKeyDown(e);
        }

        #endregion

        #region Checkpoints

        private void saveCheckpoint(double time)
        {
            Checkpoint = Math.Clamp(time, GameplayClockContainer.StartTime, GameplayState.Beatmap.GetLastObjectTime());
            Attempts = 0;
            CheckpointStore.Save(storage, profileId, beatmapMD5, Checkpoint.Value);
            showToast($@"Checkpoint saved at {formatTime(Checkpoint.Value)}");
        }

        private void loadCheckpoint()
        {
            if (Checkpoint is not double time)
            {
                showToast(@"No checkpoint yet. Quick save first (F6).");
                return;
            }

            usePracticeTool(() =>
            {
                Attempts++;
                showToast($@"Checkpoint {formatTime(time)}, attempt {Attempts}");
                PracticeSeek(time, !scrubbing);
            });
        }

        private static string formatTime(double time) => TimeSpan.FromMilliseconds(Math.Max(0, time)).ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);

        private void showToast(LocalisableString text)
        {
            toast.Text = text;
            toast.FadeIn(100).Then().Delay(1500).FadeOut(300);
        }

        #endregion

        #region Practice rules

        /// <summary>
        /// Use a practice tool. In a normal run, first ask whether to turn the run into practice.
        /// </summary>
        private void usePracticeTool(Action tool)
        {
            if (IsPractice)
            {
                tool();
                return;
            }

            if (GameplayState.HasFailed || PracticePrompt.State.Value == Visibility.Visible)
                return;

            bool wasPaused = GameplayClockContainer.IsPaused.Value;

            if (IsResuming)
            {
                DrawableRuleset.CancelResume();
                IsResuming = false;
                wasPaused = false;
            }

            GameplayClockContainer.Stop();

            PracticePrompt.SetButtons(
                (@"Practice this run (it won't be saved)", colours.Green, () =>
                {
                    becomePractice();
                    tool();
                }),
                (@"Keep playing", colours.Yellow, () =>
                {
                    if (!wasPaused)
                        Resume();
                }));
            PracticePrompt.Show();
        }

        private void becomePractice()
        {
            if (IsPractice)
                return;

            IsPractice = true;
            SectionScore.Restart();

            // A practice run stays in gameplay when the last object is judged, and its score is never imported.
            Configuration.ShowResults = false;
        }

        // Health still drains and shows, but lazer's fail sequence cannot be rewound, so it never starts.
        protected override bool CheckModsAllowFailure() => !IsPractice && base.CheckModsAllowFailure();

        protected override Task ImportScore(Score score) => IsPractice ? Task.CompletedTask : base.ImportScore(score);

        protected override ResultsScreen CreateResults(ScoreInfo score) => IsPractice
            // The practice score is not in the database, and its replay is the edited timeline.
            ? new SoloResultsScreen(score) { AllowRetry = true, AllowWatchingReplay = false, IsLocalPlay = true }
            : base.CreateResults(score);

        private void updateEndPrompt()
        {
            bool show = IsPractice && !resultsRequested && ScoreProcessor.HasCompleted.Value && !SeekSession.IsActive && !GameplayState.HasFailed;

            if (show == endPromptShown)
                return;

            endPromptShown = show;

            if (!show)
            {
                EndPrompt.Hide();
                return;
            }

            var buttons = new List<(LocalisableString, Color4, Action)>();

            if (Checkpoint != null)
                buttons.Add((@"Load checkpoint", colours.Green, loadCheckpoint));

            buttons.Add((@"Play from the start", colours.Yellow, () => PracticeSeek(GameplayClockContainer.StartTime, true)));
            buttons.Add((@"Results", colours.Blue, showResults));

            EndPrompt.SetButtons(buttons.ToArray());
            EndPrompt.Show();
        }

        private void showResults()
        {
            resultsRequested = true;
            Configuration.ShowResults = true;
            ProgressToResultsNow();
        }

        #endregion

        protected override void Update()
        {
            base.Update();

            if (!LoadedBeatmapSuccessfully)
                return;

            // Short seeks end within a few frames. Only show the text for those the player waits on.
            seekingFor = SeekSession.IsSeeking ? seekingFor + Time.Elapsed : 0;
            seekingText.Alpha = seekingFor > 200 ? 1 : 0;

            // Resume here, before children update: the recorder that Commit adds must load before the next input arrives.
            if (resumeOnArrival)
                ResumeFromSeek();

            updateEndPrompt();

            var events = ScoreProcessor.HitEvents;
            int markerState = HashCode.Combine(events.Count, Checkpoint, failPoints.Count, SeekSession.SkippedRanges.Count);

            if (scrubbing && markerState != lastMarkerState)
            {
                lastMarkerState = markerState;
                ScrubOverlay.SetMarkers(Checkpoint, events.Where(e => e.Result == HitResult.Miss).Select(e => e.HitObject.StartTime), failPoints, SeekSession.SkippedRanges);
            }
        }
    }
}
