using System.Text;
using System.Text.Json;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Client.Seguranca;

/// <summary>Códigos estáveis de estado da licença — a tela mostra o código, não um texto inventado.</summary>
public static class CodigosLicenca
{
    public const string Ok = "licenca_ok";
    public const string Ausente = "licenca_ausente";
    public const string EmTolerancia = "licenca_em_tolerancia";
    public const string Expirada = "licenca_expirada";
    public const string Revogada = "licenca_revogada";
    public const string Adulterada = "licenca_adulterada";
}

/// <summary>Licença que não permite operar. Carrega o código para a UI e para o log.</summary>
public sealed class LicencaBloqueadaException : Exception
{
    public string Codigo { get; }

    public LicencaBloqueadaException(string codigo, string mensagem) : base(mensagem) => Codigo = codigo;
}

/// <summary>Retrato da licença num instante. <see cref="PodeOperar"/> é o que a operação consulta.</summary>
public sealed record EstadoLicenca(
    StatusLicenca? Status,
    bool PodeOperar,
    string Codigo,
    string Mensagem,
    DateTimeOffset? ExpiraEm,
    int DiasRestantes,
    DateTimeOffset? UltimaValidacaoEm,
    bool AssinaturaVerificada)
{
    public bool EmTolerancia => Codigo == CodigosLicenca.EmTolerancia;

    /// <summary>Dentro da janela de aviso: ainda opera, mas a tela precisa avisar.</summary>
    public bool ProximaDoVencimento => PodeOperar && DiasRestantes <= ServicoDeLicenca.DiasDeAviso;
}

/// <summary>
/// Licença <b>em operação</b> — o que faltava: até aqui a licença só era conferida no
/// instalador, então o app abria e vendia com licença vencida.
///
/// Regras (todas verificáveis nos testes):
/// <list type="bullet">
/// <item><b>Ausente</b> — sem registro em <c>ConfigLicencas</c>: não opera.</item>
/// <item><b>Revogada</b> — não opera, independentemente de data.</item>
/// <item><b>Expirada</b> — passou de <c>ExpiraEm</c> e da janela de tolerância: não opera.</item>
/// <item><b>Em tolerância</b> — passou de <c>ExpiraEm</c>, mas está dentro de
/// <c>ToleranciaOfflineDias</c> contados da <b>última validação online</b>: opera com aviso.</item>
/// <item><b>Adulterada</b> — o <c>exp</c> do token não bate com o gravado, ou a assinatura não
/// confere quando há chave pública: não opera.</item>
/// </list>
///
/// A tolerância <b>não é renovada offline</b> de propósito: se cada abertura gravasse
/// <c>UltimaValidacaoEm</c>, a janela nunca fecharia e a licença viraria perpétua — o erro que
/// o legado cometia por omissão.
/// </summary>
public sealed class ServicoDeLicenca
{
    /// <summary>Dias antes do vencimento em que a operação começa a avisar.</summary>
    public const int DiasDeAviso = 7;

    /// <summary>Janela mínima entre dois registros de auditoria do mesmo estado.</summary>
    public static readonly TimeSpan IntervaloDeAuditoria = TimeSpan.FromHours(12);

    private readonly LandipayDbContext _db;
    private readonly TimeProvider _relogio;
    private readonly byte[]? _chavePublicaSpki;

    /// <param name="chavePublicaSpki">
    /// Chave ES256 pinada. Quando informada, a assinatura do token é conferida a cada avaliação;
    /// quando ausente (implantação atual, sem chave pinada no binário), o estado é calculado a
    /// partir das claims + do registro e <see cref="EstadoLicenca.AssinaturaVerificada"/> fica
    /// <c>false</c> — declarado, não escondido.
    /// </param>
    public ServicoDeLicenca(LandipayDbContext db, byte[]? chavePublicaSpki = null, TimeProvider? relogio = null)
    {
        _db = db;
        _chavePublicaSpki = chavePublicaSpki;
        _relogio = relogio ?? TimeProvider.System;
    }

