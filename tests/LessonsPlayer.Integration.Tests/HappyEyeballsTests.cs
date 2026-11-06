using System.Diagnostics;
using System.Net;
using LessonsPlayer.Infrastructure.Http;

namespace LessonsPlayer.Integration.Tests;

/// <summary>Lógica da corrida de conexões, sem rede (rápido; fora da categoria Integration).</summary>
public sealed class HappyEyeballsTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task Address_that_never_answers_does_not_block_the_next_one()
    {
        // Como numa rede sem rota IPv6: o primeiro endereço fica sem resposta até ser cancelado.
        var canceled = false;
        var stopwatch = Stopwatch.StartNew();

        var winner = await HappyEyeballs.RaceAsync(
            ["v6", "v4"],
            async (item, ct) =>
            {
                if (item == "v4")
                {
                    return item;
                }

                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    throw;
                }

                return item;
            },
            _ => { },
            Delay,
            TestContext.Current.CancellationToken);

        winner.ShouldBe("v4");
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        canceled.ShouldBeTrue("a tentativa perdedora é cancelada");
    }

    [Fact]
    public async Task Failure_starts_the_next_attempt_without_waiting_the_delay()
    {
        var stopwatch = Stopwatch.StartNew();

        var winner = await HappyEyeballs.RaceAsync(
            ["a", "b"],
            (item, _) => item == "a" ? Task.FromException<string>(new IOException("recusado")) : Task.FromResult(item),
            _ => { },
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        winner.ShouldBe("b");
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task All_failing_throws()
    {
        var failure = await Should.ThrowAsync<AggregateException>(() => HappyEyeballs.RaceAsync(
            ["a", "b"],
            (item, _) => Task.FromException<string>(new IOException(item)),
            _ => { },
            Delay,
            TestContext.Current.CancellationToken));

        failure.InnerExceptions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Late_success_of_a_loser_is_discarded()
    {
        var slow = new TaskCompletionSource<string>();
        var discarded = new TaskCompletionSource<string>();

        var winner = await HappyEyeballs.RaceAsync(
            ["slow", "fast"],
            (item, _) => item == "slow" ? slow.Task : Task.FromResult(item),
            discarded.SetResult,
            Delay,
            TestContext.Current.CancellationToken);
        slow.SetResult("slow"); // ignorou o cancelamento e conectou mesmo assim

        winner.ShouldBe("fast");
        (await discarded.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)).ShouldBe("slow");
    }

    [Fact]
    public void Families_are_interleaved_keeping_dns_order()
    {
        IPAddress[] addresses =
        [
            IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2"), IPAddress.Parse("2001:db8::3"),
            IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2"),
        ];

        HappyEyeballs.Interleave(addresses).Select(a => a.ToString()).ShouldBe(
            ["2001:db8::1", "192.0.2.1", "2001:db8::2", "192.0.2.2", "2001:db8::3"]);
    }
}
