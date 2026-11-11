using System.Text.Json.Serialization;

namespace Landipay.Updates;

/// <summary>
/// Manifesto de release. É o único documento que autoriza o cliente a baixar e executar
/// um pacote: contém a versão, a URL, o tamanho e o SHA-256 do artefato. A assinatura
/// (Ed25519) cobre este objeto serializado de forma canônica.
/// </summary>
public sealed record ManifestoRelease
{
    public required string Versao { get; init; }
    public required string Canal { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required long Tamanho { get; init; }
    public required string VersaoMinima { get; init; }
    public required DateTimeOffset PublicadoEm { get; init; }

    /// <summary>Assinatura Ed25519 em base64. Não entra no payload assinado.</summary>
    [JsonIgnore]
    public string? Assinatura { get; init; }

    /// <summary>Identificador da chave usada (rotação). Não entra no payload assinado.</summary>
    [JsonIgnore]
    public string? KeyId { get; init; }
}

/// <summary>
/// Serialização canônica: ordem de propriedade fixa (definida pelo record), sem espaços,
/// cultura invariante, datas em ISO-8601 com "O". Assinador e validador usam exatamente
/// esta função — se um lado mudar, o outro detecta na hora (assinatura inválida).
/// </summary>
public static class JsonCanonico
{
    private static readonly System.Text.Json.JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static byte[] Serializar(ManifestoRelease manifesto) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifesto, Opcoes);

    public static string SerializarTexto(ManifestoRelease manifesto) =>
        System.Text.Json.JsonSerializer.Serialize(manifesto, Opcoes);
}
