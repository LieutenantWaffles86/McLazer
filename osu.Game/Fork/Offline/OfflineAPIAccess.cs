// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Localisation;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Online.Notifications.WebSocket;

namespace osu.Game.Fork.Offline
{
    /// <summary>
    /// The API of an offline build. It never logs in and fails every request at once, so nothing reaches the network.
    /// </summary>
    /// <remarks>
    /// Must be added to the scene graph: request callbacks run on its scheduler.
    /// </remarks>
    public partial class OfflineAPIAccess : Component, IAPIProvider
    {
        private readonly DummyAPIAccess.DummyLocalUserState localUserState = new DummyAPIAccess.DummyLocalUserState();

        /// <summary>
        /// The local player. Scores are saved under this user.
        /// </summary>
        public Bindable<APIUser> LocalUser => localUserState.User;

        IBindable<APIUser> IAPIProvider.LocalUser => LocalUser;

        public ILocalUserState LocalUserState => localUserState;

        public string ScoreProcessingNoticeUrl => string.Empty;

        public Language Language => Language.en;

        public string AccessToken => string.Empty;

        public Guid SessionIdentifier { get; } = Guid.NewGuid();

        public bool IsLoggedIn => false;

        public string ProvidedUsername => LocalUser.Value.Username;

        public EndpointConfiguration Endpoints { get; } = new OfflineEndpointConfiguration();

        public int APIVersion => 0;

        public Exception? LastLoginError => null;

        public IBindable<APIState> State { get; } = new Bindable<APIState>();

        public IBindable<string?> UserFacingOutageMessage { get; } = new Bindable<string?>();

        public SessionVerificationMethod? SessionVerificationMethod => null;

        public INotificationsClient NotificationsClient { get; } = new DummyNotificationsClient();

        public OfflineAPIAccess()
        {
            LocalUser.Value = new GuestUser();
        }

        public void Queue(APIRequest request) => fail(request);

        public void Perform(APIRequest request) => fail(request);

        public Task PerformAsync(APIRequest request)
        {
            fail(request);
            return Task.CompletedTask;
        }

        private void fail(APIRequest request)
        {
            request.AttachAPI(this);
            request.Fail(new InvalidOperationException(@"McLazer is an offline build."));
        }

        void IAPIProvider.Schedule(Action action) => Schedule(action);

        public void Login(string username, string password)
        {
        }

        public void AuthenticateSecondFactor(string code)
        {
        }

        public void Logout()
        {
        }

        public IHubClientConnector? GetHubConnector(string clientName, string endpoint) => null;

        public IChatClient GetChatClient() => new OfflineChatClient();

        public RegistrationRequest.RegistrationRequestErrors? CreateAccount(string email, string username, string password) => null;

        private class OfflineChatClient : IChatClient
        {
#pragma warning disable CS0067 // Nothing ever arrives.
            public event Action<Channel>? ChannelJoined;
            public event Action<Channel>? ChannelParted;
            public event Action<List<Message>>? NewMessages;
            public event Action? PresenceReceived;
#pragma warning restore CS0067

            public void RequestPresence()
            {
            }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// Endpoints that cannot resolve (RFC 2606), so a request that slipped past <see cref="OfflineAPIAccess"/> fails fast instead of reaching ppy.
    /// </summary>
    public class OfflineEndpointConfiguration : EndpointConfiguration
    {
        public OfflineEndpointConfiguration()
        {
            WebsiteUrl = APIUrl = SpectatorUrl = MultiplayerUrl = MetadataUrl = @"https://offline.invalid";
        }
    }
}
