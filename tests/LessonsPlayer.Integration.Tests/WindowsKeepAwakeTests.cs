using System.Runtime.InteropServices;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Infrastructure.Power;
using Microsoft.Extensions.Logging.Abstractions;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Pedidos de energia de verdade, no Windows. O estado do sistema é lido com <c>CallNtPowerInformation</c>
/// (<c>SystemExecutionState</c>), que soma os pedidos de todos os processos: por isso o teste só confere que os
/// bits pedidos ficam ligados enquanto o pedido existe. Não muda nenhuma configuração de energia.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WindowsKeepAwakeTests
{
    private const uint SystemRequired = 0x1;
    private const uint DisplayRequired = 0x2;

    private readonly WindowsKeepAwake _keepAwake = new(NullLogger<WindowsKeepAwake>.Instance);

    [Fact]
    public void Generating_keeps_the_system_and_the_screen_awake_while_held()
    {
        using (_keepAwake.Acquire(KeepAwakeReason.GeneratingSubtitles))
        {
            var state = SystemExecutionState();
            (state & SystemRequired).ShouldBe(SystemRequired);
            (state & DisplayRequired).ShouldBe(DisplayRequired);
        }
    }

    [Fact]
    public void Playing_keeps_the_screen_on_while_held()
    {
        using (_keepAwake.Acquire(KeepAwakeReason.Playing))
        {
            (SystemExecutionState() & DisplayRequired).ShouldBe(DisplayRequired);
        }
    }

    [Fact]
    public void Releasing_twice_is_harmless()
    {
        var request = _keepAwake.Acquire(KeepAwakeReason.Playing);

        request.Dispose();
        Should.NotThrow(request.Dispose);
    }

    private static uint SystemExecutionState()
    {
        const int SystemExecutionStateLevel = 16;
        var status = CallNtPowerInformation(SystemExecutionStateLevel, IntPtr.Zero, 0, out var state, sizeof(uint));
        status.ShouldBe(0, "CallNtPowerInformation");
        return state;
    }

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CallNtPowerInformation(int level, IntPtr input, uint inputLength, out uint output, uint outputLength);
}
