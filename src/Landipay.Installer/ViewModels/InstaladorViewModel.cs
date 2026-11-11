using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Landipay.Client.Servicos;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;

namespace Landipay.Installer.ViewModels;

public partial class PassoItem : ObservableObject
{
    public required string Titulo { get; init; }
    public required string Descricao { get; init; }

    [ObservableProperty] private string _estado = "pendente";   // pendente | ativo | ok | falha
}

public partial class InstaladorViewModel : ObservableObject
{
    private readonly ServicoInstalador _instalador;
    private readonly HttpClient _http;
    private readonly byte[]? _chaveLicenca;
    private readonly string _urlApi;

    [ObservableProperty] private int _passo;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private string _mensagem = string.Empty;
    [ObservableProperty] private bool _mensagemDeErro;
    [ObservableProperty] private bool _concluido;

    // Passo 3 — empresa
    [ObservableProperty] private string _cnpj = string.Empty;
    [ObservableProperty] private string _razaoSocial = string.Empty;
    [ObservableProperty] private string _nomeFantasia = string.Empty;
    [ObservableProperty] private string _inscricaoEstadual = string.Empty;
    [ObservableProperty] private string _logradouro = string.Empty;
    [ObservableProperty] private string _numero = string.Empty;
    [ObservableProperty] private string _bairro = string.Empty;
    [ObservableProperty] private string _cidade = string.Empty;
    [ObservableProperty] private string _uf = string.Empty;
    [ObservableProperty] private string _cep = string.Empty;
    [ObservableProperty] private string _codigoLoja = "01";
    [ObservableProperty] private string _nomeLoja = "Loja 01";

    // Passo 4 — licença
    [ObservableProperty] private string _tokenLicenca = string.Empty;

    // Passo 6 — aplicativos da estação
    [ObservableProperty] private bool _instalarAdmin = true;
    [ObservableProperty] private bool _instalarCaixa = true;
    [ObservableProperty] private string _resumoEstacao = string.Empty;

    /// <summary>Escopo que a licença permite. Fora dele, a caixa fica desabilitada na tela.</summary>
    [ObservableProperty] private bool _licencaPermiteAdmin = true;
    [ObservableProperty] private bool _licencaPermiteCaixa = true;
    [ObservableProperty] private string _escopoLicenca = "Admin + Caixa";

    private void AplicarEscopoDaLicenca()
    {
        var apps = _instalador.AppsDaLicenca();
        if (apps.Count == 0) return;

        LicencaPermiteAdmin = apps.Contains("admin");
        LicencaPermiteCaixa = apps.Contains("pos");

        EscopoLicenca = (LicencaPermiteAdmin, LicencaPermiteCaixa) switch
        {
            (true, true) => "Admin + Caixa",
            (true, false) => "somente Admin",
            (false, true) => "somente Caixa",
            _ => "nenhum app"
        };

        // Não deixa marcado o que a licença não permite: a escolha nasce dentro do escopo.
        if (!LicencaPermiteAdmin) InstalarAdmin = false;
        if (!LicencaPermiteCaixa) InstalarCaixa = false;
    }

    // Passo 5 — administrador
    [ObservableProperty] private string _adminLogin = string.Empty;
    [ObservableProperty] private string _adminSenha = string.Empty;
    [ObservableProperty] private string _adminConfirmacao = string.Empty;

