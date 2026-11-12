// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// Runs every jump in time during a practice run: rewind, fast-forward, scrub and checkpoint load.
    /// </summary>
    /// <remarks>
    /// While a session is active, the run's own recorded input is attached as a replay.
    /// Lazer's replay seeking then walks the clock to the target and undoes or re-applies judgements on the way.
    /// Nothing is cut from the recorded input until <see cref="Commit"/>, so the player can scrub over their own attempt.
    /// </remarks>
    public partial class SeekSession : Component
    {
        /// <summary>
        /// Whether the recorded input is attached as a replay. True from the first seek until <see cref="Commit"/>.
        /// </summary>
        public bool IsActive => seekScore != null;

        /// <summary>
        /// Whether the frame-stable clock is still walking to the target time.
        /// </summary>
        public bool IsSeeking { get; private set; }

        /// <summary>
        /// Ranges of time that were jumped over with no input. Objects inside them count as misses.
        /// </summary>
        public IReadOnlyList<(double Start, double End)> SkippedRanges => skippedRanges;

        private readonly List<(double Start, double End)> skippedRanges = new List<(double Start, double End)>();

        private readonly DrawableRuleset drawableRuleset;
        private readonly GameplayClockContainer clock;
        private readonly GameplayState gameplayState;

        private Score? seekScore;

        /// <summary>
        /// The time of the last input recorded before this session began. Null if nothing was recorded.
        /// </summary>
        private double? recordedUntil;

        public SeekSession(DrawableRuleset drawableRuleset, GameplayClockContainer clock, GameplayState gameplayState)
        {
            this.drawableRuleset = drawableRuleset;
            this.clock = clock;
            this.gameplayState = gameplayState;
        }

        /// <summary>
        /// Jump to a time. Starts a session if none is active. Gameplay stays paused at the target until <see cref="Commit"/>.
        /// </summary>
        public void SeekTo(double time)
        {
            if (!IsActive)
                begin();

            IsSeeking = true;
            clock.Seek(time);
        }

        private void begin()
        {
            clock.Stop();

            // Otherwise the recorder would write replay-driven input back into the run.
            drawableRuleset.SetRecordTarget(null);

            var run = gameplayState.Score;

            seekScore = new Score { ScoreInfo = run.ScoreInfo, Replay = run.Replay.DeepClone() };

            var lastFrame = seekScore.Replay.Frames.LastOrDefault();
            recordedUntil = lastFrame?.Time;

            // Release every key after the last recorded input, so time past it is played with no input.
            if (lastFrame != null && createIdleFrame(lastFrame, lastFrame.Time) is ReplayFrame idle)
                seekScore.Replay.Frames.Add(idle);

            // With a replay attached, FrameStabilityContainer keeps running while paused and accepts jumps of any size.
            drawableRuleset.SetReplayScore(seekScore);

            // A new replay handler would jump to the first frame and play the whole run forward again. Start it where the run is now.
            (drawableRuleset.FrameStableClock as IHasReplayHandler)?.ReplayInputHandler?.StartFrom(drawableRuleset.FrameStableClock.CurrentTime);

            // GlobalCursorDisplay hides the gameplay cursor on its next update, because the ruleset no longer provides the user's cursor.
            // Show it again: the player wants to see where they aimed, and the osu! resume overlay skips itself if the cursor is hidden.
            Schedule(() => drawableRuleset.Cursor?.Show());
        }

        protected override void Update()
        {
            base.Update();

            // The gameplay clock is stopped for the whole session, so its time is the target.
            if (IsSeeking && drawableRuleset.FrameStableClock.CurrentTime == clock.CurrentTime)
            {
                IsSeeking = false;

                // Reverted judgements do not refresh ScoreInfo on their own.
                gameplayState.ScoreProcessor.PopulateScore(gameplayState.Score.ScoreInfo);
                gameplayState.HealthProcessor.PopulateScore(gameplayState.Score.ScoreInfo);
            }
        }

        /// <summary>
        /// End the session at the current time: drop recorded input after it, detach the replay and record live input again.
        /// The caller resumes gameplay afterwards.
        /// </summary>
        public void Commit()
        {
            if (seekScore == null || IsSeeking)
                return;

            double time = drawableRuleset.FrameStableClock.CurrentTime;

            var frames = seekScore.Replay.Frames.Where(f => f.Time <= time).ToList();

            // The replay releases its keys here, and the cursor leaves the recorded path. Record that, so a replay of the run matches.
            if (frames.Count > 0 && createIdleFrame(frames[^1], time) is ReplayFrame idle)
                frames.Add(idle);

            gameplayState.Score.Replay.Frames = frames;

            // Everything after the resume point is discarded, including skips recorded there.
            skippedRanges.RemoveAll(r => r.Start >= time);
            for (int i = 0; i < skippedRanges.Count; i++)
                skippedRanges[i] = (skippedRanges[i].Start, System.Math.Min(skippedRanges[i].End, time));

            double skippedFrom = recordedUntil ?? clock.StartTime;
            if (time > skippedFrom)
                skippedRanges.Add((skippedFrom, time));

            seekScore = null;
            recordedUntil = null;

            drawableRuleset.SetReplayScore(null);
            drawableRuleset.SetRecordTarget(gameplayState.Score);
        }

        /// <summary>
        /// A copy of <paramref name="source"/> at <paramref name="time"/> with the same cursor position and no keys held.
        /// Null if the ruleset's frames cannot be converted.
        /// </summary>
        private ReplayFrame? createIdleFrame(ReplayFrame source, double time)
        {
            if (source is not IConvertibleReplayFrame convertible || gameplayState.Ruleset.CreateConvertibleReplayFrame() is not IConvertibleReplayFrame idle)
                return null;

            LegacyReplayFrame legacy = convertible.ToLegacy(gameplayState.Beatmap);
            legacy.ButtonState = ReplayButtonState.None;

            idle.FromLegacy(legacy, gameplayState.Beatmap);

            var frame = (ReplayFrame)idle;
            frame.Time = time;
            return frame;
        }
    }
}
