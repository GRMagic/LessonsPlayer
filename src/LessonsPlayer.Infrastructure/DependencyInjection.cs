using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure.Audio;
using LessonsPlayer.Infrastructure.Http;
using LessonsPlayer.Infrastructure.Playlist;
using LessonsPlayer.Infrastructure.Storage;
using LessonsPlayer.Infrastructure.Whisper;
using Microsoft.Extensions.DependencyInjection;

namespace LessonsPlayer.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra as implementações concretas das abstrações do Core.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, InfrastructureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(options ?? InfrastructureOptions.CreateDefault());
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IAudioPlayer, NAudioPlayer>();
        services.AddSingleton<IAudioDecoder, AudioFileDecoder>();
        services.AddSingleton<ISpeechDetector, SileroSpeechDetector>();
        services.AddSingleton<ITranscriber, WhisperNetTranscriber>();
        services.AddSingleton<IGpuDetector, VulkanGpuDetector>();
        services.AddSingleton<ISubtitleStore, TxtSubtitleStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IPlaylistSource, FileSystemPlaylistSource>();

        services.AddHttpClient<IModelStore, HuggingFaceModelStore>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan; // modelos têm centenas de MB; o cancelamento é pelo token
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LessonsPlayer/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(HappyEyeballs.CreateHandler); // redes sem rota IPv6

        services.AddTransient<SubtitleGenerator>();
        return services;
    }
}
