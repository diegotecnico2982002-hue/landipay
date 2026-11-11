using System.IO;
using System.Net.Http;
using Landipay.Client.Configuracao;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Enums;
using Landipay.Domain.Seguranca;
using Landipay.Ui.ViewModels;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Landipay.Ui;

/// <summary>
/// Composição manual (sem contêiner de DI externo): um DbContext para a sessão do app,
/// serviços compartilhados e o shell de navegação. Mantém o startup auditável — dá para
/// ler de cima a baixo o que a aplicação monta antes de abrir a janela.
/// </summary>
public sealed class ComposicaoApp : IDisposable
{
    public LandipayDbContext Db { get; }
    public ServicoDeAutenticacao Autenticacao { get; }
    public ServicoDeCaixa Caixa { get; }
    public ServicoDeRelatorios Relatorios { get; }
    public SessaoService Sessao { get; }
    public Argon2idSenhaHasher Hasher { get; }
    public ArmazemDePreferencias Preferencias { get; }
    public EnvioDeLogOptIn EnvioDeLog { get; }
    public ShellViewModel Shell { get; } = new();
    public string PastaDados { get; }

    public bool TemaEscuro { get; private set; } = true;
    public string TemaAtual => TemaEscuro ? "dark" : "light";

    /// <summary>Qual aplicativo está rodando: decide a navegação e o escopo do login.</summary>
    public Aplicativo App { get; }

    public ServicoDeAutorizacao Autorizacao { get; }

    /// <summary>Licença em operação: avalia a cada abertura e alimenta o caixa com a porta de bloqueio.</summary>
    public ServicoDeLicenca Licenca { get; }

    /// <summary>Estado avaliado no início. Enquanto <c>PodeOperar</c> é falso, o app não passa do login.</summary>
    public EstadoLicenca EstadoDaLicenca { get; private set; } = null!;

    public string? UltimaNegativa { get; private set; }

    public ComposicaoApp() : this(Aplicativo.Pos) { }

    public ComposicaoApp(Aplicativo app)
    {
        App = app;
        PastaDados = Environment.GetEnvironmentVariable("LANDIPAY_DADOS") is { Length: > 0 } pasta
            ? pasta
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Landipay");
        Directory.CreateDirectory(PastaDados);

        // Log local sempre ativo (retenção 30 dias). Envio só com opt-in explícito.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(PastaDados, "logs", "landipay-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();

        var opcoes = new DbContextOptionsBuilder<LandipayDbContext>()
            .UseSqlite($"Data Source={Path.Combine(PastaDados, "landipay.db")}")
            .Options;

        Db = new LandipayDbContext(opcoes);
        Db.Database.Migrate();

        Hasher = new Argon2idSenhaHasher();
        Sessao = new SessaoService();
        Autenticacao = new ServicoDeAutenticacao(Db, Hasher);
        Autorizacao = new ServicoDeAutorizacao(Db);
        Licenca = new ServicoDeLicenca(Db);
        Caixa = new ServicoDeCaixa(Db, autorizacao: Autorizacao, licenca: Licenca);
        Relatorios = new ServicoDeRelatorios(Db, Autorizacao);

        // Papéis e permissões existem desde o primeiro start, inclusive em base antiga.
        SemeadorDeSeguranca.Garantir(Db);
        Preferencias = new ArmazemDePreferencias();
        EnvioDeLog = new EnvioDeLogOptIn(new HttpClient());

        var prefs = Preferencias.Carregar();
        TemaEscuro = prefs.Tema != "light";

        Log.Information("Landipay iniciado. App={App} Tema={Tema} Banco={Banco}",
            app, TemaAtual, Path.Combine(PastaDados, "landipay.db"));
    }

    public void Iniciar()
    {
        EstadoDaLicenca = Licenca.AvaliarNoInicio();
        AtualizarAviso();

        Log.Information("Licença: {Codigo} · expira {Expira} · opera {Pode} · assinatura {Assinatura}",
            EstadoDaLicenca.Codigo, EstadoDaLicenca.ExpiraEm, EstadoDaLicenca.PodeOperar,
            EstadoDaLicenca.AssinaturaVerificada);

        Shell.Atual = EstadoDaLicenca.PodeOperar ? CriarLogin() : CriarLicenca();
    }

    private LicencaViewModel CriarLicenca() => new(EstadoDaLicenca, App, VerificarLicenca);

