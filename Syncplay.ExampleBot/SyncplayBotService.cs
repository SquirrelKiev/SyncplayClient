using Humanizer;
using Microsoft.Extensions.Logging;
using SyncPlay.Protocol;
using SyncPlay.Protocol.Models;

namespace Syncplay.ExampleBot;

public class SyncplayBotService(SyncplayClient client, ILogger<SyncplayBotService> logger)
{
    private readonly HashSet<SyncplayUser> usersToMonitor = [];

    public async Task RunAsync(string host, int port, string? hostPassword, string roomName, string username,
        CancellationToken token)
    {
        client.RequestPassiveStateReport += ReportCurrentPlaybackState;

        client.OnHelloReceived += HelloReceived;
        client.OnChatMessageReceived += ChatMessageReceived;
        client.OnUserJoined += UserJoined;
        client.OnUserLeft += UserLeft;
        client.OnUserReadyStateChanged += UserReady;
        client.OnPlaylistChanged += PlaylistChanged;
        client.OnPlaylistIndexChanged += PlaylistIndexChanged;
        client.OnUserFileChanged += UserFileChanged;
        client.OnForcedPlaybackState += ForcedPlaybackState;

        await client.ConnectAsync(host, port, hostPassword, roomName, username, token);

        if (client.ListenTask != null)
            await client.ListenTask;
    }

    private void ChatMessageReceived(ChatCommand chatMessage)
    {
        const string prefix = "?";

        if (!chatMessage.Message.StartsWith(prefix))
            return;

        var command = chatMessage.Message[prefix.Length..];
        var segments = command.Split(' ');

        if (segments.Length == 0)
            return;

        if (segments[0] == "hello")
        {
            Task.Run(async () => await client.SendChatMessageAsync("hello!"));
        }
        else if (segments[0] == "users")
        {
            Task.Run(async () =>
                await client.SendChatMessageAsync(
                    $"There are {client.Users.Count} users in this room: {client.Users.Select(x => x.Username).Humanize()}"));
        }
        // toggles the bot's ready state
        else if (segments[0] == "ready")
        {
            Task.Run(async () =>
            {
                if (segments.Length == 1)
                {
                    await client.SetReadyAsync(!client.CurrentUser.IsReady);
                }
                else if (segments.Length == 2)
                {
                    var username = segments[1];
                    if (!client.TryGetUser(username, out var user))
                    {
                        await client.SendChatMessageAsync($"User {username} not found.");
                        return;
                    }

                    await client.SetReadyAsync(username, !user.IsReady);
                }
                else
                {
                    await client.SendChatMessageAsync("Usage: ?ready [username]");
                }
            });
        }
        // toggles copying the specified user's reported file state to the bot's reported file state
        else if (segments[0] == "copy-file")
        {
            Task.Run(async () =>
            {
                if (segments.Length != 2)
                {
                    await client.SendChatMessageAsync($"Usage: {prefix}copy-file <username>");
                    return;
                }

                var username = segments[1];

                if (!client.TryGetUser(username, out var desiredUser))
                {
                    await client.SendChatMessageAsync($"User {username} not found!");
                    return;
                }

                if (desiredUser.FileInfo != null)
                    await client.SetFileAsync(desiredUser.FileInfo);

                if (!usersToMonitor.Add(desiredUser))
                    usersToMonitor.Remove(desiredUser);
            });
        }
        else if (segments[0] == "seek")
        {
            Task.Run(async () =>
            {
                if (segments.Length != 2)
                {
                    await client.SendChatMessageAsync($"Usage: {prefix}seek <seconds>");
                    return;
                }

                var seconds = int.Parse(segments[1]);
                await client.ForcePlaybackStateAsync(client.ServerPaused, seconds, true);
                await client.SendChatMessageAsync($"Seeked to {seconds} seconds!");
            });
        }
        // dumb tests
        else if (segments[0] == "playlist-test")
        {
            Task.Run(async () =>
            {
                await client.SetPlaylistAsync([
                    "some-cool-video.mkv", "another-cool-video.mkv", "yet-another-cool-video.mkv",
                    "and-another-cool-video.mkv"
                ]);

                await client.SetPlaylistIndexAsync(0);

                await client.SendChatMessageAsync("Playlist set!");
            });
        }
        else if (segments[0] == "file-test")
        {
            Task.Run(async () => { await client.SetFileAsync(new MediaFile("test.mkv", 10, 1000000000)); });
        }
    }

    private void HelloReceived()
    {
        logger.LogInformation("Message of the day: {MessageOfTheDay}", client.MessageOfTheDay);
    }


    private void UserJoined(SyncplayUser user)
    {
        Task.Run(async () => await client.SendChatMessageAsync($"hello {user.Username}!"));
    }

    private void UserLeft(SyncplayUser user)
    {
        usersToMonitor.Remove(user);

        Task.Run(async () => await client.SendChatMessageAsync($"bye bye {user.Username}!"));
    }

    private void UserReady(UserReadyStateChangedEventArgs args)
    {
        var user = args.User;

        Task.Run(async () =>
            await client.SendChatMessageAsync($"oo {user.Username} is{(user.IsReady ? "" : " NOT")} ready!"));
    }

    private void PlaylistChanged(PlaylistChangedEventArgs args)
    {
        Task.Run(async () => await client.SendChatMessageAsync($"oo new playlist! {args.Playlist.Humanize()}"));
    }

    private void PlaylistIndexChanged(PlaylistIndexChangedEventArgs args)
    {
        Task.Run(async () =>
            await client.SendChatMessageAsync(
                $"changed playlist index! should be playing {client.ServerSelectedPlaylistEntry} now"));
    }

    private void UserFileChanged(UserFileChangedEventArgs args)
    {
        Task.Run(async () =>
        {
            if (usersToMonitor.Contains(args.User))
            {
                if (args.User.FileInfo != null)
                    await client.SetFileAsync(args.User.FileInfo);
            }

            await client.SendChatMessageAsync(
                $"user {args.User.Username} is now using file {args.User.FileInfo?.Name}!");
            await client.SendChatMessageAsync(
                $"was originally {args.PreviousFile?.Name}. new file is {args.User.FileInfo?.Duration}s long, {args.User.FileInfo?.FileSize} big.");
        });
    }

    private void ForcedPlaybackState()
    {
        Task.Run(async () =>
            await client.SendChatMessageAsync(
                $"playback state was forced to {client.ServerPlaybackPosition}s, {(client.ServerPaused ? "paused" : "playing")}, {(client.ServerLastPlaybackWasSeek ? "seeking" : "seekn't")}"));
    }

    private PassiveStateReport ReportCurrentPlaybackState()
    {
        return new PassiveStateReport(client.ServerPlaybackPosition, client.ServerPaused);
    }
}