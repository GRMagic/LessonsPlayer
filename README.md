# LessonsPlayer

Player de áudio para aulas com **legenda estilo karaokê**, gerada localmente com Whisper. A palavra falada fica destacada enquanto o áudio toca, e um **clique numa palavra leva o áudio ao instante em que ela é dita**. A legenda é salva num `.txt` ao lado do áudio, pode ser editada e é reaberta automaticamente.

- Playlist a partir de uma pasta (ordem natural: `aula2` antes de `aula10`), de um arquivo avulso ou por arrastar e soltar.
- Formatos: MP3, WAV, M4A, AAC, WMA e FLAC (Media Foundation do Windows).
- Transcrição 100% local: nada é enviado para a internet. A única conexão é o download do modelo, feito uma vez.
- Usa a GPU quando há (qualquer placa com driver Vulkan: NVIDIA, AMD ou Intel) e cai para a CPU quando não há.

## Usando

1. **Abrir pasta** (ou arquivo) e dar duplo clique na faixa.
2. **Gerar legenda**: escolha o modelo e o idioma ("Detectar automaticamente" funciona bem). Na primeira vez o modelo é baixado.
3. Dê play. Clique em qualquer palavra para ir até ela.

| Ação | Atalho |
|---|---|
| Tocar / pausar | `Espaço` |
| Voltar / avançar 5 s | `←` / `→` |
| Faixa anterior / próxima | `Ctrl+←` / `Ctrl+→` |
| Corrigir a palavra selecionada (ou duplo clique) | `F2` |
| Editar a frase inteira | `Ctrl+E` |
| Início / fim da palavra = posição atual | `[` / `]` |
| Início −/+ 50 ms | `Alt+←` / `Alt+→` |
| Fim −/+ 50 ms | `Alt+Shift+←` / `Alt+Shift+→` |
| Dividir a frase antes da palavra / juntar com a próxima | `Ctrl+D` / `Ctrl+J` |
| Excluir palavra | `Del` |
| Desfazer / refazer / salvar | `Ctrl+Z` / `Ctrl+Y` / `Ctrl+S` |

O botão direito numa palavra abre todas as opções de edição. Ao editar uma frase inteira, as palavras que não mudaram mantêm o tempo original; só as novas ou alteradas recebem tempo redistribuído.

### Modelos

| Modelo | Tamanho | Quando usar |
|---|---|---|
| Tiny / Base | 32 / 60 MB | Máquinas lentas, áudio limpo |
| **Small** (padrão) | 190 MB | Bom equilíbrio |
| Medium / Large v3 Turbo | 540 / 575 MB | Áudio difícil (ruído, sotaque); mais lentos sem GPU |
| Base.en / Small.en / Medium.en | 60 / 190 / 540 MB | Só inglês (o idioma fica fixo em inglês) |

