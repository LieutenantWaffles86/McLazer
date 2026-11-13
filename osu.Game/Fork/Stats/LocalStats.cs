// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Fork.Profiles;
using osu.Game.Scoring;
using Realms;

namespace osu.Game.Fork.Stats
{
    /// <summary>
    /// A profile's stats from the scores saved on this PC, as on an osu! profile.
    /// </summary>
    /// <param name="TotalPP">The best score per ranked map, sorted by pp, each counting 95% as much as the one before. No bonus pp.</param>
    /// <param name="Accuracy">The accuracy of the same scores, weighted the same way.</param>
    /// <param name="PlayCount">Saved scores. Practice runs and failed runs are not saved.</param>
    /// <param name="TopPlays">The ten scores that count most toward <see cref="TotalPP"/>.</param>
    /// <param name="RecentPlays">The ten newest scores.</param>
    /// <param name="Grades">The grade of the best score on each map.</param>
    public record ProfileStats(double TotalPP, double Accuracy, int PlayCount, IReadOnlyList<ScoreInfo> TopPlays, IReadOnlyList<ScoreInfo> RecentPlays, IReadOnlyDictionary<ScoreRank, int> Grades);

    /// <summary>
    /// Local pp. Only the profiles' own passed scores on ranked maps earn it. Copies of lazer's scores never do.
    /// </summary>
    public static class LocalStats
    {
        /// <summary>
        /// Each score in the pp total counts this much as the one before it.
        /// </summary>
        public const double WEIGHT = 0.95;

        public static bool EarnsPP(ScoreInfo score) =>
            ProfileStore.IsLocal(score.UserID) && score.Rank != ScoreRank.F && score.BeatmapInfo?.Status.GrantsPerformancePoints() == true;

        /// <summary>
        /// Calculate and store pp for the profiles' scores that earn it and have none yet. The value is kept in <see cref="ScoreInfo.PP"/>.
        /// </summary>
        public static async Task PopulateMissingPP(RealmAccess realm, BeatmapDifficultyCache difficultyCache, CancellationToken cancellationToken = default)
        {
            var ids = realm.Run(r => r.All<ScoreInfo>().Filter(@"User.OnlineID >= $0 AND PP == nil AND DeletePending == false", ProfileStore.FIRST_ID)
                                      .AsEnumerable().Where(EarnsPP).Select(s => s.ID).ToList());

            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var score = realm.Run(r => r.Find<ScoreInfo>(id)?.Detach());

                if (score?.BeatmapInfo == null)
                    continue;

                try
                {
                    var attributes = await difficultyCache.GetDifficultyAsync(score.BeatmapInfo, score.Ruleset, score.Mods, cancellationToken).ConfigureAwait(false);
                    var calculator = score.Ruleset.CreateInstance().CreatePerformanceCalculator();

                    // The beatmap's files may be gone from the lazer folder.
                    if (attributes?.DifficultyAttributes == null || calculator == null)
                        continue;

                    double pp = (await calculator.CalculateAsync(score, attributes.Value.DifficultyAttributes, cancellationToken).ConfigureAwait(false)).Total;

                    realm.Write(r =>
                    {
                        if (r.Find<ScoreInfo>(id) is ScoreInfo stored)
                            stored.PP = pp;
                    });
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Logger.Error(e, $@"Could not calculate pp for score {id}.");
                }
            }
        }

        public static ProfileStats For(RealmAccess realm, int profileId)
        {
            var scores = realm.Run(r => r.All<ScoreInfo>().Filter(@"User.OnlineID == $0 AND DeletePending == false", profileId).AsEnumerable().Select(s => s.Detach()).ToList());

            var counted = scores.Where(s => s.PP != null && EarnsPP(s))
                                .GroupBy(s => s.BeatmapInfo!.ID)
                                .Select(g => g.MaxBy(s => s.PP)!)
                                .OrderByDescending(s => s.PP)
                                .ToList();

            double weights = counted.Select((_, i) => Math.Pow(WEIGHT, i)).Sum();

            var grades = scores.Where(s => s.BeatmapInfo != null)
                               .GroupBy(s => s.BeatmapInfo!.ID)
                               .Select(g => g.MaxBy(s => s.TotalScore)!.Rank)
                               .GroupBy(rank => rank)
                               .ToDictionary(g => g.Key, g => g.Count());

            return new ProfileStats(
                counted.Select((s, i) => s.PP!.Value * Math.Pow(WEIGHT, i)).Sum(),
                weights > 0 ? counted.Select((s, i) => s.Accuracy * Math.Pow(WEIGHT, i)).Sum() / weights : 0,
                scores.Count,
                counted.Take(10).ToList(),
                scores.OrderByDescending(s => s.Date).Take(10).ToList(),
                grades);
        }
    }
}
