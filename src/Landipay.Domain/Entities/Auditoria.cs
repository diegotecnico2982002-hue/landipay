using Landipay.Domain.Enums;

namespace Landipay.Domain.Entities;

/// <summary>
/// Trilha de auditoria. <b>Append-only</b>: a tabela tem triggers que abortam UPDATE e DELETE
/// (criados na migration). Não herda de Entity porque usa <see cref="long"/> sequencial local,
/// que dá ordem cronológica estável dentro da loja mesmo com relógio ajustado.
/// </summary>
public sealed class LogAuditoria
{
    public long Id { get; private set; }
    public Guid? EmpresaId { get; private set; }
    public Guid? LojaId { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string Acao { get; private set; } = string.Empty;
    public string Entidade { get; private set; } = string.Empty;
    public string? EntidadeId { get; private set; }
    public string? DadosJson { get; private set; }
    public OrigemLog Origem { get; private set; } = OrigemLog.Sistema;
    public DateTimeOffset OcorridoEm { get; private set; }
    public string? Maquina { get; private set; }

    private LogAuditoria() { }

    public static LogAuditoria Registrar(string acao, string entidade, Guid? usuarioId = null,
        Guid? empresaId = null, Guid? lojaId = null, string? entidadeId = null, string? dadosJson = null,
        OrigemLog origem = OrigemLog.Sistema, DateTimeOffset? quando = null, string? maquina = null)
    {
        if (string.IsNullOrWhiteSpace(acao)) throw new Common.DomainException("Ação é obrigatória.");
        if (string.IsNullOrWhiteSpace(entidade)) throw new Common.DomainException("Entidade é obrigatória.");

        return new LogAuditoria
        {
            Acao = acao.Trim(),
            Entidade = entidade.Trim(),
            UsuarioId = usuarioId,
            EmpresaId = empresaId,
            LojaId = lojaId,
            EntidadeId = entidadeId,
            DadosJson = dadosJson,
            Origem = origem,
            OcorridoEm = quando ?? DateTimeOffset.UtcNow,
            Maquina = maquina
        };
    }
}

/// <summary>
/// Outbox transacional: cada evento de negócio grava aqui na mesma transação da venda.
/// A sincronização com a nuvem lê a fila — o caixa nunca depende de rede para fechar venda.
/// </summary>
public sealed class OutboxMensagem : Common.Entity
{
    public Guid LojaId { get; private set; }
    public TipoMensagemOutbox Tipo { get; private set; }
    public string PayloadJson { get; private set; } = "{}";
    public DateTimeOffset? EnviadoEm { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }

    private OutboxMensagem() { }

    public static OutboxMensagem Criar(Guid lojaId, TipoMensagemOutbox tipo, string payloadJson)
    {
        if (lojaId == Guid.Empty) throw new Common.DomainException("Loja é obrigatória.");
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new Common.DomainException("Payload é obrigatório.");

        return new OutboxMensagem { LojaId = lojaId, Tipo = tipo, PayloadJson = payloadJson };
    }

    public void MarcarEnviada(DateTimeOffset quando) { EnviadoEm = quando; UltimoErro = null; }

    public void MarcarFalha(string erro)
    {
        Tentativas++;
        UltimoErro = (erro ?? string.Empty).Length > 500 ? erro![..500] : erro;
    }
}
