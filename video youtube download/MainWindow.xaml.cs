using System;
using System.IO;
using System.Windows;
using FFMpegCore;
using Microsoft.Win32;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace YtDownloader
{
    public partial class MainWindow : Window
    {
        private readonly YoutubeClient _youtube;
        private string _destinationFolder;

        public MainWindow()
        {
            InitializeComponent();
            _youtube = new YoutubeClient();

            _destinationFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            TxtFolder.Text = _destinationFolder;

            EnsureFfmpegConfigured();
        }

        
        private static void EnsureFfmpegConfigured()
        {
            try
            {
                var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
                if (pathDirs.Any(dir => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "ffmpeg.exe"))))
                {
                    return;
                }

                var candidateFolders = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
                    AppContext.BaseDirectory
                };

                foreach (var baseFolder in candidateFolders)
                {
                    if (!Directory.Exists(baseFolder))
                    {
                        continue;
                    }

                    string? ffmpegExe = Directory.EnumerateFiles(baseFolder, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (ffmpegExe is not null)
                    {
                        GlobalFFOptions.Configure(new FFOptions { BinaryFolder = Path.GetDirectoryName(ffmpegExe)! });
                        return;
                    }
                }
            }
            catch
            {
              
            }
        }

        private void BtnChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecione a pasta onde salvar o download",
                InitialDirectory = _destinationFolder
            };

            if (dialog.ShowDialog() == true)
            {
                _destinationFolder = dialog.FolderName;
                TxtFolder.Text = _destinationFolder;
            }
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtUrl.Text.Trim();

            if (string.IsNullOrEmpty(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                MessageBox.Show("Por favor, insira um link válido do YouTube.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_destinationFolder) || !Directory.Exists(_destinationFolder))
            {
                MessageBox.Show("A pasta de destino selecionada não existe mais. Escolha outra pasta.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetControlsEnabled(false);
            PbProgress.Value = 0;

            try
            {
                TxtStatus.Text = "Obtendo informações do vídeo...";

                var video = await _youtube.Videos.GetAsync(url);
                string safeTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));

                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(url);
                var progress = new Progress<double>(p => PbProgress.Value = p * 100);

                if (RbVideo.IsChecked == true)
                {
                    var muxedStreams = streamManifest.GetMuxedStreams();

                    if (muxedStreams.Any())
                    {
                    
                        var streamInfo = muxedStreams.GetWithHighestVideoQuality();
                        string filePath = GetUniqueFilePath(safeTitle, "mp4");

                        TxtStatus.Text = "Baixando vídeo. Aguarde...";
                        await _youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, progress);
                        TxtStatus.Text = $"Concluído! Salvo em: {filePath}";
                    }
                    else
                    {
                        var videoOnlyStreams = streamManifest.GetVideoOnlyStreams();
                        var videoStreamInfo = videoOnlyStreams.Where(s => s.Container == Container.Mp4).GetWithHighestVideoQuality()
                            ?? videoOnlyStreams.GetWithHighestVideoQuality();

                        var audioOnlyStreams = streamManifest.GetAudioOnlyStreams();
                        var audioStreamInfo = audioOnlyStreams.Where(s => s.Container == Container.Mp4).GetWithHighestBitrate()
                            ?? audioOnlyStreams.GetWithHighestBitrate();

                        if (videoStreamInfo is null || audioStreamInfo is null)
                        {
                            MessageBox.Show("Nenhum formato de vídeo/áudio compatível foi encontrado para este link.", "Formato Indisponível", MessageBoxButton.OK, MessageBoxImage.Information);
                            TxtStatus.Text = "Download cancelado.";
                        }
                        else
                        {
                            string filePath = GetUniqueFilePath(safeTitle, "mp4");
                            await DownloadAndMuxAsync(videoStreamInfo, audioStreamInfo, filePath, progress);
                            TxtStatus.Text = $"Concluído! Salvo em: {filePath}";
                        }
                    }
                }
                else
                {
                    var audioStreams = streamManifest.GetAudioOnlyStreams();

                    if (audioStreams.Any())
                    {
                        var streamInfo = audioStreams.GetWithHighestBitrate();
                        string filePath = GetUniqueFilePath(safeTitle, "mp3");

                        TxtStatus.Text = "Baixando áudio. Aguarde...";
                        await _youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, progress);
                        TxtStatus.Text = $"Concluído! Salvo em: {filePath}";
                    }
                    else
                    {
                        MessageBox.Show("Nenhum arquivo de áudio encontrado para este link.", "Formato Indisponível", MessageBoxButton.OK, MessageBoxImage.Information);
                        TxtStatus.Text = "Download cancelado.";
                    }
                }

                TxtUrl.Text = string.Empty;
            }
            catch (Exception ex)
            {
                string detail = ex.InnerException is not null ? $"{ex.Message}\n\nDetalhe: {ex.InnerException.Message}" : ex.Message;
                MessageBox.Show($"Ocorreu um erro: {detail}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "Falha no download.";
            }
            finally
            {
                SetControlsEnabled(true);
            }
        }

        private async Task DownloadAndMuxAsync(IStreamInfo videoStreamInfo, IStreamInfo audioStreamInfo, string outputFilePath, IProgress<double> progress)
        {
            string tempVideoPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.{videoStreamInfo.Container.Name}");
            string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.{audioStreamInfo.Container.Name}");

            try
            {
                TxtStatus.Text = "Baixando vídeo. Aguarde...";
                var videoProgress = new Progress<double>(p => progress.Report(p * 0.5));
                await _youtube.Videos.Streams.DownloadAsync(videoStreamInfo, tempVideoPath, videoProgress);

                TxtStatus.Text = "Baixando áudio. Aguarde...";
                var audioProgress = new Progress<double>(p => progress.Report(0.5 + p * 0.4));
                await _youtube.Videos.Streams.DownloadAsync(audioStreamInfo, tempAudioPath, audioProgress);

                TxtStatus.Text = "Combinando vídeo e áudio...";
                try
                {
                    await FFMpegArguments
                        .FromFileInput(tempVideoPath)
                        .AddFileInput(tempAudioPath)
                        .OutputToFile(outputFilePath, true, options => options
                            .WithVideoCodec("copy")
                            .WithAudioCodec("copy")
                            .WithFastStart())
                        .ProcessAsynchronously();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Não foi possível combinar vídeo e áudio. Verifique se o FFmpeg está instalado e disponível no PATH do sistema (ex.: 'winget install ffmpeg').", ex);
                }

                progress.Report(1);
            }
            finally
            {
                TryDeleteFile(tempVideoPath);
                TryDeleteFile(tempAudioPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            
            }
        }

        private string GetUniqueFilePath(string safeTitle, string extension)
        {
            string filePath = Path.Combine(_destinationFolder, $"{safeTitle}.{extension}");
            int counter = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(_destinationFolder, $"{safeTitle} ({counter}).{extension}");
                counter++;
            }

            return filePath;
        }

        private void SetControlsEnabled(bool enabled)
        {
            BtnDownload.IsEnabled = enabled;
            BtnChooseFolder.IsEnabled = enabled;
            TxtUrl.IsEnabled = enabled;
            RbVideo.IsEnabled = enabled;
            RbAudio.IsEnabled = enabled;
        }
    }
}
