using Landipay.Backend.Dados;
using Landipay.Backend.Seguranca;
using Landipay.Backend.Servicos;
using Landipay.Updates;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Landipay.Tests;

/// <summary>Relógio controlável — permite provar expiração sem Thread.Sleep.</summary>
public sealed class RelogioFalso : TimeProvider
{
    private DateTimeOffset _agora;
    public RelogioFalso(DateTimeOffset inicio) => _agora = inicio;
    public override DateTimeOffset GetUtcNow() => _agora;
    public void Avancar(TimeSpan delta) => _agora = _agora.Add(delta);
}

/// <summary>Backend em SQLite temporário, criado pelo próprio contexto (Fase 5 migra para migrations).</summary>
public sealed class BackendDeTeste : IDisposable
{
    public string Caminho { get; }

    public BackendDeTeste()
    {
        Caminho = Path.Combine(Path.GetTempPath(), $"landipay-api-test-{Guid.NewGuid():N}.db");
        using var db = Criar();
        db.Database.EnsureCreated();
    }

    public BackendDbContext Criar() => BackendDbContext.ParaArquivo(Caminho);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(Caminho)) File.Delete(Caminho); } catch { }
    }
}

// ── A3: manifesto assinado ───────────────────────────────────────────────────
public sealed class ManifestoTests
{
    private static ManifestoRelease ManifestoValido() => new()
    {
        Versao = "1.4.0",
        Canal = "stable",
        Url = "https://releases.landipay.example/landipay-1.4.0.zip",
        Sha256 = new string('a', 64),
        Tamanho = 1024,
        VersaoMinima = "1.0.0",
        PublicadoEm = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero),
        KeyId = "upd-teste"
    };

    [Fact]
    public void Manifesto_assinado_com_a_chave_correta_e_aceito()
    {
        var (semente, publica) = AssinaturaEd25519.GerarPar();
        var manifesto = ManifestoValido();
        var assinatura = AssinaturaEd25519.Assinar(JsonCanonico.Serializar(manifesto), semente);

        var resultado = ValidadorDeManifesto.Validar(manifesto, assinatura, publica, versaoInstalada: "1.3.9");

        Assert.True(resultado.Valido, resultado.Descricao);
        Assert.Equal("ok", resultado.Codigo);
    }

    [Fact]
    public void Manifesto_adulterado_depois_de_assinado_e_rejeitado()
    {
        var (semente, publica) = AssinaturaEd25519.GerarPar();
        var original = ManifestoValido();
        var assinatura = AssinaturaEd25519.Assinar(JsonCanonico.Serializar(original), semente);

        // Atacante troca a URL e mantém a assinatura original.
        var adulterado = original with { Url = "https://atacante.example/payload.zip" };

        var resultado = ValidadorDeManifesto.Validar(adulterado, assinatura, publica);

        Assert.False(resultado.Valido);
        Assert.Equal(ValidadorDeManifesto.CodigoAssinaturaInvalida, resultado.Codigo);
    }

    [Fact]
    public void Assinatura_de_outra_chave_e_rejeitada()
    {
        var (sementeAtacante, _) = AssinaturaEd25519.GerarPar();
        var (_, publicaConfiavel) = AssinaturaEd25519.GerarPar();
        var manifesto = ManifestoValido();
        var assinaturaForjada = AssinaturaEd25519.Assinar(JsonCanonico.Serializar(manifesto), sementeAtacante);

        var resultado = ValidadorDeManifesto.Validar(manifesto, assinaturaForjada, publicaConfiavel);

        Assert.False(resultado.Valido);
        Assert.Equal(ValidadorDeManifesto.CodigoAssinaturaInvalida, resultado.Codigo);
    }

    [Fact]
    public void Manifesto_mais_antigo_que_o_instalado_bloqueia_downgrade()
    {
        var (semente, publica) = AssinaturaEd25519.GerarPar();
        var manifesto = ManifestoValido();
        var assinatura = AssinaturaEd25519.Assinar(JsonCanonico.Serializar(manifesto), semente);

        var resultado = ValidadorDeManifesto.Validar(manifesto, assinatura, publica, versaoInstalada: "2.0.0");

        Assert.False(resultado.Valido);
        Assert.Equal(ValidadorDeManifesto.CodigoDowngrade, resultado.Codigo);
    }

    [Fact]
    public void Url_sem_https_e_rejeitada_antes_da_criptografia()
    {
        var manifesto = ManifestoValido() with { Url = "http://releases.landipay.example/x.zip" };

        var resultado = ValidadorDeManifesto.Validar(manifesto, "qualquer", new byte[32]);

        Assert.False(resultado.Valido);
        Assert.Equal("url_insegura", resultado.Codigo);
    }

    [Fact]
    public void Hash_do_artefato_baixado_divergente_e_rejeitado()
    {
        var conteudo = "conteudo real do pacote"u8.ToArray();
        var hashCorreto = ValidadorDeManifesto.CalcularSha256(conteudo);

        Assert.True(ValidadorDeManifesto.ValidarArtefato(conteudo, hashCorreto).Valido);

        var resultado = ValidadorDeManifesto.ValidarArtefato(conteudo, new string('b', 64));
        Assert.False(resultado.Valido);
        Assert.Equal(ValidadorDeManifesto.CodigoHashDivergente, resultado.Codigo);
    }

    [Fact]
    public void Servico_publica_release_e_o_manifesto_gerado_valida_com_a_chave_publica()
    {
        using var banco = new BackendDeTeste();
        using var db = banco.Criar();
        using var chaves = ProvedorDeChaves.EmMemoria();
        var servico = new ServicoDeAtualizacao(db, chaves);

        servico.Publicar("1.4.0", "stable", "https://releases.landipay.example/landipay-1.4.0.zip",
            new string('c', 64), 2048, "1.0.0");

        var assinado = servico.ObterManifesto("stable");
        Assert.NotNull(assinado);

        var resultado = ValidadorDeManifesto.Validar(
            assinado!.Manifesto, assinado.Assinatura, chaves.ChavePublicaUpdate, versaoInstalada: "1.3.0");

        Assert.True(resultado.Valido, resultado.Descricao);
        Assert.Equal(chaves.KidUpdate, assinado.KeyId);
        Assert.Equal("1.4.0", assinado.Manifesto.Versao);
    }

    [Fact]
    public void Manifesto_sobrevive_ao_transporte_json_e_continua_valido()
    {
        var (semente, publica) = AssinaturaEd25519.GerarPar();

        // Data com 7 casas de fração, como o servidor serializa.
        var publicadoEm = new DateTimeOffset(2026, 11, 11, 5, 57, 29, TimeSpan.Zero).AddTicks(4208741);
        var manifesto = ManifestoValido() with { PublicadoEm = publicadoEm };
        var assinatura = AssinaturaEd25519.Assinar(JsonCanonico.Serializar(manifesto), semente);

        // Simula o que o cliente recebe: JSON do servidor, sem assinatura/kid no corpo.
        var json = System.Text.Json.JsonSerializer.Serialize(manifesto);
        var recebido = System.Text.Json.JsonSerializer.Deserialize<ManifestoRelease>(json)! with { KeyId = "upd-teste" };

        var resultado = ValidadorDeManifesto.Validar(recebido, assinatura, publica, "1.3.0");
        Assert.True(resultado.Valido, resultado.Descricao);

        // E se alguém mexer no JSON no caminho (MITM sem chave), a assinatura cai.
        var jsonAdulterado = json.Replace(manifesto.Sha256, new string('f', 64));
        var adulterado = System.Text.Json.JsonSerializer.Deserialize<ManifestoRelease>(jsonAdulterado)! with { KeyId = "upd-teste" };
        Assert.False(ValidadorDeManifesto.Validar(adulterado, assinatura, publica).Valido);
    }

    [Fact]
    public void Servico_recusa_publicar_url_sem_https()
    {
        using var banco = new BackendDeTeste();
        using var db = banco.Criar();
        using var chaves = ProvedorDeChaves.EmMemoria();
        var servico = new ServicoDeAtualizacao(db, chaves);

        Assert.Throws<ArgumentException>(() =>
            servico.Publicar("1.0.0", "stable", "http://inseguro/x.zip", new string('d', 64), 10));
    }
}

