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

A precisão do **tempo** das palavras é parecida em todos (veja [Precisão](#precisão)). Modelos maiores ajudam no **texto**.

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
- No build Debug, `$env:LESSONSPLAYER_DEBUG_SNAPSHOTS = "<pasta>"` faz a janela se salvar em `latest.png` a cada segundo. Serve para testes de UI automatizados, mesmo com a tela bloqueada.

### Arquitetura

```
App (WPF: views, serviços de UI, composition root com Generic Host + Serilog)
 ├─► Presentation (ViewModels MVVM, net10.0 sem WPF: o compilador impede dependência de UI)
 └─► Infrastructure (NAudio, Whisper.net, HTTP, arquivos)
       └─► Core (domínio imutável, formato .txt, editor, regras puras; sem dependências externas)
```

- **Core**: `Transcript`/`Segment`/`Word` imutáveis, `SubtitleTxtFormat`, `TranscriptEditor` (desfazer/refazer), `TextRealigner` (LCS), `WordIndex` (busca binária), `TokenWordMerger`, `SubtitleGenerator` (caso de uso) e as interfaces implementadas pela Infrastructure.
- Qualidade: `Nullable`, analisadores `latest-recommended`, *warnings as errors*, `.editorconfig` e versões centralizadas em `Directory.Packages.props`.

### Precisão

Medida pelo teste `TimestampPrecisionTests`, que compara com o instante real de cada palavra em áudio sintetizado. Erro no **início** da palavra:

| Modelo | pt-BR (mediana / p90) | en-US (mediana / p90) |
|---|---|---|
| base | 41 / 130 ms | 50 / 220 ms |
| small | 38 / 149 ms | 50 / 130 ms |
| medium | 82 / 248 ms | 70 / 165 ms |
| large-v3-turbo | 52 / 136 ms | 60 / 160 ms |

### Notas técnicas (descobertas com evidência)

- **`t_dtw` do whisper.cpp marca o *fim* do token**, não o início. A atribuição é deslocada em um índice em relação à implementação da OpenAI. Por isso o início de uma palavra é o `t_dtw` do token anterior (`TokenWordMerger`). Interpretado do jeito ingênuo, o erro mediano era ~250 ms.
- **Com DTW, o whisper.cpp só entrega a primeira janela de 30 s** de cada chamada. O laço do `new_segment_callback` usa `n_segments` em vez de `result_all.size()` ([código](https://github.com/ggml-org/whisper.cpp/blob/f24588a272ae8e23280d9c220536437164e6ed28/src/whisper.cpp#L7727); issues [#2669](https://github.com/ggml-org/whisper.cpp/issues/2669) e [#3111](https://github.com/ggml-org/whisper.cpp/issues/3111)). O `WhisperNetTranscriber` processa janela a janela, retomando onde o último trecho terminou.
- **Reprodução e transcrição usam o mesmo leitor (`AudioFileReader`)**. Assim o atraso inicial do MP3 e o seek em VBR não desalinham legenda e áudio. A posição exibida vem do relógio do WASAPI, ou seja, do que já saiu no alto-falante, e não do buffer.
- Nos testes, o `AudioPosition` do System.Speech só é correto no formato nativo da voz (16 kHz mono). Isso é um bug do `AudioFileOut.Duration`, por isso o TTS é gerado nesse formato e convertido depois.
