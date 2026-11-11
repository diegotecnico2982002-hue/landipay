using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Landipay.Backend.Dados;
using Landipay.Backend.Seguranca;
using Landipay.Backend.Servicos;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

// ─────────────────────────────────────────────────────────────────────────────
// Landipay API — licença, updates assinados, CNPJ e suporte remoto consentido.
// Nenhum segredo neste arquivo: a chave de administração vem de LANDIPAY_ADMIN_KEY
// (em Development, se ausente, é gerada em memória e impressa uma vez no console).
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

var pastaChaves = Environment.GetEnvironmentVariable("LANDIPAY_CHAVES")
                  ?? Path.Combine(AppContext.BaseDirectory, "chaves");

var connectionString = Environment.GetEnvironmentVariable("LANDIPAY_API_DB")
                       ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, "landipay-api.db")}";

builder.Services.AddSingleton(_ => ProvedorDeChaves.CarregarOuCriar(pastaChaves));
builder.Services.AddDbContext<BackendDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddScoped<EmissorDeLicenca>();
builder.Services.AddScoped<ServicoDeAtualizacao>();
builder.Services.AddScoped<ServicoDeSuporte>();
builder.Services.AddHttpClient<ServicoCnpj>();

// ── Rate limiting ────────────────────────────────────────────────────────────
// Três políticas, por IP. A mais importante é "sensivel": o código de suporte tem 6
// dígitos, então sem limite um atacante teria 1M de tentativas — com 5/min e TTL de
// 30 min, o máximo é 150 tentativas (0,015% do espaço). Também protege /licencas/validar.
builder.Services.AddRateLimiter(opcoes =>
{
    opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opcoes.OnRejected = async (contexto, token) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "60";
        await contexto.HttpContext.Response.WriteAsJsonAsync(new
        {
            erro = "limite_de_requisicoes",
            mensagem = "Muitas requisicoes. Tente novamente em 1 minuto."
        }, token);
    };

    opcoes.AddPolicy("publico", http => ParticaoPorIp(http, 60));
    opcoes.AddPolicy("sensivel", http => ParticaoPorIp(http, 5));
    opcoes.AddPolicy("admin", http => ParticaoPorIp(http, 30));
});

static RateLimitPartition<string> ParticaoPorIp(HttpContext http, int limite) =>
    RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limite,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

var app = builder.Build();

app.UseRateLimiter();

// Chave administrativa: nunca hardcoded.
var chaveAdmin = Environment.GetEnvironmentVariable("LANDIPAY_ADMIN_KEY");
if (string.IsNullOrWhiteSpace(chaveAdmin))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("LANDIPAY_ADMIN_KEY é obrigatória fora de Development.");

    chaveAdmin = "dev-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    app.Logger.LogWarning("LANDIPAY_ADMIN_KEY ausente; chave temporária gerada: {Chave}", chaveAdmin);
}

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BackendDbContext>().Database.EnsureCreated();
}

bool AdminAutorizado(HttpContext http)
{
    var recebida = http.Request.Headers["X-Admin-Key"].ToString();
    if (string.IsNullOrEmpty(recebida)) return false;

    var a = Encoding.UTF8.GetBytes(recebida);
    var b = Encoding.UTF8.GetBytes(chaveAdmin);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}

var v1 = app.MapGroup("/v1");

v1.MapGet("/saude", () => Results.Ok(new { status = "ok", versao = "0.1.0", hora = DateTimeOffset.UtcNow }));

// ── Licença ──────────────────────────────────────────────────────────────────
v1.MapPost("/licencas", (PedidoLicenca pedido, EmissorDeLicenca emissor, BackendDbContext db) =>
{
    if (pedido.InstalacaoId is null || pedido.InstalacaoId.Length < 8)
        return Results.BadRequest(new { erro = "instalacao_id_invalido" });

    // Escopo de apps é opcional no pedido: ausente = ambos (AppsPadrao). Código desconhecido
    // é erro de emissão (400), não licença silenciosamente mais permissiva.
    string[] apps;
    try
    {
        apps = EmissorDeLicenca.NormalizarApps(pedido.Apps).ToArray();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { erro = "apps_invalidos", mensagem = ex.Message });
    }

    var (token, expiraEm, kid) = emissor.Emitir(pedido.EmpresaId, pedido.InstalacaoId,
        pedido.Plano ?? "padrao", apps: apps);

    var registro = db.Licencas.SingleOrDefault(l => l.InstalacaoId == pedido.InstalacaoId);
    if (registro is null)
    {
        db.Licencas.Add(new LicencaRegistro
        {
            EmpresaId = pedido.EmpresaId,
            InstalacaoId = pedido.InstalacaoId,
            Plano = pedido.Plano ?? "padrao",
            ExpiraEm = expiraEm
        });
    }
    else
    {
        registro.ExpiraEm = expiraEm;
        registro.Revogada = false;
    }

    db.Auditoria.Add(new EventoAuditoriaApi
    {
        Acao = "licenca.emitida",
        Detalhe = $"instalacao={pedido.InstalacaoId} emp={pedido.EmpresaId} expira={expiraEm:O} apps={string.Join("+", apps)}",
        Ator = "api"
    });
    db.SaveChanges();

    return Results.Ok(new { token, expiraEm, kid, algoritmo = "ES256", apps });
}).RequireRateLimiting("publico");

// Chave pública para validação offline da licença (não é segredo; é o que se pina no instalador).
v1.MapGet("/licencas/chave-publica", (ProvedorDeChaves chaves) =>
    Results.Ok(new { keyId = chaves.KidLicenca, chavePublica = chaves.ChavePublicaLicencaBase64, algoritmo = "ES256" }))
    .RequireRateLimiting("publico");

