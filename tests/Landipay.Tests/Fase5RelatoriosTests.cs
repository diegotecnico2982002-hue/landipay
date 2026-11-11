using Landipay.Client.Servicos;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;
using Xunit;

namespace Landipay.Tests;

/// <summary>
/// Fase 5 — relatórios por período. Cada teste fixa um número esperado, somando à mão:
/// relatório que "parece certo" mas soma errado é pior que relatório ausente.
/// </summary>
public sealed class Fase5RelatoriosTests
{
    private static readonly DateTimeOffset Base = new(2026, 11, 10, 12, 0, 0, TimeSpan.FromHours(-3));

    private static Venda Vender(LandipayDbContext db, Loja loja, CaixaTurno turno, Usuario usuario,
        Produto produto, long numero, decimal quantidade, FormaPagamento forma, DateTimeOffset quando,
        bool cancelar = false)
    {
        var venda = Venda.Abrir(loja.Id, turno.Id, usuario.Id, numero);
        venda.AdicionarItem(produto, quantidade);
        venda.RegistrarPagamento(forma, venda.Total, null, quando);

        if (cancelar) venda.Cancelar("cliente desistiu da compra", quando);
        else venda.Finalizar(quando);

        db.Vendas.Add(venda);
        db.SaveChanges();
        return venda;
    }

    /// <summary>Cenário: 2 vendas boas, 1 cancelada e 1 fora do período.</summary>
    private static (Loja Loja, CaixaTurno Turno, Usuario Usuario, Produto Produto) Cenario(LandipayDbContext db,
        BancoDeTeste banco)
    {
        var (_, loja, usuario, produto, turno) = banco.Semear(db, Base);

        Vender(db, loja, turno, usuario, produto, 1, 2m, FormaPagamento.Dinheiro, Base.AddHours(1));      // 37,80
        Vender(db, loja, turno, usuario, produto, 2, 1m, FormaPagamento.Pix, Base.AddHours(2));           // 18,90
        Vender(db, loja, turno, usuario, produto, 3, 1m, FormaPagamento.Credito, Base.AddHours(3), cancelar: true);
        Vender(db, loja, turno, usuario, produto, 4, 5m, FormaPagamento.Dinheiro, Base.AddDays(-5));      // fora do período

        // Sangria e suprimento dentro do período (30 e 50).
        db.MovimentosCaixa.Add(turno.RegistrarMovimento(TipoMovimentoCaixa.Sangria, 30m, "troco para o banco",
            usuario.Id, Base.AddHours(4)));
        db.MovimentosCaixa.Add(turno.RegistrarMovimento(TipoMovimentoCaixa.Suprimento, 50m, "reforço de troco",
            usuario.Id, Base.AddHours(5)));
        db.SaveChanges();

        return (loja, turno, usuario, produto);
    }

    [Fact]
    public void Relatorio_soma_apenas_vendas_finalizadas_do_periodo()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (loja, _, usuario, _) = Cenario(db, banco);

        using var dbLeitura = banco.Criar();
        var relatorio = new ServicoDeRelatorios(dbLeitura)
            .Gerar(loja.Id, Base, Base.AddDays(1), usuario.Id);

