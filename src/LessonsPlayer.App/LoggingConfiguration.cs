using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LessonsPlayer.App;

internal static class LoggingConfiguration
{
    /// <summary>Log em arquivo diário (7 dias), exposto ao app como <c>ILogger&lt;T&gt;</c>.</summary>
    public static IServiceCollection AddFileLogging(this IServiceCollection services, string logsDirectory) =>
        services.AddSerilog(log => log
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logsDirectory, "lessonsplayer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                formatProvider: CultureInfo.InvariantCulture));
}