    public ObservableCollection<PassoItem> Passos { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    public string PastaDeDados => _instalador.PastaDeDados;

    public bool TemChaveDeLicenca => _chaveLicenca is { Length: > 0 };

    public InstaladorViewModel(ServicoInstalador instalador, HttpClient http, byte[]? chaveLicenca,
        string urlApi, string? tokenInicial = null)
    {
        _instalador = instalador;
        _http = http;
        _chaveLicenca = chaveLicenca;
        _urlApi = urlApi.TrimEnd('/');

        Passos.Add(new PassoItem { Titulo = "Pré-requisitos", Descricao = "Runtime, permissões e espaço" });
        Passos.Add(new PassoItem { Titulo = "Banco de dados", Descricao = "Schema e migração idempotente" });
        Passos.Add(new PassoItem { Titulo = "Empresa", Descricao = "CNPJ com fallback manual" });
        Passos.Add(new PassoItem { Titulo = "Ativação", Descricao = "Licença validada offline" });
        Passos.Add(new PassoItem { Titulo = "Administrador", Descricao = "Senha definida agora" });
        Passos.Add(new PassoItem { Titulo = "Aplicativos", Descricao = "Admin, Caixa ou ambos" });
        Passos[0].Estado = "ativo";

        Registrar($"Pasta de dados: {instalador.PastaDeDados}");

        if (instalador.InstalacaoExistente(out var resumo))
        {
            Registrar($"Instalação anterior detectada: {resumo}");
            Registrar("Modo reparo: os dados existentes são mantidos e atualizados, não recriados.");
        }

        // Reparo/reinstalação: traz o cadastro existente para a tela em vez de obrigar a redigitar.
        if (instalador.EmpresaInstalada() is { } empresa)
        {
            Cnpj = empresa.Cnpj;
            RazaoSocial = empresa.RazaoSocial;
            NomeFantasia = empresa.NomeFantasia ?? string.Empty;
            InscricaoEstadual = empresa.InscricaoEstadual ?? string.Empty;
            Logradouro = empresa.Logradouro;
            Numero = empresa.Numero;
            Bairro = empresa.Bairro;
            Cidade = empresa.Cidade;
            Uf = empresa.Uf;
            Cep = empresa.Cep;
            CodigoLoja = empresa.CodigoLoja;
            NomeLoja = empresa.NomeLoja;
            Registrar("Empresa já cadastrada — dados carregados para conferência.");
        }

        if (!string.IsNullOrWhiteSpace(tokenInicial))
        {
            TokenLicenca = tokenInicial.Trim();
            Registrar("Licença recebida por provisionamento (LANDIPAY_LICENCA).");
        }

        if (!TemChaveDeLicenca)
            Registrar("AVISO: chave pública de licença não configurada (LANDIPAY_CHAVE_LICENCA).");
    }

    private void Registrar(string linha)
    {
        Log.Add($"[{DateTimeOffset.Now:HH:mm:ss}] {linha}");
        while (Log.Count > 200) Log.RemoveAt(0);
    }

    [RelayCommand]
    private async Task AvancarAsync()
    {
        if (Ocupado || Concluido) return;
        Ocupado = true;
        MensagemDeErro = false;

        try
        {
            var resultado = await ExecutarPassoAsync(Passo);
            Registrar($"{Passos[Passo].Titulo}: {resultado.Mensagem}" +
                      (resultado.Detalhe is null ? "" : $" — {resultado.Detalhe}"));

            if (!resultado.Ok)
            {
                Passos[Passo].Estado = "falha";
                MensagemDeErro = true;
                Mensagem = resultado.Detalhe is null
                    ? resultado.Mensagem
                    : $"{resultado.Mensagem} {resultado.Detalhe}";
                return;
            }

            Passos[Passo].Estado = "ok";
            Mensagem = resultado.Mensagem;

            // Licença ativada: o escopo do token passa a limitar a escolha do passo 6.
            if (Passo == 3) AplicarEscopoDaLicenca();

            Passo++;
            if (Passo >= Passos.Count)
            {
                Passo = Passos.Count - 1;
                Concluido = true;
                Mensagem = $"Instalação concluída. Abra pelo atalho: {ResumoAtalhos()}.";
                Registrar("Instalação concluída.");
            }
            else
            {
                Passos[Passo].Estado = "ativo";
            }
        }
        catch (Exception ex)
        {
            Passos[Passo].Estado = "falha";
            MensagemDeErro = true;
            Mensagem = ex.Message;
            Registrar($"ERRO: {ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private Task<ResultadoPasso> ExecutarPassoAsync(int passo) => passo switch
    {
        0 => Task.Run(_instalador.VerificarPreRequisitos),
        1 => Task.Run(_instalador.PrepararBanco),
        2 => Task.Run(() => _instalador.ConfigurarEmpresa(MontarEmpresa())),
        3 => Task.Run(() => _chaveLicenca is null
            ? ResultadoPasso.Falha("Chave pública de licença não configurada.",
                "defina LANDIPAY_CHAVE_LICENCA para validar offline")
            : _instalador.AtivarLicenca(TokenLicenca, _chaveLicenca)),
        4 => ValidarAdminECriar(),
        5 => Task.Run(() => _instalador.SelecionarAplicativos(
                new EscolhaDeAplicativos(InstalarAdmin, InstalarCaixa))),
        _ => Task.FromResult(ResultadoPasso.Sucesso("Nada a fazer."))
    };

    private Task<ResultadoPasso> ValidarAdminECriar()
    {
        // Reparo: base já tem administrador — não pede senha de novo nem recria o usuário.
        if (_instalador.AdminExistente())
            return Task.FromResult(ResultadoPasso.Sucesso(
                "Administrador já existe — senha atual mantida.", "reparo idempotente"));

        if (AdminSenha != AdminConfirmacao)
            return Task.FromResult(ResultadoPasso.Falha("As senhas não conferem."));
        if (AdminSenha.Length < 10)
            return Task.FromResult(ResultadoPasso.Falha("Senha muito curta.", "mínimo de 10 caracteres"));

        return Task.Run(() => _instalador.CriarAdministrador(AdminLogin, AdminSenha));
    }

    private DadosEmpresa MontarEmpresa() => new(
        Cnpj, RazaoSocial, NomeFantasia, InscricaoEstadual,
        RegimeTributario.SimplesNacional,
        Logradouro, Numero, null, Bairro, Cidade, Uf, Cep,
        string.IsNullOrWhiteSpace(CodigoLoja) ? "01" : CodigoLoja,
        string.IsNullOrWhiteSpace(NomeLoja) ? "Loja 01" : NomeLoja);

    [RelayCommand]
    private void Voltar()
    {
        if (Passo == 0 || Ocupado || Concluido) return;
        Passos[Passo].Estado = "pendente";
        Passo--;
        Passos[Passo].Estado = "ativo";
    }

    /// <summary>
    /// Busca o CNPJ no backend do Landipay (nunca numa API de terceiro com chave no cliente).
    /// Sem rede ou sem resposta, a tela continua utilizável: o operador preenche à mão.
    /// </summary>
    [RelayCommand]
    private async Task BuscarCnpjAsync()
    {
        var digitos = new string(Cnpj.Where(char.IsDigit).ToArray());
        if (digitos.Length != 14)
        {
            MensagemDeErro = true;
            Mensagem = "CNPJ precisa ter 14 dígitos.";
            return;
        }

        Ocupado = true;
        try
        {
            Registrar($"Consultando CNPJ {digitos} no backend...");
            using var resposta = await _http.GetAsync($"{_urlApi}/v1/cnpj/{digitos}");

            if (!resposta.IsSuccessStatusCode)
            {
                MensagemDeErro = false;
                Mensagem = resposta.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
                    ? "Sem conexão com o cadastro público. Preencha manualmente."
                    : $"Não foi possível consultar (HTTP {(int)resposta.StatusCode}). Preencha manualmente.";
                Registrar(Mensagem);
                return;
            }

            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            var raiz = json.RootElement;

            RazaoSocial = Ler(raiz, "razaoSocial") ?? RazaoSocial;
            NomeFantasia = Ler(raiz, "nomeFantasia") ?? NomeFantasia;
            if (raiz.TryGetProperty("endereco", out var end) && end.ValueKind == JsonValueKind.Object)
            {
                Logradouro = Ler(end, "logradouro") ?? Logradouro;
                Numero = Ler(end, "numero") ?? Numero;
                Bairro = Ler(end, "bairro") ?? Bairro;
                Cidade = Ler(end, "cidade") ?? Cidade;
                Uf = Ler(end, "uf") ?? Uf;
                Cep = Ler(end, "cep") ?? Cep;
            }

            MensagemDeErro = false;
            Mensagem = "Dados preenchidos a partir do cadastro público. Revise antes de salvar.";
            Registrar("CNPJ localizado e dados preenchidos.");
        }
        catch (HttpRequestException)
        {
            MensagemDeErro = false;
            Mensagem = "Sem conexão. Preencha os dados manualmente.";
            Registrar("Sem conexão — modo manual.");
        }
        catch (JsonException)
        {
            MensagemDeErro = true;
            Mensagem = "Resposta inválida do cadastro público.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private string ResumoAtalhos() => (InstalarAdmin, InstalarCaixa) switch
    {
        (true, true) => "\"Landipay Admin\" ou \"Landipay Caixa\"",
        (true, false) => "\"Landipay Admin\"",
        (false, true) => "\"Landipay Caixa\"",
        _ => "(nenhum aplicativo marcado)"
    };

    private static string? Ler(JsonElement raiz, string nome) =>
        raiz.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