    /// <summary>Avalia sem gravar nada. Chamado a cada operação sensível.</summary>
    public EstadoLicenca Avaliar()
    {
        var agora = _relogio.GetUtcNow();
        var registro = Registro();

        if (registro is null)
            return new EstadoLicenca(null, false, CodigosLicenca.Ausente,
                "Nenhuma licença registrada nesta instalação. Abra o instalador do Landipay (reparo) e ative a licença.",
                null, 0, null, AssinaturaVerificada: false);

        // 1) O exp do token manda. Se o registro diz que vale mais que o token assinado,
        //    alguém esticou a data no banco. Comparação em segundos: o JWT só tem precisão
        //    de segundo, então o valor gravado é truncado antes de comparar.
        var expDoToken = LerExpiracaoDoToken(registro.TokenAssinado);
        var expGravado = DateTimeOffset.FromUnixTimeSeconds(registro.ExpiraEm.ToUnixTimeSeconds());

        if (expDoToken is null || expDoToken.Value < expGravado)
            return Adulterada(registro, "o vencimento gravado não corresponde ao token assinado", agora);

        // 2) Com chave pinada, assinatura inválida bloqueia. Expiração não é adulteração —
        //    é tratada adiante, com tolerância.
        var assinaturaOk = false;
        if (_chavePublicaSpki is { Length: > 0 })
        {
            var verificacao = ValidadorDeLicencaJwt.Validar(registro.TokenAssinado, _chavePublicaSpki, agora);
            assinaturaOk = verificacao.Valida || verificacao.Codigo == "licenca_expirada";

            if (!assinaturaOk)
                return Adulterada(registro, $"token recusado pelo validador ({verificacao.Codigo})", agora);
        }

        // 3) Revogação vence data.
        if (registro.Status == StatusLicenca.Revogada)
            return new EstadoLicenca(StatusLicenca.Revogada, false, CodigosLicenca.Revogada,
                "Licença revogada. Fale com o suporte para regularizar antes de voltar a vender.",
                registro.ExpiraEm, DiasRestantes(registro, agora), registro.UltimaValidacaoEm, assinaturaOk);

        // 4) Status pela data, com a tolerância offline.
        var status = registro.Reavaliar(agora);
        var dias = DiasRestantes(registro, agora);

        return status switch
        {
            StatusLicenca.EmTolerancia => new EstadoLicenca(status, true, CodigosLicenca.EmTolerancia,
                $"Licença vencida em {registro.ExpiraEm:dd/MM/yyyy} e a janela offline termina em " +
                $"{registro.UltimaValidacaoEm!.Value.AddDays(registro.ToleranciaOfflineDias):dd/MM/yyyy}. " +
                "Conecte para renovar — depois disso o caixa para.",
                registro.ExpiraEm, dias, registro.UltimaValidacaoEm, assinaturaOk),

            StatusLicenca.Expirada => new EstadoLicenca(status, false, CodigosLicenca.Expirada,
                $"Licença expirada em {registro.ExpiraEm:dd/MM/yyyy} e a janela offline já encerrou. " +
                "Abra o instalador do Landipay (reparo) e ative a licença nova.",
                registro.ExpiraEm, dias, registro.UltimaValidacaoEm, assinaturaOk),

            _ => new EstadoLicenca(status, true, CodigosLicenca.Ok,
                dias <= DiasDeAviso
                    ? $"Licença válida até {registro.ExpiraEm:dd/MM/yyyy} ({dias} dia(s)). Renove antes do vencimento."
                    : $"Licença válida até {registro.ExpiraEm:dd/MM/yyyy}.",
                registro.ExpiraEm, dias, registro.UltimaValidacaoEm, assinaturaOk)
        };
    }

    /// <summary>
    /// Porta de entrada das operações que movimentam dinheiro/estoque. Lança
    /// <see cref="LicencaBloqueadaException"/> e registra o bloqueio (no máximo um por dia,
    /// para não inundar a trilha).
    /// </summary>
    public void ExigirOperacao(string operacao, Guid? usuarioId = null)
    {
        var estado = Avaliar();
        if (estado.PodeOperar) return;

        Auditar("licenca.bloqueio", "Licenca", usuarioId,
            $"{{\"operacao\":\"{operacao}\",\"codigo\":\"{estado.Codigo}\"}}");

        throw new LicencaBloqueadaException(estado.Codigo,
            $"Operação '{operacao}' bloqueada: {estado.Mensagem}");
    }

