// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Fork.Practice;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Tests.Fork.Practice
{
    [TestFixture]
    public class SectionStatsTest
    {
        [Test]
        public void TestStats()
        {
            var processor = new OsuRuleset().CreateScoreProcessor();
            var events = new[] { (HitResult.Great, 10.0), (HitResult.Great, -10.0), (HitResult.Ok, 50.0), (HitResult.Miss, 0.0) }
                         .Select(r => new HitEvent(r.Item2, 1, r.Item1, new HitCircle(), null, null)).ToList();

            var stats = SectionStats.From(events, processor.GetBaseScoreForResult);

            // osu! base scores: 300, 300, 100 and 0 out of 4 × 300.
            Assert.That(stats.Accuracy, Is.EqualTo(700.0 / 1200).Within(1e-9));
            Assert.That((stats.Greats, stats.Oks, stats.Mehs, stats.Misses), Is.EqualTo((2, 1, 0, 1)));
            Assert.That(stats.UnstableRate, Is.Not.Null);
        }

        [Test]
        public void TestEmptySection()
        {
            var stats = SectionStats.From(Enumerable.Empty<HitEvent>().ToList(), new OsuRuleset().CreateScoreProcessor().GetBaseScoreForResult);

            Assert.That(stats.Accuracy, Is.EqualTo(1));
            Assert.That(stats.UnstableRate, Is.Null);
        }
    }
}
