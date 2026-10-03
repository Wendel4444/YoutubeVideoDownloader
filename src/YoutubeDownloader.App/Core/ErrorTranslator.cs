using System.Text.RegularExpressions;

namespace YoutubeDownloader.App.Core;

/// <summary>Converte as mensagens de erro do yt-dlp em algo que diga ao usuário o que fazer.</summary>
public static partial class ErrorTranslator
{
    private static readonly (string[] Patterns, string Message)[] Known =
    [
        (["not a bot", "confirm you’re not", "confirm you're not"],
            "O YouTube pediu verificação anti-robô. Em Configurações, escolha um navegador em “Cookies do navegador” (com sua conta do YouTube logada) e tente de novo."),
        (["confirm your age", "age-restricted", "inappropriate for some users", "age restricted"],
            "Vídeo com restrição de idade. Em Configurações, use os cookies de um navegador com sua conta logada e tente de novo."),
        (["could not copy", "cookie database", "failed to decrypt", "cookies from browser"],
            "Não foi possível ler os cookies do navegador. Feche o navegador por completo e tente de novo. Chrome e Edge costumam bloquear; o Firefox funciona melhor."),
        (["private video"],
            "Este vídeo é privado."),
        (["members-only", "join this channel", "available to this channel's members"],
            "Vídeo exclusivo para membros do canal. Use os cookies de um navegador com uma conta que seja membro."),
        (["not available in your country", "geo restriction", "geo-restricted", "blocked it in your country"],
            "Este vídeo não está disponível no seu país."),
        (["premieres in", "live event will begin", "this live event will", "is upcoming"],
            "A estreia ou transmissão ainda não começou."),
        (["requested format is not available", "no video formats found"],
            "Este formato não está disponível para o vídeo. Escolha outro formato ou outra qualidade."),
        (["http error 403", "403: forbidden"],
            "O servidor recusou o download (erro 403). Atualize o yt-dlp em Configurações e tente de novo."),
        (["http error 429", "too many requests"],
            "Muitas requisições seguidas: o site bloqueou temporariamente. Espere alguns minutos ou use cookies do navegador."),
        (["unsupported url"],
            "Este link não é suportado. Confira se é o link de um vídeo, playlist ou canal."),
        (["is not a valid url", "invalid url"],
            "O link parece inválido. Confira e cole de novo."),
        (["ffmpeg not found", "ffprobe not found", "ffmpeg is not installed", "ffprobe and ffmpeg not found"],
            "O FFmpeg não foi encontrado. Instale em Configurações."),
        (["no space left", "not enough space", "errno 28"],
            "Sem espaço em disco na pasta de destino."),
        (["permission denied", "access is denied", "errno 13"],
            "Sem permissão para gravar na pasta de destino. Escolha outra pasta em Configurações."),
        (["unable to download webpage", "getaddrinfo failed", "timed out", "connection reset", "network is unreachable", "failed to resolve"],
            "Falha de conexão. Verifique a internet e tente de novo."),
        (["video unavailable", "this video is unavailable", "has been removed", "account associated with this video has been terminated", "video does not exist"],
            "Vídeo indisponível: foi removido ou o link está errado."),
        (["signature extraction failed", "nsig extraction failed", "unable to extract", "some formats may be missing"],
            "O YouTube mudou algo e o yt-dlp precisa de atualização. Atualize em Configurações e tente de novo."),
    ];

    public static string Translate(IEnumerable<string> stderr)
    {
        var lines = stderr.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var errors = lines.Where(l => l.StartsWith("ERROR:", StringComparison.Ordinal)).ToList();
        var relevant = errors.Count > 0 ? errors : lines;

        foreach (var line in relevant.AsEnumerable().Reverse())
        {
            foreach (var (patterns, message) in Known)
            {
                if (patterns.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    return message;
            }
        }

        var last = errors.LastOrDefault() ?? lines.LastOrDefault();
        return last is null ? "O yt-dlp terminou com erro sem dar detalhes." : Clean(last);
    }

    /// <summary>"ERROR: [youtube] abc123: Mensagem" → "Mensagem".</summary>
    public static string Clean(string line)
    {
        var text = line.StartsWith("ERROR:", StringComparison.Ordinal) ? line[6..].Trim() : line.Trim();
        return ExtractorPrefix().Replace(text, "").Trim();
    }

    [GeneratedRegex(@"^\[[^\]]+\]\s*([^:\s]+:\s*)?")]
    private static partial Regex ExtractorPrefix();
}
