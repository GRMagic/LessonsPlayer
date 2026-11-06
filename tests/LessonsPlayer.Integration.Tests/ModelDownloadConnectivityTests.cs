using System.Diagnostics;
using LessonsPlayer.Core.Transcription;
using LessonsPlayer.Infrastructure.Http;

namespace LessonsPlayer.Integration.Tests;

/// <summary>
/// Regressão do QA: numa rede sem rota IPv6, o download do modelo esperava ~170 s antes do primeiro byte
/// (o .NET tentava os 8 endereços IPv6 do huggingface.co em sequência).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ModelDownloadConnectivityTests
{
    [Fact]
    public async Task Model_server_answers_quickly()
    {
        using var http = new HttpClient(HappyEyeballs.CreateHandler());
        var url = new Uri($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{WhisperModels.Tiny.FileName}");
        var stopwatch = Stopwatch.StartNew();

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
        TestContext.Current.TestOutputHelper!.WriteLine($"Cabeçalhos em {stopwatch.ElapsedMilliseconds} ms");
    }
}
