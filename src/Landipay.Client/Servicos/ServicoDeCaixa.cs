using System.Text.Json;
using Landipay.Domain.Common;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Client.Servicos;

public sealed record ResumoFechamento(
    int QuantidadeVendas,
    decimal TotalVendas,
    IReadOnlyDictionary<FormaPagamento, decimal> PorForma,
    decimal Sangrias,
    decimal Suprimentos,
    decimal ValorAbertura,
    decimal ValorCalculado,
    decimal ValorInformado,
    decimal Diferenca);

/// <summary>
/// Operação de caixa. Tudo que altera dinheiro ou estoque passa por aqui, numa única
/// transação por operação — e cada operação sensível deixa rastro em <c>LogAuditoria</c>
/// e entra no <c>Outbox</c> quando precisa chegar à nuvem.
/// </summary>
public sealed class ServicoDeCaixa
{
    private readonly LandipayDbContext _db;
    private readonly TimeProvider _relogio;
    private readonly Seguranca.ServicoDeAutorizacao _autorizacao;

    public ServicoDeCaixa(LandipayDbContext db, TimeProvider? relogio = null,
        Seguranca.ServicoDeAutorizacao? autorizacao = null)
    {
        _db = db;
        _relogio = relogio ?? TimeProvider.System;
        // Autorização SEMPRE presente: sem ela nenhuma operação de caixa acontece.
        _autorizacao = autorizacao ?? new Seguranca.ServicoDeAutorizacao(db, _relogio);
    }

    public CaixaTurno AbrirTurno(Guid lojaId, Guid usuarioId, decimal valorAbertura)
    {
        _autorizacao.Exigir(usuarioId, Domain.Seguranca.CatalogoDeAcesso.CaixaAbrir);
        var agora = _relogio.GetUtcNow();

        var aberto = _db.CaixaTurnos.FirstOrDefault(t => t.LojaId == lojaId && t.Status == StatusTurno.Aberto);
        if (aberto is not null)
            throw new DomainException($"Já existe turno aberto (#{aberto.Numero}) nesta loja.");

        var proximo = (_db.CaixaTurnos.Where(t => t.LojaId == lojaId).Max(t => (long?)t.Numero) ?? 0) + 1;
        var turno = CaixaTurno.Abrir(lojaId, proximo, usuarioId, valorAbertura, agora);

        _db.CaixaTurnos.Add(turno);
        _db.Auditoria.Add(LogAuditoria.Registrar("caixa.aberto", "CaixaTurno", usuarioId, lojaId: lojaId,
            entidadeId: turno.Id.ToString(), dadosJson: $"{{\"valor\":{valorAbertura:0.00}}}",
            origem: OrigemLog.Operador, quando: agora));
        _db.SaveChanges();

        return turno;
    }

    public Venda IniciarVenda(CaixaTurno turno, Guid usuarioId)
    {
        _autorizacao.Exigir(usuarioId, Domain.Seguranca.CatalogoDeAcesso.VendaCriar);
        if (turno.Status != StatusTurno.Aberto)
            throw new DomainException("Turno fechado não inicia venda.");

        var proximo = (_db.Vendas.Where(v => v.LojaId == turno.LojaId).Max(v => (long?)v.Numero) ?? 0) + 1;
        var venda = Venda.Abrir(turno.LojaId, turno.Id, usuarioId, proximo);

        _db.Vendas.Add(venda);
        return venda;
    }

    /// <summary>
    /// Finaliza a venda: valida pagamento, baixa estoque (respeitando saldo), grava a
    /// fila de sincronização e audita — tudo na mesma transação.
    /// </summary>
    public void FinalizarVenda(Venda venda)
    {
        _autorizacao.Exigir(venda.UsuarioId, Domain.Seguranca.CatalogoDeAcesso.VendaCriar);
        var agora = _relogio.GetUtcNow();

        venda.Finalizar(agora);

        foreach (var item in venda.Itens)
        {
            var produto = _db.Produtos.Single(p => p.Id == item.ProdutoId);
            if (!produto.ControlaEstoque) continue;

            var saldo = CalcularSaldo(venda.LojaId, produto.Id);
            if (saldo < item.Quantidade)
                throw new DomainException(
                    $"Estoque insuficiente para '{produto.Nome}': disponível {saldo:0.###}, pedido {item.Quantidade:0.###}.");

            _db.EstoqueMovimentos.Add(EstoqueMovimento.Criar(
                venda.LojaId, produto.Id, TipoMovimentoEstoque.Saida, item.Quantidade,
                saldo - item.Quantidade, venda.UsuarioId, agora, $"Venda #{venda.Numero}", venda.Id));
        }

        var payload = JsonSerializer.Serialize(new
        {
            venda.Id,
            venda.Numero,
            venda.LojaId,
            Total = venda.Total,
            Itens = venda.Itens.Select(i => new { i.ProdutoId, i.Quantidade, i.PrecoUnitario, i.Total }),
            Pagamentos = venda.Pagamentos.Select(p => new { Forma = p.Forma.ToString(), p.Valor })
        });

        _db.Outbox.Add(OutboxMensagem.Criar(venda.LojaId, TipoMensagemOutbox.VendaFinalizada, payload));
        _db.Auditoria.Add(LogAuditoria.Registrar("venda.finalizada", "Venda", venda.UsuarioId,
            lojaId: venda.LojaId, entidadeId: venda.Id.ToString(),
            dadosJson: $"{{\"numero\":{venda.Numero},\"total\":{venda.Total.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}",
            origem: OrigemLog.Operador, quando: agora));

        _db.SaveChanges();
    }

