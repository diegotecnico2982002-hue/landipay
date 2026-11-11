namespace Landipay.Updates;

public sealed record ResultadoValidacao(bool Valido, string Codigo, string Descricao)
{
    public static ResultadoValidacao Ok(string descricao = "Manifesto e artefato validos.")
        => new(true, "ok", descricao);

    public static ResultadoValidacao Falha(string codigo, string descricao)
        => new(false, codigo, descricao);
}

/// <summary>
/// Validador do lado do cliente. A ordem das verificações é deliberada:
/// 1) forma (URL, hash, tamanho) — barato, rejeita lixo antes de criptografia;
/// 2) assinatura Ed25519 — prova de origem;
/// 3) regras de versão — anti-downgrade;
/// 4) hash do artefato baixado — prova de integridade do que chegou no disco.
///
/// Nenhuma exceção escapa: entrada inválida vira <see cref="ResultadoValidacao.Falha"/>.
/// </summary>
public static class ValidadorDeManifesto
{
    public const string CodigoAssinaturaInvalida = "assinatura_invalida";
    public const string CodigoHashDivergente = "hash_divergente";
    public const string CodigoDowngrade = "downgrade_bloqueado";

    public static ResultadoValidacao Validar(
        ManifestoRelease? manifesto,
        string? assinaturaBase64,
        byte[]? chavePublica,
        string? versaoInstalada = null)
    {
        if (manifesto is null)
            return ResultadoValidacao.Falha("manifesto_ausente", "Nenhum manifesto recebido.");

        if (string.IsNullOrWhiteSpace(assinaturaBase64))
            return ResultadoValidacao.Falha("assinatura_ausente", "Manifesto sem assinatura.");
        if (string.IsNullOrWhiteSpace(manifesto.KeyId))
            return ResultadoValidacao.Falha("kid_ausente", "Manifesto sem identificador de chave.");

        if (string.IsNullOrWhiteSpace(manifesto.Versao))
            return ResultadoValidacao.Falha("versao_invalida", "Versao ausente.");
        if (!EhHttps(manifesto.Url))
            return ResultadoValidacao.Falha("url_insegura", "A URL do artefato precisa ser HTTPS.");
        if (!EhSha256(manifesto.Sha256))
            return ResultadoValidacao.Falha("hash_malformado", "SHA-256 deve ter 64 caracteres hexadecimais.");
        if (manifesto.Tamanho <= 0)
            return ResultadoValidacao.Falha("tamanho_invalido", "Tamanho do artefato deve ser positivo.");

        // 2) Assinatura sobre o JSON canônico.
        var payload = JsonCanonico.Serializar(manifesto);
        if (chavePublica is null || !AssinaturaEd25519.Verificar(payload, assinaturaBase64, chavePublica))
            return ResultadoValidacao.Falha(CodigoAssinaturaInvalida,
                "Assinatura Ed25519 nao confere com a chave pinada.");

        // 3) Regras de versão.
        if (!TentarVersao(manifesto.Versao, out var versao))
            return ResultadoValidacao.Falha("versao_invalida", $"Versao '{manifesto.Versao}' nao e semantica.");
        if (!TentarVersao(manifesto.VersaoMinima, out var minima))
            return ResultadoValidacao.Falha("versao_invalida", $"VersaoMinima '{manifesto.VersaoMinima}' nao e semantica.");

        if (versao < minima)
            return ResultadoValidacao.Falha("manifesto_inconsistente",
                $"Manifesto declara {versao} abaixo da versao minima {minima}.");

        if (!string.IsNullOrWhiteSpace(versaoInstalada) && TentarVersao(versaoInstalada, out var instalada))
        {
            if (instalada > versao)
                return ResultadoValidacao.Falha(CodigoDowngrade,
                    $"Downgrade bloqueado: instalada {instalada} > disponivel {versao}.");
        }

        return ResultadoValidacao.Ok();
    }

    /// <summary>
    /// Confere o SHA-256 do arquivo efetivamente baixado contra o hash assinado.
    /// Deve ser chamado em streaming, durante o download, e novamente ao final.
    /// </summary>
    public static ResultadoValidacao ValidarArtefato(byte[] conteudo, string sha256Esperado)
    {
        if (conteudo is null || conteudo.Length == 0)
            return ResultadoValidacao.Falha("artefato_vazio", "Artefato baixado esta vazio.");
        if (!EhSha256(sha256Esperado))
            return ResultadoValidacao.Falha("hash_malformado", "SHA-256 esperado esta malformado.");

        var calculado = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(conteudo)).ToLowerInvariant();
        return string.Equals(calculado, sha256Esperado.ToLowerInvariant(), StringComparison.Ordinal)
            ? ResultadoValidacao.Ok("Hash do artefato confere.")
            : ResultadoValidacao.Falha(CodigoHashDivergente,
                $"Hash divergente. esperado={sha256Esperado} calculado={calculado}");
    }

    public static string CalcularSha256(byte[] conteudo) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(conteudo)).ToLowerInvariant();

    private static bool EhHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool EhSha256(string? hash) =>
        !string.IsNullOrWhiteSpace(hash) && hash.Length == 64 && hash.All(Uri.IsHexDigit);

    private static bool TentarVersao(string? texto, out Version versao)
    {
        versao = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var limpo = new string(texto.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');
        if (limpo.Count(c => c == '.') < 1) limpo += ".0";
        return Version.TryParse(limpo, out versao!);
    }
}