v1.MapPost("/licencas/validar", (PedidoValidacao pedido, EmissorDeLicenca emissor) =>
{
    var principal = emissor.Validar(pedido.Token ?? string.Empty);
    if (principal is null) return Results.Ok(new { valida = false });

    return Results.Ok(new
    {
        valida = true,
        instalacao = principal.FindFirst("sub")?.Value,
        empresa = principal.FindFirst("emp")?.Value,
        plano = principal.FindFirst("plano")?.Value,
        expiraEm = DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirst("exp")!.Value))
    });
}).RequireRateLimiting("sensivel");

// ── Updates (manifesto assinado) ─────────────────────────────────────────────
v1.MapGet("/updates/manifesto", (string? canal, ServicoDeAtualizacao servico) =>
{
    var manifesto = servico.ObterManifesto(string.IsNullOrWhiteSpace(canal) ? "stable" : canal);
    return manifesto is null
        ? Results.NotFound(new { erro = "nenhum_release_publicado" })
        : Results.Ok(new
        {
            manifesto = manifesto.Manifesto,
            assinatura = manifesto.Assinatura,
            keyId = manifesto.KeyId,
            algoritmo = "Ed25519"
        });
}).RequireRateLimiting("publico");

v1.MapGet("/updates/chave-publica", (ProvedorDeChaves chaves) =>
    Results.Ok(new { keyId = chaves.KidUpdate, chavePublica = chaves.ChavePublicaUpdateBase64, algoritmo = "Ed25519" }))
    .RequireRateLimiting("publico");

v1.MapPost("/updates/releases", (PedidoRelease pedido, ServicoDeAtualizacao servico, HttpContext http) =>
{
    if (!AdminAutorizado(http)) return Results.Unauthorized();

    try
    {
        var release = servico.Publicar(
            pedido.Versao ?? string.Empty,
            pedido.Canal ?? "stable",
            pedido.Url ?? string.Empty,
            pedido.Sha256 ?? string.Empty,
            pedido.Tamanho,
            pedido.VersaoMinima ?? "0.0.0");

        return Results.Created($"/v1/updates/releases/{release.Id}", release);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { erro = "release_invalido", detalhe = ex.Message });
    }
}).RequireRateLimiting("admin");

// ── CNPJ ─────────────────────────────────────────────────────────────────────
v1.MapGet("/cnpj/{cnpj}", async (string cnpj, ServicoCnpj servico, CancellationToken ct) =>
{
    var dados = await servico.ConsultarAsync(cnpj, ct);
    return dados is null
        ? Results.Json(new { offline = true, mensagem = "Consulte o CNPJ não disponível. Preencha manualmente." },
            statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(dados);
}).RequireRateLimiting("publico");

// ── Suporte remoto consentido ────────────────────────────────────────────────
v1.MapPost("/suporte/sessoes", (PedidoSessao pedido, ServicoDeSuporte servico, HttpContext http) =>
{
    if (!AdminAutorizado(http)) return Results.Unauthorized();

    try
    {
        var criada = servico.Criar(pedido.EmpresaId, pedido.LojaId, pedido.Tecnico ?? string.Empty,
            pedido.Motivo ?? string.Empty, pedido.Escopo ?? "somente_leitura");
        return Results.Ok(new { id = criada.Id, codigo = criada.Codigo, expiraEm = criada.ExpiraEm });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { erro = "sessao_invalida", detalhe = ex.Message });
    }
}).RequireRateLimiting("admin");

v1.MapPost("/suporte/sessoes/{id:guid}/consentir", (Guid id, PedidoConsentimento pedido, ServicoDeSuporte servico) =>
{
    var resultado = servico.Consentir(id, pedido.Codigo);
    return resultado.Ok
        ? Results.Ok(new { credencial = resultado.Credencial, expiraEm = resultado.ExpiraEm })
        : Results.Json(new { erro = resultado.Erro }, statusCode: StatusCodes.Status403Forbidden);
}).RequireRateLimiting("sensivel");

v1.MapPost("/suporte/sessoes/{id:guid}/encerrar", (Guid id, ServicoDeSuporte servico, HttpContext http) =>
{
    var ator = AdminAutorizado(http) ? "tecnico" : "operador-local";
    return servico.Encerrar(id, ator)
        ? Results.Ok(new { encerrada = true })
        : Results.NotFound(new { erro = "sessao_nao_encontrada_ou_encerrada" });
}).RequireRateLimiting("sensivel");

v1.MapGet("/suporte/sessoes/{id:guid}", (Guid id, ServicoDeSuporte servico, HttpContext http) =>
{
    if (!AdminAutorizado(http)) return Results.Unauthorized();
    var sessao = servico.Obter(id);
    return sessao is null ? Results.NotFound() : Results.Ok(sessao);
}).RequireRateLimiting("admin");

app.Run();

// ── Contratos de entrada ─────────────────────────────────────────────────────
public sealed record PedidoLicenca(Guid EmpresaId, string? InstalacaoId, string? Plano, string[]? Apps);
public sealed record PedidoValidacao(string? Token);
public sealed record PedidoRelease(string? Versao, string? Canal, string? Url, string? Sha256, long Tamanho, string? VersaoMinima);
public sealed record PedidoSessao(Guid EmpresaId, Guid LojaId, string? Tecnico, string? Motivo, string? Escopo);
public sealed record PedidoConsentimento(string? Codigo);

public partial class Program { }