// ── Licença JWT ES256 ────────────────────────────────────────────────────────
public sealed class LicencaTests
{
    [Fact]
    public void Token_emitido_valida_e_carrega_kid_e_claims()
    {
        using var chaves = ProvedorDeChaves.EmMemoria();
        var emissor = new EmissorDeLicenca(chaves);
        var empresa = Guid.NewGuid();

        var (token, expiraEm, kid) = emissor.Emitir(empresa, "instalacao-abc123", "pro", TimeSpan.FromHours(1));

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(chaves.KidLicenca, kid);
        Assert.True(expiraEm > DateTimeOffset.UtcNow);

        var principal = emissor.Validar(token);
        Assert.NotNull(principal);
        Assert.Equal("instalacao-abc123", principal!.FindFirst("sub")?.Value);
        Assert.Equal(empresa.ToString(), principal.FindFirst("emp")?.Value);
        Assert.Equal("pro", principal.FindFirst("plano")?.Value);
    }

    [Fact]
    public void Token_assinado_por_outra_chave_e_rejeitado()
    {
        using var chavesApi = ProvedorDeChaves.EmMemoria();
        using var chavesAtacante = ProvedorDeChaves.EmMemoria();

        var tokenForjado = new EmissorDeLicenca(chavesAtacante)
            .Emitir(Guid.NewGuid(), "instalacao-x", "pro").Token;

        var principal = new EmissorDeLicenca(chavesApi).Validar(tokenForjado);

        Assert.Null(principal);
    }

