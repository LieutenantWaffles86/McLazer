// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online.Rooms;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
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
    public partial class PracticePlayer : SoloPlayer
    {
        /// <summary>
        /// Whether this run is a practice run. Once set, it stays set for the run.
        /// </summary>
        public bool IsPractice { get; private set; }

        protected SeekSession SeekSession { get; private set; } = null!;

        private bool resumeOnArrival;

        public PracticePlayer(PlayerConfiguration? configuration = null)
            : base(configuration)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (LoadedBeatmapSuccessfully)
                AddInternal(SeekSession = new SeekSession(DrawableRuleset, GameplayClockContainer, GameplayState));
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

        protected override void Update()
        {
            base.Update();

            if (resumeOnArrival)
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

            IsPractice = true;

            // A practice run stays in gameplay when the last object is judged, and its score is never imported.
            Configuration.ShowResults = false;
        }

        // Health still drains and shows, but lazer's fail sequence cannot be rewound, so it never starts.
        protected override bool CheckModsAllowFailure() => !IsPractice && base.CheckModsAllowFailure();

        protected override Task ImportScore(Score score) => IsPractice ? Task.CompletedTask : base.ImportScore(score);

        #endregion
    }
}
