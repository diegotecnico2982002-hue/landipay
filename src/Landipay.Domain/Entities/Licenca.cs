using Landipay.Domain.Common;
using Landipay.Domain.Enums;

namespace Landipay.Domain.Entities;

/// <summary>
/// Licença por instalação. <see cref="TokenAssinado"/> é o JWT ES256 emitido pelo backend
/// (Fase 2); aqui ele é tratado como dado opaco — a validação criptográfica acontece no
/// serviço de licença, nunca no domínio.
/// A janela de tolerância offline existe porque a loja não pode parar por falta de rede,
/// mas é curta e monitorada (não é "licença perpétua por omissão").
/// </summary>
public sealed class ConfigLicenca : Entity
{
    public Guid EmpresaId { get; private set; }
    public string InstalacaoId { get; private set; } = string.Empty;
    public string TokenAssinado { get; private set; } = string.Empty;
    public string KeyId { get; private set; } = string.Empty;
    public DateTimeOffset EmitidaEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? UltimaValidacaoEm { get; private set; }
    public StatusLicenca Status { get; private set; } = StatusLicenca.Ativa;
    public int ToleranciaOfflineDias { get; private set; } = 7;

    /// <summary>
    /// Escopo de aplicativos que a licença habilita, em CSV ("admin,pos").
    /// Vazio = licença emitida antes do claim <c>apps</c> → tratada como "ambos".
    /// É o <b>teto</b> da instalação: `apps.json` nunca pode habilitar além disto.
    /// </summary>
    public string AppsHabilitados { get; private set; } = string.Empty;

    /// <summary>Escopo efetivo da licença. Licença sem o claim libera os dois apps.</summary>
    public IReadOnlyList<string> Apps() =>
        string.IsNullOrWhiteSpace(AppsHabilitados)
            ? new[] { "admin", "pos" }
            : AppsHabilitados.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool Permite(string app) => Apps().Contains(app, StringComparer.OrdinalIgnoreCase);

    private ConfigLicenca() { }

    public static ConfigLicenca Registrar(Guid empresaId, string instalacaoId, string token, string keyId,
        DateTimeOffset emitidaEm, DateTimeOffset expiraEm, int toleranciaOfflineDias = 7,
        IEnumerable<string>? apps = null)
    {
        if (empresaId == Guid.Empty) throw new DomainException("Empresa é obrigatória.");
        if (string.IsNullOrWhiteSpace(instalacaoId)) throw new DomainException("Identificador da instalação é obrigatório.");
        if (string.IsNullOrWhiteSpace(token)) throw new DomainException("Token da licença é obrigatório.");
        if (expiraEm <= emitidaEm) throw new DomainException("Expiração deve ser posterior à emissão.");
        if (toleranciaOfflineDias < 0) throw new DomainException("Tolerância offline não pode ser negativa.");

        return new ConfigLicenca
        {
            EmpresaId = empresaId,
            InstalacaoId = instalacaoId.Trim(),
            TokenAssinado = token,
            KeyId = keyId,
            EmitidaEm = emitidaEm,
            ExpiraEm = expiraEm,
            ToleranciaOfflineDias = toleranciaOfflineDias,
            AppsHabilitados = JuntarApps(apps)
        };
    }

    private static string JuntarApps(IEnumerable<string>? apps)
    {
        if (apps is null) return string.Empty;

        var limpo = apps.Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        return string.Join(",", limpo);
    }

    /// <summary>Atualiza o escopo quando a licença é renovada com um vetor diferente.</summary>
    public void DefinirApps(IEnumerable<string>? apps) => AppsHabilitados = JuntarApps(apps);

    public void RegistrarValidacao(DateTimeOffset agora, DateTimeOffset? novaExpiracao = null, string? novoToken = null, string? novoKeyId = null)
    {
        UltimaValidacaoEm = agora;
        if (novaExpiracao is not null && novaExpiracao > ExpiraEm)
        {
            ExpiraEm = novaExpiracao.Value;
            if (!string.IsNullOrWhiteSpace(novoToken)) TokenAssinado = novoToken;
            if (!string.IsNullOrWhiteSpace(novoKeyId)) KeyId = novoKeyId;
            Status = StatusLicenca.Ativa;
        }
        AtualizarStatus(agora);
    }

    public void Revogar() => Status = StatusLicenca.Revogada;

    /// <summary>
    /// Recalcula o status (Ativa / EmTolerancia / Expirada) sem tocar em ExpiraEm, token ou
    /// última validação. É o que o serviço de licença chama a cada abertura e a cada operação:
    /// o status passa a refletir o relógio, não o momento da instalação.
    /// </summary>
    public StatusLicenca Reavaliar(DateTimeOffset agora)
    {
        AtualizarStatus(agora);
        return Status;
    }

    /// <summary>
    /// Licença utilizável se não revogada e (não expirou OU está dentro da janela de tolerância
    /// contada a partir da última validação online bem-sucedida).
    /// </summary>
    public bool EstaUtilizavel(DateTimeOffset agora)
    {
        if (Status == StatusLicenca.Revogada) return false;
        if (agora <= ExpiraEm) return true;

        if (UltimaValidacaoEm is null) return false;
        return agora <= UltimaValidacaoEm.Value.AddDays(ToleranciaOfflineDias);
    }

    private void AtualizarStatus(DateTimeOffset agora)
    {
        if (Status == StatusLicenca.Revogada) return;
        if (agora <= ExpiraEm) { Status = StatusLicenca.Ativa; return; }
        Status = EstaUtilizavel(agora) ? StatusLicenca.EmTolerancia : StatusLicenca.Expirada;
    }
}
