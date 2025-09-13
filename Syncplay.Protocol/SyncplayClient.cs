using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SyncPlay.Protocol.Models;

namespace SyncPlay.Protocol;

// TODO: Add support for controlled rooms
public sealed class SyncplayClient(ILogger<SyncplayClient> logger) : IDisposable
{
    // not sure if I should have these as nullable or not - for API consumer reasons
    // the only time they're null is if the user hasn't called ConnectAsync yet for whatever reason
    // but that shouldn't ever really be a problem
    [PublicAPI] public string? Host { get; private set; }

    [PublicAPI] public int? Port { get; private set; }

    // TODO: username and RoomName should be replaced with CurrentUser
    [PublicAPI] public string? Username { get; private set; }
    [PublicAPI] public string? RoomName { get; private set; }
    [PublicAPI] public FeatureSet? ServerFeatures { get; private set; }
    [PublicAPI] public string? ServerVersion { get; private set; }

    [PublicAPI] public string? MessageOfTheDay { get; private set; }

    // replace with IReadOnlyDictionary<string, RoomUser>?
    [PublicAPI] public IReadOnlyCollection<RoomUser> Users => userlist.Values;

    [PublicAPI]
    public RoomUser CurrentUser
    {
        get
        {
            if (currentUser != null)
                return currentUser;

            if (Username == null || !TryGetUser(Username, out var user))
                throw new InvalidOperationException(
                    "Could not find current user in user list? This is bad, report this.");

            currentUser = user;
            return user;
        }
    }

    private RoomUser? currentUser;

    // wonder if this should be a float or a double
    [PublicAPI] public float ServerPlaybackPosition { get; private set; } = 0f;
    [PublicAPI] public bool ServerPaused { get; private set; } = true;
    [PublicAPI] public string? ServerPlaybackSetBy { get; private set; }
    [PublicAPI] public bool ServerLastPlaybackWasSeek { get; private set; }
    [PublicAPI] public IReadOnlyList<string> ServerPlaylist { get; private set; } = [];
    [PublicAPI] public int ServerPlaylistIndex { get; private set; } = 0;

    [PublicAPI] public X509Certificate? TlsCertificate => (currentStream as SslStream)?.RemoteCertificate;

    [PublicAPI] public string? ServerSelectedPlaylistEntry => ServerPlaylist.ElementAtOrDefault(ServerPlaylistIndex);

    [PublicAPI] public double ClientRtt { get; private set; }
    [PublicAPI] public double ClientLastForwardDelay { get; set; }
    private double ClientAverageRtt { get; set; }
    [PublicAPI] public double ServerRtt { get; private set; }

    [PublicAPI] public event Action<RoomUser>? OnUserJoined, OnUserLeft;
    [PublicAPI] public event Action<UserReadyStateChangedEventArgs>? OnUserReadyStateChanged;

    // TODO: Change this to an OnReady, that makes sure we've received users etc
    // (Hello command gets sent along side a lot of other state commands, and we need to manually ask for a user list)
    [PublicAPI] public event Action? OnHelloReceived;
    [PublicAPI] public event Action? OnForcedPlaybackState;
    [PublicAPI] public event Action<ChatCommand>? OnChatMessageReceived;
    [PublicAPI] public event Action<PlaylistChangedEventArgs>? OnPlaylistChanged;
    [PublicAPI] public event Action<PlaylistIndexChangedEventArgs>? OnPlaylistIndexChanged;
    [PublicAPI] public event Action<UserFileChangedEventArgs>? OnUserFileChanged;
    [PublicAPI] public event Action<UserRoomChangedEventArgs>? OnUserRoomChanged;

    [PublicAPI] public event Func<PassiveStateReport?>? RequestPassiveStateReport;

    private int clientIgnoringOnTheFly;

    private readonly Dictionary<string, RoomUser> userlist = [];
    private readonly TcpClient tcpClient = new();
    private Stream? currentStream;
    private StreamReader? reader;
    private StreamWriter? writer;
    private readonly Encoding encoding = new UTF8Encoding(false);

    /// <summary>
    /// The task responsible for listening to responses from the server. Exposed with the intention of being awaited.
    /// </summary>
    // this feels like a bit of a dumb solution
    [PublicAPI]
    public Task? ListenTask { get; private set; }

