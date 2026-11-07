using System.Buffers;
using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Transcription;
using Microsoft.Extensions.Logging;

namespace LessonsPlayer.Infrastructure.Whisper;

/// <summary>
/// Modelos ggml do repositório oficial do whisper.cpp no Hugging Face, guardados em
/// <see cref="InfrastructureOptions.ModelsDirectory"/>. O download vai para um arquivo <c>.part</c>
/// e só é renomeado quando termina completo, então um download interrompido nunca é usado.
/// </summary>
public sealed partial class HuggingFaceModelStore(
    HttpClient httpClient,
    InfrastructureOptions options,
    ILogger<HuggingFaceModelStore> logger) : IModelStore
{
    public static Uri BaseUri { get; } = new("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/");

    private const int BufferSize = 1 << 16;

    public string GetModelPath(WhisperModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Path.Combine(options.ModelsDirectory, model.FileName);
    }

    public bool IsDownloaded(WhisperModel model) => File.Exists(GetModelPath(model));

    public async Task<string> EnsureDownloadedAsync(
        WhisperModel model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var path = GetModelPath(model);
        if (File.Exists(path))
        {
            progress?.Report(1);
            return path;
        }

        Directory.CreateDirectory(options.ModelsDirectory);
        var partialPath = path + ".part";
        LogDownloadStarted(logger, model.Id);

        try
        {
            await DownloadAsync(new Uri(BaseUri, model.FileName), partialPath, model.SizeBytes, progress, cancellationToken);
            File.Move(partialPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }

        LogDownloadFinished(logger, model.Id, path);
        return path;
    }

    private async Task DownloadAsync(
        Uri uri, string targetPath, long expectedSize, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? expectedSize;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0)
                {
                    progress?.Report(Math.Min(1, (double)received / total));
                }
            }

            if (total > 0 && received != total)
            {
                throw new IOException($"Incomplete download: {received} of {total} bytes.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            LogCleanupFailed(logger, ex, path);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogCleanupFailed(logger, ex, path);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Baixando modelo {ModelId}")]
    private static partial void LogDownloadStarted(ILogger logger, string modelId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Modelo {ModelId} salvo em {Path}")]
    private static partial void LogDownloadFinished(ILogger logger, string modelId, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível remover o download parcial {Path}")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception, string path);
}
