using Landipay.Backend.Seguranca;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;
using Xunit;

namespace Landipay.Tests;

/// <summary>
/// Licença <b>em operação</b> (P0-3). Até esta correção a licença só era conferida no
/// instalador: o app abria e vendia com licença vencida. Cada teste aqui fixa uma regra
/// que o caixa precisa obedecer, com relógio controlado (sem Thread.Sleep).
///
/// O emissor assina com o relógio real, então a <see cref="Ancora"/> é capturada antes de
/// emitir: validade positiva gera licença futura, validade negativa gera licença vencida, e
/// o <see cref="RelogioFalso"/> coloca o serviço nesse instante.
/// </summary>
public sealed class LicencaOperacionalTests
{
    private static readonly DateTimeOffset Ancora = DateTimeOffset.UtcNow;

    private static (string Token, DateTimeOffset ExpiraEm) Emitir(ProvedorDeChaves chaves,
        Guid empresaId, TimeSpan validade)
    {
        var emissor = new EmissorDeLicenca(chaves);
        var (token, expira, _) = emissor.Emitir(empresaId, "instalacao-teste-01", "pro", validade);
        return (token, expira);
    }

    /// <summary>
    /// Semeia o registro de licença. <paramref name="expiraGravada"/> diferente do exp do token
    /// é o caso de "alguém esticou a data no banco".
    /// </summary>
    private static ConfigLicenca Registrar(LandipayDbContext db, Guid empresaId, string token,
        DateTimeOffset expiraGravada, DateTimeOffset? ultimaValidacao = null)
    {
        var registro = ConfigLicenca.Registrar(empresaId, "instalacao-teste-01", token, "lic-api",
            emitidaEm: expiraGravada.AddDays(-30), expiraEm: expiraGravada);

        if (ultimaValidacao is { } quando)
            registro.RegistrarValidacao(quando);

        db.ConfigLicencas.Add(registro);
        db.SaveChanges();
        return registro;
    }

    /// <summary>O turno da semente vem sem estoque; a venda precisa de saldo.</summary>
    private static void DarEstoque(LandipayDbContext db, Loja loja, Usuario usuario, Produto produto,
        decimal quantidade = 10m)
    {
        db.EstoqueMovimentos.Add(EstoqueMovimento.Criar(loja.Id, produto.Id, TipoMovimentoEstoque.Entrada,
            quantidade, quantidade, usuario.Id, Ancora, "carga de teste"));
        db.SaveChanges();
    }

    private static (ServicoDeLicenca Licenca, ServicoDeCaixa Caixa) Servicos(
        LandipayDbContext db, TimeProvider relogio, byte[]? chave = null)
    {
        var licenca = new ServicoDeLicenca(db, chave, relogio);
        var caixa = new ServicoDeCaixa(db, relogio, new ServicoDeAutorizacao(db, relogio), licenca);
        return (licenca, caixa);
    }

    private static void Vender(ServicoDeCaixa caixa, CaixaTurno turno, Usuario usuario, Produto produto)
    {
        var venda = caixa.IniciarVenda(turno, usuario.Id);
        venda.AdicionarItem(produto, 1);
        venda.RegistrarPagamento(FormaPagamento.Dinheiro, venda.Total);
        caixa.FinalizarVenda(venda);
    }

    // ── 1. Sem licença ───────────────────────────────────────────────────────
    [Fact]
    public void Sem_registro_de_licenca_nao_opera()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (_, loja, usuario, produto, turno) = banco.Semear(db);
        DarEstoque(db, loja, usuario, produto);

        var (licenca, caixa) = Servicos(db, new RelogioFalso(Ancora));
        var estado = licenca.Avaliar();

        Assert.Equal(CodigosLicenca.Ausente, estado.Codigo);
        Assert.False(estado.PodeOperar);

