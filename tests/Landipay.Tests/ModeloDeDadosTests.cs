using Landipay.Domain.Common;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Landipay.Tests;

/// <summary>
/// Fase 1 — provas do modelo de dados. Cada teste corresponde a um controle declarado
/// em docs/01-modelo-dados.md, não a "cobertura" genérica.
/// </summary>
public sealed class ModeloDeDadosTests
{
    // ── Estrutura ─────────────────────────────────────────────────────────────
    [Fact]
    public void Migrations_criam_as_17_tabelas_e_os_triggers_de_auditoria()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var conn = db.Database.GetDbConnection();
        conn.Open();

        static int Contar(System.Data.Common.DbConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        var tabelas = Contar(conn,
            "SELECT count(*) FROM sqlite_master WHERE type='table' " +
            "AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'");
        var triggers = Contar(conn, "SELECT count(*) FROM sqlite_master WHERE type='trigger'");
        var indices = Contar(conn, "SELECT count(*) FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%'");

        Assert.Equal(17, tabelas);
        Assert.Equal(2, triggers);   // UPDATE e DELETE da auditoria
        Assert.True(indices >= 20, $"esperava ao menos 20 índices, veio {indices}");
    }

    // ── Fluxo de venda ponta a ponta (domínio + persistência) ─────────────────
    [Fact]
    public void Venda_ponta_a_ponta_persiste_itens_pagamentos_e_totais()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, usuario, produto, turno) = banco.Semear(db);

        var agora = DateTimeOffset.UtcNow;
        var venda = Venda.Abrir(loja.Id, turno.Id, usuario.Id, numero: 1);
        venda.AdicionarItem(produto, 2);                      // 2 × 18,90 = 37,80
        venda.AdicionarItem(produto, 1, precoUnitario: 10.00m); // 10,00
        venda.AplicarDesconto(2.80m);
        venda.RegistrarPagamento(FormaPagamento.Dinheiro, 50.00m, quando: agora);
        venda.Finalizar(agora);

        db.Vendas.Add(venda);
        db.SaveChanges();

        var carregada = db.Vendas
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .Single(v => v.Id == venda.Id);

        Assert.Equal(2, carregada.Itens.Count);
        Assert.Single(carregada.Pagamentos);
        Assert.Equal(47.80m, carregada.Subtotal);
        Assert.Equal(45.00m, carregada.Total);
        Assert.Equal(5.00m, carregada.Troco);
        Assert.Equal(StatusVenda.Finalizada, carregada.Status);
        Assert.Equal(37.80m, carregada.Itens.OrderBy(i => i.Sequencia).First().Total);
    }

    [Fact]
    public void Venda_sem_pagamento_suficiente_nao_finaliza()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, usuario, produto, turno) = banco.Semear(db);

        var venda = Venda.Abrir(loja.Id, turno.Id, usuario.Id, 2);
        venda.AdicionarItem(produto, 1);                  // 18,90
        venda.RegistrarPagamento(FormaPagamento.Pix, 10.00m);

        var ex = Assert.Throws<DomainException>(() => venda.Finalizar(DateTimeOffset.UtcNow));
        Assert.Contains("Pagamento insuficiente", ex.Message);
        Assert.Equal(StatusVenda.Aberta, venda.Status);
    }

    [Fact]
    public void Produto_inativo_nao_entra_na_venda()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, usuario, produto, turno) = banco.Semear(db);
        produto.Desativar();

        var venda = Venda.Abrir(loja.Id, turno.Id, usuario.Id, 3);
        var ex = Assert.Throws<DomainException>(() => venda.AdicionarItem(produto, 1));
        Assert.Contains("inativo", ex.Message);
    }

    // ── Integridade do banco (não da aplicação) ───────────────────────────────
    [Fact]
    public void Sku_duplicado_na_mesma_empresa_viola_indice_unico()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);

        db.Produtos.Add(Produto.Criar(empresa.Id, "CAF-500", "Café Duplicado", 9.90m));
        var ex = Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.Contains("Produtos", ex.InnerException?.Message ?? ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_constraint_do_banco_bloqueia_numero_de_venda_invalido()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, usuario, _, turno) = banco.Semear(db);

        // INSERT direto (parametrizado), passando por cima do domínio: quem barra é o banco.
        var ex = Record.Exception(() => db.Database.ExecuteSqlRaw(
            "INSERT INTO Vendas (Id, LojaId, CaixaTurnoId, UsuarioId, Numero, Status, Desconto, CriadoEm) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})",
            Guid.NewGuid().ToString(), loja.Id.ToString(), turno.Id.ToString(), usuario.Id.ToString(),
            0, "Aberta", 0, DateTimeOffset.UtcNow.ToString("O")));

        Assert.NotNull(ex);
        Assert.Contains("CK_Vendas_NumeroPositivo", ex!.ToString());
    }

    [Fact]
    public void Auditoria_e_append_only_bloqueia_update_e_delete()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, usuario, _, _) = banco.Semear(db);

        var log = LogAuditoria.Registrar("venda.finalizada", "Venda", usuario.Id, empresa.Id, loja.Id);
        db.Auditoria.Add(log);
        db.SaveChanges();
        Assert.True(log.Id > 0);

        var update = Record.Exception(() => db.Database.ExecuteSqlRaw(
            "UPDATE LogAuditoria SET Acao = 'adulterada' WHERE Id = {0}", log.Id));
        Assert.NotNull(update);
        Assert.Contains("append-only", update!.ToString());

        var delete = Record.Exception(() => db.Database.ExecuteSqlRaw(
            "DELETE FROM LogAuditoria WHERE Id = {0}", log.Id));
        Assert.NotNull(delete);
        Assert.Contains("append-only", delete!.ToString());

        Assert.Equal("venda.finalizada", db.Auditoria.Single(a => a.Id == log.Id).Acao);
    }

    // ── A4: zero SQL concatenado ──────────────────────────────────────────────
    [Fact]
    public void Nome_de_produto_com_payload_de_injecao_e_gravado_literalmente()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);

        const string payload = "'; DROP TABLE Produtos; --";
        db.Produtos.Add(Produto.Criar(empresa.Id, "INJ-001", payload, 1.00m));
        db.SaveChanges();

        var carregado = db.Produtos.Single(p => p.Sku == "INJ-001");
        Assert.Equal(payload, carregado.Nome);

        // A tabela continua existindo: nada foi interpretado como comando.
        var aindaExiste = db.Produtos.Count();
        Assert.Equal(2, aindaExiste);
    }

    // ── A5: bloqueio progressivo é regra de domínio ───────────────────────────
    [Fact]
    public void Cinco_falhas_bloqueiam_por_5_minutos_e_dez_por_30()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, _, usuario, _, _) = banco.Semear(db);
        var agora = DateTimeOffset.UtcNow;

        for (var i = 0; i < 4; i++)
        {
            usuario.RegistrarFalhaDeAcesso(agora);
            Assert.False(usuario.EstaBloqueado(agora));
        }

        usuario.RegistrarFalhaDeAcesso(agora);              // 5ª
        Assert.True(usuario.EstaBloqueado(agora));
        Assert.Equal(agora.AddMinutes(5), usuario.BloqueadoAte);

        for (var i = 0; i < 5; i++) usuario.RegistrarFalhaDeAcesso(agora);
        Assert.Equal(agora.AddMinutes(30), usuario.BloqueadoAte);   // 10ª

        usuario.RegistrarAcessoBemSucedido(agora);
        Assert.False(usuario.EstaBloqueado(agora));
        Assert.Equal(0, usuario.TentativasFalhas);
    }

    // ── Licença: tolerância offline curta e explícita ─────────────────────────
    [Fact]
    public void Licenca_expirada_sobrevive_apenas_dentro_da_tolerancia_offline()
    {
        var agora = DateTimeOffset.UtcNow;
        var licenca = ConfigLicenca.Registrar(Guid.NewGuid(), "instalacao-teste", "token.opaco", "lic-2026.1",
            emitidaEm: agora.AddDays(-40), expiraEm: agora.AddDays(-3));

        // Sem validação online registrada: tolerância não se aplica.
        Assert.False(licenca.EstaUtilizavel(agora));

        // Validou ontem: ainda dentro dos 7 dias.
        licenca.RegistrarValidacao(agora.AddDays(-1));
        Assert.True(licenca.EstaUtilizavel(agora));

        // Oito dias depois da última validação: fora da tolerância.
        licenca.RegistrarValidacao(agora.AddDays(-8));
        Assert.False(licenca.EstaUtilizavel(agora));
    }

    [Fact]
    public void Movimento_de_estoque_guarda_saldo_com_quatro_casas()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, usuario, produto, _) = banco.Semear(db);

        db.EstoqueMovimentos.Add(EstoqueMovimento.Criar(
            loja.Id, produto.Id, TipoMovimentoEstoque.Entrada, 12.3456m, 12.3456m,
            usuario.Id, DateTimeOffset.UtcNow, "carga inicial"));

        db.SaveChanges();

        var mov = db.EstoqueMovimentos.Single();
        Assert.Equal(12.3456m, mov.Quantidade);
        Assert.Equal(12.3456m, mov.SaldoApos);
        Assert.Equal(1, mov.Sinal);
    }
}
