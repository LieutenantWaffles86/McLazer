// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// The quick-save checkpoint of each map, per profile, in <c>practice/&lt;profile id&gt;/&lt;beatmap MD5&gt;.json</c> in McLazer's own folder.
    /// </summary>
    /// <remarks>
    /// Editing a map changes its MD5, so checkpoints of the old version simply stop showing.
    /// </remarks>
    public static class CheckpointStore
    {
        /// <summary>
        /// The saved checkpoint time, or null if there is none or the file cannot be read.
        /// </summary>
        public static double? Load(Storage storage, int profileId, string beatmapMD5)
        {
            if (string.IsNullOrEmpty(beatmapMD5))
                return null;

            string path = storage.GetFullPath(pathFor(profileId, beatmapMD5));

            if (!File.Exists(path))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Data>(File.ReadAllText(path))?.QuickSave;
            }
            catch (Exception e)
            {
                // A lost checkpoint is cheap to set again. The next save replaces the file.
                Logger.Error(e, $@"Could not read the checkpoint file {path}.");
                return null;
            }
        }

        public static void Save(Storage storage, int profileId, string beatmapMD5, double time)
        {
            if (string.IsNullOrEmpty(beatmapMD5))
                return;

            try
            {
                string path = storage.GetFullPath(pathFor(profileId, beatmapMD5), true);

                // Write a temporary file and rename it, so a crash never leaves half a file.
                File.WriteAllText(path + @".tmp", JsonConvert.SerializeObject(new Data { QuickSave = time }));
                File.Move(path + @".tmp", path, true);
            }
            catch (Exception e)
            {
                Logger.Error(e, @"Could not save the checkpoint.");
            }
        }

        private static string pathFor(int profileId, string beatmapMD5) =>
            Path.Combine(@"practice", profileId.ToString(CultureInfo.InvariantCulture), $@"{beatmapMD5}.json");

        private class Data
        {
            public double? QuickSave { get; set; }
        }
    }
}
