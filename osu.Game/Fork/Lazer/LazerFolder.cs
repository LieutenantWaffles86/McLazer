// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using osu.Framework.Logging;

namespace osu.Game.Fork.Lazer
{
    /// <summary>
    /// Finds the osu!lazer data folder McLazer borrows from. Nothing here writes to it.
    /// </summary>
    public static class LazerFolder
    {
        private const string storage_config = @"storage.ini";

        // McLazer's own options file, in McLazer's own folder.
        private const string own_config = @"mclazer.ini";

        /// <summary>
        /// The lazer folder the user chose in McLazer, or null if none was chosen.
        /// </summary>
        public static string? ReadSavedPath(string ownFolder) => readValue(Path.Combine(ownFolder, own_config), @"LazerPath");

        // shortcut: writes the whole file, which holds this one option so far. Merge lines once practice options live here too (M5).
        public static void SavePath(string ownFolder, string lazerPath) => File.WriteAllText(Path.Combine(ownFolder, own_config), $"LazerPath = {lazerPath}\n");

        /// <summary>
        /// Where to look for lazer when nothing else is given: the folder the user chose, else lazer's default folder.
        /// </summary>
        public static string DefaultPath(string ownFolder) =>
            // Lazer's default folder sits next to McLazer's own: ~/.local/share/osu and ~/.local/share/mclazer.
            ReadSavedPath(ownFolder) ?? Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(ownFolder))!, @"osu");

        /// <summary>
        /// Replace McLazer's input settings (mouse, tablet, keyboard handlers) and framework settings (window, volume, frame limiter) with lazer's.
        /// Must run before the host starts, because the host loads both files.
        /// </summary>
        public static void BorrowHostSettings(string ownFolder, string lazerPath)
        {
            foreach (string name in new[] { @"input.json", @"framework.ini" })
            {
                // When lazer's data was moved, the live copies are in the moved folder and stale ones can remain in the default folder.
                string? source = new[] { Resolve(lazerPath), lazerPath }
                                 .Where(folder => folder != null)
                                 .Select(folder => Path.Combine(folder!, name))
                                 .FirstOrDefault(File.Exists);

                if (source == null)
                    continue;

                try
                {
                    File.Copy(source, Path.Combine(ownFolder, name), true);
                }
                catch (Exception e)
                {
                    // Keep McLazer's last copy.
                    Logger.Log($@"Could not read lazer's {name}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Resolve a lazer data folder, following <c>storage.ini</c> if the user moved lazer's data.
        /// </summary>
        /// <returns>The folder holding lazer's <c>client.realm</c>, or null if there is none.</returns>
        public static string? Resolve(string path)
        {
            string? moved = readValue(Path.Combine(path, storage_config), @"FullPath");

            if (!string.IsNullOrEmpty(moved))
                path = moved;

            return File.Exists(Path.Combine(path, OsuGameBase.CLIENT_DATABASE_FILENAME)) ? path : null;
        }

        // The same "Key = Value" format as lazer's config files. Parsed by hand, because a config manager saves its file when disposed.
        private static string? readValue(string file, string key) =>
            File.Exists(file)
                ? File.ReadLines(file)
                      .Select(l => l.Split('=', 2))
                      .Where(kv => kv.Length == 2 && kv[0].Trim() == key)
                      .Select(kv => kv[1].Trim())
                      .FirstOrDefault()
                : null;

        /// <summary>
        /// Whether a lazer release build is running. Its database must not be copied while it is.
        /// </summary>
        /// <remarks>
        /// Lazer holds a named pipe to stay single-instance. If the pipe cannot be created here, lazer has it.
        /// </remarks>
        public static bool IsLazerRunning()
        {
            try
            {
                using (new NamedPipeServerStream(@"osu-lazer", PipeDirection.InOut, 1))
                    return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }
}
