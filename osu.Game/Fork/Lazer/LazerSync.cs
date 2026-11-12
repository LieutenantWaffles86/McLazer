// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Skinning;
using Realms;

namespace osu.Game.Fork.Lazer
{
    public enum LazerSyncResult
    {
        Synced,

        /// <summary>
        /// Lazer is running, so its database was not copied. McLazer keeps the data from the last sync.
        /// </summary>
        LazerRunning,

        /// <summary>
        /// Lazer's database is from a newer lazer than this McLazer can read.
        /// </summary>
        LazerNewer,

        /// <summary>
        /// The copy of lazer's database could not be opened.
        /// </summary>
        Unreadable,
    }

    /// <summary>
    /// Copies beatmap, skin, collection and key binding records from a linked lazer folder into McLazer's own database. Lazer is the source of truth.
    /// </summary>
    /// <remarks>
    /// Lazer's database is never opened: opening it would put McLazer's lock on it. It is copied, and the copy is opened.
    /// Only records are copied. The beatmap files stay in lazer's folder and are read from there (see <see cref="FallbackReadStorage"/>).
    /// </remarks>
    public static class LazerSync
    {
        private const string snapshot_directory = @"lazer-snapshot";

        /// <param name="realm">McLazer's own database.</param>
        /// <param name="lazerFolder">The lazer data folder, as returned by <see cref="LazerFolder.Resolve"/>.</param>
        /// <param name="storage">McLazer's own storage. The copy of lazer's database lives here during the sync.</param>
        /// <param name="isLazerRunning">Whether lazer is running. Replaced in tests.</param>
        public static LazerSyncResult Run(RealmAccess realm, string lazerFolder, Storage storage, Func<bool>? isLazerRunning = null)
        {
            if ((isLazerRunning ?? LazerFolder.IsLazerRunning)())
                return LazerSyncResult.LazerRunning;

            storage.DeleteDirectory(snapshot_directory);
            Storage snapshotStorage = storage.GetStorageForDirectory(snapshot_directory);

            try
            {
                File.Copy(Path.Combine(lazerFolder, OsuGameBase.CLIENT_DATABASE_FILENAME), snapshotStorage.GetFullPath(OsuGameBase.CLIENT_DATABASE_FILENAME));

                using (var snapshot = new RealmAccess(snapshotStorage, OsuGameBase.CLIENT_DATABASE_FILENAME))
                {
                    // RealmAccess does not throw on a database it cannot use. It moves it aside under one of these names and starts an empty one.
                    // Syncing from that empty database would remove every beatmap.
                    string[] files = snapshotStorage.GetFiles(string.Empty).ToArray();

                    if (files.Any(f => f.Contains(@"_newer_version")))
                        return LazerSyncResult.LazerNewer;

                    if (files.Any(f => f.Contains(@"_corrupt")))
                        return LazerSyncResult.Unreadable;

                    // shortcut: one write transaction for the whole library, which blocks other writes for its duration. Batch it if a first sync of a large library stalls the game.
                    snapshot.Run(source => realm.Write(target =>
                    {
                        syncBeatmaps(source, target);
                        syncSkins(source, target);
                        syncCollections(source, target);
                        syncKeyBindings(source, target);
                    }));
                }

                return LazerSyncResult.Synced;
            }
            catch (Exception e)
            {
                Logger.Error(e, @"Could not sync from the lazer folder.");
                return LazerSyncResult.Unreadable;
            }
            finally
            {
                storage.DeleteDirectory(snapshot_directory);
            }
        }

        private static void syncBeatmaps(Realm source, Realm target)
        {
            var seen = new HashSet<Guid>();

            // Protected sets are each game's own bundled tracks.
            foreach (var sourceSet in source.All<BeatmapSetInfo>().Where(s => !s.Protected && !s.DeletePending))
            {
                seen.Add(sourceSet.ID);

                var existing = target.Find<BeatmapSetInfo>(sourceSet.ID);

                if (existing?.Hash == sourceSet.Hash)
                    copyOnlineState(sourceSet, existing);
                else
                    replaceSet(target, existing, sourceSet);
            }

            foreach (var removed in target.All<BeatmapSetInfo>().Where(s => !s.Protected).ToList().Where(s => !seen.Contains(s.ID)))
            {
                if (removed.Beatmaps.Any(b => b.Scores.Any()))
                {
                    // Keep the records so scores set in McLazer survive, but take the set out of song select.
                    foreach (var beatmap in removed.Beatmaps)
                        beatmap.Hidden = true;
                }
                else
                    removeSet(target, removed);
            }
        }