    [Fact]
    public void Token_expirado_nao_valida()
    {
        using var chaves = ProvedorDeChaves.EmMemoria();
        var emissor = new EmissorDeLicenca(chaves);

        var (token, _, _) = emissor.Emitir(Guid.NewGuid(), "instalacao-expirada", "pro", TimeSpan.FromSeconds(-90));

        Assert.Null(emissor.Validar(token));
    }
}

// ── A1: suporte remoto consentido ────────────────────────────────────────────
public sealed class SuporteTests
{
    [Fact]
    public void Codigo_errado_nao_ativa_sessao()
    {
        using var banco = new BackendDeTeste();
        using var db = banco.Criar();
        var servico = new ServicoDeSuporte(db);

        var sessao = servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "tecnico.silva", "Cliente sem acesso ao caixa");

        var resultado = servico.Consentir(sessao.Id, "000000");

        Assert.False(resultado.Ok);
        Assert.Equal("codigo_invalido", resultado.Erro);
        Assert.Null(resultado.Credencial);
    }

    [Fact]
    public void Codigo_correto_ativa_uma_vez_e_nao_pode_ser_reutilizado()
    {
        using var banco = new BackendDeTeste();
        using var db = banco.Criar();
        var servico = new ServicoDeSuporte(db);
        var sessao = servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "tecnico.silva", "Cliente sem acesso ao caixa");

        var primeira = servico.Consentir(sessao.Id, sessao.Codigo);
        Assert.True(primeira.Ok);
        Assert.False(string.IsNullOrWhiteSpace(primeira.Credencial));

        var segunda = servico.Consentir(sessao.Id, sessao.Codigo);
        Assert.False(segunda.Ok);
        Assert.Equal("codigo_ja_utilizado", segunda.Erro);

        // O código não fica em claro no banco.
        var registro = db.Sessoes.Single(s => s.Id == sessao.Id);
        Assert.NotEqual(sessao.Codigo, registro.CodigoHash);
        Assert.Equal(64, registro.CodigoHash.Length);
    }

    [Fact]
    public void Sessao_expira_sozinha_apos_o_ttl()
    {
        using var banco = new BackendDeTeste();
        using var db = banco.Criar();
        var relogio = new RelogioFalso(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
        var servico = new ServicoDeSuporte(db, relogio);

        var sessao = servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "tecnico.silva", "Cliente sem acesso ao caixa");
        var consentida = servico.Consentir(sessao.Id, sessao.Codigo);
        Assert.True(consentida.Ok);

        relogio.Avancar(TimeSpan.FromMinutes(31));

        var depois = servico.Obter(sessao.Id);
        Assert.NotNull(depois);
        Assert.Equal(StatusSessaoSuporte.Expirada, depois!.Status);

        // Nova tentativa de consentir também falha.
        var outra = servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "tecnico.silva", "Segundo atendimento");
        relogio.Avancar(TimeSpan.FromMinutes(31));
        Assert.Equal("sessao_expirada", servico.Consentir(outra.Id, outra.Codigo).Erro);
    }

    [Fact]
    public void Encerramento_e_auditado_com_autor()
    {
        using var banco = new BackendDeTeste();
        using var db = banco.Criar();
        var servico = new ServicoDeSuporte(db);
        var sessao = servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "tecnico.silva", "Cliente sem acesso ao caixa");
        servico.Consentir(sessao.Id, sessao.Codigo);

        Assert.True(servico.Encerrar(sessao.Id, "operador-local"));
        Assert.False(servico.Encerrar(sessao.Id, "operador-local"));  // idempotente: já encerrada

        var acoes = db.Auditoria.Select(a => a.Acao).ToList();
        Assert.Contains("suporte.sessao_criada", acoes);
        Assert.Contains("suporte.consentido", acoes);
        Assert.Contains("suporte.encerrado", acoes);
    }

    [Fact]
    public void Ttl_acima_do_limite_e_recusado()
    {
        using var banco = new BackendDeTeste();
        using var db = banco.Criar();
        var servico = new ServicoDeSuporte(db);

        Assert.Throws<ArgumentException>(() =>
            servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "tecnico", "Atendimento longo demais", ttl: TimeSpan.FromHours(5)));
    }
}

