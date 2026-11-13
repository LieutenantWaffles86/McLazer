// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// A second score processor that holds only the current section: everything since the last resume.
    /// The HUD resolves this one, so the skin's own counters (hit counts, accuracy, UR, combo) show the section.
    /// </summary>
    /// <remarks>
    /// It mirrors the run's processor. Until the first restart, as in a normal run, the two are the same.
    /// </remarks>
    public class SectionScore
    {
        public readonly ScoreProcessor Processor;

        /// <summary>
        /// The run's results applied here, with the combo fields this processor wrote on them.
        /// Both processors write those fields on the same result, and each reverts with its own, so they are swapped around every call.
        /// </summary>
        private readonly List<(JudgementResult Result, Combos Section)> applied = new List<(JudgementResult, Combos)>();

        public SectionScore(GameplayState state, ScoreProcessor run)
        {
            Processor = state.Ruleset.CreateScoreProcessor();
            Processor.Mods.Value = state.Mods;
            Processor.ApplyBeatmap(state.Beatmap);

            run.NewJudgement += apply;
            run.JudgementReverted += revert;
        }

        /// <summary>
        /// Start a new section: clear everything shown.
        /// </summary>
        public void Restart()
        {
            while (applied.Count > 0)
                revertLast();
        }

        private void apply(JudgementResult result)
        {
            var run = Combos.Of(result);
            Processor.ApplyResult(result);
            applied.Add((result, Combos.Of(result)));
            run.WriteTo(result);
        }

        private void revert(JudgementResult result)
        {
            // Results from before the section are not here.
            if (applied.Count > 0 && applied[^1].Result == result)
                revertLast();
        }

        private void revertLast()
        {
            var (result, section) = applied[^1];
            applied.RemoveAt(applied.Count - 1);

            var run = Combos.Of(result);
            section.WriteTo(result);
            Processor.RevertResult(result);
            run.WriteTo(result);
        }

        private readonly record struct Combos(int At, int After, int HighestAt, int HighestAfter)
        {
            public static Combos Of(JudgementResult r) => new Combos(r.ComboAtJudgement, r.ComboAfterJudgement, r.HighestComboAtJudgement, r.HighestComboAfterJudgement);

            public void WriteTo(JudgementResult r)
            {
                r.ComboAtJudgement = At;
                r.ComboAfterJudgement = After;
                r.HighestComboAtJudgement = HighestAt;
                r.HighestComboAfterJudgement = HighestAfter;
            }
        }
    }
}
