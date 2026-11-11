using System.Collections.Concurrent;
using System.Text.Json;

namespace Landipay.Backend.Servicos;

public sealed record EnderecoCnpj(
    string Logradouro, string Numero, string? Complemento, string Bairro, string Cidade, string Uf, string Cep);

public sealed record DadosCnpj(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    EnderecoCnpj? Endereco,
    bool DeCache,
    string Fonte);

/// <summary>
/// Consulta de CNPJ via API pública, com cache local de 24h.
/// Substitui o padrão do legado (chave de terceiro embutida no cliente — A2): aqui a
/// credencial, se houver, fica no servidor; o cliente fala só com o backend do Landipay.
/// Sem rede ou sem resposta válida, retorna <c>null</c> e a UI segue para preenchimento
/// manual — o cadastro nunca bloqueia por falta de internet.
/// </summary>
public sealed class ServicoCnpj
{
    public const string FontePadrao = "brasilapi";
    private static readonly TimeSpan TtlCache = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly string _urlBase;
    private readonly TimeProvider _relogio;
    private readonly ConcurrentDictionary<string, (DadosCnpj Dados, DateTimeOffset Expira)> _cache = new();

    public ServicoCnpj(HttpClient http, string urlBase = "https://brasilapi.com.br/api/cnpj/v1/", TimeProvider? relogio = null)
    {
        _http = http;
        _urlBase = urlBase.EndsWith('/') ? urlBase : urlBase + "/";
        _relogio = relogio ?? TimeProvider.System;
    }

    public async Task<DadosCnpj?> ConsultarAsync(string? cnpj, CancellationToken ct = default)
    {
        var normalizado = new string((cnpj ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalizado.Length != 14) return null;

        if (_cache.TryGetValue(normalizado, out var emCache) && emCache.Expira > _relogio.GetUtcNow())
            return emCache.Dados with { DeCache = true };

        try
        {
            using var resposta = await _http.GetAsync($"{_urlBase}{normalizado}", ct);
            if (!resposta.IsSuccessStatusCode) return null;

            await using var stream = await resposta.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var raiz = json.RootElement;

            var razao = Ler(raiz, "razao_social");
            if (string.IsNullOrWhiteSpace(razao)) return null;

            var dados = new DadosCnpj(
                Cnpj: normalizado,
                RazaoSocial: razao!,
                NomeFantasia: Ler(raiz, "nome_fantasia"),
                Endereco: new EnderecoCnpj(
                    Logradouro: Ler(raiz, "logradouro") ?? "Não informado",
                    Numero: Ler(raiz, "numero") ?? "S/N",
                    Complemento: Ler(raiz, "complemento"),
                    Bairro: Ler(raiz, "bairro") ?? string.Empty,
                    Cidade: Ler(raiz, "municipio") ?? string.Empty,
                    Uf: (Ler(raiz, "uf") ?? string.Empty).ToUpperInvariant(),
                    Cep: new string((Ler(raiz, "cep") ?? string.Empty).Where(char.IsDigit).ToArray())),
                DeCache: false,
                Fonte: FontePadrao);

            _cache[normalizado] = (dados, _relogio.GetUtcNow().Add(TtlCache));
            return dados;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string? Ler(JsonElement raiz, string propriedade) =>
        raiz.TryGetProperty(propriedade, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()?.Trim()
            : null;
}