// ── CNPJ: cache e fallback offline ───────────────────────────────────────────
public sealed class CnpjTests
{
    private sealed class HandlerFalso : HttpMessageHandler
    {
        private readonly int _status;
        private readonly string _corpo;
        public int Chamadas { get; private set; }

        public HandlerFalso(int status, string corpo) { _status = status; _corpo = corpo; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Chamadas++;
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)_status)
            {
                Content = new StringContent(_corpo, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private const string RespostaBrasilApi = """
    {
      "cnpj": "12345678000190",
      "razao_social": "MERCADO CENTRAL LTDA",
      "nome_fantasia": "MERCADO CENTRAL",
      "logradouro": "RUA DAS ACACIAS",
      "numero": "1200",
      "complemento": null,
      "bairro": "CENTRO",
      "municipio": "CAMPINAS",
      "uf": "SP",
      "cep": "13010000"
    }
    """;

    [Fact]
    public async Task Consulta_valida_preenche_e_a_segunda_chamada_vem_do_cache()
    {
        var handler = new HandlerFalso(200, RespostaBrasilApi);
        var servico = new ServicoCnpj(new HttpClient(handler));

        var primeira = await servico.ConsultarAsync("12.345.678/0001-90");
        Assert.NotNull(primeira);
        Assert.Equal("MERCADO CENTRAL LTDA", primeira!.RazaoSocial);
        Assert.Equal("SP", primeira.Endereco!.Uf);
        Assert.False(primeira.DeCache);

        var segunda = await servico.ConsultarAsync("12345678000190");
        Assert.NotNull(segunda);
        Assert.True(segunda!.DeCache);
        Assert.Equal(1, handler.Chamadas);   // cache de 24h funcionando
    }

    [Fact]
    public async Task Sem_rede_retorna_null_para_a_ui_cair_no_modo_manual()
    {
        var servico = new ServicoCnpj(new HttpClient(new HandlerFalso(500, "erro")));

        var resultado = await servico.ConsultarAsync("12345678000190");

        Assert.Null(resultado);   // cliente preenche manualmente; cadastro não bloqueia
    }

    [Fact]
    public async Task Cnpj_invalido_nao_chama_a_api()
    {
        var handler = new HandlerFalso(200, RespostaBrasilApi);
        var servico = new ServicoCnpj(new HttpClient(handler));

        Assert.Null(await servico.ConsultarAsync("123"));
        Assert.Equal(0, handler.Chamadas);
    }
}