    public MovimentoCaixa RegistrarSangria(CaixaTurno turno, decimal valor, string motivo, Guid usuarioId) =>
        RegistrarMovimento(turno, TipoMovimentoCaixa.Sangria, valor, motivo, usuarioId);

    public MovimentoCaixa RegistrarSuprimento(CaixaTurno turno, decimal valor, string motivo, Guid usuarioId) =>
        RegistrarMovimento(turno, TipoMovimentoCaixa.Suprimento, valor, motivo, usuarioId);

    private MovimentoCaixa RegistrarMovimento(CaixaTurno turno, TipoMovimentoCaixa tipo, decimal valor,
        string motivo, Guid usuarioId)
    {
        _autorizacao.Exigir(usuarioId, Domain.Seguranca.CatalogoDeAcesso.CaixaMovimentar);
        var agora = _relogio.GetUtcNow();
        var movimento = turno.RegistrarMovimento(tipo, valor, motivo, usuarioId, agora);

        _db.MovimentosCaixa.Add(movimento);
        _db.Auditoria.Add(LogAuditoria.Registrar($"caixa.{tipo.ToString().ToLowerInvariant()}", "CaixaTurno",
            usuarioId, lojaId: turno.LojaId, entidadeId: turno.Id.ToString(),
            dadosJson: $"{{\"valor\":{valor:0.00},\"motivo\":\"{motivo.Replace("\"", "'")}\"}}",
            origem: OrigemLog.Operador, quando: agora));
        _db.SaveChanges();

        return movimento;
    }

    public ResumoFechamento FecharTurno(CaixaTurno turno, Guid usuarioId, decimal valorInformado)
    {
        _autorizacao.Exigir(usuarioId, Domain.Seguranca.CatalogoDeAcesso.CaixaFechar);
        var agora = _relogio.GetUtcNow();

        var vendas = _db.Vendas
            .Include(v => v.Itens)
            .Where(v => v.CaixaTurnoId == turno.Id && v.Status == StatusVenda.Finalizada)
            .ToList();

        var ids = vendas.Select(v => v.Id).ToList();
        var pagamentos = _db.Pagamentos.Where(p => ids.Contains(p.VendaId)).ToList();

        // Agregação em memória: SQLite guarda decimal como TEXT (ver docs/01, limitação 1).
        var porForma = pagamentos
            .GroupBy(p => p.Forma)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Valor));

        // O que entra na gaveta em dinheiro é o valor pago MENOS o troco devolvido.
        // (Sem isso, uma venda de 37,80 paga com 40 inflaria o caixa em 2,20.)
        var dinheiroNaGaveta = 0m;
        foreach (var venda in vendas)
        {
            var pagamentosDaVenda = pagamentos.Where(p => p.VendaId == venda.Id).ToList();
            var emDinheiro = pagamentosDaVenda.Where(p => p.Forma == FormaPagamento.Dinheiro).Sum(p => p.Valor);
            if (emDinheiro <= 0) continue;

            var totalPago = pagamentosDaVenda.Sum(p => p.Valor);
            var troco = Math.Max(0m, totalPago - venda.Total);
            dinheiroNaGaveta += emDinheiro - Math.Min(troco, emDinheiro);
        }

        var calculado = Math.Round(turno.ValorAbertura + dinheiroNaGaveta - turno.TotalSangrias + turno.TotalSuprimentos,
            2, MidpointRounding.AwayFromZero);

        turno.Fechar(usuarioId, valorInformado, calculado, agora);

        _db.Auditoria.Add(LogAuditoria.Registrar("caixa.fechado", "CaixaTurno", usuarioId,
            lojaId: turno.LojaId, entidadeId: turno.Id.ToString(),
            dadosJson: $"{{\"calculado\":{calculado.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"informado\":{valorInformado.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}",
            origem: OrigemLog.Operador, quando: agora));
        _db.SaveChanges();

        return new ResumoFechamento(
            vendas.Count,
            vendas.Sum(v => v.Total),
            porForma,
            turno.TotalSangrias,
            turno.TotalSuprimentos,
            turno.ValorAbertura,
            calculado,
            valorInformado,
            Math.Round(valorInformado - calculado, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Saldo atual do produto na loja, somando os movimentos em memória.
    /// (Limitação conhecida: em SQLite, somar decimal no SQL não é confiável —
    /// a tabela de saldos materializada entra na Fase 5.)
    /// </summary>
    public decimal CalcularSaldo(Guid lojaId, Guid produtoId)
    {
        var movimentos = _db.EstoqueMovimentos
            .Where(m => m.LojaId == lojaId && m.ProdutoId == produtoId)
            .Select(m => new { m.Tipo, m.Quantidade })
            .ToList();

        var saldo = 0m;
        foreach (var m in movimentos)
        {
            saldo += m.Tipo switch
            {
                TipoMovimentoEstoque.Entrada => m.Quantidade,
                TipoMovimentoEstoque.Devolucao => m.Quantidade,
                TipoMovimentoEstoque.Ajuste => m.Quantidade,
                _ => -m.Quantidade
            };
        }

        return saldo;
    }
}