        /// <summary>
        /// For a set whose files have not changed: copy what lazer learns online, such as ranked status. Local pp depends on it.
        /// </summary>
        private static void copyOnlineState(BeatmapSetInfo source, BeatmapSetInfo target)
        {
            target.StatusInt = source.StatusInt;
            target.OnlineID = source.OnlineID;
            target.DateRanked = source.DateRanked;
            target.DateSubmitted = source.DateSubmitted;

            foreach (var sourceBeatmap in source.Beatmaps)
            {
                var beatmap = target.Beatmaps.FirstOrDefault(b => b.ID == sourceBeatmap.ID);

                if (beatmap == null)
                    continue;

                beatmap.StatusInt = sourceBeatmap.StatusInt;
                beatmap.OnlineID = sourceBeatmap.OnlineID;
                beatmap.OnlineMD5Hash = sourceBeatmap.OnlineMD5Hash;
                beatmap.LastOnlineUpdate = sourceBeatmap.LastOnlineUpdate;
                beatmap.StarRating = sourceBeatmap.StarRating;
                beatmap.Hidden = sourceBeatmap.Hidden;
            }
        }

        private static void replaceSet(Realm target, BeatmapSetInfo? existing, BeatmapSetInfo source)
        {
            // Scores set in McLazer on the old version of the set. They go back on a difficulty only if it is still the same map.
            var scores = existing?.Beatmaps.SelectMany(b => b.Scores).ToList() ?? new List<ScoreInfo>();

            if (existing != null)
                removeSet(target, existing);

            BeatmapSetInfo set = source.Detach();

            // Difficulties for rulesets this build does not have are left out.
            foreach (var beatmap in set.Beatmaps.ToList())
            {
                var ruleset = target.Find<RulesetInfo>(beatmap.Ruleset.ShortName);

                if (ruleset?.Available == true)
                    beatmap.Ruleset = ruleset;
                else
                    set.Beatmaps.Remove(beatmap);
            }

            if (set.Beatmaps.Count == 0)
                return;

            copyFiles(target, source, set);
            target.Add(set);

            foreach (var score in scores)
                score.BeatmapInfo = set.Beatmaps.FirstOrDefault(b => b.Hash == score.BeatmapHash);
        }

        private static void removeSet(Realm target, BeatmapSetInfo set)
        {
            foreach (var beatmap in set.Beatmaps.ToList())
            {
                target.Remove(beatmap.Metadata);
                target.Remove(beatmap);
            }

            target.Remove(set);
        }

        /// <summary>
        /// Give a detached copy the file list of its source. Detaching does not carry it over reliably.
        /// </summary>
        private static void copyFiles(Realm target, IHasRealmFiles source, IHasRealmFiles copy)
        {
            copy.Files.Clear();

            // A file record is shared by every set and skin that uses the file.
            var newFiles = new Dictionary<string, RealmFile>();

            foreach (var usage in source.Files)
            {
                string hash = usage.File.Hash;

                if (target.Find<RealmFile>(hash) is not RealmFile file && !newFiles.TryGetValue(hash, out file!))
                    newFiles[hash] = file = new RealmFile { Hash = hash };

                copy.Files.Add(new RealmNamedFileUsage(file, usage.Filename));
            }
        }

        private static void syncSkins(Realm source, Realm target)
        {
            var seen = new HashSet<Guid>();

            // Protected skins are each game's own built-in skins.
            foreach (var sourceSkin in source.All<SkinInfo>().Where(s => !s.Protected && !s.DeletePending))
            {
                seen.Add(sourceSkin.ID);

                var existing = target.Find<SkinInfo>(sourceSkin.ID);

                // Lazer updates the hash whenever a skin is saved, so an equal hash means nothing to do.
                if (existing?.Hash == sourceSkin.Hash)
                    continue;

                if (existing != null)
                    target.Remove(existing);

                SkinInfo skin = sourceSkin.Detach();
                copyFiles(target, sourceSkin, skin);
                target.Add(skin);
            }

            foreach (var removed in target.All<SkinInfo>().Where(s => !s.Protected).ToList().Where(s => !seen.Contains(s.ID)))
                target.Remove(removed);
        }

        /// <summary>
        /// McLazer keeps no collections of its own: lazer's replace them.
        /// </summary>
        private static void syncCollections(Realm source, Realm target)
        {
            target.RemoveAll<BeatmapCollection>();

            foreach (var collection in source.All<BeatmapCollection>())
                target.Add(collection.Detach());
        }

        /// <summary>
        /// Lazer's binding replaces McLazer's for every action lazer knows. Actions only McLazer has keep their bindings.
        /// </summary>
        private static void syncKeyBindings(Realm source, Realm target)
        {
            var sourceBindings = source.All<RealmKeyBinding>().ToList();
            var actions = sourceBindings.Select(b => (b.RulesetName, b.Variant, b.ActionInt)).ToHashSet();

            foreach (var replaced in target.All<RealmKeyBinding>().ToList().Where(b => actions.Contains((b.RulesetName, b.Variant, b.ActionInt))))
                target.Remove(replaced);

            foreach (var binding in sourceBindings)
                target.Add(binding.Detach());
        }
    }
}