    /// <summary>
    /// Reavalia a licença (botão "Verificar novamente"). É o caminho depois de renovar pelo
    /// instalador, sem precisar fechar e abrir o app.
    /// </summary>
    public void VerificarLicenca()
    {
        EstadoDaLicenca = Licenca.AvaliarNoInicio();
        AtualizarAviso();
        Shell.Atual = EstadoDaLicenca.PodeOperar ? CriarLogin() : CriarLicenca();
    }

    private void AtualizarAviso()
    {
        Shell.Aviso = EstadoDaLicenca.ProximaDoVencimento
            ? $"Licença expira em {EstadoDaLicenca.DiasRestantes} dia(s) — renove antes do vencimento."
            : string.Empty;
        Shell.TemAviso = Shell.Aviso.Length > 0;
    }

    public LoginViewModel CriarLogin() =>
        new(Autenticacao, Hasher, Db, Sessao, Entrar, App);

    private void Entrar(SessaoUsuario sessao)
    {
        // Reavalia no login: o app pode ter ficado aberto a noite toda e vencido no meio.
        EstadoDaLicenca = Licenca.Avaliar();
        AtualizarAviso();

        if (!EstadoDaLicenca.PodeOperar)
        {
            Shell.Atual = CriarLicenca();
            Log.Warning("Login barrado por licença: {Codigo}", EstadoDaLicenca.Codigo);
            return;
        }

        Shell.Operador = sessao.Nome;
        Shell.Contexto = $"{CatalogoDeAcesso.NomeDoApp(App)} · {sessao.Login}";

        // A tela inicial depende do aplicativo — e é consequência do papel, não da vontade da UI.
        Shell.Atual = App == Aplicativo.Admin ? CriarDashboard() : CriarCaixa(sessao);

        Log.Information("Login efetuado por {Login} no app {App} com papéis {Papeis}",
            sessao.Login, App, string.Join(",", sessao.Papeis));
    }

    private CaixaViewModel CriarCaixa(SessaoUsuario sessao) =>
        new(Caixa, Db, Sessao, sessao, Sair);

    private RelatoriosViewModel CriarDashboard()
    {
        var sessao = Sessao.Atual!;
        var lojaId = sessao.LojaId ?? Db.Lojas.Select(l => l.Id).FirstOrDefault();
        return new RelatoriosViewModel(Relatorios, lojaId, sessao.UsuarioId);
    }

    /// <summary>Tela de acesso negado: usada quando uma operação é barrada em sessão válida.</summary>
    public void MostrarAcessoNegado(string codigo, string mensagem)
    {
        UltimaNegativa = codigo;
        Shell.Atual = new AcessoNegadoViewModel(codigo, mensagem, VotarParaInicio);
    }

    private void VotarParaInicio() => Shell.Atual = App == Aplicativo.Admin ? CriarDashboard() : CriarLogin();

    public void Sair()
    {
        Sessao.Encerrar();
        Shell.Operador = string.Empty;
        Shell.Contexto = "Landipay";
        Shell.Atual = CriarLogin();
        Log.Information("Sessão encerrada");
    }

    public void AbrirCatalogo()
    {
        if (Sessao.Atual is null) return;
        Shell.Atual = new ProdutosViewModel(Db, Caixa);
    }

    /// <summary>Volta à tela inicial do app corrente (Caixa = PDV, Admin = dashboard).</summary>
    public void AbrirInicio()
    {
        if (Sessao.Atual is null) { Shell.Atual = CriarLogin(); return; }
        Shell.Atual = App == Aplicativo.Admin ? CriarDashboard() : CriarCaixa(Sessao.Atual);
    }

    /// <summary>Relatórios: só faz sentido com sessão aberta (a loja vem da sessão).</summary>
    public void AbrirRelatorios()
    {
        if (Sessao.Atual is null) return;

        // Sessão sem loja (usuário criado sem vínculo) cai na primeira loja cadastrada.
        var lojaId = Sessao.Atual.LojaId ?? Db.Lojas.Select(l => l.Id).FirstOrDefault();
        if (lojaId == Guid.Empty) return;

        Shell.Atual = new RelatoriosViewModel(Relatorios, lojaId, Sessao.Atual.UsuarioId);
    }

    public void SalvarTema(string tema)
    {
        TemaEscuro = tema != "light";
        Preferencias.Salvar(Preferencias.Carregar() with { Tema = TemaAtual });
    }

    public void Dispose()
    {
        Db.Dispose();
        Log.CloseAndFlush();
    }
}
