using Landipay.Client.Seguranca;
using Landipay.Domain.Entities;
using Landipay.Domain.Seguranca;
using Landipay.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Tests;

/// <summary>
/// Banco SQLite temporário, criado pelas MIGRATIONS reais do projeto (não por EnsureCreated):
/// se a migration quebrar, o teste quebra — é isso que se quer.
/// </summary>
public sealed class BancoDeTeste : IDisposable
{
    public string Caminho { get; }

    public BancoDeTeste()
    {
        Caminho = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"landipay-test-{Guid.NewGuid():N}.db");
        using var db = Criar();
        db.Database.Migrate();
    }

    public LandipayDbContext Criar()
    {
        var options = new DbContextOptionsBuilder<LandipayDbContext>()
            .UseSqlite($"Data Source={Caminho}")
            .Options;
        return new LandipayDbContext(options);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(Caminho)) File.Delete(Caminho); } catch { /* arquivo travado no Windows: fica no temp */ }
    }

    // ── Semente mínima: empresa, loja, usuário, produto e turno aberto ────────
    ///
    /// <param name="agora">
    /// Momento gravado no turno. Testes que filtram por período (ex.: Fase 5) precisam passar
    /// uma data fixa — usar <see cref="DateTimeOffset.UtcNow"/> aqui tornava o teste dependente
    /// do relógio da máquina: passava no dia da âncora e quebrava no dia seguinte.
    /// </param>
    public (Empresa Empresa, Loja Loja, Usuario Usuario, Produto Produto, CaixaTurno Turno) Semear(
        LandipayDbContext db, DateTimeOffset? agora = null)
    {
        var quando = agora ?? DateTimeOffset.UtcNow;
        var empresa = Empresa.Criar("Mercado Central Ltda", "Mercado Central", "12345678000190",
            Landipay.Domain.Enums.RegimeTributario.SimplesNacional,
            new Landipay.Domain.ValueObjects.Endereco("Rua das Acácias", "1200", null, "Centro", "Campinas", "SP", "13010000"));

        var loja = Loja.Criar(empresa.Id, "01", "Loja Centro",
            new Landipay.Domain.ValueObjects.Endereco("Rua das Acácias", "1200", null, "Centro", "Campinas", "SP", "13010000"));

        var usuario = Usuario.Criar(empresa.Id, "Diego G.", "diego.guilherme",
            "$argon2id$v=19$m=65536,t=3,p=4$c2FsdHNhbHQ$aGFzaGZha2VwYXJhZGV2", loja.Id);

        var produto = Produto.Criar(empresa.Id, "CAF-500", "Café Torrado 500g", 18.90m, "UN", "7891000053508");

        db.Empresas.Add(empresa);
        db.Lojas.Add(loja);
        db.Usuarios.Add(usuario);
        db.Produtos.Add(produto);
        db.SaveChanges();

        // Estrutura de acesso + papel Gerente: cobre caixa E relatórios, que é o que os
        // testes de domínio e de fluxo precisam exercitar. Testes de autorização criam
        // usuários próprios com os outros papéis.
        SemeadorDeSeguranca.Garantir(db);
        SemeadorDeSeguranca.ConcederPapel(db, usuario.Id, CatalogoDeAcesso.PapelGerente);

        var turno = CaixaTurno.Abrir(loja.Id, 1, usuario.Id, 100.00m, quando);
        db.CaixaTurnos.Add(turno);
        db.SaveChanges();

        return (empresa, loja, usuario, produto, turno);
    }
}
