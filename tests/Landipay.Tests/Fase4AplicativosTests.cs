using Landipay.Backend.Seguranca;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.ValueObjects;
using Landipay.Infrastructure.Persistence;
using Xunit;

namespace Landipay.Tests;

/// <summary>
/// Passo 6 do instalador — escolha de aplicativos por estação.
/// Cobre: exigência de licença, validação da escolha, gravação idempotente,
/// auditoria da decisão e o reparo que não pede senha de novo.
/// </summary>
public sealed class Fase4AplicativosTests
{
    private static (ServicoInstalador Instalador, LandipayDbContext Db, string Pasta) Criar(BancoDeTeste banco)
    {
        var db = banco.Criar();
        var pasta = Path.Combine(Path.GetTempPath(), $"landipay-apps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        return (new ServicoInstalador(db, pasta), db, pasta);
    }

    private static DadosEmpresa EmpresaPadrao() =>
        new("12345678000190", "Mercado Central Ltda", "Mercado Central", "123456789",
            RegimeTributario.SimplesNacional, "Rua das Acácias", "1200", null, "Centro",
            "Campinas", "SP", "13010000", "01", "Loja Centro");

    private static (string Token, byte[] Publica) EmitirLicenca(Guid empresaId, string[]? apps = null)
    {
        using var chaves = ProvedorDeChaves.EmMemoria();
        var emissor = new EmissorDeLicenca(chaves);
        var (token, _, _) = emissor.Emitir(empresaId, "instalacao-teste-01", "pro", TimeSpan.FromDays(30), apps);
        return (token, chaves.ChaveLicenca.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Instalação pronta até o passo 5: empresa cadastrada + licença ativa.</summary>
    private static (ServicoInstalador Instalador, LandipayDbContext Db, string Pasta) Instalado(
        BancoDeTeste banco, string[]? apps = null)
    {
        var (instalador, db, pasta) = Criar(banco);

        var empresa = instalador.ConfigurarEmpresa(EmpresaPadrao());
        Assert.True(empresa.Ok, empresa.Mensagem);

        var empresaId = db.Empresas.Select(e => e.Id).First();
        var (token, publica) = EmitirLicenca(empresaId, apps);
        var licenca = instalador.AtivarLicenca(token, publica);
        Assert.True(licenca.Ok, licenca.Mensagem);

        return (instalador, db, pasta);
    }

    [Fact]
    public void Sem_licenca_nao_configura_estacao()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, pasta) = Criar(banco);
        instalador.ConfigurarEmpresa(EmpresaPadrao());

        var resultado = instalador.SelecionarAplicativos(new EscolhaDeAplicativos(true, true));

        Assert.False(resultado.Ok);
        Assert.Contains("licença", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(pasta, "apps.json")));
    }

    [Fact]
    public void Nenhum_app_marcado_e_recusado()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Instalado(banco);

        var resultado = instalador.SelecionarAplicativos(new EscolhaDeAplicativos(false, false));

        Assert.False(resultado.Ok);
        Assert.Contains("ao menos um", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Somente_caixa_grava_estacao_pos()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Instalado(banco);

        var resultado = instalador.SelecionarAplicativos(new EscolhaDeAplicativos(false, true));

        Assert.True(resultado.Ok, resultado.Mensagem);
        var config = instalador.EstacaoConfigurada();
        Assert.NotNull(config);
        Assert.Equal(TipoEstacao.Pos, config!.Tipo);
        Assert.Equal(new[] { "pos" }, config.Aplicativos);
        Assert.False(string.IsNullOrWhiteSpace(config.Maquina));
    }

    [Fact]
    public void Escolha_e_auditada_com_o_tipo_da_estacao()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Instalado(banco);

        instalador.SelecionarAplicativos(new EscolhaDeAplicativos(true, false));

