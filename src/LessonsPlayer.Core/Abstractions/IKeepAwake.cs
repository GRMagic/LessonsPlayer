namespace LessonsPlayer.Core.Abstractions;

/// <summary>Por que o programa pede ao sistema para ficar acordado.</summary>
public enum KeepAwakeReason
{
    /// <summary>
    /// Gerando uma legenda: o sistema não pode entrar em espera. Na espera, a GPU pode sumir no meio da geração
    /// (visto na bateria, com a GTX 1650), e o processo cai ou trava.
    /// </summary>
    GeneratingSubtitles,

    /// <summary>Tocando uma aula: a tela não apaga, porque a turma acompanha a legenda nela.</summary>
    Playing,
}

/// <summary>Pedidos ao sistema para não dormir nem apagar a tela enquanto algo importante acontece.</summary>
public interface IKeepAwake
{
    /// <summary>
    /// Mantém o pedido até o retorno ser descartado. O sistema também o descarta sozinho se o programa terminar,
    /// cair ou for encerrado: nenhuma configuração de energia é alterada. Não lança: sem o recurso, não faz nada.
    /// </summary>
    IDisposable Acquire(KeepAwakeReason reason);
}
