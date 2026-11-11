using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Client.Servicos;

public sealed record ResumoVendas(
    int Finalizadas,
    int Canceladas,
    decimal Bruto,
    decimal Desconto,
    decimal Liquido,
    decimal TicketMedio);

public sealed record LinhaFormaPagamento(FormaPagamento Forma, decimal Total, int Lancamentos);
public sealed record LinhaProduto(string Descricao, decimal Quantidade, decimal Total);

public sealed record LinhaTurno(
    long Numero,
    DateTimeOffset AbertoEm,
    DateTimeOffset? FechadoEm,
    decimal Abertura,
    decimal Calculado,
    decimal? Informado,
    decimal Diferenca);

public sealed record RelatorioPeriodo(
    DateTimeOffset De,
    DateTimeOffset Ate,
    ResumoVendas Resumo,
    IReadOnlyList<LinhaFormaPagamento> Formas,
    IReadOnlyList<LinhaProduto> Produtos,
    decimal Sangrias,
    decimal Suprimentos,
    IReadOnlyList<LinhaTurno> Turnos)
{
    public decimal DinheiroEmGaveta => Math.Round(Sangrias * -1 + Suprimentos, 2);
}

/// <summary>
/// Relatórios por período. Somente leitura: nada aqui altera venda, turno ou estoque.
///
/// As datas são filtradas <b>em memória</b> de propósito. O EF Core sobre SQLite não traduz
/// comparação de <c>DateTimeOffset</c> para SQL (o valor é gravado como texto), e uma tradução
/// parcial silenciosa devolveria número errado — pior do que custo. O recorte por loja vai para
/// o SQL; o recorte por data e as somas acontecem em memória sobre o conjunto já filtrado.
/// Para o volume de uma loja (milhares de vendas por mês) isso é irrelevante; se um dia não
/// for, o caminho é uma coluna de data indexada, não um filtro no SQL que mente.
/// </summary>
public sealed class ServicoDeRelatorios
{
    private readonly LandipayDbContext _db;
    private readonly Seguranca.ServicoDeAutorizacao _autorizacao;

    public ServicoDeRelatorios(LandipayDbContext db, Seguranca.ServicoDeAutorizacao? autorizacao = null)
    {
        _db = db;
        _autorizacao = autorizacao ?? new Seguranca.ServicoDeAutorizacao(db);
    }

    /// <summary>
    /// Gera o relatório do período. Exige <c>relatorio.ler</c>: operador de caixa que chamar
    /// isto recebe <see cref="Seguranca.AcessoNegadoException"/> e a negativa vai para a auditoria,
    /// independentemente do que a tela mostra.
    /// </summary>
    public RelatorioPeriodo Gerar(Guid lojaId, DateTimeOffset de, DateTimeOffset ate, Guid usuarioId)
    {
        _autorizacao.Exigir(usuarioId, Domain.Seguranca.CatalogoDeAcesso.RelatorioLer);
        if (ate < de) (de, ate) = (ate, de);

        var vendas = _db.Vendas
            .Where(v => v.LojaId == lojaId)
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .AsSplitQuery()
            .ToList();

        var noPeriodo = vendas
            .Where(v => v.FinalizadaEm is not null
                        && v.FinalizadaEm.Value >= de
                        && v.FinalizadaEm.Value < ate)
            .ToList();

        var finalizadas = noPeriodo.Where(v => v.Status == StatusVenda.Finalizada).ToList();
        var canceladas = noPeriodo.Where(v => v.Status == StatusVenda.Cancelada).ToList();

        var bruto = Arred(finalizadas.Sum(v => v.Subtotal));
        var desconto = Arred(finalizadas.Sum(v => v.Desconto));
        var liquido = Arred(finalizadas.Sum(v => v.Total));

        var resumo = new ResumoVendas(
            finalizadas.Count,
            canceladas.Count,
            bruto,
            desconto,
            liquido,
            finalizadas.Count == 0 ? 0m : Arred(liquido / finalizadas.Count));

        var formas = finalizadas
            .SelectMany(v => v.Pagamentos)
            .GroupBy(p => p.Forma)
            .Select(g => new LinhaFormaPagamento(g.Key, Arred(g.Sum(p => p.Valor)), g.Count()))
            .OrderByDescending(l => l.Total)
            .ToList();

        var produtos = finalizadas
            .SelectMany(v => v.Itens)
            .GroupBy(i => i.DescricaoSnapshot)
            .Select(g => new LinhaProduto(g.Key, Arred(g.Sum(i => i.Quantidade)), Arred(g.Sum(i => i.Total))))
            .OrderByDescending(l => l.Total)
            .Take(10)
            .ToList();

        // Movimentos de caixa do período (sangria/suprimento), somados pelo momento do movimento.
        var movimentos = _db.MovimentosCaixa
            .Join(_db.CaixaTurnos.Where(t => t.LojaId == lojaId),
                m => m.CaixaTurnoId, t => t.Id, (m, t) => m)
            .ToList()
            .Where(m => m.OcorridoEm >= de && m.OcorridoEm < ate)
            .ToList();

        var sangrias = Arred(movimentos.Where(m => m.Tipo == TipoMovimentoCaixa.Sangria).Sum(m => m.Valor));
        var suprimentos = Arred(movimentos.Where(m => m.Tipo == TipoMovimentoCaixa.Suprimento).Sum(m => m.Valor));

        var turnos = _db.CaixaTurnos
            .Where(t => t.LojaId == lojaId)
            .ToList()
            .Where(t => t.AbertoEm >= de && t.AbertoEm < ate)
            .OrderBy(t => t.Numero)
            .Select(t => new LinhaTurno(
                t.Numero,
                t.AbertoEm,
                t.FechadoEm,
                t.ValorAbertura,
                t.ValorFechamentoCalculado ?? 0m,
                t.ValorFechamentoInformado,
                t.DiferencaConferencia))
            .ToList();

        return new RelatorioPeriodo(de, ate, resumo, formas, produtos, sangrias, suprimentos, turnos);
    }

    private static decimal Arred(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
