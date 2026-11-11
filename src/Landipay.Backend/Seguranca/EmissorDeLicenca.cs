using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Landipay.Backend.Seguranca;

/// <summary>
/// Emissão e validação de licença como JWT <b>ES256</b> (ECDSA P-256), com <c>kid</c> no
/// cabeçalho para permitir rotação sem quebrar clientes antigos.
/// A licença é autocontida: o cliente valida a assinatura offline com a chave pública
/// pinada, e o servidor só é necessário para renovar.
/// </summary>
public sealed class EmissorDeLicenca
{
    public const string Emissor = "landipay-api";
    public const string Audiencia = "landipay-client";
    public static readonly TimeSpan ValidadePadrao = TimeSpan.FromDays(365);

    /// <summary>Aplicativos habilitados quando o emissor não recebe escopo explícito.</summary>
    public static readonly IReadOnlyList<string> AppsPadrao = new[] { "admin", "pos" };

    public static readonly IReadOnlyList<string> AppsConhecidos = new[] { "admin", "pos" };

    private readonly ProvedorDeChaves _chaves;

    public EmissorDeLicenca(ProvedorDeChaves chaves) => _chaves = chaves;

    public (string Token, DateTimeOffset ExpiraEm, string Kid) Emitir(
        Guid empresaId, string instalacaoId, string plano, TimeSpan? validade = null,
        IReadOnlyCollection<string>? apps = null)
    {
        if (empresaId == Guid.Empty) throw new ArgumentException("Empresa é obrigatória.", nameof(empresaId));
        if (string.IsNullOrWhiteSpace(instalacaoId)) throw new ArgumentException("Instalação é obrigatória.", nameof(instalacaoId));

        // Escopo de aplicativos: a licença é o teto do que a instalação pode habilitar.
        // Token antigo sem o claim continua valendo (tratado como "ambos" no validador) —
        // mas licença nova sempre carrega o vetor, mesmo que seja o padrão.
        var escopo = NormalizarApps(apps);

        var agora = DateTimeOffset.UtcNow;
        var expira = agora.Add(validade ?? ValidadePadrao);

        // Validade não positiva (teste de expiração, relógio adiantado): nbf não pode ser
        // maior que exp, senão o construtor do JWT recusa. Usa -1 min para manter o token íntegro.
        var naoAntes = agora <= expira ? agora : expira.AddMinutes(-1);

        var chave = new ECDsaSecurityKey(_chaves.ChaveLicenca) { KeyId = _chaves.KidLicenca };
        var credenciais = new SigningCredentials(chave, SecurityAlgorithms.EcdsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, instalacaoId),
            new Claim("emp", empresaId.ToString()),
            new Claim("plano", plano),
            new Claim("apps", JsonSerializer.Serialize(escopo)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: Emissor,
            audience: Audiencia,
            claims: claims,
            notBefore: naoAntes.UtcDateTime,
            expires: expira.UtcDateTime,
            signingCredentials: credenciais);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira, _chaves.KidLicenca);
    }

    /// <summary>
    /// Normaliza o escopo: minúsculo, sem duplicata e só valores conhecidos.
    /// Escopo vazio ou com código desconhecido é erro de emissão — não se emite licença
    /// que não habilita nada, nem licença com app que o produto não conhece.
    /// </summary>
    public static IReadOnlyList<string> NormalizarApps(IReadOnlyCollection<string>? apps)
    {
        if (apps is null || apps.Count == 0) return AppsPadrao;

        var limpo = apps
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (limpo.Count == 0) return AppsPadrao;

        var invalido = limpo.FirstOrDefault(a => !AppsConhecidos.Contains(a));
        if (invalido is not null)
            throw new ArgumentException($"Aplicativo desconhecido na licença: '{invalido}'.", nameof(apps));

        return limpo;
    }

    /// <summary>Validação server-side (o cliente valida offline com a chave pública).</summary>
    public ClaimsPrincipal? Validar(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parametros = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Emissor,
            ValidateAudience = true,
            ValidAudience = Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(_chaves.ChaveLicenca) { KeyId = _chaves.KidLicenca },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(60),
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 }
        };

        try
        {
            // Sem mapeamento de claims: o cliente lê "sub"/"emp"/"plano" literalmente,
            // como estão no token, sem a tradução para ClaimTypes.* do .NET.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, parametros, out _);
            return principal;
        }
        catch (SecurityTokenException) { return null; }
        catch (ArgumentException) { return null; }
    }
}
