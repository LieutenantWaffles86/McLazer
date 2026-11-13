// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Fork.Lazer;
using osu.Game.Fork.Offline;
using osu.Game.Fork.Profiles;
using osu.Game.Fork.Stats;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Scoring;
using osu.Game.Users;
using Realms;

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

        public ProfileStore Profiles { get; private set; } = null!;

        public LocalProfileOverlay ProfileOverlay { get; private set; } = null!;

        // The online profile overlay only shows failed requests offline. Lazer's players (scores copied from lazer) have no local profile.
        public override void ShowUser(IUser user)
        {
            if (ProfileStore.IsLocal(user.OnlineID))
                ProfileOverlay.ShowProfile(user.OnlineID);
        }

        private readonly CancellationTokenSource ppCancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim ppRunning = new SemaphoreSlim(1);
        private IDisposable? newScoresSubscription;

        /// <summary>
        /// Completes when the last requested pp calculation has finished.
        /// </summary>
        public Task PPTask { get; private set; } = Task.CompletedTask;

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
            string ownFile = Path.Combine(ownFolder, @"game.ini");

            try
            {
                // McLazer keeps its own last-run version. With lazer's, it would post "updated" whenever the two differ.
                string? ownVersion = File.Exists(ownFile) ? File.ReadLines(ownFile).FirstOrDefault(isVersion) : null;

                // Lazer's login token must not leave lazer's folder. McLazer never logs in.
                var lines = File.ReadLines(Path.Combine(lazerFolder, @"game.ini")).Where(line => !line.StartsWith(@"Token", StringComparison.Ordinal) && !isVersion(line)).ToList();

                if (ownVersion != null)
                    lines.Add(ownVersion);

                File.WriteAllLines(ownFile, lines);
            }
            catch (Exception e)
            {
                // No settings file in lazer's folder, or it is unreadable: keep McLazer's last copy.
                Logger.Log($@"Could not read lazer's settings: {e.Message}");
            }

            static bool isVersion(string line) => line.Split('=')[0].Trim() == nameof(OsuSetting.Version);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var api = (OfflineAPIAccess)API;

            // OsuGameBase only adds an APIAccess to the scene graph.
            Add(api);

            // The first profile takes the username saved in lazer's settings.
            Profiles = new ProfileStore(Storage, Dependencies.Get<RealmAccess>(), LocalConfig.Get<string>(OsuSetting.Username));
            Dependencies.Cache(Profiles);

            Dependencies.Cache(ProfileOverlay = new LocalProfileOverlay());

            // New scores are saved under the local user, so they belong to the active profile.
            api.LocalUser.Value = Profiles.Active.ToAPIUser();
            Profiles.Changed += () => Schedule(() =>
            {
                var user = Profiles.Active.ToAPIUser();

                if (user.Equals(api.LocalUser.Value))
                {
                    if (user.Username == api.LocalUser.Value.Username)
                        return;

                    // A rename. Users are equal by ID, and TriggerChange() does not reach bound copies, so step through a guest.
                    api.LocalUser.Value = new GuestUser();
                }

                api.LocalUser.Value = user;
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ((IOverlayManager)this).RegisterBlockingOverlay(ProfileOverlay);

            // New scores of the profiles get their pp as soon as they are saved.
            newScoresSubscription = Dependencies.Get<RealmAccess>().RegisterForNotifications(
                r => r.All<ScoreInfo>().Filter(@"User.OnlineID >= $0 AND PP == nil AND DeletePending == false", ProfileStore.FIRST_ID),
                (_, changes) =>
                {
                    if (changes?.InsertedIndices.Length > 0)
                        calculateMissingPP();
                });

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

                // Ranked status comes from the sync, so pp waits for it.
                calculateMissingPP();

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

        private void calculateMissingPP()
        {
            var realm = Dependencies.Get<RealmAccess>();
            var difficultyCache = Dependencies.Get<BeatmapDifficultyCache>();
            var token = ppCancellation.Token;

            // One calculation at a time. A second request runs after the first and finds only what is still missing.
            PPTask = Task.Run(async () =>
            {
                await ppRunning.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    await LocalStats.PopulateMissingPP(realm, difficultyCache, token).ConfigureAwait(false);
                }
                finally
                {
                    ppRunning.Release();
                }
            }, token);
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
            ppCancellation.Cancel();
            newScoresSubscription?.Dispose();
            base.Dispose(isDisposing);
            RealmFileStore.ReadFallback = null;
        }
    }
}
