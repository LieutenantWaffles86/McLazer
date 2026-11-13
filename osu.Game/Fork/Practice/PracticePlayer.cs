// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.Rooms;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osuTK.Input;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// The player for all local play. A run is either normal or practice.
    /// </summary>
    /// <remarks>
    /// A normal run plays as in lazer's <see cref="SoloPlayer"/> and is saved locally. Nothing is submitted online.
    /// A practice run can jump in time (see <see cref="SeekSession"/>), never fails and is never saved as a score.
    /// </remarks>
    public partial class PracticePlayer : SoloPlayer, IKeyBindingHandler<GlobalAction>
    {
        /// <summary>
        /// How far the seek keys jump, as in replays.
        /// </summary>
        public const double SEEK_STEP = 5 * ReplayPlayer.BASE_SEEK_AMOUNT;

        /// <summary>
        /// Whether this run is a practice run. Once set, it stays set for the run.
        /// </summary>
        public bool IsPractice => isPractice.Value;

        private readonly BindableBool isPractice = new BindableBool();

        // Only practice runs seek from the progress bar. In a normal run, a stray click there would throw away the run's score.
        public override IBindable<bool> SeekingAllowed => isPractice;

        protected SeekSession SeekSession { get; private set; } = null!;

        private bool resumeOnArrival;
        private OsuSpriteText seekingText = null!;
        private double seekingFor;
        private InputManager inputManager = null!;

        public PracticePlayer(PlayerConfiguration? configuration = null)
            : base(configuration)
        {
        }

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
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            inputManager = GetContainingInputManager()!;
        }

        // With no token, SubmittingPlayer skips score submission. Everything it does locally still runs.
        protected override APIRequest<APIScoreToken>? CreateTokenRequest() => null;

        #region Seeking

        /// <summary>
        /// Jump to a time in the map. This turns the run into a practice run.
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

            resumeOnArrival = false;
            SeekSession.Commit();
            Resume();
        }

        // The progress bar seeks through here.
        public override void Seek(double time) => PracticeSeek(time, true);

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (!LoadedBeatmapSuccessfully)
                return false;

            // shortcut: a normal run turns into practice with no question asked. M5 asks first ("Switch this run to practice?").
            switch (e.Action)
            {
                case GlobalAction.SeekReplayBackward:
                    PracticeSeek(GameplayClockContainer.CurrentTime - SEEK_STEP, true);
                    return true;

                case GlobalAction.SeekReplayForward:
                    PracticeSeek(GameplayClockContainer.CurrentTime + SEEK_STEP, true);
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        protected override void Update()
        {
            base.Update();

            if (!LoadedBeatmapSuccessfully)
                return;

            // Short seeks end within a few frames. Only show the text for those the player waits on.
            seekingFor = SeekSession.IsSeeking ? seekingFor + Time.Elapsed : 0;
            seekingText.Alpha = seekingFor > 200 ? 1 : 0;

            // While the progress bar is dragged, stay at each target, so the player can scrub over their own attempt.
            // Resume here, before children update: the recorder that Commit adds must load before the next input arrives.
            if (resumeOnArrival && !SeekSession.IsSeeking && inputManager.DraggedDrawable is not SongProgressBar)
                ResumeFromSeek();
        }

        // spike: one hard-coded key that rewinds 30 s and resumes. Replace with the practice key bindings in M5.
        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.F7 && !e.Repeat && LoadedBeatmapSuccessfully)
            {
                PracticeSeek(GameplayClockContainer.CurrentTime - 30000, true);
                return true;
            }

            return base.OnKeyDown(e);
        }

        #endregion

        #region Practice rules

        private void becomePractice()
        {
            if (IsPractice)
                return;

            isPractice.Value = true;

            // A practice run stays in gameplay when the last object is judged, and its score is never imported.
            Configuration.ShowResults = false;
        }

        // Health still drains and shows, but lazer's fail sequence cannot be rewound, so it never starts.
        protected override bool CheckModsAllowFailure() => !IsPractice && base.CheckModsAllowFailure();

        protected override Task ImportScore(Score score) => IsPractice ? Task.CompletedTask : base.ImportScore(score);

        #endregion
    }
}
