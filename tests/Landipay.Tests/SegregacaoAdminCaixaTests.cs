using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.Seguranca;
using Landipay.Infrastructure.Persistence;
using Xunit;

namespace Landipay.Tests;

/// <summary>
/// Segregação App Admin × App Caixa.
///
/// O ponto destes testes: a negativa acontece no SERVIÇO. Nenhum deles passa pela tela —
/// eles chamam o serviço direto, como faria um cliente malicioso que ignorasse a UI.
/// </summary>
public sealed class SegregacaoAdminCaixaTests
{
    private const string Senha = "SenhaForte#2026";

    private static Usuario CriarUsuarioComPapel(LandipayDbContext db, Guid empresaId, Guid lojaId,
        string login, string papel)
    {
        var usuario = Usuario.Criar(empresaId, login, login, new Argon2idSenhaHasher().Hash(Senha), lojaId);
        db.Usuarios.Add(usuario);
        db.SaveChanges();

        SemeadorDeSeguranca.DefinirPapeis(db, usuario.Id, new[] { papel });
        return usuario;
    }

    private static (Empresa Empresa, Loja Loja, Produto Produto, CaixaTurno Turno, Usuario Admin)
        Cenario(BancoDeTeste banco, LandipayDbContext db)
    {
        var (empresa, loja, _, produto, turno) = banco.Semear(db);
        var admin = CriarUsuarioComPapel(db, empresa.Id, loja.Id, "admin.teste", CatalogoDeAcesso.PapelAdmin);
        return (empresa, loja, produto, turno, admin);
    }

