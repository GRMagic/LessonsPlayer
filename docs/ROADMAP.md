# Roadmap

Melhorias pedidas pelo usuário em 2026-11-06, depois de usar o programa, para fazer depois do trabalho em andamento (correções do QA, frases inteiras, alucinação do Whisper). Cada item tem uma nota de como fazer e as dúvidas em aberto.

## Próximas

### Interface em inglês
Quem usa o programa é o professor de inglês, ao lado dos alunos. Os textos ficam todos em `UiText` (Presentation), então dá para trocar num lugar só. Vale aproveitar e passar para `.resx`, o que deixa outros idiomas possíveis depois.
- Mensagens do sistema traduzidas por `UiText.ErrorReason` também passam para inglês.
- **Em aberto:** o formato do arquivo de legenda. O cabeçalho (`# LessonsPlayer legenda v1`, `modelo`, `idioma`, `gerado`) e o sufixo `.legenda.txt` também são vistos pelo usuário no Bloco de Notas. Sugestão: gravar em inglês (`# LessonsPlayer subtitle v2`, `.subtitle.txt`) e continuar lendo o formato v1.

### Clicar no horário da frase vai para o início dela
Hoje o horário à esquerda de cada trecho é só texto. Ele vira um botão discreto, com o comando `SeekToSegment`, e o clique leva ao início do trecho (a primeira palavra). Também confirma uma edição em andamento, como o clique em palavra.

### "Usar GPU" só quando houver GPU
Desabilitar a opção, e tratá-la como desmarcada, quando não houver GPU utilizável.
- A detecção precisa refletir o que o Whisper.net consegue usar: um dispositivo Vulkan (inclui GPU integrada Intel/AMD). Opções:
  - enumerar dispositivos com o `vulkan-1.dll` (`vkEnumeratePhysicalDevices`);
  - DXGI, excluindo o "Microsoft Basic Render Driver";
  - carregar o runtime Vulkan e ver se o Whisper.net caiu para CPU (`RuntimeOptions.LoadedLibrary`).
- Mostrar o nome da GPU na dica ("Usa a GPU Intel Iris Xe…").

### Avançar para a próxima aula automaticamente (desligado por padrão)
Uma opção, por exemplo uma caixa "Tocar a próxima ao terminar" perto do player, persistida em `AppSettings`. Desligada, o player só para no fim. Hoje o avanço é sempre automático (`MainViewModel` → `PlaybackCompleted`).

### Busca de aulas
Uma caixa de busca acima da playlist que filtra pelo nome da aula (sem acento e sem maiúsculas: "licao" acha "Lição").
- Opcional: "buscar também no conteúdo" das aulas que têm legenda, mostrando o trecho encontrado. O clique abre a aula e vai até a frase.
- Para a busca no conteúdo, um índice simples em memória (texto das `.legenda.txt` da pasta, recarregado quando uma legenda muda) basta para centenas de aulas.

### Salvar habilitado só quando há o que salvar
O `SaveCommand` passa a depender de `IsDirty` (com `NotifyCanExecuteChanged` no evento `Changed` do editor).

### Ícone do programa
Um ícone simples (por exemplo, um balão de fala com uma onda sonora ou com "CC"), em `.ico` com 16, 32, 48 e 256 px. Ele vai no `ApplicationIcon` do csproj e no `Icon` da janela.

### GitHub Actions: testes e release a cada push na master
O projeto ainda não está no GitHub. O workflow (`.github/workflows/release.yml`) roda em `windows-latest`:
1. `dotnet build` e os testes rápidos (`--filter-not-trait "Category=Integration"`).
2. Os testes de integração que não dependem de dispositivo de áudio, GPU ou download grande. O runner não tem placa de som e talvez não tenha as vozes pt-BR; esses testes precisam se marcar como "skip" quando faltar o recurso, e alguns já fazem isso.
3. Se tudo passar: `dotnet publish` win-x64 e um GitHub Release com tag de versão (ex.: `v0.1.<run_number>`).
- **Em aberto:** publicar em arquivo único. O Whisper.net carrega as bibliotecas nativas da pasta `runtimes/` (CPU e Vulkan). Com `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` elas são extraídas para uma pasta temporária, e é preciso verificar se o carregador do Whisper.net as encontra (`RuntimeOptions.LibraryPath`). Se não der, o release continua sendo o zip da pasta.

### README com diagramas Mermaid
Arquitetura em camadas, fluxo da geração de legenda (download do modelo → decodificação → janelas do Whisper → palavras → frases → `.txt`) e fluxo do clique na palavra (palavra → seek → relógio do WASAPI → destaque).

## Futuro

### Busca inteligente (semântica)
Achar aulas pelo assunto e não pelas palavras exatas ("aula sobre verbos irregulares" acha "Lesson 01 – Irregular verbs"). Seria preciso:
- um modelo de embeddings pequeno, local, via ONNX Runtime (por exemplo, `all-MiniLM-L6-v2`, ~23 MB, ou `bge-small`), baixado na primeira vez como os modelos do Whisper;
- vetores por frase ou por trecho de cada legenda, guardados ao lado do `.txt` ou num cache em `%LOCALAPPDATA%`;
- busca por similaridade de cosseno, que em memória basta para milhares de trechos.
Interessante, mas não é prioridade.

## Já verificado

- **Os testes não gravam em `C:\aulas`.** Esses caminhos são fictícios nos testes de ViewModel e de Core: os stores são em memória (`InMemorySubtitleStore`), e o player é falso. Os testes de integração gravam só em pastas temporárias (`Directory.CreateTempSubdirectory`).
