# Roadmap

Melhorias pedidas pelo usuário em 2026-11-06, depois de usar o programa, para fazer depois do trabalho em andamento (correções do QA, frases inteiras, alucinação do Whisper). Cada item tem uma nota de como fazer e as dúvidas em aberto.

## Resolvidos

### Áudio e legenda fora de sincronia depois de editar e posicionar
Relatado pelo usuário: ouvindo um áudio de teste (uma pessoa real falando), ele editou uma palavra com duplo clique ("shop." → "#."). Depois disso, tocar desde o início ficava sincronizado, mas tocar a partir de um ponto depois da edição, não.

**Causa (medida):** o seek em MP3 VBR, e não a edição. O arquivo era um MP3 VBR (LAME, 32–224 kbps). O `AudioFileReader` do NAudio 3 decodifica MP3 pelo Media Foundation, e depois de um seek o áudio começava até 3 s longe da posição informada (+3,0 s em 5 s, −2,0 s em 40 s). Qualquer clique numa palavra faz um seek. A edição foi coincidência: o duplo clique começa com um clique, que já posiciona.

**Correção:** o `AudioFileSource` abre MP3 com o `Mp3FileReader`, que indexa os quadros ao abrir. O erro do seek caiu para 0 ms, tanto no arquivo do usuário quanto no teste `SeekAccuracyTests`, que usa um MP3 VBR montado no próprio teste.

**Hipóteses descartadas com teste:**
- a edição deslocaria os tempos ou o índice do karaokê;
- o "#." seria lido como comentário ao salvar e recarregar.

### Palavras a mais no fim da legenda
Relatado pelo usuário: no mesmo áudio de teste, gerado com o small, apareceram palavras que não foram ditas no fim da legenda. O áudio termina no meio de uma frase, em "So simple console application,", e a legenda trazia "I'm going to show you how to do that." no silêncio que vem depois, com as palavras todas em 50,37 s e duração zero.

**Causa (medida):** o VAD marca fala até 0,2 s depois do último trecho do Whisper. Essa sobra virava uma fatia própria, de 1 s, cujo áudio a janela anterior já tinha ouvido, e nela o modelo completava a frase. Sem o VAD, o mesmo áudio sai certo: "application." e depois só `[BLANK_AUDIO]`.

**Correção:** uma fatia sem fala depois do fim da janela anterior não é decodificada. Em 4 áudios reais com 3 modelos houve 8 fatias assim, e nenhuma tinha palavra real (detalhes no README, "Frase completada depois do fim da fala"). O `NonSpeechHallucinationTests` reproduz o caso com TTS: sem a correção, os 4 modelos decodificavam a sobra, e o small inventou "...you can use it to make your own console.".

**Por que não só descartar as palavras com duração zero:** no meio de um trecho elas são quase sempre fala real (Khan 8/8, StoryCorps 13/13); o DTW às vezes junta duas palavras no mesmo instante.

### Palavras sem alinhamento no fim de uma janela cortada no meio da fala
Na NASA (small), em duas janelas, as palavras finais ficaram todas no mesmo instante, com duração zero:
- em 6:25, "United States Senate and the United States Senate have been in the process of making the": texto inventado, por cima da fala real ("from the Massachusetts school issued an apology...");
- em 11:52–11:55, "Look at that. / Thank you guys so, so much. / It was so awesome.": fala real, mas até 3 s antes do tempo certo.

**Causa (medida):** o DTW do whisper.cpp só alinha o áudio até o último timestamp que o decodificador previu (`n_frames = min(seek_delta, …)`), e não até o fim da janela. O texto que passa desse ponto fica empilhado nele. No 11:52 a janela ia até 11:58,1, mas o decodificador, com trechos de exatamente 1 s, previu o último timestamp em 11:55,45; "Thank you guys so, so much." foi dito de 11:55,9 a 11:57,5. Nos 3 vídeos com 3 modelos houve 14 fatias com palavras empilhadas:
- 1 a 3 palavras (10 fatias: "can", "now.", "were", "is it?", "about the mission." e outras): todas eram fala real e estavam a até 0,3 s do tempo da legenda automática;
- 8 a 16 palavras (4 fatias): o texto inventado do 6:25; uma continuação inventada depois do fim da janela (Khan, large, 7:54: "...specific way that it's a little bit more than a plane.", e a fatia seguinte repetiu "That's a specific way"); a frase anterior repetida numa fatia de 1 s no começo da música (StoryCorps, large, 2:02: "I'm going to go back to black and white."); e fala real fora do tempo, junto com "It was so awesome.", uma versão errada de "It was so wonderful", que a fatia seguinte também transcreveu (NASA, small, 11:52).

