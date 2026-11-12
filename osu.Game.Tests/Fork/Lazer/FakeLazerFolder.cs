// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Tests.Resources;
using Realms;

namespace osu.Game.Tests.Fork.Lazer
{
    /// <summary>
    /// A stand-in for an osu!lazer data folder, built with lazer's own database and importer code.
    /// </summary>
    public class FakeLazerFolder : IDisposable
    {
        public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mclazer-test-lazer-{Guid.NewGuid()}");

        private readonly GameHost host;
        private readonly AudioManager audio;
        private readonly IResourceStore<byte[]> resources;
        private readonly WorkingBeatmap defaultBeatmap;

        public FakeLazerFolder(GameHost host, AudioManager audio, IResourceStore<byte[]> resources, WorkingBeatmap defaultBeatmap)
        {
            this.host = host;
            this.audio = audio;
            this.resources = resources;
            this.defaultBeatmap = defaultBeatmap;

            Directory.CreateDirectory(Path);

            // Creates the database with rulesets registered, as a first run of lazer would.
            Write(_ => { });
        }

        /// <summary>
        /// Import the quick test beatmap, as lazer would. Returns the set's ID.
        /// </summary>
        public Guid ImportBeatmap()
        {
            Guid id = Guid.Empty;

            use((realm, storage) =>
            {
                var beatmaps = new BeatmapManager(storage, realm, null, audio, resources, host, defaultBeatmap);
                id = beatmaps.Import(new ImportTask(TestResources.GetQuickTestBeatmapForImport())).GetResultSafely()!.ID;
            });

            return id;
        }

        /// <summary>
        /// Store a file in lazer's <c>files</c> folder, as lazer would. Returns its hash, for a file record.
        /// </summary>
        public string AddFile(string content)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string folder = System.IO.Path.Combine(Path, "files", hash[..1], hash[..2]);

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(System.IO.Path.Combine(folder, hash), bytes);

            return hash;
        }

        /// <summary>
        /// Change lazer's database, as lazer would while running.
        /// </summary>
        public void Write(Action<Realm> action) => use((realm, _) => realm.Write(action));

        private void use(Action<RealmAccess, Storage> action)
        {
            Storage storage = host.GetStorage(Path);

            using (var realm = new RealmAccess(storage, OsuGameBase.CLIENT_DATABASE_FILENAME))
            using (new RealmRulesetStore(realm, storage))
                action(realm, storage);
        }

        /// <summary>
        /// Every file in the folder with a hash of its contents, to check that nothing changed.
        /// </summary>
        public Dictionary<string, string> Fingerprint() =>
            Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
                     // Realm's named pipe. Reading it would block.
                     .Where(f => !f.EndsWith(".note", StringComparison.Ordinal))
                     .ToDictionary(f => System.IO.Path.GetRelativePath(Path, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
            }
        }
    }
}
