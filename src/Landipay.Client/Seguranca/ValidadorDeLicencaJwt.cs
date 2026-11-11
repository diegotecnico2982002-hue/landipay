using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Landipay.Client.Seguranca;

public sealed record ResultadoLicenca(
    bool Valida,
    string Codigo,
    string? InstalacaoId = null,
    Guid? EmpresaId = null,
    string? Plano = null,
    DateTimeOffset? ExpiraEm = null,
    IReadOnlyList<string>? Apps = null);

/// <summary>
/// Validação de licença <b>offline</b>: o token JWT é verificado com a chave pública
/// ES256 pinada no binário — sem chamar o servidor. É o que permite instalar e operar
/// numa loja sem internet, sem transformar isso em licença perpétua (o `exp` é checado).
///
/// Implementação sem dependência externa: base64url + ECDSA P-256 (R||S, como manda o JWS).
/// </summary>
public static class ValidadorDeLicencaJwt
{
    public const string EmissorEsperado = "landipay-api";
    public const string AudienciaEsperada = "landipay-client";

    public static ResultadoLicenca Validar(string? token, byte[]? chavePublicaSpki,
        DateTimeOffset? agora = null, string emissor = EmissorEsperado, string audiencia = AudienciaEsperada)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new ResultadoLicenca(false, "token_ausente");
        if (chavePublicaSpki is null || chavePublicaSpki.Length == 0)
            return new ResultadoLicenca(false, "chave_ausente");

        var partes = token.Split('.');
        if (partes.Length != 3)
            return new ResultadoLicenca(false, "token_malformado");

        byte[] assinatura;
        string headerJson, payloadJson;
        try
        {
            headerJson = Encoding.UTF8.GetString(Base64UrlDecode(partes[0]));
            payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(partes[1]));
            assinatura = Base64UrlDecode(partes[2]);
        }
        catch (FormatException) { return new ResultadoLicenca(false, "token_malformado"); }

        using var header = JsonDocument.Parse(headerJson);
        if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "ES256")
            return new ResultadoLicenca(false, "algoritmo_nao_aceito");

        // 1) Assinatura sobre "header.payload" (exatamente como veio, sem re-serializar).
        var assinado = Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}");
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(chavePublicaSpki, out _);

            var ok = ecdsa.VerifyData(assinado, assinatura, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            if (!ok) return new ResultadoLicenca(false, "assinatura_invalida");
        }
        catch (CryptographicException) { return new ResultadoLicenca(false, "chave_invalida"); }

        // 2) Claims.
        using var payload = JsonDocument.Parse(payloadJson);
        var raiz = payload.RootElement;

        if (LerTexto(raiz, "iss") != emissor)
            return new ResultadoLicenca(false, "emissor_invalido");
        if (LerTexto(raiz, "aud") != audiencia)
            return new ResultadoLicenca(false, "audiencia_invalida");

        var expira = LerEpoch(raiz, "exp");
        if (expira is null) return new ResultadoLicenca(false, "sem_expiracao");

        var momento = agora ?? DateTimeOffset.UtcNow;
        if (momento > expira.Value)
            return new ResultadoLicenca(false, "licenca_expirada", LerTexto(raiz, "sub"), null, null, expira);

        var nbf = LerEpoch(raiz, "nbf");
        if (nbf is not null && momento.AddMinutes(1) < nbf.Value)
            return new ResultadoLicenca(false, "ainda_nao_valida");

        var empresaTexto = LerTexto(raiz, "emp");
        Guid? empresa = Guid.TryParse(empresaTexto, out var e) ? e : null;

        return new ResultadoLicenca(true, "ok", LerTexto(raiz, "sub"), empresa, LerTexto(raiz, "plano"), expira,
            LerApps(raiz));
    }

    /// <summary>
    /// Lê o claim <c>apps</c>. Ausente = licença antiga → <c>null</c> (o chamador trata como
    /// "ambos"). Presente mas malformado = escopo vazio: falha fechada, nunca "libera tudo".
    /// </summary>
    private static IReadOnlyList<string>? LerApps(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("apps", out var v)) return null;

        var lista = new List<string>();

        if (v.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in v.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    lista.Add(s.Trim().ToLowerInvariant());
        }
        else if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } texto)
        {
            // O emissor grava o vetor como JSON serializado dentro do claim, então o payload
            // traz a string `["admin","pos"]`. Aceita também o formato manual "admin,pos".
            var limpo = texto.Trim();

            if (limpo.StartsWith('['))
            {
                try
                {
                    using var doc = JsonDocument.Parse(limpo);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (var item in doc.RootElement.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                                lista.Add(s.Trim().ToLowerInvariant());
                }
                catch (JsonException) { return Array.Empty<string>(); }
            }
            else
            {
                foreach (var parte in limpo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    lista.Add(parte.ToLowerInvariant());
            }
        }
        else
        {
            return Array.Empty<string>();
        }

        var conhecidos = new[] { "admin", "pos" };
        return lista.Where(conhecidos.Contains).Distinct().ToList();
    }

    private static string? LerTexto(JsonElement raiz, string nome) =>
        raiz.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? LerEpoch(JsonElement raiz, string nome)
    {
        if (!raiz.TryGetProperty(nome, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        return v.TryGetInt64(out var segundos)
            ? DateTimeOffset.FromUnixTimeSeconds(segundos)
            : null;
    }

    private static byte[] Base64UrlDecode(string entrada)
    {
        var texto = entrada.Replace('-', '+').Replace('_', '/');
        texto = (texto.Length % 4) switch
        {
            2 => texto + "==",
            3 => texto + "=",
            _ => texto
        };
        return Convert.FromBase64String(texto);
    }
}
