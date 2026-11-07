using System.ComponentModel;
using System.Runtime.InteropServices;
using LessonsPlayer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Infrastructure.Power;

/// <summary>
/// Pedidos de energia do Windows (<c>PowerCreateRequest</c>/<c>PowerSetRequest</c>). Cada pedido é um handle do
/// processo: some quando é fechado e também quando o processo termina, cai ou é encerrado. Por isso nada fica
/// preso, e nenhuma configuração de energia é alterada. O motivo aparece em <c>powercfg /requests</c>.
/// <list type="bullet">
/// <item>Gerando legenda: sistema e tela acordados. Em espera moderna (tela apagada), a GPU dedicada pode ser
/// desligada no meio da geração.</item>
/// <item>Tocando: só a tela, porque a turma acompanha a legenda. O próprio Windows já mantém o sistema acordado
/// enquanto há áudio tocando.</item>
/// </list>
/// </summary>
public sealed partial class WindowsKeepAwake(ILogger<WindowsKeepAwake> logger) : IKeepAwake
{
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 0x1;

    private enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
    }

    public IDisposable Acquire(KeepAwakeReason reason)
    {
        var (text, types) = reason switch
        {
            KeepAwakeReason.GeneratingSubtitles =>
                ("LessonsPlayer: generating subtitles", new[] { PowerRequestType.SystemRequired, PowerRequestType.DisplayRequired }),
            _ => ("LessonsPlayer: playing a lesson", new[] { PowerRequestType.DisplayRequired }),
        };

        var reasonText = Marshal.StringToHGlobalUni(text);
        var context = new ReasonContext
        {
            Version = PowerRequestContextVersion,
            Flags = PowerRequestContextSimpleString,
            SimpleReasonString = reasonText,
        };
        var handle = Native.PowerCreateRequest(ref context);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            Marshal.FreeHGlobal(reasonText);
            LogFailed(logger, reason, new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            return Released.Instance;
        }

        var request = new Request(handle, reasonText, types);
        foreach (var type in types)
        {
            if (!Native.PowerSetRequest(handle, type))
            {
                LogFailed(logger, reason, new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            }
        }

        LogAcquired(logger, reason);
        return request;
    }

    /// <summary>Um pedido ativo. Descartar mais de uma vez é inofensivo.</summary>
    private sealed class Request(IntPtr handle, IntPtr reasonText, PowerRequestType[] types) : IDisposable
    {
        private IntPtr _handle = handle;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (current == IntPtr.Zero)
            {
                return;
            }

            foreach (var type in types)
            {
                Native.PowerClearRequest(current, type);
            }

            Native.CloseHandle(current);
            Marshal.FreeHGlobal(reasonText);
        }
    }

    private sealed class Released : IDisposable
    {
        public static readonly Released Instance = new();

        public void Dispose()
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr PowerCreateRequest(ref ReasonContext context);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PowerSetRequest(IntPtr request, PowerRequestType type);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PowerClearRequest(IntPtr request, PowerRequestType type);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pedido de energia ativo: {Reason}")]
    private static partial void LogAcquired(ILogger logger, KeepAwakeReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pedido de energia falhou ({Reason}): {Error}")]
    private static partial void LogFailed(ILogger logger, KeepAwakeReason reason, string error);
}
