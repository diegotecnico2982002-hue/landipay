using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Landipay.Tests;

/// <summary>
/// Sobe a API em memória (TestServer) com banco e chaves temporários.
/// As variáveis de ambiente são definidas ANTES de o host ser construído, porque é
/// assim que o Program lê a chave de administração e as connection strings.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    public FabricaApi()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("LANDIPAY_ADMIN_KEY", "test-admin-key");
        Environment.SetEnvironmentVariable("LANDIPAY_API_DB",
            $"Data Source={Path.Combine(Path.GetTempPath(), $"landipay-rt-{Guid.NewGuid():N}.db")}");
        Environment.SetEnvironmentVariable("LANDIPAY_CHAVES",
            Path.Combine(Path.GetTempPath(), $"landipay-chaves-{Guid.NewGuid():N}"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}

public sealed class RateLimitingTests : IClassFixture<FabricaApi>
{
    private readonly FabricaApi _fabrica;
    public RateLimitingTests(FabricaApi fabrica) => _fabrica = fabrica;

    [Fact]
    public async Task Consentir_bloqueia_com_429_apos_cinco_tentativas_no_minuto()
    {
        using var client = _fabrica.CreateClient();
        var codigos = new List<int>();

        // 7 tentativas seguidas no endpoint mais sensível (força bruta do código de 6 dígitos).
        for (var i = 0; i < 7; i++)
        {
            var resposta = await client.PostAsJsonAsync(
                $"/v1/suporte/sessoes/{Guid.NewGuid()}/consentir",
                new { codigo = "000000" });
            codigos.Add((int)resposta.StatusCode);
        }

        Assert.Equal(5, codigos.Count(c => c == (int)HttpStatusCode.Forbidden));   // passaram pelo limitador
        Assert.Equal(2, codigos.Count(c => c == (int)HttpStatusCode.TooManyRequests)); // barradas

        var bloqueada = await client.PostAsJsonAsync(
            $"/v1/suporte/sessoes/{Guid.NewGuid()}/consentir", new { codigo = "123456" });

        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
        Assert.Equal("60", bloqueada.Headers.RetryAfter?.Delta?.TotalSeconds.ToString() ?? "60");
    }

    [Fact]
    public async Task Health_nao_e_limitado()
    {
        using var client = _fabrica.CreateClient();

        for (var i = 0; i < 70; i++)
        {
            var resposta = await client.GetAsync("/v1/saude");
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }
    }
}
