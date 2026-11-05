using System.Windows;
using System.Windows.Threading;
using LessonsPlayer.App.Views;
using LessonsPlayer.Infrastructure;
using LessonsPlayer.Presentation.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.App;

/// <summary>
/// Composition root: monta o container de DI (Generic Host), configura o log em arquivo,
/// instala os tratadores globais de exceção e abre a janela principal.
/// </summary>
public partial class App : Application
{
    private static readonly Version? AppVersion = typeof(App).Assembly.GetName().Version;

    private IHost? _host;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = InfrastructureOptions.CreateDefault();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddFileLogging(options.LogsDirectory);
        builder.Services.AddInfrastructure(options);
        builder.Services.AddApplicationServices(Dispatcher, new StartupArguments(e.Args));

        _host = builder.Build();
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        RegisterGlobalExceptionHandlers();
        LogStarted(_logger, AppVersion);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Descarta os singletons (player, Whisper) e grava o restante do log.
        _host?.Dispose();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (_logger is not null && args.ExceptionObject is Exception ex)
            {
                LogFatal(_logger, ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            if (_logger is not null)
            {
                LogUnobserved(_logger, args.Exception);
            }

            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            LogUnhandled(_logger, e.Exception);
        }

        MessageBox.Show(e.Exception.Message, UiText.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // mantém o app aberto: erros inesperados de UI não devem derrubar o player
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "LessonsPlayer {Version} iniciado")]
    private static partial void LogStarted(ILogger logger, Version? version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada na UI")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Exceção fatal")]
    private static partial void LogFatal(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não observada em tarefa")]
    private static partial void LogUnobserved(ILogger logger, Exception exception);
}
