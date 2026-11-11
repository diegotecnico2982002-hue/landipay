using System.Text.Json;
using Landipay.Updates;

namespace Landipay.Client.Servicos;

public sealed record ResultadoAtualizacao(bool Aplicado, string Codigo, string Mensagem, string? ArquivoBaixado = null);

/// <summary>
/// Updater do cliente (A3). Regras não negociáveis:
///  - manifesto é validado com a chave pública PINADA antes de qualquer download;
///  - hash do arquivo baixado é conferido contra o hash assinado, antes de gravar;
///  - pacote inválido é descartado, com código de recusa, e nada é executado;
///  - a aplicação do pacote (executar instalador) é passo separado e explícito —
///    este serviço nunca chama Process.Start.
/// </summary>
public sealed class AtualizadorDesktop
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly byte[] _chavePublica;
    private readonly string _urlBase;

    public AtualizadorDesktop(HttpClient http, byte[] chavePublicaEd25519, string urlBase)
    {
        _http = http;
        _chavePublica = chavePublicaEd25519;
        _urlBase = urlBase.TrimEnd('/');
    }

    public async Task<ResultadoAtualizacao> VerificarEAplicarAsync(string versaoAtual, string canal,
        string pastaDestino, CancellationToken ct = default)
    {
        EnvelopeManifesto? envelope;
        try
        {
            using var resposta = await _http.GetAsync($"{_urlBase}/updates/manifesto?canal={Uri.EscapeDataString(canal)}", ct);
            if (!resposta.IsSuccessStatusCode)
                return new ResultadoAtualizacao(false, "servidor_indisponivel", $"HTTP {(int)resposta.StatusCode} ao consultar o manifesto.");

            var json = await resposta.Content.ReadAsStringAsync(ct);
            envelope = JsonSerializer.Deserialize<EnvelopeManifesto>(json, Json);
        }
        catch (HttpRequestException ex)
        {
            return new ResultadoAtualizacao(false, "sem_conexao", ex.Message);
        }
        catch (JsonException)
        {
            return new ResultadoAtualizacao(false, "manifesto_malformado", "Resposta do servidor não é um manifesto válido.");
        }

        if (envelope?.Manifesto is null)
            return new ResultadoAtualizacao(false, "manifesto_ausente", "Manifesto vazio.");

        // O KeyId vem no envelope e não faz parte do payload assinado.
        var manifesto = envelope.Manifesto with { KeyId = envelope.KeyId ?? envelope.Manifesto.KeyId };

        var validacao = ValidadorDeManifesto.Validar(manifesto, envelope.Assinatura, _chavePublica, versaoAtual);
        if (!validacao.Valido)
            return new ResultadoAtualizacao(false, validacao.Codigo, validacao.Descricao);

        if (string.Equals(manifesto.Versao, versaoAtual, StringComparison.OrdinalIgnoreCase))
            return new ResultadoAtualizacao(false, "sem_atualizacao", $"Já está na versão {versaoAtual}.");

        byte[] conteudo;
        try
        {
            conteudo = await _http.GetByteArrayAsync(manifesto.Url, ct);
        }
        catch (HttpRequestException ex)
        {
            return new ResultadoAtualizacao(false, "download_falhou", ex.Message);
        }

        var integridade = ValidadorDeManifesto.ValidarArtefato(conteudo, manifesto.Sha256);
        if (!integridade.Valido)
            return new ResultadoAtualizacao(false, integridade.Codigo, integridade.Descricao);

        Directory.CreateDirectory(pastaDestino);
        var destino = Path.Combine(pastaDestino, $"landipay-{manifesto.Versao}.zip");
        await File.WriteAllBytesAsync(destino, conteudo, ct);

        return new ResultadoAtualizacao(true, "ok", $"Versão {manifesto.Versao} baixada e validada.", destino);
    }

    private sealed record EnvelopeManifesto(ManifestoRelease? Manifesto, string? Assinatura, string? KeyId);
}
