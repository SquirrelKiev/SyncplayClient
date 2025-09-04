using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SyncPlay.Protocol.Models;

namespace SyncPlay.Protocol;

public sealed class SyncplayClient(ILogger<SyncplayClient> logger) : IDisposable
{
    [PublicAPI] public string? Host { get; private set; }
    [PublicAPI] public int? Port { get; private set; }
    [PublicAPI] public string? Username { get; private set; }
    [PublicAPI] public string? RoomName { get; private set; }
    [PublicAPI] public FeatureSet? ServerFeatures { get; private set; }
    [PublicAPI] public string? ServerVersion { get; private set; }
    [PublicAPI] public string? MessageOfTheDay { get; private set; }
    [PublicAPI] public List<SyncplayUser> Users { get; private set; } = [];

    [PublicAPI] public float ServerPlaybackPosition { get; private set; } = 0f;
    [PublicAPI] public bool ServerPaused { get; private set; } = true;
    [PublicAPI] public IReadOnlyList<string> ServerPlaylist { get; private set; } = [];
    [PublicAPI] public int ServerPlaylistIndex { get; private set; } = 0;

    [PublicAPI] public string? ServerSelectedPlaylistEntry => ServerPlaylist.ElementAtOrDefault(ServerPlaylistIndex);

    [PublicAPI] public double ClientRtt { get; private set; }
    [PublicAPI] public double ServerRtt { get; private set; }

    [PublicAPI] public event Action<SyncplayUser>? OnUserJoined, OnUserLeft;

    // TODO: Change this to an OnReady, that makes sure we've received users etc (Hello command gets sent along side a lot of other state commands, and we need to manually ask for a user list)
    [PublicAPI] public event Action? OnHelloReceived;
    [PublicAPI] public event Action<ChatCommand>? OnChatMessageReceived;
    [PublicAPI] public event Action<PlaylistChangedEventArgs>? OnPlaylistChanged;
    [PublicAPI] public event Action<PlaylistIndexChangedEventArgs>? OnPlaylistIndexChanged;

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
    // [MemberNotNull(nameof(ListenTask))] // technically will be null if this task gets canceled. i suppose it'll throw in that case tho?
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
            RoomName = roomName;
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
            }).ToJson());

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
            }).ToJson());

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

        var data = new Dictionary<string, string> { { "Chat", message } };

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

        await WriteDataAsync(JsonSerializer.Serialize(data));
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

        await WriteDataAsync(JsonSerializer.Serialize(data));
    }

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

            var clientLatencyCalc =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d; // server wants it in seconds

            // this message is from the server; should have the ServerRtt - if not, we should probably throw anyway
            ServerRtt = serverState.Ping.ServerRtt!.Value;
            ClientRtt = clientLatencyCalc - serverState.Ping.ClientLatencyCalculation;

            responseState.Ping = new StateCommand.PingInfo
            {
                LatencyCalculation = serverLatencyCalc,
                ClientLatencyCalculation = clientLatencyCalc,
                // not sure if this actually does anything? all the docs just have it as 0.
                ClientRtt = ClientRtt
            };
        }

        if (serverState.PlayState != null)
        {
            ServerPlaybackPosition = serverState.PlayState.Position;
            ServerPaused = serverState.PlayState.Paused;

            if (serverState.IgnoringOnTheFly?.ServerIgnoring == true)
            {
                responseState.IgnoringOnTheFly = new StateCommand.IgnoringOnTheFlyInfo
                {
                    ServerIgnoring = true
                };
            }
        }

        var data = new RootCommand(responseState).ToJson();

        await WriteDataAsync(data);
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
            // TODO: Check if this is necessary. Test against servers with "--isolate-room" disabled
            if (userData.RoomInfo.Name != RoomName)
                continue;

            if (userData.EventInfo != null)
            {
                if (userData.EventInfo.Joined)
                {
                    logger.LogInformation("User {username} has joined room {roomName}.", username,
                        userData.RoomInfo.Name);

                    var user = new SyncplayUser(username, userData);
                    Users.Add(user);

                    OnUserJoined?.Invoke(user);
                }
                else if (userData.EventInfo.Left)
                {
                    logger.LogInformation("User {username} has left room {roomName}.", username,
                        userData.RoomInfo.Name);

                    var user = Users.FirstOrDefault(x => x.Username == username);

                    if (user == null)
                    {
                        logger.LogError(
                            "Internal error: User {username} could not be found in local user list! This is very bad, we should be aware of them.",
                            username);
                        continue;
                    }

                    Users.Remove(user);

                    OnUserLeft?.Invoke(user);
                }
            }
        }
    }

    private void HandleSet_Ready(SetCommand.ReadyInfo readyInfo)
    {
        if (!readyInfo.IsReady.HasValue)
            return;

        var user = Users.FirstOrDefault(x => x.Username == readyInfo.Username);

        if (user == null)
        {
            logger.LogWarning("User {username} is not in the user list, ignoring ready state.", readyInfo.Username);
            return;
        }

        user.IsReady = readyInfo.IsReady.Value;
        logger.LogTrace("Set user {username} ready state as {readyState}", readyInfo.Username, readyInfo.IsReady.Value);
    }

    private void HandleSet_PlaylistChange(SetCommand.PlaylistChangeInfo playlistChangeInfo)
    {
        var oldPlaylist = ServerPlaylist;

        ServerPlaylist = playlistChangeInfo.Files.AsReadOnly();

        logger.LogTrace("{user} set server playlist to {playlist}", playlistChangeInfo.ChangedBy,
            playlistChangeInfo.Files);

        // not sure if I should just return the username or SyncplayUser here
        OnPlaylistChanged?.Invoke(new PlaylistChangedEventArgs(oldPlaylist, ServerPlaylist,
            playlistChangeInfo.ChangedBy));
    }

    private void HandleSet_PlaylistIndex(SetCommand.PlaylistIndexInfo playlistIndexInfo)
    {
        var oldIndex = ServerPlaylistIndex;

        ServerPlaylistIndex = playlistIndexInfo.Index ?? -1;

        logger.LogTrace("{user} set server playlist index to {playlistIndex} ({playlistEntry})",
            playlistIndexInfo.ChangedBy, playlistIndexInfo.Index, ServerSelectedPlaylistEntry);

        OnPlaylistIndexChanged?.Invoke(
            new PlaylistIndexChangedEventArgs(oldIndex, ServerPlaylistIndex, playlistIndexInfo.ChangedBy));
    }

    #endregion Set

    private void HandleList(Dictionary<string, Dictionary<string, ListCommandUserInfo>> list)
    {
        Debug.Assert(RoomName != null);

        // TODO: investigate why it sends multiple rooms. --isolate-room?
        if (!list.TryGetValue(RoomName, out var usernameToDataMap))
        {
            return;
        }

        foreach (var (username, userInfo) in usernameToDataMap)
        {
            var existingUser = Users.FirstOrDefault(x => x.Username == username);

            if (existingUser == null)
            {
                logger.LogTrace("Not seen user {username} before, adding to user list.", username);

                existingUser = new SyncplayUser(username, userInfo);
                Users.Add(existingUser);
            }
            else
            {
                logger.LogTrace("Existing user {username}, updating state.", username);
                existingUser.UpdateProperties(userInfo);
            }
        }
    }

    private async Task RequestUserListRefreshAsync()
    {
        var data = new Dictionary<string, object?> { { "List", null } };

        var json = JsonSerializer.Serialize(data);

        await WriteDataAsync(json);
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