Nos casos em que as referências discordavam, o próprio áudio decidiu, com o RMS a cada 100 ms e trechos curtos transcritos isolados, sem prompt, com large-v3-turbo, small e medium.

**Correção:** a partir de 4 palavras empilhadas (`UnalignedTail`), a sequência final é descartada, e a fatia seguinte começa no fim da última palavra alinhada, para esse áudio ser decodificado de novo. Se não houver palavra alinhada antes (6:25 e StoryCorps), a fatia é decodificada de novo sem o prompt, como no laço de repetição. A nova versão só fica se o DTW tiver posto alguma palavra no áudio. Sem o prompt, o small transcreveu a fala real do 6:25. No StoryCorps, a nova versão foi só "Music", esticado sobre a fatia inteira; aceita, ela tirava a pontuação das fatias seguintes (o "Music" ia para o prompt), então foi recusada.

**Medido** (antes → depois, mesmas condições; a GPU deu resultados idênticos em duas execuções da `main`):
- nenhum empilhamento de 4 ou mais palavras; os de 1 a 3 continuam iguais;
- 6:25: "...school issued an apology saying they did not need to disrespect those affected by the tragedy. And one of President Trump's favorite phrases" no lugar do texto inventado; 23 das 30 palavras da legenda automática casadas, a 0,09 s dela (mediana);
- 11:52: "Look at that. Thank you guys so so much. It was so wonderful having you on this morning.", sem a duplicata; o erro contra o trecho transcrito isolado caiu de até 3,1 s para até 0,27 s. "It just spins on its own" não foi decodificado (antes saía "It's been on its own.", 3 s adiantado);
- NASA, small: WER 34,0% → 33,2%, palavras casadas com a automática 1.922 → 1.965, erro de tempo igual (mediana 91 ms, p90 210 ms);
- Khan, large: WER 3,2% → 2,4% (saíram as 13 palavras inventadas ou repetidas do 7:54); StoryCorps, large: 4,5% → 4,0% (4 palavras casadas a menos: variação do decodificador nas fatias seguintes, como "both hands" → "potions");
- as outras 5 legendas saíram idênticas.

**Por que não descartar 1 a 3 palavras:** eram fala real no tempo certo. Nos testes TTS, o DTW erra o tempo de uma palavra em 40–80 ms (mediana), e em até ~250 ms (p90). Recomeçar a fatia no fim da palavra anterior pode cair no meio de uma palavra real e perdê-la.

**Teste:** o `UnalignedTailTests` reproduz os padrões de tokens medidos (Khan, 11:52, 6:25, "about the mission."). Com TTS não deu para reproduzir: em 3 aulas de ~55 s com base, small, small.en e large-v3-turbo (13 janelas cortadas no meio da fala), o decodificador sempre fechou o trecho no limite de uma palavra.

## A investigar

### Palavras presas aos limites de trechos de 1 s
Na NASA (small), 11:46–11:52, o decodificador previu trechos de exatamente 1 s ("These are professionals in space." [11:46,45–11:47,45], "That is awesome." [11:47,45–11:48,45]...). O DTW alinhou as palavras no lugar certo ("fidget spinner" em 11:50,37–11:50,87; isolado, o trecho dá 11:50,34–11:50,92), mas o `TokenWordMerger` limita cada palavra ao seu trecho, e elas saíram de 1,5 a 3 s antes, várias com duração zero. Depois da correção das palavras sem alinhamento, o fatiamento mudou e o deslocamento pôs "just awesome. Yeah," (11:34) longe da fala do VAD; o filtro de fala as descartou.
- Frequência (palavras com o fim do DTW mais de 0,5 s depois do fim do trecho): NASA small 36 em 8 trechos, NASA small.en 5, Khan small.en 1, nos outros 6 casos nenhuma.
- Ideia a medir: quando o DTW passa do fim do trecho, confiar no DTW (e mover a palavra para o trecho seguinte), em vez de limitar.

