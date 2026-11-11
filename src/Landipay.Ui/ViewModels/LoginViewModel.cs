using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Common;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.Seguranca;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Ui.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ServicoDeAutenticacao _autenticacao;
    private readonly Argon2idSenhaHasher _hasher;
    private readonly LandipayDbContext _db;
    private readonly SessaoService _sessao;
    private readonly Action<SessaoUsuario> _aoEntrar;
    private readonly Aplicativo _app;

    [ObservableProperty] private string _login = string.Empty;
    [ObservableProperty] private string _senha = string.Empty;
    [ObservableProperty] private string _confirmacao = string.Empty;
    [ObservableProperty] private string _mensagem = string.Empty;
    [ObservableProperty] private bool _mensagemDeErro = true;
    [ObservableProperty] private bool _primeiroAcesso;
    [ObservableProperty] private bool _ocupado;

    public LoginViewModel(ServicoDeAutenticacao autenticacao, Argon2idSenhaHasher hasher,
        LandipayDbContext db, SessaoService sessao, Action<SessaoUsuario> aoEntrar,
        Aplicativo app = Aplicativo.Pos)
    {
        _autenticacao = autenticacao;
        _hasher = hasher;
        _db = db;
        _sessao = sessao;
        _aoEntrar = aoEntrar;
        _app = app;

        // A estrutura de papéis nasce junto com o app: base antiga ganha os papéis aqui.
        SemeadorDeSeguranca.Garantir(_db);

        // Banco sem usuário = primeira execução. O administrador só nasce pelo App Admin:
        // o Caixa não cria usuário (segregação de funções também na criação).
        PrimeiroAcesso = !_db.Usuarios.Any() && _app == Aplicativo.Admin;
        if (PrimeiroAcesso)
        {
            MensagemDeErro = false;
            Mensagem = "Primeiro acesso: crie o administrador da loja.";
        }
        else if (!_db.Usuarios.Any())
        {
            MensagemDeErro = true;
            Mensagem = "Nenhum usuário cadastrado. Abra o Landipay Admin para criar o administrador.";
        }
        else
        {
            MensagemDeErro = false;
            Mensagem = $"Entre com seu usuário do {CatalogoDeAcesso.NomeDoApp(_app)}.";
        }
    }

    public string NomeDoApp => CatalogoDeAcesso.NomeDoApp(_app);

    /// <summary>Título da tela: o mesmo formulário serve os dois apps, com texto próprio.</summary>
    public string Titulo => _app == Aplicativo.Admin ? "Entrar na gestão" : "Entrar no caixa";

    [RelayCommand]
    private async Task EntrarAsync()
    {
        if (Ocupado) return;
        Ocupado = true;
        Mensagem = string.Empty;

        try
        {
            // Argon2id é caro por design: não bloquear a thread de UI.
            var resultado = await Task.Run(() => _autenticacao.Autenticar(Login, Senha, _app));

            if (!resultado.Ok)
            {
                MensagemDeErro = true;
                Mensagem = resultado.Codigo switch
                {
                    "usuario_bloqueado" => $"Usuário bloqueado até {resultado.BloqueadoAte:HH:mm}.",
                    "usuario_inativo" => "Usuário inativo.",
                    "app_nao_autorizado" =>
                        $"Seu perfil não tem acesso ao {NomeDoApp}. A tentativa foi registrada na auditoria.",
                    "sem_papel" => "Usuário sem perfil de acesso. Procure o administrador.",
                    _ => "Login ou senha inválidos."
                };
                Senha = string.Empty;
                return;
            }

            _sessao.Restaurar(resultado.Sessao!);
            _aoEntrar(resultado.Sessao!);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task CriarAdministradorAsync()
    {
        if (Ocupado) return;
        Mensagem = string.Empty;

        if (string.IsNullOrWhiteSpace(Login) || Senha.Length < 10)
        {
            MensagemDeErro = true;
            Mensagem = "Informe um login e uma senha com pelo menos 10 caracteres.";
            return;
        }

        if (Senha != Confirmacao)
        {
            MensagemDeErro = true;
            Mensagem = "As senhas não conferem.";
            return;
        }

        Ocupado = true;
        try
        {
            var hash = await Task.Run(() => _hasher.Hash(Senha));

            var empresa = Empresa.Criar("Empresa Landipay", "Landipay", "00000000000191",
                RegimeTributario.SimplesNacional,
                new Landipay.Domain.ValueObjects.Endereco("Rua Principal", "100", null, "Centro", "São Paulo", "SP", "01001000"));

            var loja = Loja.Criar(empresa.Id, "01", "Loja 01",
                new Landipay.Domain.ValueObjects.Endereco("Rua Principal", "100", null, "Centro", "São Paulo", "SP", "01001000"));

            var usuario = Usuario.Criar(empresa.Id, Login, Login, hash, loja.Id);
            usuario.DefinirSenha(hash, DateTimeOffset.UtcNow, exigirTroca: false);

            _db.Empresas.Add(empresa);
            _db.Lojas.Add(loja);
            _db.Usuarios.Add(usuario);
            _db.SaveChanges();

            // Papel Admin explícito: sem isso o usuário criado seria negado no próprio Admin.
            SemeadorDeSeguranca.ConcederPapel(_db, usuario.Id, CatalogoDeAcesso.PapelAdmin);

            // Catálogo de demonstração: o caixa já pode ser operado no primeiro acesso.
            var criados = CatalogoDemo.Semear(_db, empresa.Id, loja.Id, usuario.Id);

            PrimeiroAcesso = false;
            MensagemDeErro = false;
            Mensagem = criados > 0
                ? $"Administrador criado. {criados} produtos de demonstração no catálogo."
                : "Administrador criado. Entre com a senha definida.";
            Senha = string.Empty;
            Confirmacao = string.Empty;
        }
        catch (DomainException ex)
        {
            MensagemDeErro = true;
            Mensagem = ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }
}

public partial class ProdutosViewModel : ObservableObject
{
    private readonly LandipayDbContext _db;
    private readonly ServicoDeCaixa _caixa;

    [ObservableProperty] private string _busca = string.Empty;

    public ObservableCollection<LinhaProduto> Produtos { get; } = new();

    public ProdutosViewModel(LandipayDbContext db, ServicoDeCaixa caixa)
    {
        _db = db;
        _caixa = caixa;
        Carregar();
    }

    [RelayCommand]
    private void Atualizar() => Carregar();

    public void Carregar()
    {
        var lojaId = _db.Lojas.Select(l => l.Id).FirstOrDefault();

        var itens = _db.Produtos
            .Where(p => p.Ativo && (Busca == string.Empty || p.Nome.Contains(Busca) || p.Sku.Contains(Busca)))
            .OrderBy(p => p.Nome)
            .ToList();

        Produtos.Clear();
        foreach (var p in itens)
            Produtos.Add(new LinhaProduto(p.Sku, p.Nome, p.PrecoVenda, _caixa.CalcularSaldo(lojaId, p.Id)));
    }
}

public sealed record LinhaProduto(string Sku, string Nome, decimal Preco, decimal Saldo)
{
    public string PrecoFormatado => Preco.ToString("C2");
    public string SaldoFormatado => Saldo.ToString("0.###");
}

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty] private object? _atual;
    [ObservableProperty] private string _operador = string.Empty;
    [ObservableProperty] private string _contexto = "Landipay";

    public event EventHandler? TemaAlternado;
    public Func<bool>? TemaEscuro { get; set; }

    public void AlternarTema() => TemaAlternado?.Invoke(this, EventArgs.Empty);
}
