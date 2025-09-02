using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Syncplay.ExampleBot;

public class SyncplayBotManagerService(IServiceProvider serviceProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botService = serviceProvider.GetRequiredService<SyncplayBotService>();

        await botService.RunAsync("localhost", 8999, null, "kievtesting", "bot", stoppingToken);
    }
}