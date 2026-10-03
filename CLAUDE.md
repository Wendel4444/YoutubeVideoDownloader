# YouTube Downloader

App desktop (WPF, .NET 10) que baixa vídeos e áudios do YouTube e de outros sites. O visual segue o Cinema Production OS (mesma paleta, `Theme.xaml`, sidebar, cards).

## Decisões fixas
- **Motor = yt-dlp**, não bibliotecas .NET (o YoutubeExplode quebrava a cada mudança do YouTube). O app baixa o `yt-dlp.exe` para a própria pasta de ferramentas e o atualiza com `-U` (uma vez por dia, opcional).
- O yt-dlp precisa de um **runtime JavaScript** (Deno ou Node) para liberar todos os formatos do YouTube; `ToolsService` detecta, ou baixa o Deno.
- **FFmpeg** não é embutido: `ToolsService` procura (configuração → pasta do app → PATH → winget → pastas comuns) ou baixa o build do yt-dlp.
- Dados em `%LocalAppData%\YoutubeDownloader` (ferramentas, temp, logs, settings.json), nunca no OneDrive.
- Cada download usa uma pasta temporária própria (`-P temp:`); cancelar ou fechar o app não deixa lixo no destino.
- Argumentos sempre via `ArgumentList` e a URL depois de `--` (link nunca vira opção).
- Testes de interface só via UI Automation nas janelas do app, nunca com teclas globais.
- Licença MIT (`LICENSE`, copiado como `LICENSE.txt` ao lado do exe). Doação por Pix: o código "copia e cola" sai de `Core/PixPayload` (chave `delsanvfx@gmail.com`) e o QR é gerado no app (QRCoder); `docs/pix-qrcode.png` é o mesmo QR para o README. Mudou a chave ou o nome? Atualize os testes de `PixPayloadTests`, o README e regenere o PNG.

## Estrutura
- `src/YoutubeDownloader.App/Core`: regras puras e testáveis (formatos, `YtDlpArguments`, leitura de progresso, tradução de erros, JSON do yt-dlp).
- `src/YoutubeDownloader.App/Services`: processos, ferramentas, cliente yt-dlp, configurações, Shell.
- `src/YoutubeDownloader.App/ViewModels` + `Views`: MVVM (CommunityToolkit.Mvvm). Fila e limite de simultâneos em `DownloadsViewModel.Pump`.
- `tests/YoutubeDownloader.Tests`: xUnit. `IntegrationTests` fazem downloads reais só com `YTD_INTEGRATION=1` (opcional `YTD_YTDLP=<caminho do yt-dlp.exe>`), em pasta temporária.

## Convenções
- Identificadores em inglês; textos de UI, logs e comentários em português.
- Erros do yt-dlp viram mensagens com ação ("atualize o yt-dlp", "use cookies do navegador") em `ErrorTranslator`.
- Cores e tipografia só via `Themes/` (`Brush.*`, `Text.*`, `Card`, `Button.*`, `Segment`, `Badge`).
- Release compila com avisos como erros.

## Comandos
```powershell
dotnet build
dotnet test
$env:YTD_INTEGRATION=1; dotnet test --filter IntegrationTests   # downloads reais
dotnet run --project src/YoutubeDownloader.App
dotnet publish src/YoutubeDownloader.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/win-x64
# executável do release no GitHub (autocontido: quem baixa não precisa do .NET)
dotnet publish src/YoutubeDownloader.App/YoutubeDownloader.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/release
gh release create vX.Y.Z -R Wendel4444/YoutubeVideoDownloader --target master --latest <exe> <zip>
```

Repositório: https://github.com/Wendel4444/YoutubeVideoDownloader (releases em /releases/latest). Ao lançar versão nova, suba `<Version>` em `Directory.Build.props`.