A precisão do **tempo** das palavras é parecida em todos (veja [Precisão](#precisão)); o base.en é o único que fica um pouco atrás. Modelos maiores ajudam no **texto** em áudio difícil.

## Formato da legenda

`aula01.mp3` → `aula01.legenda.txt`, na mesma pasta, em UTF-8. Dá para editar no Bloco de Notas:

```
# LessonsPlayer legenda v1
# audio: aula01.mp3
# modelo: small-q5_1
# idioma: pt
# gerado: 2026-11-05T14:00:04-03:00

[00:00:00.180 --> 00:00:07.320] Olá, pessoal! Hoje vamos estudar a diferença entre os tempos verbais do inglês.
00:00:00.180 00:00:00.420 Olá,
00:00:00.840 00:00:01.140 pessoal!
...
```

- As linhas `início fim palavra` são a fonte da verdade. O texto do cabeçalho `[… --> …]` é regenerado ao salvar.
- Um trecho escrito à mão **sem** linhas de palavra é aceito: o tempo é distribuído entre as palavras proporcionalmente ao tamanho delas.
- Aceita `hh:mm:ss.fff`, `mm:ss.fff` ou `mm:ss`, com ponto ou vírgula. Linhas não reconhecidas são ignoradas, e o app avisa quantas foram.

## Distribuição

```powershell
./scripts/publish.ps1          # gera artifacts/publish/LessonsPlayer-win-x64 (+ .zip)
```

A pasta é **self-contained**: roda em Windows 10/11 x64 sem .NET, Python ou CUDA instalados. Basta descompactar e abrir `LessonsPlayer.exe`. Dos ~100 MB do zip, cerca de metade é o runtime Vulkan, que habilita a GPU. Nas edições "N" do Windows é preciso o *Media Feature Pack*, por causa da decodificação de áudio.

Dados do usuário:

| O quê | Onde |
|---|---|
| Modelos Whisper | `%LOCALAPPDATA%\LessonsPlayer\models` |
| Logs (7 dias) | `%LOCALAPPDATA%\LessonsPlayer\logs` |
| Preferências | `%APPDATA%\LessonsPlayer\settings.json` |

## Desenvolvimento

Requisitos: .NET SDK 10.

```powershell
dotnet build
dotnet test -- --filter-not-trait "Category=Integration"   # testes rápidos (unidade)
dotnet test --project tests/LessonsPlayer.Integration.Tests # Whisper real, TTS e dispositivo de áudio
dotnet run --project src/LessonsPlayer.App
```

- Os testes de integração baixam modelos e usam as vozes do Windows (pt-BR e en-US). Para testar outros modelos: `$env:LESSONSPLAYER_TEST_MODELS = "small-q5_1,large-v3-turbo-q5_0"`.
- Áudio real (opcional): `$env:LESSONSPLAYER_REAL_AUDIO_DIR = "<pasta>"`, com uma subpasta por áudio contendo `audio.mp3`, `reference-segments.tsv` (legenda humana: `start_ms, end_ms, text`) e `reference-words.tsv` (legenda automática do YouTube: `start_ms, word`). O `RealAudioComparisonTests` grava `comparison-<modelo>.txt` com WER, erro por palavra e erro no início das frases.
- No build Debug, `$env:LESSONSPLAYER_DEBUG_SNAPSHOTS = "<pasta>"` faz a janela se salvar em `latest.png` a cada segundo. Serve para testes de UI automatizados, mesmo com a tela bloqueada.

### Arquitetura

```
App (WPF: views, serviços de UI, composition root com Generic Host + Serilog)
 ├─► Presentation (ViewModels MVVM, net10.0 sem WPF: o compilador impede dependência de UI)
 └─► Infrastructure (NAudio, Whisper.net, HTTP, arquivos)
       └─► Core (domínio imutável, formato .txt, editor, regras puras; sem dependências externas)
```

- **Core**: `Transcript`/`Segment`/`Word` imutáveis, `SubtitleTxtFormat`, `TranscriptEditor` (desfazer/refazer), `TextRealigner` (LCS), `WordIndex` (busca binária), `TokenWordMerger`, `SentenceRegrouper`, `SubtitleGenerator` (caso de uso) e as interfaces implementadas pela Infrastructure.
- Qualidade: `Nullable`, analisadores `latest-recommended`, *warnings as errors*, `.editorconfig` e versões centralizadas em `Directory.Packages.props`.

### Precisão

Medida pelo teste `TimestampPrecisionTests`, que compara com o instante real de cada palavra em áudio sintetizado (WAV e MP3). Erro no **início** da palavra, mediana / p90:

| Modelo | pt-BR WAV | pt-BR MP3 | en-US WAV | en-US MP3 | Aula longa en-US MP3 (53 s) |
|---|---|---|---|---|---|
| base | 41 / 130 ms | 32 / 123 ms | 50 / 220 ms | 35 / 115 ms | 45 / 125 ms |
| base.en | — | — | 95 / 245 ms | 75 / 240 ms | 80 / 210 ms |
| small | 38 / 149 ms | 41 / 129 ms | 50 / 130 ms | 35 / 130 ms | 30 / 140 ms |
| small.en | — | — | 45 / 145 ms | 40 / 125 ms | 45 / 125 ms |
| medium.en | — | — | 35 / 110 ms | 40 / 115 ms | 45 / 130 ms |
| large-v3-turbo | 52 / 136 ms | 43 / 136 ms | 60 / 160 ms | 40 / 145 ms | 40 / 165 ms |

Em todas as aulas longas a transcrição foi até a última palavra, com 99–100% das palavras corretas em inglês.

**Áudio real** (`RealAudioComparisonTests`, MP3 do YouTube): uma aula da Khan Academy (10 min, 1 falante), uma conversa do StoryCorps com música de fundo (6 min, 2 falantes) e uma entrevista da NASA via satélite (13 min, 10+ falantes). O texto é comparado com a legenda humana. O tempo é comparado com a legenda automática do YouTube, que é de outro reconhecedor e também tem erro.

| Modelo | WER Khan | WER StoryCorps | Palavra vs. YouTube (mediana): Khan / StoryCorps / NASA | Início de frase vs. legenda humana, Khan (mediana / p90) |
|---|---|---|---|---|
| small | 3,1% | 5,1% | 180 / 59 / 81 ms | 80 / 340 ms |
| small.en | 2,7% | 5,9% | 180 / 41 / 100 ms | 80 / 310 ms |
| large-v3-turbo | 2,3% | 4,6% | 180 / 41 / 80 ms | 160 / 390 ms |

- No Khan, todas as palavras ficam ~180 ms **antes** da legenda automática. É um viés constante entre os dois reconhecedores. A legenda humana, que é bem sincronizada nesse vídeo, fica a 80 ms.
- A legenda humana da NASA é estenotipia ao vivo, ~3,5 s atrasada e com omissões (WER de 34–45% contra ela). Lá vale só a comparação de tempo com a automática.
- Nenhuma frase ficou cortada (sem o `SentenceRegrouper`, eram 67 de 125 trechos no Khan com o small).
- Em trechos longos sem fala (créditos com música, fim da transmissão), o Whisper às vezes **inventa texto** ("Thank you.", frases repetidas). Ainda não é filtrado.

### Notas técnicas (descobertas com evidência)

- **`t_dtw` do whisper.cpp marca o *fim* do token**, não o início. A atribuição é deslocada em um índice em relação à implementação da OpenAI. Por isso o início de uma palavra é o `t_dtw` do token anterior (`TokenWordMerger`). Interpretado do jeito ingênuo, o erro mediano era ~250 ms.
- **Com DTW, o whisper.cpp só entrega a primeira janela de 30 s** de cada chamada. O laço do `new_segment_callback` usa `n_segments` em vez de `result_all.size()` ([código](https://github.com/ggml-org/whisper.cpp/blob/f24588a272ae8e23280d9c220536437164e6ed28/src/whisper.cpp#L7727); issues [#2669](https://github.com/ggml-org/whisper.cpp/issues/2669) e [#3111](https://github.com/ggml-org/whisper.cpp/issues/3111)). O `WhisperNetTranscriber` processa janela a janela, retomando onde o último trecho terminou.
- **Cada fatia processa só a primeira janela.** Depois dela, o whisper.cpp seguiria com a sobra da fatia: trabalho perdido (os trechos não são entregues) e, numa sobra de até ~150 ms, um abort nativo no filtro mediano do DTW (`WHISPER_ASSERT(filter_width < a->ne[2])`, corrigido no whisper.cpp depois da versão do Whisper.net 1.9.1). O handler de início do encoder interrompe antes da segunda janela: mesmos trechos, transcrição mais rápida (medium: 45 s → 22 s numa aula de 2,5 min).
- **O Whisper corta frases longas em mais de um trecho**, pelos timestamps que o próprio decodificador prevê. Acontece com e sem DTW e com e sem prompt, quase sempre no meio da janela e não na fronteira das fatias. Numa aula TTS em inglês de 2,5 min, o small cortou as 15 frases. O `SentenceRegrouper` completa cada frase inacabada com as palavras seguintes até o ponto final (até 30 s, sem pausa maior que 2 s), sem mudar o tempo das palavras.
- **Reprodução e transcrição usam o mesmo leitor (`AudioFileReader`)**. Assim o atraso inicial do MP3 e o seek em VBR não desalinham legenda e áudio. A posição exibida vem do relógio do WASAPI, ou seja, do que já saiu no alto-falante, e não do buffer.
- **Fim de faixa = fonte esgotada.** Em MP3 o tamanho total (`Length`) é estimado e pode passar do que é decodificado; comparar posição com tamanho nunca chegava ao fim, e o player ficava "tocando" em 0:00. Além disso, o `PlaybackStopped` do NAudio é postado na thread da UI, depois que o `Stop()` de um seek já voltou. O player marca o fim quando o leitor devolve 0 bytes (`EndTrackingWaveProvider`).
- **Liberar o modelo com o whisper.cpp calculando derruba o processo** (0xC0000005 em `ggml-cpu-whisper.dll`, ao fechar o app durante uma geração na CPU). O Whisper.net não tem callback de abortar: o cancelamento só vale entre etapas, e na CPU com o large pode levar ~20 s. Por isso o `Dispose` do transcritor cancela e espera, e a janela some antes dessa espera.
- **Download sem esperar o IPv6.** Numa rede sem rota IPv6, o .NET tentava os 8 endereços IPv6 do huggingface.co em sequência (~21 s cada) antes do IPv4: ~170 s em "0%". O `HttpClient` dos modelos conecta no estilo *Happy Eyeballs* (RFC 8305): IPv6 e IPv4 em paralelo. Medido: 2 s até os cabeçalhos.
- **Marcações do Whisper que não são fala**: `[BLANK_AUDIO]` (silêncio), `[Music]`, `(risos)` e o `>>` de troca de falante (aprendido de legendas de TV) são descartados.
- Nos testes, o `AudioPosition` do System.Speech só é correto no formato nativo da voz (16 kHz mono). Isso é um bug do `AudioFileOut.Duration`, por isso o TTS é gerado nesse formato e convertido depois.
