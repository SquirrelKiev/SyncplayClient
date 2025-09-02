using Humanizer;
using Microsoft.Extensions.Logging;
using SyncPlay.Protocol;
using SyncPlay.Protocol.Models;

namespace Syncplay.ExampleBot;

public class SyncplayBotService(SyncplayClient client, ILogger<SyncplayBotService> logger)
{
    public async Task RunAsync(string host, int port, string? hostPassword, string roomName, string username, CancellationToken token)
    {
        client.OnHelloReceived += HelloReceived;
        client.OnChatMessageReceived += ChatMessageReceived;
        
        await client.ConnectAsync(host, port, hostPassword, roomName, username, token);

        if(client.ListenTask != null)
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
            Task.Run(async () => await client.SendChatMessageAsync($"There are {client.Users.Count} users in this room: {client.Users.Select(x => x.Username).Humanize()}"));
        }
    }

    private void HelloReceived()
    {
        logger.LogInformation("Message of the day: {MessageOfTheDay}", client.MessageOfTheDay);
    }
}