        var erro = Assert.Throws<LicencaBloqueadaException>(() => Vender(caixa, turno, usuario, produto));
        Assert.Equal(CodigosLicenca.Ausente, erro.Codigo);
        Assert.Empty(db.Vendas);
    }

    // ── 2. Licença válida ────────────────────────────────────────────────────
    [Fact]
    public void Licenca_valida_permite_vender_e_marca_assinatura_verificada()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, usuario, produto, turno) = banco.Semear(db);
        DarEstoque(db, loja, usuario, produto);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(30));
        Registrar(db, empresa.Id, token, expira);

        var (licenca, caixa) = Servicos(db, new RelogioFalso(Ancora),
            autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo());
        var estado = licenca.Avaliar();

        Assert.Equal(CodigosLicenca.Ok, estado.Codigo);
        Assert.True(estado.PodeOperar);
        Assert.True(estado.AssinaturaVerificada);
        // O emissor usa o relógio real: a âncora fica alguns milissegundos atrás da emissão.
        Assert.InRange(estado.DiasRestantes, 29, 31);
        Assert.False(estado.ProximaDoVencimento);

        Vender(caixa, turno, usuario, produto);
        Assert.Single(db.Vendas);
    }

    // ── 3. Aviso antes do vencimento ─────────────────────────────────────────
    [Fact]
    public void Licenca_a_menos_de_sete_dias_avisa_mas_opera()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(3));
        Registrar(db, empresa.Id, token, expira);

        var estado = new ServicoDeLicenca(db, autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo(),
            new RelogioFalso(Ancora)).Avaliar();

        Assert.True(estado.PodeOperar);
        Assert.True(estado.ProximaDoVencimento);
        Assert.InRange(estado.DiasRestantes, 2, 4);
        Assert.Contains("válida até", estado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    // ── 4. Expirada, dentro da tolerância offline ────────────────────────────
    [Fact]
    public void Expirada_dentro_da_tolerancia_opera_com_aviso()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, usuario, produto, turno) = banco.Semear(db);
        DarEstoque(db, loja, usuario, produto);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        // Token venceu ontem; última validação online foi há 2 dias (tolerância = 7).
        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(-1));
        Registrar(db, empresa.Id, token, expira, ultimaValidacao: Ancora.AddDays(-2));

        var (licenca, caixa) = Servicos(db, new RelogioFalso(Ancora),
            autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo());
        var estado = licenca.Avaliar();

        Assert.Equal(CodigosLicenca.EmTolerancia, estado.Codigo);
        Assert.True(estado.PodeOperar);
        Assert.True(estado.EmTolerancia);

        Vender(caixa, turno, usuario, produto);
        Assert.Single(db.Vendas);
    }

    // ── 5. Expirada, fora da tolerância ──────────────────────────────────────
    [Fact]
    public void Expirada_fora_da_tolerancia_bloqueia_a_venda()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, loja, usuario, produto, turno) = banco.Semear(db);
        DarEstoque(db, loja, usuario, produto);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(-1));
        // Última validação há 30 dias: a janela de 7 dias já fechou.
        Registrar(db, empresa.Id, token, expira, ultimaValidacao: Ancora.AddDays(-30));

        var (licenca, caixa) = Servicos(db, new RelogioFalso(Ancora),
            autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo());
        var estado = licenca.Avaliar();

        Assert.Equal(CodigosLicenca.Expirada, estado.Codigo);
        Assert.False(estado.PodeOperar);

        var erro = Assert.Throws<LicencaBloqueadaException>(() => Vender(caixa, turno, usuario, produto));
        Assert.Equal(CodigosLicenca.Expirada, erro.Codigo);
        Assert.Empty(db.Vendas);
    }

    // ── 6. Revogada vence data ───────────────────────────────────────────────
    [Fact]
    public void Revogada_bloqueia_mesmo_com_data_futura()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(300));
        var registro = Registrar(db, empresa.Id, token, expira);
        registro.Revogar();
        db.SaveChanges();

        var estado = new ServicoDeLicenca(db, autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo(),
            new RelogioFalso(Ancora)).Avaliar();

        Assert.Equal(CodigosLicenca.Revogada, estado.Codigo);
        Assert.False(estado.PodeOperar);
    }

    // ── 7. Data esticada no banco ────────────────────────────────────────────
    [Fact]
    public void Vencimento_gravado_maior_que_o_token_e_adulteracao()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, _) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(1));
        // Token diz "vale 1 dia", banco diz "vale 365": alguém editou a tabela.
        Registrar(db, empresa.Id, token, Ancora.AddDays(365));

        var estado = new ServicoDeLicenca(db, autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo(),
            new RelogioFalso(Ancora)).Avaliar();

        Assert.Equal(CodigosLicenca.Adulterada, estado.Codigo);
        Assert.False(estado.PodeOperar);
    }

    // ── 8. Assinatura de outra chave ─────────────────────────────────────────
    [Fact]
    public void Token_assinado_por_outra_chave_e_recusado_quando_ha_chave_pinada()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();
        using var impostor = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(30));
        Registrar(db, empresa.Id, token, expira);

        var estado = new ServicoDeLicenca(db, impostor.ChaveLicenca.ExportSubjectPublicKeyInfo(),
            new RelogioFalso(Ancora)).Avaliar();

        Assert.Equal(CodigosLicenca.Adulterada, estado.Codigo);
        Assert.False(estado.PodeOperar);
    }

    // ── 9. Fechar turno continua permitido ───────────────────────────────────
    [Fact]
    public void Fechar_turno_funciona_com_licenca_expirada()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, usuario, _, turno) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(-1));
        Registrar(db, empresa.Id, token, expira, ultimaValidacao: Ancora.AddDays(-30));

        var (licenca, caixa) = Servicos(db, new RelogioFalso(Ancora),
            autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo());
        Assert.False(licenca.Avaliar().PodeOperar);

        // O dinheiro do turno precisa ser conferido: fechar não é vender.
        var resumo = caixa.FecharTurno(turno, usuario.Id, valorInformado: 100m);

        Assert.Equal(0, resumo.QuantidadeVendas);
        Assert.Equal(100m, resumo.ValorCalculado);
        Assert.Equal(0m, resumo.Diferenca);
        Assert.Equal(StatusTurno.Fechado, db.CaixaTurnos.Single().Status);
    }

    // ── 10. Auditoria da abertura e do bloqueio ──────────────────────────────
    [Fact]
    public void Abertura_audita_o_estado_e_o_bloqueio_fica_na_trilha()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(-1));
        Registrar(db, empresa.Id, token, expira, ultimaValidacao: Ancora.AddDays(-30));

        var licenca = new ServicoDeLicenca(db, autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo(),
            new RelogioFalso(Ancora));
        licenca.AvaliarNoInicio();

        Assert.True(db.Auditoria.Any(a => a.Acao == "licenca.expirada"));

        Assert.Throws<LicencaBloqueadaException>(() => licenca.ExigirOperacao("venda.finalizar"));

        var bloqueio = db.Auditoria.Single(a => a.Acao == "licenca.bloqueio");
        Assert.Contains("venda.finalizar", bloqueio.DadosJson!);
        Assert.Contains(CodigosLicenca.Expirada, bloqueio.DadosJson!);
    }

    // ── 11. Tolerância não é renovada offline ────────────────────────────────
    [Fact]
    public void Avaliar_nao_estende_a_ultima_validacao()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (token, expira) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(-1));
        Registrar(db, empresa.Id, token, expira, ultimaValidacao: Ancora.AddDays(-2));

        var licenca = new ServicoDeLicenca(db, autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo(),
            new RelogioFalso(Ancora));
        var ultimaAntes = db.ConfigLicencas.Single().UltimaValidacaoEm;

        licenca.AvaliarNoInicio();
        licenca.AvaliarNoInicio();

        // A janela conta da validação ONLINE, não de cada abertura — senão a licença
        // vencida nunca expiraria (o defeito que este controle corrige).
        Assert.Equal(ultimaAntes, db.ConfigLicencas.Single().UltimaValidacaoEm);
        Assert.Equal(CodigosLicenca.EmTolerancia, licenca.Avaliar().Codigo);
    }

    // ── 12. Renovação feita pelo instalador libera o app ─────────────────────
    [Fact]
    public void Renovar_a_licenca_volta_a_permitir_operar()
    {
        using var banco = new BancoDeTeste();
        using var db = banco.Criar();
        var (empresa, _, _, _, _) = banco.Semear(db);
        using var autoridade = ProvedorDeChaves.EmMemoria();

        var (tokenVencido, expirado) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(-1));
        var registro = Registrar(db, empresa.Id, tokenVencido, expirado, ultimaValidacao: Ancora.AddDays(-30));

        var licenca = new ServicoDeLicenca(db, autoridade.ChaveLicenca.ExportSubjectPublicKeyInfo(),
            new RelogioFalso(Ancora));
        Assert.False(licenca.Avaliar().PodeOperar);

        // Renovação: é o que o instalador grava ao reativar (passo 4), com o MESMO emissor.
        var (tokenNovo, expiraNovo) = Emitir(autoridade, empresa.Id, TimeSpan.FromDays(365));
        registro.RegistrarValidacao(Ancora, expiraNovo, tokenNovo, "lic-api");
        db.SaveChanges();

        var estado = licenca.Avaliar();
        Assert.True(estado.PodeOperar);
        Assert.Equal(CodigosLicenca.Ok, estado.Codigo);
    }
}