    // ── Caixa não invade gestão ──────────────────────────────────────────────
    [Fact]
    public void Operador_de_caixa_nao_le_relatorio_mesmo_chamando_o_servico()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, _, _, _) = Cenario(banco, db);
        var operador = CriarUsuarioComPapel(db, empresa.Id, loja.Id, "caixa.teste", CatalogoDeAcesso.PapelOperadorCaixa);

        var servico = new ServicoDeRelatorios(db);
        var erro = Assert.Throws<AcessoNegadoException>(() =>
            servico.Gerar(loja.Id, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), operador.Id));

        Assert.Equal(CatalogoDeAcesso.RelatorioLer, erro.Codigo);

        var auditoria = db.Auditoria.OrderByDescending(a => a.Id).First();
        Assert.Equal("acesso.negado", auditoria.Acao);
        Assert.Contains("relatorio.ler", auditoria.DadosJson);
        Assert.Equal(operador.Id, auditoria.UsuarioId);
    }

    [Fact]
    public void Operador_de_caixa_nao_cadastra_produto()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, _, _, _) = Cenario(banco, db);
        var operador = CriarUsuarioComPapel(db, empresa.Id, loja.Id, "caixa2.teste", CatalogoDeAcesso.PapelOperadorCaixa);

        Assert.False(new ServicoDeAutorizacao(db).Pode(operador.Id, CatalogoDeAcesso.ProdutoEscrever));
        Assert.False(new ServicoDeAutorizacao(db).Pode(operador.Id, CatalogoDeAcesso.UsuarioGerenciar));
        Assert.True(new ServicoDeAutorizacao(db).Pode(operador.Id, CatalogoDeAcesso.VendaCriar));
    }

    // ── Gestão não opera caixa (decisão: segregar) ───────────────────────────
    [Fact]
    public void Admin_nao_abre_turno_de_caixa_e_a_negativa_e_auditada()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, _, _, admin) = Cenario(banco, db);

        var caixa = new ServicoDeCaixa(db);
        var erro = Assert.Throws<AcessoNegadoException>(() => caixa.AbrirTurno(loja.Id, admin.Id, 100m));

        Assert.Equal(CatalogoDeAcesso.CaixaAbrir, erro.Codigo);
        Assert.Contains(db.Auditoria.ToList(), a => a.Acao == "acesso.negado" && a.UsuarioId == admin.Id);
    }

    [Fact]
    public void Admin_nao_inicia_venda()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, _, _, turno, admin) = Cenario(banco, db);

        var caixa = new ServicoDeCaixa(db);
        var erro = Assert.Throws<AcessoNegadoException>(() => caixa.IniciarVenda(turno, admin.Id));
        Assert.Equal(CatalogoDeAcesso.VendaCriar, erro.Codigo);
    }

    [Fact]
    public void Admin_le_relatorio_e_gerencia_usuario_mas_nao_vende()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, _, _, admin) = Cenario(banco, db);

        var autorizacao = new ServicoDeAutorizacao(db);
        Assert.True(autorizacao.Pode(admin.Id, CatalogoDeAcesso.RelatorioLer));
        Assert.True(autorizacao.Pode(admin.Id, CatalogoDeAcesso.UsuarioGerenciar));
        Assert.True(autorizacao.Pode(admin.Id, CatalogoDeAcesso.ProdutoEscrever));
        Assert.False(autorizacao.Pode(admin.Id, CatalogoDeAcesso.VendaCriar));
        Assert.False(autorizacao.Pode(admin.Id, CatalogoDeAcesso.CaixaFechar));

        // e o relatório funciona de verdade para ele
        var relatorio = new ServicoDeRelatorios(db).Gerar(loja.Id,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), admin.Id);
        Assert.NotNull(relatorio);
    }

    // ── Login por aplicativo ─────────────────────────────────────────────────
    [Fact]
    public void Operador_de_caixa_nao_entra_no_app_admin()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, _, _, _) = Cenario(banco, db);
        var operador = CriarUsuarioComPapel(db, empresa.Id, loja.Id, "caixa3.teste", CatalogoDeAcesso.PapelOperadorCaixa);

        var resultado = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher())
            .Autenticar("caixa3.teste", Senha, Aplicativo.Admin);

        Assert.False(resultado.Ok);
        Assert.Equal("app_nao_autorizado", resultado.Codigo);
        Assert.Null(resultado.Sessao);

        var negativa = db.Auditoria.OrderByDescending(a => a.Id).First();
        Assert.Equal("acesso.negado", negativa.Acao);
        Assert.Equal("Aplicativo", negativa.Entidade);
        Assert.Contains("\"app\":\"Admin\"", negativa.DadosJson);
    }

    [Fact]
    public void Admin_nao_entra_no_app_caixa()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, _, _, _) = Cenario(banco, db);
        var admin = CriarUsuarioComPapel(db, empresa.Id, loja.Id, "admin2.teste", CatalogoDeAcesso.PapelAdmin);

        var resultado = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher())
            .Autenticar("admin2.teste", Senha, Aplicativo.Pos);

        Assert.False(resultado.Ok);
        Assert.Equal("app_nao_autorizado", resultado.Codigo);
        Assert.Contains(db.Auditoria.ToList(), a => a.Acao == "acesso.negado" && a.UsuarioId == admin.Id);
    }

    [Fact]
    public void Gerente_entra_nos_dois_apps()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, _, _, _) = Cenario(banco, db);
        var gerente = CriarUsuarioComPapel(db, empresa.Id, loja.Id, "gerente.teste", CatalogoDeAcesso.PapelGerente);

        var autenticacao = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher());
        var noAdmin = autenticacao.Autenticar("gerente.teste", Senha, Aplicativo.Admin);
        var noCaixa = autenticacao.Autenticar("gerente.teste", Senha, Aplicativo.Pos);

        Assert.True(noAdmin.Ok, noAdmin.Codigo);
        Assert.True(noCaixa.Ok, noCaixa.Codigo);
        Assert.Equal(Aplicativo.Admin, noAdmin.Sessao!.App);
        Assert.Equal(Aplicativo.Pos, noCaixa.Sessao!.App);

        var autorizacao = new ServicoDeAutorizacao(db);
        Assert.True(autorizacao.PodeAcessarApp(gerente.Id, Aplicativo.Admin));
        Assert.True(autorizacao.PodeAcessarApp(gerente.Id, Aplicativo.Pos));
    }

    [Fact]
    public void Sessao_carrega_app_e_permissoes()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, _, _, _) = Cenario(banco, db);
        var operador = CriarUsuarioComPapel(db, empresa.Id, loja.Id, "caixa4.teste", CatalogoDeAcesso.PapelOperadorCaixa);

        var resultado = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher())
            .Autenticar("caixa4.teste", Senha, Aplicativo.Pos);

        Assert.True(resultado.Ok, resultado.Codigo);
        var sessao = resultado.Sessao!;
        Assert.Equal(Aplicativo.Pos, sessao.App);
        Assert.Contains(CatalogoDeAcesso.VendaCriar, sessao.PermissoesEfetivas);
        Assert.DoesNotContain(CatalogoDeAcesso.RelatorioLer, sessao.PermissoesEfetivas);
        Assert.False(sessao.Pode(CatalogoDeAcesso.RelatorioLer));
        Assert.True(sessao.Pode(CatalogoDeAcesso.VendaCriar));
    }

    [Fact]
    public void Usuario_sem_papel_nao_entra_em_nenhum_app()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, _, _, _) = Cenario(banco, db);

        var semPapel = Usuario.Criar(empresa.Id, "Sem Papel", "sempapel", new Argon2idSenhaHasher().Hash(Senha), loja.Id);
        db.Usuarios.Add(semPapel);
        db.SaveChanges();

        var autenticacao = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher());
        Assert.Equal("sem_papel", autenticacao.Autenticar("sempapel", Senha, Aplicativo.Pos).Codigo);
        Assert.Equal("sem_papel", autenticacao.Autenticar("sempapel", Senha, Aplicativo.Admin).Codigo);
    }

    [Fact]
    public void Semeador_e_idempotente_e_nao_escala_privilegio()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, usuario, _, _) = banco.Semear(db);

        var permissoesAntes = db.Permissoes.Count();
        var papeisAntes = db.Papeis.Count();
        var vinculosAntes = db.PapelPermissoes.Count();

        SemeadorDeSeguranca.Garantir(db);
        SemeadorDeSeguranca.Garantir(db);

        Assert.Equal(permissoesAntes, db.Permissoes.Count());
        Assert.Equal(papeisAntes, db.Papeis.Count());
        Assert.Equal(vinculosAntes, db.PapelPermissoes.Count());

        // usuário criado sem papel cai em OperadorCaixa, nunca em Admin
        var novo = Usuario.Criar(empresa.Id, "Novo", "novo.usuario", "hash", loja.Id);
        db.Usuarios.Add(novo);
        db.SaveChanges();

        SemeadorDeSeguranca.Garantir(db);
        var papeis = new ServicoDeAutorizacao(db).PapeisDe(novo.Id);
        Assert.Equal(new[] { CatalogoDeAcesso.PapelOperadorCaixa }, papeis);
        Assert.DoesNotContain(CatalogoDeAcesso.PapelAdmin, new ServicoDeAutorizacao(db).PapeisDe(usuario.Id));
    }
}