    private IDisposable? logContextScope;

    public SyncplayClient() : this(NullLogger<SyncplayClient>.Instance)
    {
    }

    #region Public API

    [PublicAPI]
    // [MemberNotNull(nameof(ListenTask))] // technically will be null if this task gets canceled. I suppose it'll throw in that case tho?
    public async Task ConnectAsync(string host, int port, string? hostPassword, string roomName, string username,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            logContextScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["host"] = host,
                ["port"] = port,
                ["room"] = roomName,
                ["username"] = username
            });
            await tcpClient.ConnectAsync(host, port, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("Connection to {host}:{port} established", host, port);

            Host = host;
            Port = port;
            RoomName = roomName; // this and username will be overwritten by the hello response
            Username = username;
            string? passwordHash = null;
            if (hostPassword != null)
                passwordHash = Convert.ToHexString(MD5.HashData(encoding.GetBytes(hostPassword)));

            currentStream = tcpClient.GetStream();
            reader = new StreamReader(currentStream, encoding);
            writer = new StreamWriter(currentStream, encoding) { AutoFlush = true };

            // the server won't respond to anything until we ask about TLS,
            // so probably best to handle this outside the main loop
            await WriteDataAsync(new RootCommand(new TlsSupportCommand
            {
                StartTls = TlsSupportCommand.TlsState.Send
            }));

            var response = await ReadDataAsync(cancellationToken);
            if (response == null) throw new IOException("Connection closed before TLS negotiation?");

            var command = JsonSerializer.Deserialize<RootCommand>(response)!;
            if (command.Tls is { StartTls: TlsSupportCommand.TlsState.True })
            {
                await UpgradeToTlsAsync();
            }

            await WriteDataAsync(new RootCommand(new HelloCommand()
            {
                Username = username,
                PasswordHash = passwordHash,
                RoomInfo = new RoomInfo()
                {
                    Name = roomName
                }
            }));

            ListenTask = Task.Run(() => MainListenLoopAsync(cancellationToken), cancellationToken);
        }
        catch (Exception)
        {
            Dispose();
            throw;
        }
    }

    [PublicAPI]
    public void Disconnect()
    {
        if (tcpClient.Connected)
        {
            tcpClient.Close();
        }
    }

    [PublicAPI]
    public async Task SendChatMessageAsync(string message)
    {
        ThrowIfNotReady();

        var data = new SentByClientChatCommand()
        {
            Message = message
        };

        await WriteDataAsync(JsonSerializer.Serialize(data));
    }

    [PublicAPI]
    public async Task SetPlaylistAsync(IEnumerable<string> playlist)
    {
        ThrowIfNotReady();

        var data = new RootCommand(new SetCommand()
        {
            PlaylistChange = new SetCommand.PlaylistChangeInfo()
            {
                Files = playlist.ToList()
            }
        });

        await WriteDataAsync(data);
    }

    [PublicAPI]
    public async Task SetPlaylistIndexAsync(int index)
    {
        ThrowIfNotReady();

        var data = new RootCommand(new SetCommand()
        {
            PlaylistIndex = new SetCommand.PlaylistIndexInfo()
            {
                Index = index
            }
        });

        await WriteDataAsync(data);
    }

    [PublicAPI]
    public async Task SetReadyAsync(bool ready, bool manuallyInitiated = true)
    {
        ThrowIfNotReady();

        var data = new RootCommand(new SetCommand()
        {
            Ready = new SetCommand.ReadyInfo()
            {
                IsReady = ready,
                ManuallyInitiated = manuallyInitiated
            }
        });

        await WriteDataAsync(data);
    }

    [PublicAPI]
    public async Task SetReadyAsync(string username, bool ready, bool manuallyInitiated = true)
    {
        ThrowIfNotReady();

        var data = new RootCommand(new SetCommand()
        {
            Ready = new SetCommand.ReadyInfo()
            {
                IsReady = ready,
                Username = username,
                ManuallyInitiated = manuallyInitiated
            }
        });

        await WriteDataAsync(data);
    }

    [PublicAPI]
    public async Task SetFileAsync(MediaFile file)
    {
        // syncplay server seems to completely ignore null files (`{"Set":{"file":{}}}` or `{"Set":{"file":null}}`)
        ThrowIfNotReady();

        var data = new RootCommand(new SetCommand()
        {
            File = file
        });

        await WriteDataAsync(data);
    }

    [PublicAPI]
    public async Task ForcePlaybackStateAsync(bool paused, float position, bool isSeek)
    {
        ThrowIfNotReady();

        var data = new RootCommand(new StateCommand()
        {
            PlayState = new StateCommand.PlayStateInfo()
            {
                Paused = paused,
                Position = position,
                DoSeek = isSeek
            },
            IgnoringOnTheFly = new StateCommand.IgnoringOnTheFlyInfo()
            {
                Client = ++clientIgnoringOnTheFly
            },
            Ping = new StateCommand.PingInfo()
            {
                ClientLatencyCalculation = GetUnixTimestampInSeconds(),
                ClientRtt = ClientRtt
            }
        });

        await WriteDataAsync(data);
    }

    [PublicAPI]
    public async Task MoveToRoomAsync(string roomName)
    {
        ThrowIfNotReady();

        var data = new RootCommand(new SetCommand()
        {
            Room = new RoomInfo()
            {
                Name = roomName
            }
        });

        // TODO: batch
        await WriteDataAsync(data);

        // so we get the ready states
        await RequestUserListRefreshAsync();
    }

    [PublicAPI]
    public RoomUser? GetUser(string? username)
    {
        if (username == null)
            return null;

        userlist.TryGetValue(username, out var userObject);

        return userObject;
    }

    [PublicAPI]
    public bool TryGetUser(string username, [NotNullWhen(true)] out RoomUser? user) =>
        userlist.TryGetValue(username, out user);

    #endregion Public API

    private async Task MainListenLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            Debug.Assert(currentStream != null);
            Debug.Assert(reader != null);

            logger.LogTrace("Began listening for data");

            while (tcpClient.Connected)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Disconnect();
                    break;
                }

                var line = await ReadDataAsync(cancellationToken);
                if (line == null) break;

                var commandRoot = JsonSerializer.Deserialize<RootCommand>(line);
                Debug.Assert(commandRoot != null);

                // hello is handled here because the server usually sends the hello response after a couple state messages
                if (commandRoot.Hello != null)
                {
                    var obj = commandRoot.Hello;

                    Username = obj.Username;
                    RoomName = obj.RoomInfo.Name;
                    ServerFeatures = obj.Features;
                    ServerVersion = obj.RealVersion;

                    MessageOfTheDay = obj.MessageOfTheDay;

                    OnHelloReceived?.Invoke();

                    await RequestUserListRefreshAsync();
                }

                if (commandRoot.Tls != null)
                {
                    logger.LogWarning(
                        "Server sent a TLS request, which should only appear at initialization. Something is wrong.");
                }

                if (commandRoot.State != null)
                {
                    await HandleState(commandRoot.State);
                }

                if (commandRoot.Set != null)
                {
                    HandleSet(commandRoot.Set);
                }

                if (commandRoot.Chat != null)
                {
                    HandleChat(commandRoot.Chat);
                }

                if (commandRoot.UserList != null)
                {
                    HandleList(commandRoot.UserList);
                }

                if (commandRoot.ExtraProperties.Count != 0)
                {
                    foreach (var extraProperty in commandRoot.ExtraProperties)
                    {
                        logger.LogWarning("Unknown command {command}. Full data is {json}.", extraProperty, line);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Syncplay client session cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Uncaught exception in {clientName}!", nameof(SyncplayClient));
            throw;
        }
        finally
        {
            logger.LogDebug("Stopped listening for data.");
            tcpClient.Close();
        }
    }

    private async Task UpgradeToTlsAsync()
    {
        Debug.Assert(Host != null);
        Debug.Assert(Username != null);
        Debug.Assert(RoomName != null);

        logger.LogTrace("Upgrading to TLS...");

        var sslStream = new SslStream(tcpClient.GetStream());

        try
        {
            await sslStream.AuthenticateAsClientAsync(Host, null, SslProtocols.Tls12 | SslProtocols.Tls13, true);

            var certificate = sslStream.RemoteCertificate;

            if (certificate == null)
                throw new AuthenticationException("Certificate was null?");

            logger.LogInformation("TLS connection established.");

            currentStream = sslStream;
            reader = new StreamReader(currentStream, encoding);
            writer = new StreamWriter(currentStream, encoding) { AutoFlush = true };
        }
        catch (AuthenticationException ex)
        {
            logger.LogError("TLS authentication failed: {exceptionMessage}", ex.Message);
            throw;
        }
    }

    private void HandleChat(ChatCommand chat)
    {
        logger.LogTrace("Chat message received: <{user}> {message}", chat.Username, chat.Message);

        OnChatMessageReceived?.Invoke(chat);
    }

    private async Task HandleState(StateCommand serverState)
    {
        var responseState = new StateCommand();

        if (serverState.Ping != null)
        {
            var serverLatencyCalc = serverState.Ping.LatencyCalculation;

            Debug.Assert(serverState.Ping.ServerRtt != null);

            var timestamp = serverState.Ping.ClientLatencyCalculation;
            var serverRtt = serverState.Ping.ServerRtt;

            if (timestamp != null && serverRtt != null)
            {
                UpdateClientRtt(timestamp.Value, serverRtt.Value);
                ServerRtt = serverRtt.Value;
            }

            var clientLatencyCalc = GetUnixTimestampInSeconds();

            responseState.Ping = new StateCommand.PingInfo
            {
                LatencyCalculation = serverLatencyCalc,
                ClientLatencyCalculation = clientLatencyCalc,
                ClientRtt = ClientRtt
            };
            logger.LogTrace("ping: {ping}, serverrtt: {serverRtt}, fwdelay: {forward}", responseState.Ping,
                serverRtt, ClientLastForwardDelay);
        }

        if (serverState.PlayState != null)
        {
            // don't do anything if we have pending forced state changes that we're waiting to be acknowledged
            // unless the server demands a change in which case we barrel along
            if (clientIgnoringOnTheFly == 0 || serverState.IgnoringOnTheFly?.Server != 0)
            {
                ServerPlaybackPosition = serverState.PlayState.Position;
                ServerPaused = serverState.PlayState.Paused;
                ServerPlaybackSetBy = serverState.PlayState.SetBy;
                ServerLastPlaybackWasSeek = serverState.PlayState.DoSeek ?? false;

                if (serverState.Ping != null && !ServerPaused)
                {
                    ServerPlaybackPosition = (float)(ServerPlaybackPosition + ClientLastForwardDelay);
                }
            }

            if (serverState.IgnoringOnTheFly != null)
            {
                // server has acknowledged our forced state change
                if (serverState.IgnoringOnTheFly.Client == clientIgnoringOnTheFly)
                {
                    clientIgnoringOnTheFly = 0;
                }

                // we've got a forced state change from the server that we need to acknowledge
                if (serverState.IgnoringOnTheFly.Server != 0)
                {
                    clientIgnoringOnTheFly = 0;

                    responseState.IgnoringOnTheFly = new StateCommand.IgnoringOnTheFlyInfo
                    {
                        Server = serverState.IgnoringOnTheFly.Server
                    };
                }
            }

            if (serverState.IgnoringOnTheFly?.Server != 0 && serverState.IgnoringOnTheFly?.Client == 0)
                OnForcedPlaybackState?.Invoke();

            // if (serverState.IgnoringOnTheFly?.Server > 0)
            // {
            //     responseState.IgnoringOnTheFly = new StateCommand.IgnoringOnTheFlyInfo
            //     {
            //         Server = serverState.IgnoringOnTheFly.Server
            //     };
            //
            //     if (serverState.IgnoringOnTheFly.Client == 0)
            //         OnForcedPlaybackState?.Invoke();
            // }

            if (clientIgnoringOnTheFly == 0)
            {
                var passiveStateReport = RequestPassiveStateReport?.Invoke();

                if (passiveStateReport != null)
                {
                    responseState.PlayState = new StateCommand.PlayStateInfo
                    {
                        Position = passiveStateReport.Position,
                        Paused = passiveStateReport.Paused
                    };
                }
            }
        }

        var data = new RootCommand(responseState).ToJson();

        await WriteDataAsync(data);
    }

    private static double GetUnixTimestampInSeconds()
    {
        // server wants it in seconds
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    }

    // math magic i have no idea what does
    // https://github.com/Syncplay/syncplay/blob/db95264fae901e32e4d2ce991fc265b68545aaff/syncplay/protocols.py#L790
    private void UpdateClientRtt(double timestamp, double senderRtt)
    {
        var clientRtt = GetUnixTimestampInSeconds() - timestamp;
        if (clientRtt < 0 || senderRtt < 0)
        {
            ClientRtt = clientRtt;
            return;
        }

        var clientAverageRtt = ClientAverageRtt;

        if (clientAverageRtt == 0)
            clientAverageRtt = clientRtt;

        const float pingMovingAverageWeight = 0.85f;

        clientAverageRtt = clientAverageRtt * pingMovingAverageWeight + clientRtt * (1 - pingMovingAverageWeight);

        double clientLastForwardDelay;
        if (senderRtt < clientRtt)
        {
            clientLastForwardDelay = clientAverageRtt / 2 + (clientRtt - senderRtt);
        }
        else
        {
            clientLastForwardDelay = clientAverageRtt / 2;
        }

        ClientRtt = clientRtt;
        ClientAverageRtt = clientAverageRtt;
        ClientLastForwardDelay = clientLastForwardDelay;
    }

    #region Set

    private void HandleSet(SetCommand setData)
    {
        if (setData.Users != null)
        {
            HandleSet_Users(setData.Users);
        }

        if (setData.Ready != null)
        {
            HandleSet_Ready(setData.Ready);
        }

        if (setData.PlaylistChange != null)
        {
            HandleSet_PlaylistChange(setData.PlaylistChange);
        }

        if (setData.PlaylistIndex != null)
        {
            HandleSet_PlaylistIndex(setData.PlaylistIndex);
        }
    }

    private void HandleSet_Users(Dictionary<string, SetCommand.SetUserInfo> users)
    {
        foreach (var (username, userData) in users)
        {
            if (userData.EventInfo != null)
            {
                if (userData.EventInfo.Joined)
                {
                    logger.LogInformation("User {username} has joined room {roomName}.", username,
                        userData.RoomInfo?.Name);

                    var user = new RoomUser(username, userData);
                    userlist.Add(user.Username, user);

                    OnUserJoined?.Invoke(user);
                }
                else if (userData.EventInfo.Left)
                {
                    logger.LogInformation("User {username} has left room {roomName}.", username,
                        userData.RoomInfo?.Name);

                    if (!TryGetUser(username, out var user))
                    {
                        logger.LogError(
                            "Internal error: User {username} could not be found in local user list! This is very bad, we should be aware of them.",
                            username);
                        continue;
                    }

                    userlist.Remove(user.Username);

                    OnUserLeft?.Invoke(user);
                }
            }
            else if (userData.RoomInfo != null)
            {
                if (!TryGetUser(username, out var user))
                {
                    logger.LogError(
                        "Internal error: User {username} could not be found in local user list! This is very bad, we should be aware of them.",
                        username);
                    continue;
                }

                if (user.RoomName != userData.RoomInfo.Name)
                {
                    var oldRoomName = user.RoomName;

                    logger.LogTrace("User {username} has changed room from {oldRoomName} to {roomName}.", username,
                        oldRoomName, userData.RoomInfo.Name);

                    if (username == Username)
                    {
                        RoomName = userData.RoomInfo.Name;
                    }

                    OnUserRoomChanged?.Invoke(new UserRoomChangedEventArgs(user, oldRoomName));
                }
            }

            if (userData.FileInfo != null)
            {
                if (!TryGetUser(username, out var user))
                {
                    logger.LogError(
                        "Internal error: User {username} could not be found in local user list! This is very bad, we should be aware of them.",
                        username);
                    continue;
                }

                var oldFile = user.FileInfo;

                user.FileInfo = userData.FileInfo;

                logger.LogTrace("{user} set file to {file}", username, userData.FileInfo);

                OnUserFileChanged?.Invoke(new UserFileChangedEventArgs(user, oldFile));
            }
        }
    }

    private void HandleSet_Ready(SetCommand.ReadyInfo readyInfo)
    {
        if (!readyInfo.IsReady.HasValue || readyInfo.Username == null)
            return;

        if (!TryGetUser(readyInfo.Username, out var user))
        {
            logger.LogWarning("User {username} is not in the user list, ignoring ready state.", readyInfo.Username);
            return;
        }

        user.IsReady = readyInfo.IsReady.Value;
        logger.LogTrace("Set user {username} ready state as {readyState}", readyInfo.Username, readyInfo.IsReady.Value);

        OnUserReadyStateChanged?.Invoke(new UserReadyStateChangedEventArgs(user, GetUser(readyInfo.SetBy)));
    }

    private void HandleSet_PlaylistChange(SetCommand.PlaylistChangeInfo playlistChangeInfo)
    {
        var oldPlaylist = ServerPlaylist;

        ServerPlaylist = playlistChangeInfo.Files.ToList().AsReadOnly();

        logger.LogTrace("{user} set server playlist to {playlist}", playlistChangeInfo.ChangedBy,
            playlistChangeInfo.Files);

        // not sure if I should just return the username or RoomUser here
        OnPlaylistChanged?.Invoke(new PlaylistChangedEventArgs(oldPlaylist, ServerPlaylist,
            GetUser(playlistChangeInfo.ChangedBy)));
    }

    private void HandleSet_PlaylistIndex(SetCommand.PlaylistIndexInfo playlistIndexInfo)
    {
        var oldIndex = ServerPlaylistIndex;

        ServerPlaylistIndex = playlistIndexInfo.Index ?? -1;

        logger.LogTrace("{user} set server playlist index to {playlistIndex} ({playlistEntry})",
            playlistIndexInfo.ChangedBy, playlistIndexInfo.Index, ServerSelectedPlaylistEntry);

        OnPlaylistIndexChanged?.Invoke(
            new PlaylistIndexChangedEventArgs(oldIndex, ServerPlaylistIndex, GetUser(playlistIndexInfo.ChangedBy)));
    }

    #endregion Set

    private void HandleList(Dictionary<string, Dictionary<string, ListCommandUserInfo>> list)
    {
        Debug.Assert(RoomName != null);

        foreach (var (roomName, usernameToDataMap) in list)
        {
            foreach (var (username, userInfo) in usernameToDataMap)
            {
                if (TryGetUser(username, out var existingUser))
                {
                    logger.LogTrace("Existing user {username}, updating state.", username);
                    existingUser.UpdateProperties(userInfo, roomName);
                }
                else
                {
                    logger.LogTrace("Not seen user {username} before, adding to user list.", username);

                    existingUser = new RoomUser(username, userInfo, roomName);
                    userlist.Add(existingUser.Username, existingUser);
                }
            }
        }
    }

    private async Task RequestUserListRefreshAsync()
    {
        // dumb
        const string command = "{\"List\": null}";

        await WriteDataAsync(command);
    }

    private void ThrowIfNotReady()
    {
        if (!tcpClient.Connected) throw new IOException("Client is not connected to remote server.");
    }

    private async Task<string?> ReadDataAsync(CancellationToken cancellationToken)
    {
        Debug.Assert(reader != null);

        var line = await reader.ReadLineAsync(cancellationToken);
        logger.LogTrace("server: {text}", line);

        return line;
    }

    // TODO: support batching
    // maybe i have an optional SyncplayBatch object and push serialized data onto that
    // and the user can then call a method to commit it or smth
    // transaction style
    private async Task WriteDataAsync(RootCommand command) => await WriteDataAsync(command.ToJson());

    private async Task WriteDataAsync(string text)
    {
        Debug.Assert(writer != null);

        logger.LogTrace("client: {text}", text);
        await writer.WriteLineAsync(text);
    }

    ~SyncplayClient()
    {
        Dispose();
    }

    public void Dispose()
    {
        tcpClient.Dispose();
        logContextScope?.Dispose();

        GC.SuppressFinalize(this);
    }
}