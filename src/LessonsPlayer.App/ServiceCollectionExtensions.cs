using System.Windows.Threading;
using LessonsPlayer.App.Services;
using LessonsPlayer.App.Views;
using LessonsPlayer.Presentation.Services;
using LessonsPlayer.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LessonsPlayer.App;

/// <summary>Argumentos da linha de comando (ex.: "Abrir com" no Explorer).</summary>
public sealed record StartupArguments(IReadOnlyList<string> Paths);

internal static class ServiceCollectionExtensions
{
    /// <summary>Registra os serviços de UI, os ViewModels e as janelas.</summary>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services, Dispatcher dispatcher, StartupArguments startupArguments)
    {
        services.AddSingleton(startupArguments);
        services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(dispatcher));
        services.AddSingleton<IFrameTimer>(new DispatcherFrameTimer(dispatcher));
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<AppSettingsService>();

        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<TranscriptViewModel>();
        services.AddSingleton<TranscriptionViewModel>();
        services.AddSingleton<LessonSearchViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();
        return services;
    }
}
