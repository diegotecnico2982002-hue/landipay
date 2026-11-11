namespace Landipay.Backend.Dados;

public enum StatusSessaoSuporte
{
    AguardandoConsentimento = 1,
    Ativa = 2,
    Encerrada = 3,
    Expirada = 4
}

public sealed class LicencaRegistro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public string InstalacaoId { get; set; } = string.Empty;
    public string Plano { get; set; } = "padrao";
    public DateTimeOffset ExpiraEm { get; set; }
    public bool Revogada { get; set; }
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ReleaseRegistro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Versao { get; set; } = string.Empty;
    public string Canal { get; set; } = "stable";
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Tamanho { get; set; }
    public string VersaoMinima { get; set; } = "0.0.0";
    public DateTimeOffset PublicadoEm { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Sessão de suporte remoto. Regras que eliminam A1 e que estão no <b>servidor</b>,
/// não na boa vontade do cliente:
/// código de uso único, hash do código (nunca o código em claro), TTL curto,
/// consentimento explícito registrado e encerramento auditável.
/// </summary>
public sealed class SessaoSuporteRegistro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public Guid LojaId { get; set; }
    public string Tecnico { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public string Escopo { get; set; } = "somente_leitura";
    public string CodigoHash { get; set; } = string.Empty;
    public bool CodigoUsado { get; set; }
    public string? CredencialHash { get; set; }
    public int TtlMinutos { get; set; } = 30;
    public StatusSessaoSuporte Status { get; set; } = StatusSessaoSuporte.AguardandoConsentimento;
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? ConsentidaEm { get; set; }
    public DateTimeOffset? EncerradaEm { get; set; }
    public string? EncerradaPor { get; set; }
}

/// <summary>Trilha do backend. Append-only por trigger na migration (mesmo padrão do cliente).</summary>
public sealed class EventoAuditoriaApi
{
    public long Id { get; set; }
    public string Acao { get; set; } = string.Empty;
    public string Detalhe { get; set; } = string.Empty;
    public string? Ator { get; set; }
    public DateTimeOffset OcorridoEm { get; set; } = DateTimeOffset.UtcNow;
}
