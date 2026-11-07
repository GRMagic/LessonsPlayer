# LessonsPlayer

Player de áudio para aulas com **legenda estilo karaokê**, gerada localmente com Whisper. A palavra falada fica destacada enquanto o áudio toca, e um **clique numa palavra leva o áudio ao instante em que ela é dita**. A legenda é salva num `.txt` ao lado do áudio, pode ser editada e é reaberta automaticamente.

- Playlist a partir de uma pasta (ordem natural: `aula2` antes de `aula10`), de um arquivo avulso ou por arrastar e soltar.
- Formatos: MP3, WAV, M4A, AAC, WMA e FLAC (Media Foundation do Windows).
- Transcrição 100% local: nada é enviado para a internet. A única conexão é o download do modelo, feito uma vez.
- Usa a GPU quando há (qualquer placa com driver Vulkan: NVIDIA, AMD ou Intel) e cai para a CPU quando não há.

## Usando

A interface é em inglês, porque o programa é usado nas aulas de inglês, ao lado dos alunos.

1. **Open folder** (ou **Open file**) e dar duplo clique na faixa.
2. **Generate subtitles**: escolha o modelo e o idioma ("Auto-detect" funciona bem). Na primeira vez o modelo é baixado.
3. Dê play. Clique em qualquer palavra para ir até ela, ou no horário à esquerda de uma frase para ir ao início dela.

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

Para apagar a legenda de uma aula, clique com o botão direito nela na playlist (ou selecione e use `Shift+F10`) e escolha **Delete subtitles…**. O programa pede confirmação e avisa se há alterações não salvas, que também são descartadas.
- A exclusão apaga o `.subtitle.txt` e não dá para desfazer.
- Enquanto a legenda da aula está sendo gerada, a opção fica desabilitada.

### Modelos

| Modelo | Tamanho | Quando usar |
|---|---|---|
| Tiny / Base | 32 / 60 MB | Máquinas lentas, áudio limpo |
| **Small** (padrão) | 190 MB | Bom equilíbrio |
| Medium / Large v3 Turbo | 540 / 575 MB | Áudio difícil (ruído, sotaque); mais lentos sem GPU |
| Base.en / Small.en / Medium.en | 60 / 190 / 540 MB | Só inglês (o idioma fica fixo em inglês) |

