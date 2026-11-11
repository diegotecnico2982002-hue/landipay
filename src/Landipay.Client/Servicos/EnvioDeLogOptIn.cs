using System.Net.Http.Headers;
using Landipay.Client.Configuracao;

namespace Landipay.Client.Servicos;

/// <summary>
/// Envio de log <b>opt-in</b> (A6). Três travas antes de qualquer byte sair:
///  1. o usuário precisa ter ligado a opção explicitamente;
///  2. o endpoint precisa ser HTTPS (nunca HTTP em claro);
///  3. o arquivo precisa existir e o envio é um evento único, não um canal aberto.
/// Sem opt-in, o método retorna false sem tocar a rede.
/// </summary>
public sealed class EnvioDeLogOptIn
{
    private readonly HttpClient _http;
    public EnvioDeLogOptIn(HttpClient http) => _http = http;

    public async Task<bool> EnviarAsync(string? caminhoLog, PreferenciasUsuario? preferencias, CancellationToken ct = default)
    {
        if (preferencias is null || !preferencias.EnvioDeLogOptIn) return false;
        if (string.IsNullOrWhiteSpace(preferencias.UrlEnvioLog)) return false;
        if (!Uri.TryCreate(preferencias.UrlEnvioLog, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.IsNullOrWhiteSpace(caminhoLog) || !File.Exists(caminhoLog)) return false;

        try
        {
            using var conteudo = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(caminhoLog, ct);
            var parte = new ByteArrayContent(bytes);
            parte.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            conteudo.Add(parte, "logfile", Path.GetFileName(caminhoLog));

            using var resposta = await _http.PostAsync(uri, conteudo, ct);
            return resposta.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (IOException) { return false; }
    }
}
