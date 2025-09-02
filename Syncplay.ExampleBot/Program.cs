using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using SyncPlay.Protocol;

namespace Syncplay.ExampleBot;

public static class Program
{
    public static async Task Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        
        Console.OutputEncoding = Encoding.UTF8;

        var builder = new HostBuilder();

        const LogEventLevel logLevel = LogEventLevel.Verbose;

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen);

        var logger = logConfig.CreateLogger();
        builder.ConfigureLogging(logging => logging.AddSerilog(logger));
        builder.ConfigureServices(x => x.AddBotServices());
        builder.ConfigureHostConfiguration(configBuilder => configBuilder.AddEnvironmentVariables(prefix: "SYNCPLAY_"));

        await builder.RunConsoleAsync();
    }

    private static IServiceCollection AddBotServices(this IServiceCollection serviceCollection)
    {
        // TODO: Due to the relatively short lifespans of rooms, this should ideally be something disposable that gets added and removed by request at runtime. Scoped service?
        serviceCollection.AddSingleton<SyncplayClient>();
        serviceCollection.AddSingleton<SyncplayBotService>();
        serviceCollection.AddHostedService<SyncplayBotManagerService>();

        return serviceCollection;
    }
}