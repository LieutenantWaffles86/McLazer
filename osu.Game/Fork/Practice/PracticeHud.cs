// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// The "PRACTICE" badge and the stats of the current section: everything since the last resume.
    /// </summary>
    /// <remarks>
    /// McLazer's own element, not a skinnable one, so no skin can leave it out.
    /// Skipped time is never part of a section: every jump ends the section, and the next one starts at the resume.
    /// </remarks>
    public partial class PracticeHud : CompositeDrawable
    {
        private readonly OsuTextFlowContainer text;
        private string shown = string.Empty;

        public PracticeHud()
        {
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            AutoSizeAxes = Axes.Both;
            Margin = new MarginPadding { Left = 10 };
            Alpha = 0;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.5f },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(10),
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = @"PRACTICE", Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold) },
                        text = new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 14)) { AutoSizeAxes = Axes.Both },
                    }
                }
            };
        }

        /// <summary>
        /// Show the section stats.
        /// </summary>
        /// <param name="section">The hit events since the last resume.</param>
        /// <param name="baseScore">The ruleset's base score for a result, for accuracy.</param>
        /// <param name="checkpoint">The quick-save checkpoint, if any.</param>
        /// <param name="attempts">How many times the checkpoint was loaded.</param>
        public void ShowSection(IReadOnlyList<HitEvent> section, Func<HitResult, int> baseScore, double? checkpoint, int attempts)
        {
            var stats = SectionStats.From(section, baseScore);

            string lines = $@"Section {stats.Accuracy.ToString(@"P2", CultureInfo.InvariantCulture)}, {stats.Misses} {(stats.Misses == 1 ? @"miss" : @"misses")}"
                           + $"\n300: {stats.Greats}  100: {stats.Oks}  50: {stats.Mehs}"
                           + (stats.UnstableRate is double ur ? $"\nUR {ur.ToString(@"N1", CultureInfo.InvariantCulture)}" : string.Empty)
                           + (checkpoint is double time ? $"\nCheckpoint {FormatTime(time)}, attempt {attempts}" : string.Empty);

            if (shown != lines)
                text.Text = shown = lines;
        }

        public static string FormatTime(double time) => TimeSpan.FromMilliseconds(Math.Max(0, time)).ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }

    public readonly record struct SectionStats(double Accuracy, int Greats, int Oks, int Mehs, int Misses, double? UnstableRate)
    {
        public static SectionStats From(IReadOnlyList<HitEvent> events, Func<HitResult, int> baseScore)
        {
            double score = 0;
            double maximum = 0;

            foreach (var e in events)
            {
                var maxResult = e.HitObject.Judgement.MaxResult;

                if (!maxResult.AffectsAccuracy())
                    continue;

                maximum += baseScore(maxResult);

                if (e.Result.AffectsAccuracy())
                    score += baseScore(e.Result);
            }

            return new SectionStats(
                maximum > 0 ? score / maximum : 1,
                events.Count(e => e.Result == HitResult.Great),
                events.Count(e => e.Result == HitResult.Ok),
                events.Count(e => e.Result == HitResult.Meh),
                events.Count(e => e.Result == HitResult.Miss),
                events.Any(HitEventExtensions.AffectsUnstableRate) ? events.CalculateUnstableRate()?.Result : null);
        }
    }
}
