namespace LessonsPlayer.Core.Transcription;

/// <summary>Uma placa de vídeo para a transcrição: o nome mostrado ao usuário e a posição dela no Whisper.</summary>
/// <param name="Name">Nome dado pelo driver (ex.: "NVIDIA GeForce GTX 1650").</param>
/// <param name="Index">Posição entre as GPUs que o Whisper enxerga (o <c>GpuDevice</c> do Whisper.net).</param>
public sealed record GpuDevice(string Name, int Index);
