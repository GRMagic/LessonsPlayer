namespace LessonsPlayer.Core.Subtitles;

/// <summary>Endereço de uma palavra dentro de um <see cref="Transcript"/>.</summary>
public readonly record struct WordLocation(int SegmentIndex, int WordIndex);
