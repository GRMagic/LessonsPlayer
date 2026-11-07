using System.Net;
using System.Net.Sockets;

namespace LessonsPlayer.Infrastructure.Http;

/// <summary>
/// Conexão TCP no estilo "Happy Eyeballs" (RFC 8305): tenta os endereços IPv6 e IPv4 intercalados, cada um
/// começando <see cref="FallbackDelay"/> depois do anterior (ou logo após uma falha), e usa o primeiro que conectar.
/// <para>
/// O padrão do .NET tenta os endereços em sequência. Numa rede sem rota IPv6 (comum em redes domésticas), cada
/// endereço IPv6 só desiste depois de ~21 s. O huggingface.co tem 8 deles, e o download do modelo ficava ~170 s em
/// "0%" antes do primeiro byte (medido no QA; o curl com <c>-4</c> recebia em 0,9 s).
/// </para>
/// </summary>
internal static class HappyEyeballs
{
    /// <summary>Atraso recomendado pela RFC 8305 antes de tentar o próximo endereço.</summary>
    public static readonly TimeSpan FallbackDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Handler HTTP que conecta com <see cref="ConnectAsync"/>.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5), // reavalia o DNS de tempos em tempos
    };

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endPoint = context.DnsEndPoint;
        var addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false);

        var socket = await RaceAsync(
            Interleave(addresses),
            (address, ct) => ConnectSocketAsync(address, endPoint.Port, ct),
            static s => s.Dispose(),
            FallbackDelay,
            cancellationToken).ConfigureAwait(false);
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// Inicia uma tentativa por item, em ordem, escalonadas por <paramref name="fallbackDelay"/> (uma falha
    /// antecipa a próxima), e retorna a primeira que der certo. As outras são canceladas, e as que ainda
    /// assim derem certo são descartadas com <paramref name="discard"/>.
    /// </summary>
    internal static async Task<T> RaceAsync<TItem, T>(
        IReadOnlyList<TItem> items,
        Func<TItem, CancellationToken, Task<T>> attempt,
        Action<T> discard,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        using var losers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<T>>();
        var errors = new List<Exception>();
        var next = 0;

        while (next < items.Count || pending.Count > 0)
        {
            if (next < items.Count)
            {
                pending.Add(attempt(items[next++], losers.Token));
            }

            using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(losers.Token);
            var delay = next < items.Count ? Task.Delay(fallbackDelay, delayCancellation.Token) : null;

            while (pending.Count > 0)
            {
                Task finished = delay is null
                    ? await Task.WhenAny(pending).ConfigureAwait(false)
                    : await Task.WhenAny([.. pending, delay]).ConfigureAwait(false);
                if (finished == delay)
                {
                    break; // demorou: começa a próxima tentativa sem abandonar esta
                }

                var task = (Task<T>)finished;
                pending.Remove(task);
                if (task.IsCompletedSuccessfully)
                {
                    await losers.CancelAsync().ConfigureAwait(false);
                    foreach (var loser in pending)
                    {
                        _ = loser.ContinueWith(
                            t => { if (t.IsCompletedSuccessfully) { discard(t.Result); } else { _ = t.Exception; } },
                            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }

                    return task.Result;
                }

                cancellationToken.ThrowIfCancellationRequested();
                errors.Add(task.Exception?.InnerException ?? new OperationCanceledException());
                if (delay is not null)
                {
                    await delayCancellation.CancelAsync().ConfigureAwait(false);
                    break; // falhou: não espera o atraso para tentar o próximo
                }
            }
        }

        throw errors.Count == 1 ? errors[0] : new AggregateException("No address accepted the connection.", errors);
    }

    /// <summary>Intercala as famílias (IPv6, IPv4, IPv6, …) mantendo a ordem do DNS dentro de cada uma.</summary>
    internal static IReadOnlyList<IPAddress> Interleave(IReadOnlyList<IPAddress> addresses)
    {
        var ipv6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
        var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var result = new List<IPAddress>(addresses.Count);
        for (var i = 0; i < Math.Max(ipv6.Length, ipv4.Length); i++)
        {
            if (i < ipv6.Length)
            {
                result.Add(ipv6[i]);
            }

            if (i < ipv4.Length)
            {
                result.Add(ipv4[i]);
            }
        }

        return result;
    }

    private static async Task<Socket> ConnectSocketAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