A precisão do **tempo** das palavras é parecida em todos (veja [Precisão](#precisão)); o base.en é o único que fica um pouco atrás. Modelos maiores ajudam no **texto** em áudio difícil.

## Formato da legenda

`lesson01.mp3` → `lesson01.subtitle.txt`, na mesma pasta, em UTF-8. Dá para editar no Bloco de Notas:

```
# LessonsPlayer subtitle v1
# audio: lesson01.mp3
# model: small-q5_1
# language: en
# created: 2026-11-05T14:00:04-03:00

[00:00:00.180 --> 00:00:07.320] Hello, everyone! Today we are going to study the difference between two English tenses.
00:00:00.180 00:00:00.420 Hello,
00:00:00.840 00:00:01.140 everyone!
...
```

- As linhas `início fim palavra` são a fonte da verdade. O texto do cabeçalho `[… --> …]` é regenerado ao salvar.
- Um trecho escrito à mão **sem** linhas de palavra é aceito: o tempo é distribuído entre as palavras proporcionalmente ao tamanho delas.
- Aceita `hh:mm:ss.fff`, `mm:ss.fff` ou `mm:ss`, com ponto ou vírgula. Linhas não reconhecidas são ignoradas, e o app avisa quantas foram.

## Distribuição

```powershell
./scripts/publish.ps1          # gera artifacts/publish/LessonsPlayer-win-x64 (+ .zip)
```

A pasta é **self-contained**: roda em Windows 10/11 x64 sem .NET, Python ou CUDA instalados. Basta descompactar e abrir `LessonsPlayer.exe`. Dos ~100 MB do zip, cerca de metade é o runtime Vulkan, que habilita a GPU. O detector de voz (Silero VAD, 0,9 MB, licença MIT) vai junto, na pasta `vad`. Nas edições "N" do Windows é preciso o *Media Feature Pack*, por causa da decodificação de áudio.

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

- **Core**: `Transcript`/`Segment`/`Word` imutáveis, `SubtitleTxtFormat`, `TranscriptEditor` (desfazer/refazer), `TextRealigner` (LCS), `WordIndex` (busca binária), `TokenWordMerger`, `SentenceRegrouper`, `SpeechActivity`/`HallucinationFilter` (regiões com fala e descarte do texto inventado), `SubtitleGenerator` (caso de uso) e as interfaces implementadas pela Infrastructure.
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

O NonSpeechHallucinationTests monta uma aula TTS com 25 s de música ou ruído no meio e no fim. Antes do detector de voz, 6 dos 8 casos (base, small, small.en e large-v3-turbo) falhavam: o base inventava frases inteiras no ruído, e a primeira palavra depois da música saía até 27 s antes de ser dita. Agora não sobra palavra inventada e essa palavra fica a 30 ms do tempo real. A precisão acima não mudou (os 21 casos deram exatamente os mesmos números), porque as aulas TTS não têm pausas de 2 s.

**Áudio real** (`RealAudioComparisonTests`, MP3 do YouTube): uma aula da Khan Academy (10 min, 1 falante), uma conversa do StoryCorps com música de fundo (6 min, 2 falantes) e uma entrevista da NASA via satélite (13 min, 10+ falantes). O texto é comparado com a legenda humana. O tempo é comparado com a legenda automática do YouTube, que é de outro reconhecedor e também tem erro. Números de 2026-11-07, com o código atual na GPU:

| Modelo | WER Khan | WER StoryCorps | Palavra vs. YouTube (mediana): Khan / StoryCorps / NASA | Início de frase vs. legenda humana, Khan (mediana / p90) | Palavras inventadas: StoryCorps / NASA |
|---|---|---|---|---|---|
| small | 3,2% | 6,1% | 180 / 50 / 91 ms | 80 / 370 ms | 0 / 0 |
| small.en | 3,0% | 5,6% | 179 / 50 / 110 ms | 130 / 510 ms | 0 / 0 (eram 6) |
| large-v3-turbo | 2,4% | 4,0% | 180 / 50 / 90 ms | 160 / 480 ms | 0 (eram 12) / 0 (eram 5) |

- No Khan, todas as palavras ficam ~180 ms **antes** da legenda automática. É um viés constante entre os dois reconhecedores. A legenda humana, que é bem sincronizada nesse vídeo, fica a 80 ms.
- A legenda humana da NASA é estenotipia ao vivo, ~3,5 s atrasada e com omissões (WER de 33–34% contra ela). Lá vale só a comparação de tempo com a automática.
- Nenhuma frase ficou cortada (sem o `SentenceRegrouper`, eram 67 de 125 trechos no Khan com o small).
- Em trechos longos sem fala (créditos com música, fim da transmissão), o Whisper **inventava texto**: "I mean those are the most important things to say." e "you you Thank you." nos créditos do StoryCorps (large), "We will begin the session." repetido no fim da NASA (small.en). Com o detector de voz (ver [Notas técnicas](#notas-técnicas-descobertas-com-evidência)), nenhuma palavra inventada sobrou. "Inventada" é uma palavra fora das frases da legenda humana e a mais de 3 s de qualquer palavra da automática. As palavras casadas com a automática ficaram iguais ou subiram em 7 de 9 casos (Khan: idênticas; NASA small: 1.894 → 1.970), e caíram 2 no NASA large e 7 no StoryCorps small. A piora no WER do StoryCorps com o small (5,1% → 6,3% na medição feita junto com o detector) vem de outras escolhas de palavra ("silly pen" virou "sewie pen", "him" virou "them"): as fatias começam em outros pontos e o decodificador muda.

### Notas técnicas (descobertas com evidência)

- **`t_dtw` do whisper.cpp marca o *fim* do token**, não o início. A atribuição é deslocada em um índice em relação à implementação da OpenAI. Por isso o início de uma palavra é o `t_dtw` do token anterior (`TokenWordMerger`). Interpretado do jeito ingênuo, o erro mediano era ~250 ms.
- **Com DTW, o whisper.cpp só entrega a primeira janela de 30 s** de cada chamada. O laço do `new_segment_callback` usa `n_segments` em vez de `result_all.size()` ([código](https://github.com/ggml-org/whisper.cpp/blob/f24588a272ae8e23280d9c220536437164e6ed28/src/whisper.cpp#L7727); issues [#2669](https://github.com/ggml-org/whisper.cpp/issues/2669) e [#3111](https://github.com/ggml-org/whisper.cpp/issues/3111)). O `WhisperNetTranscriber` processa janela a janela, retomando onde o último trecho terminou.
- **Cada fatia processa só a primeira janela.** Depois dela, o whisper.cpp seguiria com a sobra da fatia: trabalho perdido (os trechos não são entregues) e, numa sobra de até ~150 ms, um abort nativo no filtro mediano do DTW (`WHISPER_ASSERT(filter_width < a->ne[2])`, corrigido no whisper.cpp depois da versão do Whisper.net 1.9.1). O handler de início do encoder interrompe antes da segunda janela: mesmos trechos, transcrição mais rápida (medium: 45 s → 22 s numa aula de 2,5 min).
- **O Whisper corta frases longas em mais de um trecho**, pelos timestamps que o próprio decodificador prevê. Acontece com e sem DTW e com e sem prompt, quase sempre no meio da janela e não na fronteira das fatias. Numa aula TTS em inglês de 2,5 min, o small cortou as 15 frases. O `SentenceRegrouper` completa cada frase inacabada com as palavras seguintes até o ponto final (até 30 s, sem pausa maior que 2 s), sem mudar o tempo das palavras.
- **Reprodução e transcrição usam o mesmo leitor (`AudioFileSource`)**, então o atraso inicial do MP3 não desalinha legenda e áudio. A posição exibida vem do relógio do WASAPI, ou seja, do que já saiu no alto-falante, e não do buffer.
- **Seek exato em MP3 VBR.** No NAudio 3, o `AudioFileReader` decodifica MP3 pelo Media Foundation, que posiciona MP3 de bitrate variável como se o bitrate fosse constante e informa a posição pedida, não a real. Num MP3 VBR real de 56 s, o áudio depois do seek começava até 3 s longe (+3,0 s em 5 s, −2,0 s em 40 s). Por isso a legenda saía de sincronia depois de clicar numa palavra, embora tocar desde o início ficasse certo. O `AudioFileSource` abre MP3 com o `Mp3FileReader`, que indexa os quadros ao abrir: o erro do seek passou a 0 ms (`SeekAccuracyTests`, com um MP3 VBR montado no teste). O custo é pequeno: abrir 10 min de MP3 leva ~35 ms e decodificar tudo, 1,3 s. Os outros formatos continuam no Media Foundation, porque os contêineres deles têm índice de tempo.
- **Fim de faixa = fonte esgotada.** Em MP3 o tamanho total (`Length`) é estimado e pode passar do que é decodificado; comparar posição com tamanho nunca chegava ao fim, e o player ficava "tocando" em 0:00. Além disso, o `PlaybackStopped` do NAudio é postado na thread da UI, depois que o `Stop()` de um seek já voltou. O player marca o fim quando o leitor devolve 0 bytes (`EndTrackingWaveProvider`).
- **Liberar o modelo com o whisper.cpp calculando derruba o processo** (0xC0000005 em `ggml-cpu-whisper.dll`, ao fechar o app durante uma geração na CPU). O Whisper.net não tem callback de abortar: o cancelamento só vale entre etapas, e na CPU com o large pode levar ~20 s. Por isso o `Dispose` do transcritor cancela e espera, e a janela some antes dessa espera.
- **Download sem esperar o IPv6.** Numa rede sem rota IPv6, o .NET tentava os 8 endereços IPv6 do huggingface.co em sequência (~21 s cada) antes do IPv4: ~170 s em "0%". O `HttpClient` dos modelos conecta no estilo *Happy Eyeballs* (RFC 8305): IPv6 e IPv4 em paralelo. Medido: 2 s até os cabeçalhos.
- **Marcações do Whisper que não são fala** são descartadas. Vistas nas legendas geradas (Whisper.net 1.9.1): `[BLANK_AUDIO]` (silêncio), `[Music]`, `[MUSIC PLAYING]`, `[ Silence ]`, `[static]`, `[sound of water]`, `[typing sounds]` (small, small.en e base) e o `>>` de troca de falante, aprendido de legendas de TV (small e small.en). O large-v3-turbo não usa colchetes: escreve `Music.` como texto, e quem o remove é o filtro de fala abaixo. O whisper.cpp tem a opção `suppress_nst`, que proíbe esses tokens, mas ela vem desligada e o Whisper.net 1.9.1 não a expõe.
- **Texto inventado em trechos sem fala.** Em música, créditos e silêncio longo, o Whisper às vezes escreve o que ninguém disse ("Thank you.", "you you", "We will begin the session." repetido), com a mesma confiança da fala real. Medido em 3 vídeos reais: a probabilidade de "sem fala" do whisper.cpp ficou em ~0 nesses trechos, e a probabilidade média dos tokens em 0,77–0,99, a mesma faixa da fala real. Por isso nem os limiares do decodificador (`WithNoSpeechThreshold`, `WithLogProbThreshold`) nem filtros por probabilidade resolvem. Quem resolve é um detector de voz: o Silero VAD (v6.2.0, pelo `WhisperVadFactory` do Whisper.net) marca onde há fala. A transcrição pula os trechos sem fala de 2 s ou mais, a fatia termina antes de um trecho sem fala de 10 s ou mais (vinheta), e as palavras que começam a mais de 1,5 s de qualquer fala são descartadas (`HallucinationFilter`). Texto que é só marcação (`[BLANK_AUDIO]`) não vai para o prompt da janela seguinte; e, quando o último trecho de uma fatia vai bem além da última palavra e o VAD ouviu fala nessa sobra, a fatia seguinte recomeça na última palavra (o modelo tinha pulado a fala). O áudio não é cortado nem emendado, então os tempos continuam absolutos.
- **Frase completada depois do fim da fala.** O VAD estende a fala um pouco além do último trecho do Whisper. Quando a janela terminava no fim da fala, essa sobra virava uma fatia própria, de ~1 s, com áudio que a janela anterior já tinha ouvido. Nela o modelo completava a frase. Num MP3 de teste cortado no meio da frase, o small escreveu "I'm going to show you how to do that." no silêncio depois de "application,", e o alinhamento pôs essas palavras todas no mesmo instante, com duração zero. Em 4 áudios reais com 3 modelos houve 8 fatias assim, e nenhuma tinha palavra real: em 5 o modelo inventou texto ("you", "Bye.", "Thank you."), e nas outras 3 só escreveu marcações. Agora uma fatia sem fala depois do fim da janela anterior não é decodificada, como o Whisper original faz quando a janela termina num trecho fechado. Descartar as palavras com duração zero não resolveria: no meio de um trecho elas são quase sempre fala real (Khan 8/8, StoryCorps 13/13), porque o DTW às vezes junta duas palavras no mesmo instante.
- **Laço de repetição.** Às vezes o decodificador repete uma frase até o fim da janela, no lugar da fala real (na NASA, com o small: "I think it's a great opportunity to talk about space." 14 vezes). O texto anterior, passado como prompt, prende o modelo no ciclo. Quando a razão de compressão da janela passa de 2,4 (o limiar do Whisper original), ela é decodificada de novo sem o prompt. Nesse caso a razão caiu de 11,2 para 1,6, e a fala real voltou.
- **Palavras sem alinhamento no fim da fatia.** O DTW do whisper.cpp só usa o áudio até o último timestamp que o decodificador previu (`n_frames = min(seek_delta, …)`, [código](https://github.com/ggml-org/whisper.cpp/blob/f24588a272ae8e23280d9c220536437164e6ed28/src/whisper.cpp#L7722)); a implementação da OpenAI alinha a janela inteira. Quando o texto vai além desse ponto, as palavras que sobram ficam todas no último instante alinhado, com duração zero. Em 3 vídeos reais com small, small.en e large-v3-turbo houve 14 fatias assim. Com 1 a 3 palavras empilhadas (10 fatias), todas eram fala real e estavam a até 0,3 s do tempo da legenda automática. Com 8 a 16 (4 fatias), havia texto inventado por cima da fala real (NASA, small, 6:18: "The United States Senate and the United States Senate have been in the process of making the", no lugar de "...school issued an apology..."), uma continuação inventada depois do fim da janela (Khan, large, 7:54: "...that it's a little bit more than a plane."), a frase da fatia anterior repetida no começo da música (StoryCorps, large) ou fala real até 3 s antes da hora (NASA, small, 11:52). A partir de 4 palavras empilhadas (`UnalignedTail`), a sequência é descartada e a fatia seguinte começa no fim da última palavra alinhada, para esse áudio ser decodificado de novo. Se não houver palavra alinhada antes, a fatia é decodificada de novo sem o prompt, e a nova versão só fica se o DTW tiver posto alguma palavra no áudio. Nos 9 casos (3 vídeos × 3 modelos), não sobrou empilhamento de 4 ou mais palavras e 6 legendas saíram idênticas. O WER caiu na NASA com o small (34,0% → 33,2%, com 43 palavras casadas a mais com a legenda automática), no Khan com o large (3,2% → 2,4%) e no StoryCorps com o large (4,5% → 4,0%), e o erro de tempo por palavra ficou igual. Com TTS não deu para reproduzir: em 12 transcrições com a janela cortada no meio da fala, o decodificador sempre fechou o trecho no limite de uma palavra.
- Nos testes, o `AudioPosition` do System.Speech só é correto no formato nativo da voz (16 kHz mono). Isso é um bug do `AudioFileOut.Duration`, por isso o TTS é gerado nesse formato e convertido depois.
