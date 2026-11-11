using System.Text.Json;
using Landipay.Client.Configuracao;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Common;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Updates;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Landipay.Tests;

public sealed class Fase3ClienteTests
{
    // ── helpers ──────────────────────────────────────────────────────────────
    private static Usuario UsuarioComSenha(BancoDeTeste banco, Landipay.Infrastructure.Persistence.LandipayDbContext db,
        string senha = "SenhaForte#2026")
    {
        var hasher = new Argon2idSenhaHasher();
        var usuario = db.Usuarios.Single();
        usuario.DefinirSenha(hasher.Hash(senha), DateTimeOffset.UtcNow);
        db.SaveChanges();
        return usuario;
    }

    private static void DarEstoque(BancoDeTeste banco, Landipay.Infrastructure.Persistence.LandipayDbContext db,
        decimal quantidade)
    {
        var loja = db.Lojas.Single();
        var produto = db.Produtos.Single();
        var usuario = db.Usuarios.Single();
        db.EstoqueMovimentos.Add(EstoqueMovimento.Criar(loja.Id, produto.Id, TipoMovimentoEstoque.Entrada,
            quantidade, quantidade, usuario.Id, DateTimeOffset.UtcNow, "carga inicial"));
        db.SaveChanges();
    }

    // ── A5: hash de senha ────────────────────────────────────────────────────
    [Fact]
    public void Argon2id_gera_phc_e_verifica_sem_guardar_a_senha()
    {
        var hasher = new Argon2idSenhaHasher();
        var hash = hasher.Hash("SenhaForte#2026");

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", hash);
        Assert.DoesNotContain("SenhaForte", hash);
        Assert.True(hasher.Verificar("SenhaForte#2026", hash));
        Assert.False(hasher.Verificar("senhaforte#2026", hash));   // case-sensitive
        Assert.False(hasher.Verificar("outra", hash));

        // Dois hashes da mesma senha são diferentes (salt aleatório).
        Assert.NotEqual(hash, hasher.Hash("SenhaForte#2026"));
    }

    [Fact]
    public void Login_valido_retorna_sessao_e_registra_auditoria()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        banco.Semear(db);
        UsuarioComSenha(banco, db);

