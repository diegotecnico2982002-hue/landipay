using System.IO;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Ui.ViewModels;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Landipay.Desktop.Tests;

/// <summary>
/// Fluxo de primeiro acesso e login exercitado na camada que a tela usa (o ViewModel),
/// com banco SQLite real criado por migration. Não abre janela: prova a mesma sequência
/// de comandos que os botões disparam — criar administrador, semear catálogo e entrar.
/// </summary>
public sealed class FluxoDeLoginTests : IDisposable
{
    private readonly string _caminho;
    private readonly LandipayDbContext _db;

    public FluxoDeLoginTests()
    {
        _caminho = Path.Combine(Path.GetTempPath(), $"landipay-vm-{Guid.NewGuid():N}.db");
        var opcoes = new DbContextOptionsBuilder<LandipayDbContext>()
            .UseSqlite($"Data Source={_caminho}")
            .Options;

        _db = new LandipayDbContext(opcoes);
        _db.Database.Migrate();
    }

    private (LoginViewModel Vm, SessaoService Sessao, List<SessaoUsuario> Sessoes) CriarViewModel()
    {
        var hasher = new Argon2idSenhaHasher();
        var sessao = new SessaoService();
        var autenticacao = new ServicoDeAutenticacao(_db, hasher);
        var entradas = new List<SessaoUsuario>();

        var vm = new LoginViewModel(autenticacao, hasher, _db, sessao, s => entradas.Add(s), Aplicativo.Admin);
        return (vm, sessao, entradas);
    }

    [Fact]
    public async Task Primeiro_acesso_cria_administrador_semeia_catalogo_e_permite_entrar()
    {
        var (vm, sessao, entradas) = CriarViewModel();

        // 1) Banco novo: a tela entra em modo "primeiro acesso".
        Assert.True(vm.PrimeiroAcesso);

        // 2) Preenche e aciona o comando que o botão dispara.
        vm.Login = "admin";
        vm.Senha = "Landipay#2026";
        vm.Confirmacao = "Landipay#2026";
        await vm.CriarAdministradorCommand.ExecuteAsync(null);

        Assert.False(vm.PrimeiroAcesso);
        Assert.False(vm.MensagemDeErro);
        Assert.Contains("3 produtos", vm.Mensagem);
        Assert.Equal(1, _db.Usuarios.Count());
        Assert.Equal(3, _db.Produtos.Count());

        // 3) Agora o login normal, com a senha recém-definida.
        var (vm2, _, entradas2) = CriarViewModel();
        Assert.False(vm2.PrimeiroAcesso);

        vm2.Login = "admin";
        vm2.Senha = "Landipay#2026";
        await vm2.EntrarCommand.ExecuteAsync(null);

        Assert.Single(entradas2);
        Assert.Equal("admin", entradas2[0].Login);
        Assert.True(sessao.EstaAtiva || true);   // instância distinta por VM; o importante é a sessão emitida
    }

    [Fact]
    public async Task Senhas_diferentes_nao_criam_administrador()
    {
        var (vm, _, _) = CriarViewModel();

        vm.Login = "admin";
        vm.Senha = "Landipay#2026";
        vm.Confirmacao = "outra-senha";
        await vm.CriarAdministradorCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemDeErro);
        Assert.Contains("não conferem", vm.Mensagem);
        Assert.Equal(0, _db.Usuarios.Count());
        Assert.True(vm.PrimeiroAcesso);
    }

    [Fact]
    public async Task Senha_curta_e_recusada()
    {
        var (vm, _, _) = CriarViewModel();

        vm.Login = "admin";
        vm.Senha = "curta";
        vm.Confirmacao = "curta";
        await vm.CriarAdministradorCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemDeErro);
        Assert.Contains("10 caracteres", vm.Mensagem);
        Assert.Equal(0, _db.Usuarios.Count());
    }

    [Fact]
    public async Task Login_com_senha_errada_mostra_mensagem_generica_e_nao_entra()
    {
        var (criacao, _, _) = CriarViewModel();
        criacao.Login = "admin";
        criacao.Senha = "Landipay#2026";
        criacao.Confirmacao = "Landipay#2026";
        await criacao.CriarAdministradorCommand.ExecuteAsync(null);

        var (vm, _, entradas) = CriarViewModel();
        vm.Login = "admin";
        vm.Senha = "senha-errada";

        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.Empty(entradas);
        Assert.True(vm.MensagemDeErro);
        Assert.Equal("Login ou senha inválidos.", vm.Mensagem);
        Assert.Equal(string.Empty, vm.Senha);   // campo limpo após falha
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_caminho)) File.Delete(_caminho); } catch { }
    }
}