    /// <summary>
    /// Avaliação de abertura do app: grava o status e registra a transição na auditoria
    /// (uma vez a cada 12 h por estado). Chamado pelo <c>ComposicaoApp</c> e pela tela de licença.
    /// </summary>
    public EstadoLicenca AvaliarNoInicio()
    {
        var estado = Avaliar();
        var registro = Registro();

        if (registro is not null)
        {
            // Só o status muda; ExpiraEm, UltimaValidacaoEm e token ficam intactos.
            _db.SaveChanges();
        }

        Auditar(AcaoDeAuditoria(estado.Codigo), "Licenca", usuarioId: null,
            $"{{\"expira\":\"{estado.ExpiraEm:yyyy-MM-dd}\",\"dias\":{estado.DiasRestantes}," +
            $"\"tolerancia\":{estado.EmTolerancia.ToString().ToLowerInvariant()}," +
            $"\"assinaturaVerificada\":{estado.AssinaturaVerificada.ToString().ToLowerInvariant()}}}");

        return estado;
    }

    private static string AcaoDeAuditoria(string codigo) => codigo switch
    {
        CodigosLicenca.Ok => "licenca.validada",
        CodigosLicenca.EmTolerancia => "licenca.em_tolerancia",
        CodigosLicenca.Ausente => "licenca.ausente",
        CodigosLicenca.Expirada => "licenca.expirada",
        CodigosLicenca.Revogada => "licenca.revogada",
        _ => "licenca.adulterada"
    };

    /// <summary>
    /// Licença mais recente da instalação. O <c>OrderBy</c> acontece <b>em memória</b>: o
    /// SQLite grava <c>DateTimeOffset</c> como texto e o EF recusa ordenar por ele no SQL.
    /// </summary>
    private ConfigLicenca? Registro() =>
        _db.ConfigLicencas.ToList()
            .OrderBy(l => l.EmitidaEm)
            .ThenBy(l => l.Id)
            .LastOrDefault();

    private EstadoLicenca Adulterada(ConfigLicenca registro, string motivo, DateTimeOffset agora) =>
        new(registro.Status, false, CodigosLicenca.Adulterada,
            $"Licença inconsistente ({motivo}). Não é possível operar até regularizar a ativação.",
            registro.ExpiraEm, DiasRestantes(registro, agora), registro.UltimaValidacaoEm, AssinaturaVerificada: false);

    private static int DiasRestantes(ConfigLicenca registro, DateTimeOffset agora) =>
        (int)Math.Ceiling((registro.ExpiraEm - agora).TotalDays);

    /// <summary>Grava o evento, no máximo uma vez por <see cref="IntervaloDeAuditoria"/>.</summary>
    private void Auditar(string acao, string entidade, Guid? usuarioId, string? dadosJson)
    {
        var agora = _relogio.GetUtcNow();
        var desde = agora.Subtract(IntervaloDeAuditoria);

        // O filtro por ação vai para o SQL (texto); a comparação de data acontece em memória,
        // porque o SQLite grava DateTimeOffset como texto e o EF não traduz a comparação.
        var registrosDaAcao = _db.Auditoria.Where(a => a.Acao == acao).ToList();
        if (registrosDaAcao.Any(a => a.OcorridoEm > desde)) return;

        _db.Auditoria.Add(LogAuditoria.Registrar(acao, "Licenca", usuarioId,
            empresaId: _db.ConfigLicencas.Select(l => (Guid?)l.EmpresaId).FirstOrDefault(),
            entidadeId: _db.ConfigLicencas.Select(l => l.InstalacaoId).FirstOrDefault(),
            dadosJson: dadosJson, origem: OrigemLog.Sistema, quando: agora,
            maquina: Environment.MachineName));
        _db.SaveChanges();
    }

    /// <summary>Lê <c>exp</c> do payload sem confiar na assinatura — serve para detectar data esticada no banco.</summary>
    private static DateTimeOffset? LerExpiracaoDoToken(string? token)
    {
        var partes = token?.Split('.');
        if (partes is not { Length: 3 }) return null;

        try
        {
            var bruto = partes[1].Replace('-', '+').Replace('_', '/');
            bruto = (bruto.Length % 4) switch { 2 => bruto + "==", 3 => bruto + "=", _ => bruto };

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(bruto)));
            return doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var segundos)
                ? DateTimeOffset.FromUnixTimeSeconds(segundos)
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