        var servico = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher());
        var resultado = servico.Autenticar("diego.guilherme", "SenhaForte#2026");

        Assert.True(resultado.Ok);
        Assert.NotNull(resultado.Sessao);
        Assert.Equal("diego.guilherme", resultado.Sessao!.Login);
        Assert.Contains("login.ok", db.Auditoria.Select(a => a.Acao).ToList());
    }

    [Fact]
    public void Cinco_falhas_bloqueiam_mesmo_com_a_senha_correta()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        banco.Semear(db);
        UsuarioComSenha(banco, db);

        var relogio = new RelogioFalso(DateTimeOffset.UtcNow);
        var servico = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher(), relogio);

        for (var i = 0; i < 5; i++)
            Assert.False(servico.Autenticar("diego.guilherme", "errada").Ok);

        var bloqueado = servico.Autenticar("diego.guilherme", "SenhaForte#2026");
        Assert.False(bloqueado.Ok);
        Assert.Equal("usuario_bloqueado", bloqueado.Codigo);

        // Depois do bloqueio de 5 minutos, a senha certa volta a funcionar.
        relogio.Avancar(TimeSpan.FromMinutes(6));
        Assert.True(servico.Autenticar("diego.guilherme", "SenhaForte#2026").Ok);
    }

    [Fact]
    public void Usuario_inexistente_devolve_o_mesmo_codigo_de_senha_errada()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        banco.Semear(db);
        UsuarioComSenha(banco, db);

        var servico = new ServicoDeAutenticacao(db, new Argon2idSenhaHasher());

        var inexistente = servico.Autenticar("nao.existe", "qualquer");
        var senhaErrada = servico.Autenticar("diego.guilherme", "errada");

        // Mesma resposta: não dá para descobrir quais logins existem.
        Assert.Equal(inexistente.Codigo, senhaErrada.Codigo);
        Assert.Equal("credenciais_invalidas", inexistente.Codigo);
    }

    // ── Sessão ───────────────────────────────────────────────────────────────
    [Fact]
    public void Sessao_expira_por_inatividade_e_por_tempo_absoluto()
    {
        var relogio = new RelogioFalso(new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero));
        var sessao = new SessaoService(relogio);

        sessao.Iniciar(Guid.NewGuid(), Guid.NewGuid(), null, "Diego", "diego");
        Assert.True(sessao.EstaAtiva);

        relogio.Avancar(TimeSpan.FromMinutes(14));
        Assert.True(sessao.RegistrarAtividade());     // renova a inatividade

        relogio.Avancar(TimeSpan.FromMinutes(14));
        Assert.True(sessao.EstaAtiva);

        relogio.Avancar(TimeSpan.FromMinutes(16));    // 16 min sem atividade
        Assert.False(sessao.EstaAtiva);
        Assert.False(sessao.RegistrarAtividade());    // ação detecta e encerra a sessão
        Assert.Null(sessao.Atual);
    }

    // ── Caixa ────────────────────────────────────────────────────────────────
    [Fact]
    public void Venda_baixa_estoque_grava_outbox_e_auditoria()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, usuario, produto, turno) = banco.Semear(db);
        DarEstoque(banco, db, 10);

        var caixa = new ServicoDeCaixa(db);
        var venda = caixa.IniciarVenda(turno, usuario.Id);
        venda.AdicionarItem(produto, 2);
        venda.RegistrarPagamento(FormaPagamento.Dinheiro, 40m);
        caixa.FinalizarVenda(venda);

        Assert.Equal(StatusVenda.Finalizada, venda.Status);
        Assert.Equal(8m, caixa.CalcularSaldo(loja.Id, produto.Id));
        Assert.Equal(1, db.Outbox.Count());
        Assert.Contains("venda.finalizada", db.Auditoria.Select(a => a.Acao).ToList());
    }

    [Fact]
    public void Venda_sem_estoque_suficiente_e_recusada_e_nao_deixa_rastro()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, _, usuario, produto, turno) = banco.Semear(db);
        DarEstoque(banco, db, 1);

        var caixa = new ServicoDeCaixa(db);
        var venda = caixa.IniciarVenda(turno, usuario.Id);
        venda.AdicionarItem(produto, 5);
        venda.RegistrarPagamento(FormaPagamento.Pix, 200m);

        var ex = Assert.Throws<DomainException>(() => caixa.FinalizarVenda(venda));
        Assert.Contains("Estoque insuficiente", ex.Message);
        Assert.Equal(0, db.Outbox.Count());
    }

    [Fact]
    public void Fechamento_considera_troco_sangria_e_suprimento()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, _, usuario, produto, turno) = banco.Semear(db);
        DarEstoque(banco, db, 10);

        var caixa = new ServicoDeCaixa(db);

        var venda = caixa.IniciarVenda(turno, usuario.Id);
        venda.AdicionarItem(produto, 2);                       // 37,80
        venda.RegistrarPagamento(FormaPagamento.Dinheiro, 40m); // troco 2,20
        caixa.FinalizarVenda(venda);

        caixa.RegistrarSangria(turno, 10m, "Depósito", usuario.Id);
        caixa.RegistrarSuprimento(turno, 5m, "Troco", usuario.Id);

        var resumo = caixa.FecharTurno(turno, usuario.Id, valorInformado: 130m);

        Assert.Equal(1, resumo.QuantidadeVendas);
        Assert.Equal(37.80m, resumo.TotalVendas);
        Assert.Equal(10m, resumo.Sangrias);
        Assert.Equal(5m, resumo.Suprimentos);

        // 100 (abertura) + 37,80 (gaveta, já sem o troco) - 10 + 5 = 132,80
        Assert.Equal(132.80m, resumo.ValorCalculado);
        Assert.Equal(-2.80m, resumo.Diferenca);   // informou 130
        Assert.Equal(StatusTurno.Fechado, turno.Status);
    }

    // ── A3: updater ──────────────────────────────────────────────────────────
    private sealed class HandlerAtualizacao : HttpMessageHandler
    {
        private readonly string _manifestoJson;
        private readonly byte[] _artefato;
        public int Downloads { get; private set; }

        public HandlerAtualizacao(string manifestoJson, byte[] artefato)
        {
            _manifestoJson = manifestoJson;
            _artefato = artefato;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/updates/manifesto"))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(_manifestoJson, System.Text.Encoding.UTF8, "application/json")
                });

            Downloads++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_artefato)
            });
        }
    }

    private static (ManifestoRelease Manifesto, string Assinatura, byte[] Publica, string Envelope) PrepararManifesto(
        byte[] artefato, string? hashForcado = null)
    {
        var (semente, publica) = AssinaturaEd25519.GerarPar();

        var manifesto = new ManifestoRelease
        {
            Versao = "1.4.0",
            Canal = "stable",
            Url = "https://releases.landipay.example/landipay-1.4.0.zip",
            Sha256 = hashForcado ?? ValidadorDeManifesto.CalcularSha256(artefato),
            Tamanho = artefato.Length,
            VersaoMinima = "1.0.0",
            PublicadoEm = new DateTimeOffset(2026, 11, 11, 6, 0, 0, TimeSpan.Zero),
            KeyId = "upd-teste"
        };

        var assinatura = AssinaturaEd25519.Assinar(JsonCanonico.Serializar(manifesto), semente);

        // Envelope como o servidor entrega (camelCase), para exercitar o parser do cliente.
        var envelope = JsonSerializer.Serialize(new
        {
            manifesto = new
            {
                versao = manifesto.Versao,
                canal = manifesto.Canal,
                url = manifesto.Url,
                sha256 = manifesto.Sha256,
                tamanho = manifesto.Tamanho,
                versaoMinima = manifesto.VersaoMinima,
                publicadoEm = manifesto.PublicadoEm
            },
            assinatura,
            keyId = "upd-teste"
        });

        return (manifesto, assinatura, publica, envelope);
    }

    [Fact]
    public async Task Update_com_tudo_valido_baixa_e_grava_o_pacote()
    {
        var artefato = "conteudo do pacote 1.4.0"u8.ToArray();
        var (_, _, publica, envelope) = PrepararManifesto(artefato);
        var handler = new HandlerAtualizacao(envelope, artefato);
        var pasta = Path.Combine(Path.GetTempPath(), $"landipay-upd-{Guid.NewGuid():N}");

        var atualizador = new AtualizadorDesktop(new HttpClient(handler), publica, "https://api.landipay.example");
        var resultado = await atualizador.VerificarEAplicarAsync("1.3.0", "stable", pasta);

        Assert.True(resultado.Aplicado, resultado.Mensagem);
        Assert.Equal(1, handler.Downloads);
        Assert.True(File.Exists(resultado.ArquivoBaixado));

        Directory.Delete(pasta, recursive: true);
    }

    [Fact]
    public async Task Update_com_hash_divergente_e_recusado_e_nada_e_gravado()
    {
        var artefato = "pacote adulterado"u8.ToArray();
        var (_, _, publica, envelope) = PrepararManifesto(artefato, hashForcado: new string('a', 64));
        var handler = new HandlerAtualizacao(envelope, artefato);
        var pasta = Path.Combine(Path.GetTempPath(), $"landipay-upd-{Guid.NewGuid():N}");

        var atualizador = new AtualizadorDesktop(new HttpClient(handler), publica, "https://api.landipay.example");
        var resultado = await atualizador.VerificarEAplicarAsync("1.3.0", "stable", pasta);

        Assert.False(resultado.Aplicado);
        Assert.Equal(ValidadorDeManifesto.CodigoHashDivergente, resultado.Codigo);
        Assert.False(Directory.Exists(pasta) && Directory.EnumerateFiles(pasta).Any());
    }

    [Fact]
    public async Task Update_com_assinatura_de_outra_chave_nao_chega_a_baixar()
    {
        var artefato = "qualquer"u8.ToArray();
        var (_, _, _, envelope) = PrepararManifesto(artefato);
        var (_, chaveDoAtacante) = AssinaturaEd25519.GerarPar();   // chave pública diferente da usada para assinar
        var handler = new HandlerAtualizacao(envelope, artefato);

        var atualizador = new AtualizadorDesktop(new HttpClient(handler), chaveDoAtacante, "https://api.landipay.example");
        var resultado = await atualizador.VerificarEAplicarAsync("1.3.0", "stable", Path.GetTempPath());

        Assert.False(resultado.Aplicado);
        Assert.Equal(ValidadorDeManifesto.CodigoAssinaturaInvalida, resultado.Codigo);
        Assert.Equal(0, handler.Downloads);   // nem tentou baixar
    }

    // ── A6: log opt-in ───────────────────────────────────────────────────────
    private sealed class HandlerContador : HttpMessageHandler
    {
        public int Chamadas { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Chamadas++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Created));
        }
    }

    [Fact]
    public async Task Log_sem_opt_in_nao_faz_requisicao()
    {
        var arquivo = Path.GetTempFileName();
        File.WriteAllText(arquivo, "[INFO] teste");
        var handler = new HandlerContador();
        var envio = new EnvioDeLogOptIn(new HttpClient(handler));

        var enviado = await envio.EnviarAsync(arquivo, new PreferenciasUsuario("dark", EnvioDeLogOptIn: false,
            UrlEnvioLog: "https://logs.landipay.example/v1/logs"));

        Assert.False(enviado);
        Assert.Equal(0, handler.Chamadas);
        File.Delete(arquivo);
    }

    [Fact]
    public async Task Log_com_endpoint_http_nao_e_enviado()
    {
        var arquivo = Path.GetTempFileName();
        File.WriteAllText(arquivo, "[INFO] teste");
        var handler = new HandlerContador();
        var envio = new EnvioDeLogOptIn(new HttpClient(handler));

        var enviado = await envio.EnviarAsync(arquivo, new PreferenciasUsuario("dark", true,
            UrlEnvioLog: "http://logs.landipay.example/v1/logs"));

        Assert.False(enviado);
        Assert.Equal(0, handler.Chamadas);   // TLS obrigatório
        File.Delete(arquivo);
    }

    // ── Catálogo de demonstração ─────────────────────────────────────────────
    [Fact]
    public void Catalogo_demo_cria_tres_produtos_com_estoque_e_e_idempotente()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, usuario, _, _) = banco.Semear(db);

        // Semear() do BancoDeTeste já criou 1 produto; removemos para simular instalação nova.
        db.Produtos.RemoveRange(db.Produtos);
        db.SaveChanges();

        var criados = CatalogoDemo.Semear(db, empresa.Id, loja.Id, usuario.Id);
        Assert.Equal(3, criados);
        Assert.Equal(3, db.Produtos.Count());

        var caixa = new ServicoDeCaixa(db);
        var produto = db.Produtos.Single(p => p.Sku == "CAF-500");
        Assert.Equal(20m, caixa.CalcularSaldo(loja.Id, produto.Id));

        // Idempotente: rodar de novo não duplica catálogo.
        Assert.Equal(0, CatalogoDemo.Semear(db, empresa.Id, loja.Id, usuario.Id));
    }

    // ── Preferências / tema ──────────────────────────────────────────────────
    [Fact]
    public void Preferencia_de_tema_invalido_volta_para_escuro()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"prefs-{Guid.NewGuid():N}", "preferences.json");
        var armazem = new ArmazemDePreferencias(caminho);

        Assert.Equal("dark", armazem.Carregar().Tema);   // sem arquivo

        armazem.Salvar(new PreferenciasUsuario("light", true));
        Assert.Equal("light", armazem.Carregar().Tema);
        Assert.True(armazem.Carregar().EnvioDeLogOptIn);

        armazem.Salvar(new PreferenciasUsuario("roxo"));
        Assert.Equal("dark", armazem.Carregar().Tema);   // valor inválido normalizado

        Directory.Delete(Path.GetDirectoryName(caminho)!, recursive: true);
    }
}