### Queda nativa ao decodificar MP3 (ACM)
Em 2026-11-07, uma execução da suíte de integração caiu nos primeiros segundos com 0xC0000005 (evento 1026 do .NET Runtime). A falha foi em `acmStreamPrepareHeader`, chamado pelo `AcmMp3FrameDecompressor` do `Mp3FileReader`, dentro do `AudioFileDecoder`. Desde a correção do seek em MP3 VBR, o `Mp3FileReader` decodifica MP3 pelo ACM do Windows. A execução seguinte, com o mesmo binário, passou. Uma hipótese: vários testes decodificando MP3 em paralelo. No app, a geração de legenda e o player podem abrir o mesmo tipo de leitor ao mesmo tempo.

### WER do large-v3-turbo maior que na primeira medição com o detector de voz
Na medição feita durante o desenvolvimento do detector de voz (2026-11-06, 9:47), o large-v3-turbo teve WER de 2,2% no Khan e 3,8% no StoryCorps. Com o código da `main` (2026-11-07), deu 3,3% e 4,5%. Com a correção da "frase completada", deu 3,2% e 4,5%. O small ficou estável: 3,1% → 3,2% no Khan e 6,3% → 6,1% no StoryCorps.
- O código do transcritor não mudou de comportamento entre o commit do detector e a `main`. A medição de 9:47 pode ter sido feita antes das últimas regras desse commit (recomeço na última palavra, nova decodificação sem prompt quando há laço de repetição, marcações fora do prompt).
- As diferenças de texto são em palavras curtas e pontuação: "Ah," e o "A" de "A tension" sumiram, e apareceu um "-" no início.
- A investigar: medir com cada uma dessas regras desligada, e verificar se a GPU dá o mesmo resultado em duas execuções seguidas.
- Verificado em 2026-11-07: duas execuções da `main` com 12 h de intervalo deram relatórios idênticos nos 3 vídeos com os 3 modelos. A GPU é determinística aqui, então a diferença não vem dela.
- Com a correção das palavras sem alinhamento, o large deu 2,4% no Khan e 4,0% no StoryCorps. No Khan, 13 dos 14 erros a menos eram uma continuação inventada no fim de uma janela ("...that it's a little bit more than a plane.") e a repetição na fatia seguinte ("That's a specific way that").

### Geração travada esperando a GPU
Em 2026-11-06, numa comparação com os vídeos reais (small, StoryCorps), a geração ficou parada por 10 h até ser encerrada à mão. A thread do Whisper estava no `ggml_vk_wait_for_fence` do backend Vulkan, que fica consultando `vkGetFenceStatus` até a GPU terminar. A GPU nunca terminou: a resposta foi `VK_NOT_READY` o tempo todo, com 100% de um núcleo ocupado. O log do Windows não tem reset do driver de vídeo nesse horário. A máquina tem GTX 1650 (driver 31.0.15.3758, de 2023) e Iris Xe. Foi a primeira vez em dezenas de gerações.
- No programa, o progresso pararia e o Cancelar não adiantaria: esse laço nativo não verifica cancelamento.
- Opções:
  - um vigia por janela: se uma janela de 30 s passar de um limite, ela é abandonada e refeita na CPU. A thread presa continua girando até o programa fechar;
  - o Whisper num processo filho, que pode ser encerrado.
- Antes: ver se um Whisper.net/ggml-vulkan mais novo já corrige isso, e testar com o driver atualizado.
- Em 2026-11-07, mais duas travas em ~14 rodadas de testes com áudio real. Pelo log nativo, o Whisper.net usa o dispositivo Vulkan 0, que nesta máquina é a Intel Iris Xe, e não a GTX 1650. A pilha .NET mostrava a thread presa no código nativo do `WhisperProcessor`. As duas começaram com o Windows em espera moderna (Kernel-Power 506/507), às 6:47 e às 7:32. Mas várias outras rodadas na espera terminaram normalmente, então não dá para dizer que a espera é a causa.

## Próximas

### Excluir a legenda de uma aula
Clique com o botão direito no item da playlist ou no ícone CC ao lado do nome → "Excluir legenda". O app pede confirmação, apaga o `.legenda.txt` e atualiza o ícone e o painel. Se a aula estiver aberta com alterações não salvas, elas são descartadas junto (avisar isso na confirmação).

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