        // 37,80 + 18,90 = 56,70. A cancelada não entra no faturamento; a de 5 dias antes está fora.
        Assert.Equal(2, relatorio.Resumo.Finalizadas);
        Assert.Equal(1, relatorio.Resumo.Canceladas);
        Assert.Equal(56.70m, relatorio.Resumo.Bruto);
        Assert.Equal(56.70m, relatorio.Resumo.Liquido);
        Assert.Equal(0m, relatorio.Resumo.Desconto);
        Assert.Equal(28.35m, relatorio.Resumo.TicketMedio);
    }

    [Fact]
    public void Relatorio_quebra_por_forma_de_pagamento()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (loja, _, usuario, _) = Cenario(db, banco);

        using var dbLeitura = banco.Criar();
        var relatorio = new ServicoDeRelatorios(dbLeitura).Gerar(loja.Id, Base, Base.AddDays(1), usuario.Id);

        Assert.Equal(2, relatorio.Formas.Count);
        var dinheiro = relatorio.Formas.Single(f => f.Forma == FormaPagamento.Dinheiro);
        var pix = relatorio.Formas.Single(f => f.Forma == FormaPagamento.Pix);

        Assert.Equal(37.80m, dinheiro.Total);
        Assert.Equal(1, dinheiro.Lancamentos);
        Assert.Equal(18.90m, pix.Total);

        // Cartão aparece só na venda cancelada, então não entra.
        Assert.DoesNotContain(relatorio.Formas, f => f.Forma == FormaPagamento.Credito);
        Assert.Equal(56.70m, relatorio.Formas.Sum(f => f.Total));
    }

    [Fact]
    public void Relatorio_lista_produtos_mais_vendidos_por_valor()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (loja, _, usuario, _) = Cenario(db, banco);

        using var dbLeitura = banco.Criar();
        var relatorio = new ServicoDeRelatorios(dbLeitura).Gerar(loja.Id, Base, Base.AddDays(1), usuario.Id);

        var linha = Assert.Single(relatorio.Produtos);
        Assert.Equal("Café Torrado 500g", linha.Descricao);
        Assert.Equal(3m, linha.Quantidade);   // 2 + 1
        Assert.Equal(56.70m, linha.Total);
    }

    [Fact]
    public void Relatorio_mostra_sangria_suprimento_e_conferencia_do_turno()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (loja, turno, usuario, _) = Cenario(db, banco);

        turno.Fechar(usuario.Id, valorInformado: 118.90m, valorCalculado: 118.90m, Base.AddHours(6));
        db.SaveChanges();

        using var dbLeitura = banco.Criar();
        var relatorio = new ServicoDeRelatorios(dbLeitura).Gerar(loja.Id, Base, Base.AddDays(1), usuario.Id);

        Assert.Equal(30m, relatorio.Sangrias);
        Assert.Equal(50m, relatorio.Suprimentos);

        var linhaTurno = Assert.Single(relatorio.Turnos);
        Assert.Equal(1, linhaTurno.Numero);
        Assert.Equal(100m, linhaTurno.Abertura);
        Assert.Equal(118.90m, linhaTurno.Calculado);
        Assert.Equal(118.90m, linhaTurno.Informado);
        Assert.Equal(0m, linhaTurno.Diferenca);
    }

    [Fact]
    public void Relatorio_acusa_diferenca_de_caixa_em_vez_de_corrigir()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (loja, turno, usuario, _) = Cenario(db, banco);

        // Operador contou 8,90 a menos: a diferença tem de aparecer, não ser escondida.
        turno.Fechar(usuario.Id, valorInformado: 110.00m, valorCalculado: 118.90m, Base.AddHours(6));
        db.SaveChanges();

        using var dbLeitura = banco.Criar();
        var relatorio = new ServicoDeRelatorios(dbLeitura).Gerar(loja.Id, Base, Base.AddDays(1), usuario.Id);

        Assert.Equal(-8.90m, relatorio.Turnos.Single().Diferenca);
    }

    [Fact]
    public void Limite_do_periodo_e_exclusivo_no_fim()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (loja, _, usuario, _) = Cenario(db, banco);

        using var dbLeitura = banco.Criar();
        // Período que termina exatamente no instante da venda 2 (Base+2h): ela fica de fora.
        var relatorio = new ServicoDeRelatorios(dbLeitura).Gerar(loja.Id, Base, Base.AddHours(2), usuario.Id);

        Assert.Equal(1, relatorio.Resumo.Finalizadas);
        Assert.Equal(37.80m, relatorio.Resumo.Liquido);
    }

    [Fact]
    public void Periodo_sem_movimento_devolve_relatorio_zerado_sem_erro()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (loja, _, usuario, _) = Cenario(db, banco);

        using var dbLeitura = banco.Criar();
        var relatorio = new ServicoDeRelatorios(dbLeitura).Gerar(loja.Id, Base.AddDays(30), Base.AddDays(60), usuario.Id);

        Assert.Equal(0, relatorio.Resumo.Finalizadas);
        Assert.Equal(0m, relatorio.Resumo.Liquido);
        Assert.Equal(0m, relatorio.Resumo.TicketMedio);   // sem divisão por zero
        Assert.Empty(relatorio.Formas);
        Assert.Empty(relatorio.Produtos);
        Assert.Empty(relatorio.Turnos);
    }

    [Fact]
    public void Relatorio_nao_ve_vendas_de_outra_loja()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, usuario, produto, turno) = banco.Semear(db);

        var outraLoja = Loja.Criar(empresa.Id, "02", "Loja Filial",
            new Landipay.Domain.ValueObjects.Endereco("Av. Brasil", "50", null, "Jardim", "Campinas", "SP", "13020000"));
        db.Lojas.Add(outraLoja);
        db.SaveChanges();

        var turnoOutra = CaixaTurno.Abrir(outraLoja.Id, 1, usuario.Id, 0m, Base);
        db.CaixaTurnos.Add(turnoOutra);
        db.SaveChanges();

        Vender(db, loja, turno, usuario, produto, 1, 1m, FormaPagamento.Dinheiro, Base.AddHours(1));
        Vender(db, outraLoja, turnoOutra, usuario, produto, 2, 3m, FormaPagamento.Dinheiro, Base.AddHours(1));

        using var dbLeitura = banco.Criar();
        var relatorio = new ServicoDeRelatorios(dbLeitura).Gerar(loja.Id, Base, Base.AddDays(1), usuario.Id);

        Assert.Equal(1, relatorio.Resumo.Finalizadas);
        Assert.Equal(18.90m, relatorio.Resumo.Liquido);
    }
}
