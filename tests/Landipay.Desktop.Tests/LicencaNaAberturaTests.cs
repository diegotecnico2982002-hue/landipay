using System.IO;
using Landipay.Client.Seguranca;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Ui;
using Landipay.Ui.ViewModels;
using Xunit;

namespace Landipay.Desktop.Tests;

/// <summary>
/// Abertura do aplicativo com licença bloqueante (P0-3), exercitando a composição real
/// (<see cref="ComposicaoApp"/>), que é quem monta o caixa e decide a primeira tela.
/// Sem licença gravada, a tela inicial tem de ser a de licença — não o login.
/// </summary>
public sealed class LicencaNaAberturaTests : IDisposable
{
    private readonly string _pasta;
    private readonly string? _anterior;

    public LicencaNaAberturaTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), $"landipay-lic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_pasta);
        _anterior = Environment.GetEnvironmentVariable("LANDIPAY_DADOS");
        Environment.SetEnvironmentVariable("LANDIPAY_DADOS", _pasta);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LANDIPAY_DADOS", _anterior);
        try { Directory.Delete(_pasta, recursive: true); } catch { /* arquivo travado no Windows */ }
    }

    [Fact]
    public void Sem_licenca_o_app_abre_na_tela_de_licenca_e_nao_no_login()
    {
        using var app = new ComposicaoApp(Aplicativo.Pos);

        app.Iniciar();

        var tela = Assert.IsType<LicencaViewModel>(app.Shell.Atual);
        Assert.False(app.EstadoDaLicenca.PodeOperar);
        Assert.Equal(CodigosLicenca.Ausente, app.EstadoDaLicenca.Codigo);
        Assert.Contains("Sem licença", tela.Titulo);

        // E a negativa fica na trilha, para o suporte saber por que o caixa não abriu.
        Assert.True(app.Db.Auditoria.Any(a => a.Acao == "licenca.ausente"));
    }

    [Fact]
    public void Com_licenca_expirada_o_app_bloqueia_e_o_botao_reavalia()
    {
        using var app = new ComposicaoApp(Aplicativo.Pos);

        var empresa = Empresa.Criar("Mercado Central Ltda", "Mercado Central", "12345678000190",
            RegimeTributario.SimplesNacional,
            new Domain.ValueObjects.Endereco("Rua das Acácias", "1200", null, "Centro", "Campinas", "SP", "13010000"));
        app.Db.Empresas.Add(empresa);

        // Token opaco no formato JWT, vencido: o serviço lê o exp do payload e recusa.
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                $"{{\"iss\":\"landipay-api\",\"aud\":\"landipay-client\",\"sub\":\"instalacao-teste\",\"exp\":{DateTimeOffset.UtcNow.AddDays(-40).ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"eyJhbGciOiJFUzI1NiJ9.{payload}.assinatura";

        var registro = ConfigLicenca.Registrar(empresa.Id, "instalacao-teste", token, "lic-api",
            DateTimeOffset.UtcNow.AddDays(-400), DateTimeOffset.UtcNow.AddDays(-40));
        registro.RegistrarValidacao(DateTimeOffset.UtcNow.AddDays(-60));
        app.Db.ConfigLicencas.Add(registro);
        app.Db.SaveChanges();

        app.Iniciar();

        Assert.IsType<LicencaViewModel>(app.Shell.Atual);
        Assert.Equal(CodigosLicenca.Expirada, app.EstadoDaLicenca.Codigo);
        Assert.False(app.EstadoDaLicenca.PodeOperar);

        // Revalidar sem renovar mantém o bloqueio (não há como "insistir" e entrar).
        app.VerificarLicenca();
        Assert.IsType<LicencaViewModel>(app.Shell.Atual);
        Assert.False(app.EstadoDaLicenca.PodeOperar);
    }
}
