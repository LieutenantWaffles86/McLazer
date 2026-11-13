// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Fork.Lazer;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Scoring;
using osu.Game.Users;
using Realms;

namespace osu.Game.Fork.Profiles
{
    /// <summary>
    /// A local player: a name, and the scores saved under its ID. There are no passwords.
    /// </summary>
    public class Profile
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }

        public APIUser ToAPIUser() => new APIUser
        {
            Id = Id,
            Username = Username,
            CountryCode = Enum.TryParse(CountryCode, out CountryCode country) ? country : Users.CountryCode.Unknown,
        };
    }

    /// <summary>
    /// The local profiles, kept in <c>profiles.json</c> in McLazer's own folder. There is always at least one, and one is always active.
    /// </summary>
    public class ProfileStore
    {
        /// <summary>
        /// Profile IDs start far above any real osu! user ID, so local scores can be told apart from lazer's.
        /// </summary>
        public const int FIRST_ID = 2_000_000_000;

        public const string DELETED_PLAYER = @"Deleted player";

        private const string filename = @"profiles.json";

        public static bool IsLocal(int userId) => userId >= FIRST_ID;

        /// <summary>
        /// Raised after any change: the active profile, a rename, a new or deleted profile.
        /// </summary>
        public event Action? Changed;

        public IReadOnlyList<Profile> Profiles => data.Profiles;

        public Profile Active => data.Profiles.First(p => p.Id == data.ActiveId);

        private readonly Storage storage;
        private readonly RealmAccess realm;
        private readonly Data data;

        public ProfileStore(Storage storage, RealmAccess realm, string defaultName)
        {
            this.storage = storage;
            this.realm = realm;

            data = load();

            if (data.Profiles.Count == 0)
            {
                var first = add(string.IsNullOrWhiteSpace(defaultName) ? @"Player" : defaultName.Trim());

                // Scores set before there were profiles were saved under the guest user. They go to the first profile. Copies of lazer's guest scores stay lazer's.
                realm.Write(r =>
                {
                    foreach (var score in r.All<ScoreInfo>().Filter(@"User.OnlineID == $0", APIUser.SYSTEM_USER_ID).ToList().Where(s => !LazerSync.IsLazerScore(s)))
                        score.User = first.ToAPIUser();
                });
            }

            if (data.Profiles.All(p => p.Id != data.ActiveId))
                data.ActiveId = data.Profiles[0].Id;
        }

        /// <summary>
        /// Create a profile and make it active. Returns null if the name is empty or taken.
        /// </summary>
        public Profile? Create(string name)
        {
            if (!isFree(name))
                return null;

            var profile = add(name.Trim());
            changed();
            return profile;
        }

        public void Switch(Profile profile)
        {
            data.ActiveId = profile.Id;
            changed();
        }

        /// <summary>
        /// Rename a profile and its scores. Returns false if the name is empty or taken.
        /// </summary>
        public bool Rename(Profile profile, string name)
        {
            if (!isFree(name))
                return false;

            profile.Username = name.Trim();
            // Scores store the name next to the ID, so they are renamed too.
            realm.Write(r =>
            {
                foreach (var score in scoresOf(r, profile))
                    score.RealmUser.Username = profile.Username;
            });
            changed();
            return true;
        }

        /// <summary>
        /// Delete a profile. Returns false for the last profile, which cannot be deleted.
        /// </summary>
        /// <param name="profile">The profile to delete.</param>
        /// <param name="deleteScores">Whether its scores go too. If not, they stay under the name "Deleted player".</param>
        public bool Delete(Profile profile, bool deleteScores)
        {
            if (data.Profiles.Count == 1)
                return false;

            realm.Write(r =>
            {
                foreach (var score in scoresOf(r, profile))
                {
                    if (deleteScores)
                        score.DeletePending = true;
                    else
                        score.RealmUser.Username = DELETED_PLAYER;
                }
            });

            data.Profiles.Remove(profile);

            if (data.ActiveId == profile.Id)
                data.ActiveId = data.Profiles[0].Id;

            changed();
            return true;
        }

        private bool isFree(string name) =>
            !string.IsNullOrWhiteSpace(name) && data.Profiles.All(p => !p.Username.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        private Profile add(string name)
        {
            // IDs are never reused: scores of a deleted profile may still carry its ID.
            var profile = new Profile { Id = data.NextId++, Username = name, CreatedAt = DateTimeOffset.Now };

            data.Profiles.Add(profile);
            data.ActiveId = profile.Id;
            save();
            return profile;
        }

        private static IQueryable<ScoreInfo> scoresOf(Realm r, Profile profile) => r.All<ScoreInfo>().Filter(@"User.OnlineID == $0", profile.Id);

        private void changed()
        {
            save();
            Changed?.Invoke();
        }

        private Data load()
        {
            string path = storage.GetFullPath(filename);

            if (!File.Exists(path))
                return new Data();

            try
            {
                return JsonConvert.DeserializeObject<Data>(File.ReadAllText(path)) ?? throw new InvalidDataException(@"The file is empty.");
            }
            catch (Exception e)
            {
                // Start afresh, but keep the unreadable file: the next save would overwrite it.
                Logger.Error(e, $@"Could not read {filename}. It was moved to {filename}.corrupt.");
                File.Move(path, path + @".corrupt", true);
                return new Data();
            }
        }

        private void save()
        {
            string path = storage.GetFullPath(filename);

            // Write a temporary file and rename it, so a crash never leaves half a file.
            File.WriteAllText(path + @".tmp", JsonConvert.SerializeObject(data, Formatting.Indented));
            File.Move(path + @".tmp", path, true);
        }

        private class Data
        {
            public int NextId { get; set; } = FIRST_ID;
            public int ActiveId { get; set; }
            public List<Profile> Profiles { get; set; } = new List<Profile>();
        }
    }
}
