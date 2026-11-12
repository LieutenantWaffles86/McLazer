// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Fork.Lazer;
using osu.Game.Fork.Offline;
using osu.Game.Online;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Fork
{
    /// <summary>
    /// osu!lazer with McLazer's changes wired in. The desktop game derives from this, and so can tests.
    /// </summary>
    public partial class McLazerGame : OsuGame
    {
        // Same config file and behaviour in Debug and Release builds. There is no server to choose.
        public override bool UseDevelopmentServer => false;

        /// <summary>
        /// The osu!lazer data folder to borrow from. If null, the folder the user chose is used, then lazer's default folder.
        /// </summary>
        public string? LazerPath { get; init; }

        /// <summary>
        /// The resolved lazer data folder, or null if none was found.
        /// </summary>
        public string? LinkedLazerFolder { get; private set; }

        /// <summary>
        /// Completes when the startup sync from the lazer folder has finished.
        /// </summary>
        public Task<LazerSyncResult?> LazerSyncTask { get; private set; } = Task.FromResult<LazerSyncResult?>(null);

        public McLazerGame(string[]? args = null)
            : base(args)
        {
            API = new OfflineAPIAccess();
        }

        public override EndpointConfiguration CreateEndpoints() => new OfflineEndpointConfiguration();

        protected override Storage CreateStorage(GameHost host, Storage defaultStorage)
        {
            // The host calls this before it loads its input and framework settings, so lazer's can still replace McLazer's here.
            string ownFolder = defaultStorage.GetFullPath(string.Empty);
            LazerFolder.BorrowHostSettings(ownFolder, LazerPath ?? LazerFolder.DefaultPath(ownFolder));

            return base.CreateStorage(host, defaultStorage);
        }

        public override void SetHost(GameHost host)
        {
            string ownFolder = (Storage ?? host.Storage).GetFullPath(string.Empty);
            LinkedLazerFolder = LazerFolder.Resolve(LazerPath ?? LazerFolder.DefaultPath(ownFolder));

            // Must be set before any file store is created.
            RealmFileStore.ReadFallback = LinkedLazerFolder == null ? null : host.GetStorage(Path.Combine(LinkedLazerFolder, @"files"));

            if (LinkedLazerFolder != null)
                borrowLazerSettings(LinkedLazerFolder, ownFolder);

            base.SetHost(host);
        }

        /// <summary>
        /// Replace McLazer's settings file with lazer's, before it is loaded. Changes made in McLazer are saved to McLazer's copy and last until the next start.
        /// </summary>
        private static void borrowLazerSettings(string lazerFolder, string ownFolder)
        {
            // shortcut: lazer's version setting comes along, so a packaged McLazer would post "updated" at every start. Drop that line when packaging (M8).
            try
            {
                // Lazer's login token must not leave lazer's folder. McLazer never logs in.
                File.WriteAllLines(Path.Combine(ownFolder, @"game.ini"),
                    File.ReadLines(Path.Combine(lazerFolder, @"game.ini")).Where(line => !line.StartsWith(@"Token", StringComparison.Ordinal)));
            }
            catch (Exception e)
            {
                // No settings file in lazer's folder, or it is unreadable: keep McLazer's last copy.
                Logger.Log($@"Could not read lazer's settings: {e.Message}");
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // OsuGameBase only adds an APIAccess to the scene graph.
            Add((OfflineAPIAccess)API);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (LinkedLazerFolder == null)
            {
                Notifications.Post(new SimpleNotification
                {
                    Text = @"No osu!lazer data folder was found, so there are no beatmaps. Click here to choose the folder.",
                    Activated = () =>
                    {
                        FirstRunOverlay.Show();
                        return true;
                    }
                });
                return;
            }

            var realm = Dependencies.Get<RealmAccess>();

            LazerSyncTask = Task.Run<LazerSyncResult?>(() => LazerSync.Run(realm, LinkedLazerFolder, Storage, IsLazerRunning));
            LazerSyncTask.ContinueWith(t => Schedule(() =>
            {
                Logger.Log($@"Sync from the lazer folder: {t.GetResultSafely()}");

                switch (t.GetResultSafely())
                {
                    case LazerSyncResult.Synced:
                        // The skin chosen in lazer's settings may only now be in the database.
                        SkinManager.SetSkinFromConfiguration(LocalConfig.Get<string>(OsuSetting.Skin));
                        break;

                    case LazerSyncResult.LazerRunning:
                        Notifications.Post(new SimpleNotification { Text = @"osu!lazer is running, so McLazer is using the beatmaps from its last sync. Close lazer and restart McLazer to pick up changes." });
                        break;

                    case LazerSyncResult.LazerNewer:
                        Notifications.Post(new SimpleErrorNotification { Text = @"Update McLazer to match your lazer version. Until then, McLazer uses the beatmaps from its last sync." });
                        break;

                    case LazerSyncResult.Unreadable:
                        Notifications.Post(new SimpleErrorNotification { Text = @"McLazer could not read lazer's database, so it is using the beatmaps from its last sync. See the logs for details." });
                        break;
                }
            }), TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>
        /// Whether lazer is running. Replaced in tests.
        /// </summary>
        protected virtual bool IsLazerRunning() => LazerFolder.IsLazerRunning();

        // New beatmaps come only through lazer. This covers drag-and-drop, file arguments and IPC.
        public override Task Import(params string[] paths) => refuseImport();

        public override Task Import(ImportTask[] imports, ImportParameters parameters = default) => refuseImport();

        private Task refuseImport()
        {
            Schedule(() => Notifications.Post(new SimpleNotification { Text = @"McLazer does not import files. Import in osu!lazer, then restart McLazer." }));
            return Task.CompletedTask;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            RealmFileStore.ReadFallback = null;
        }
    }
}
