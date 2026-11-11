using Landipay.Domain.Common;
using Landipay.Domain.Enums;

namespace Landipay.Domain.Entities;

/// <summary>
/// Turno de caixa: abre com valor inicial, recebe sangrias/suprimentos e fecha com conferência.
/// A conferência grava o valor <b>calculado</b> e o <b>informado</b> — a diferença não é corrigida,
/// é registrada (é justamente o que a auditoria precisa enxergar).
/// </summary>
public sealed class CaixaTurno : Entity
{
    private readonly List<MovimentoCaixa> _movimentos = new();

    public Guid LojaId { get; private set; }
    public long Numero { get; private set; }
    public Guid AbertoPorUsuarioId { get; private set; }
    public DateTimeOffset AbertoEm { get; private set; }
    public decimal ValorAbertura { get; private set; }
    public StatusTurno Status { get; private set; } = StatusTurno.Aberto;
    public Guid? FechadoPorUsuarioId { get; private set; }
    public DateTimeOffset? FechadoEm { get; private set; }
    public decimal? ValorFechamentoInformado { get; private set; }
    public decimal? ValorFechamentoCalculado { get; private set; }

    public IReadOnlyCollection<MovimentoCaixa> Movimentos => _movimentos.AsReadOnly();

    public decimal TotalSangrias => _movimentos.Where(m => m.Tipo == TipoMovimentoCaixa.Sangria).Sum(m => m.Valor);
    public decimal TotalSuprimentos => _movimentos.Where(m => m.Tipo == TipoMovimentoCaixa.Suprimento).Sum(m => m.Valor);
    public decimal DiferencaConferencia =>
        ValorFechamentoInformado is null || ValorFechamentoCalculado is null
            ? 0m
            : Math.Round(ValorFechamentoInformado.Value - ValorFechamentoCalculado.Value, 2, MidpointRounding.AwayFromZero);

    private CaixaTurno() { }

    public static CaixaTurno Abrir(Guid lojaId, long numero, Guid usuarioId, decimal valorAbertura, DateTimeOffset agora)
    {
        if (lojaId == Guid.Empty) throw new DomainException("Loja é obrigatória.");
        if (usuarioId == Guid.Empty) throw new DomainException("Operador é obrigatório.");
        if (numero <= 0) throw new DomainException("Número do turno deve ser positivo.");
        if (valorAbertura < 0) throw new DomainException("Valor de abertura não pode ser negativo.");

        return new CaixaTurno
        {
            LojaId = lojaId,
            Numero = numero,
            AbertoPorUsuarioId = usuarioId,
            AbertoEm = agora,
            ValorAbertura = Math.Round(valorAbertura, 2, MidpointRounding.AwayFromZero)
        };
    }

    public MovimentoCaixa RegistrarMovimento(TipoMovimentoCaixa tipo, decimal valor, string motivo,
        Guid usuarioId, DateTimeOffset agora)
    {
        if (Status != StatusTurno.Aberto) throw new DomainException("Turno fechado não aceita movimentos.");
        if (valor <= 0) throw new DomainException("Valor do movimento deve ser positivo.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new DomainException("Motivo do movimento é obrigatório.");

        var movimento = new MovimentoCaixa(Id, tipo, valor, motivo, usuarioId, agora);
        _movimentos.Add(movimento);
        return movimento;
    }

    public void Fechar(Guid usuarioId, decimal valorInformado, decimal valorCalculado, DateTimeOffset agora)
    {
        if (Status != StatusTurno.Aberto) throw new DomainException("Turno já está fechado.");
        if (valorInformado < 0 || valorCalculado < 0)
            throw new DomainException("Valores de fechamento não podem ser negativos.");

        Status = StatusTurno.Fechado;
        FechadoPorUsuarioId = usuarioId;
        FechadoEm = agora;
        ValorFechamentoInformado = Math.Round(valorInformado, 2, MidpointRounding.AwayFromZero);
        ValorFechamentoCalculado = Math.Round(valorCalculado, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed class MovimentoCaixa : Entity
{
    public Guid CaixaTurnoId { get; private set; }
    public TipoMovimentoCaixa Tipo { get; private set; }
    public decimal Valor { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public Guid UsuarioId { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    private MovimentoCaixa() { }

    internal MovimentoCaixa(Guid caixaTurnoId, TipoMovimentoCaixa tipo, decimal valor, string motivo,
        Guid usuarioId, DateTimeOffset ocorridoEm)
    {
        CaixaTurnoId = caixaTurnoId;
        Tipo = tipo;
        Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        Motivo = motivo.Trim();
        UsuarioId = usuarioId;
        OcorridoEm = ocorridoEm;
    }
}