        // SQLite não ordena DateTimeOffset no SQL — ordena em memória (mesmo padrão do relatório).
        var evento = db.Auditoria.ToList().OrderByDescending(a => a.OcorridoEm).First();
        Assert.Equal("instalacao.aplicativos", evento.Acao);
        Assert.Contains("somente Admin", evento.DadosJson);
        Assert.False(string.IsNullOrWhiteSpace(evento.Maquina));
    }

    [Fact]
    public void Reconfigurar_substitui_sem_duplicar_arquivo()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, pasta) = Instalado(banco);

        instalador.SelecionarAplicativos(new EscolhaDeAplicativos(true, false));
        var depois = instalador.SelecionarAplicativos(new EscolhaDeAplicativos(true, true));

        Assert.True(depois.Ok, depois.Mensagem);
        var config = instalador.EstacaoConfigurada()!;
        Assert.Equal(TipoEstacao.Mista, config.Tipo);
        Assert.Equal(2, config.Aplicativos.Count);
        Assert.Single(Directory.GetFiles(pasta, "apps.json"));
    }

    [Fact]
    public void Reparo_nao_pede_senha_quando_administrador_ja_existe()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Instalado(banco);

        Assert.False(instalador.AdminExistente());
        var criado = instalador.CriarAdministrador("admin", "SenhaBemLonga1!");
        Assert.True(criado.Ok, criado.Mensagem);

        // Segunda passada (reparo): o passo 5 não exige senha nem recria o usuário.
        Assert.True(instalador.AdminExistente());
        Assert.Single(db.Usuarios);
    }

    // ── Escopo de aplicativos vindo da licença ───────────────────────────────

    [Fact]
    public void Licenca_sem_claim_apps_libera_os_dois()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Instalado(banco);   // emissor sem escopo explícito

        Assert.Equal(new[] { "admin", "pos" }, instalador.AppsDaLicenca());
        Assert.True(instalador.SelecionarAplicativos(new EscolhaDeAplicativos(true, true)).Ok);
    }

    [Fact]
    public void Licenca_so_de_caixa_impede_habilitar_admin()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, pasta) = Instalado(banco, apps: new[] { "pos" });

        var resultado = instalador.SelecionarAplicativos(new EscolhaDeAplicativos(true, false));

        Assert.False(resultado.Ok);
        Assert.Contains("não habilita", resultado.Mensagem);
        Assert.Contains("licença permite: Caixa", resultado.Detalhe);
        Assert.False(File.Exists(Path.Combine(pasta, "apps.json")));

        var negado = db.Auditoria.ToList().OrderByDescending(a => a.OcorridoEm).First();
        Assert.Equal("instalacao.aplicativos.negado", negado.Acao);
    }

    [Fact]
    public void Licenca_so_de_caixa_permite_estacao_de_caixa()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Instalado(banco, apps: new[] { "pos" });

        var resultado = instalador.SelecionarAplicativos(new EscolhaDeAplicativos(false, true));

        Assert.True(resultado.Ok, resultado.Mensagem);
        Assert.Equal(TipoEstacao.Pos, instalador.EstacaoConfigurada()!.Tipo);
    }

    [Fact]
    public void Estacao_mista_e_recusada_quando_licenca_e_so_de_admin()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Instalado(banco, apps: new[] { "admin" });

        var resultado = instalador.SelecionarAplicativos(new EscolhaDeAplicativos(true, true));

        Assert.False(resultado.Ok);
        Assert.Contains("Landipay Caixa", resultado.Mensagem);
    }

    [Fact]
    public void Licenca_grava_o_escopo_no_banco()
    {
        using var banco = new BancoDeTeste();
        var (_, db, _) = Instalado(banco, apps: new[] { "pos", "pos", "ADMIN" });

        var licenca = db.ConfigLicencas.Single();
        Assert.Equal(new[] { "admin", "pos" }, licenca.Apps().OrderBy(a => a));
        Assert.True(licenca.Permite("admin"));
        Assert.True(licenca.Permite("pos"));
    }
}
