using System.Security.Cryptography;
using System.Text;
using Landipay.Backend.Dados;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Backend.Servicos;

public sealed record ResultadoCriacaoSessao(Guid Id, string Codigo, DateTimeOffset ExpiraEm);
public sealed record ResultadoConsentimento(bool Ok, string? Erro, string? Credencial, DateTimeOffset? ExpiraEm);

/// <summary>
/// Suporte remoto como <b>evento consentido e temporário</b> (A1). O que substitui a senha
/// fixa do legado:
///  - o técnico NUNCA recebe senha permanente; recebe um código de 6 dígitos, de uso único,
///    que só vale se o operador da loja digitar na tela de consentimento;
///  - o código e a credencial são gravados como hash (vazamento do banco não vira acesso);
///  - TTL de 30 minutos, renovado no consentimento, nunca estendido por uso;
///  - encerramento explícito e auditado, com autor;
///  - nada é instalado como serviço auto-start (não há canal permanente).
/// </summary>
public sealed class ServicoDeSuporte
{
    public static readonly TimeSpan TtlPadrao = TimeSpan.FromMinutes(30);

    private readonly BackendDbContext _db;
    private readonly TimeProvider _relogio;

    public ServicoDeSuporte(BackendDbContext db, TimeProvider? relogio = null)
    {
        _db = db;
        _relogio = relogio ?? TimeProvider.System;
    }

    public ResultadoCriacaoSessao Criar(Guid empresaId, Guid lojaId, string tecnico, string motivo,
        string escopo = "somente_leitura", TimeSpan? ttl = null)
    {
        if (empresaId == Guid.Empty) throw new ArgumentException("Empresa é obrigatória.");
        if (lojaId == Guid.Empty) throw new ArgumentException("Loja é obrigatória.");
        if (string.IsNullOrWhiteSpace(tecnico)) throw new ArgumentException("Técnico é obrigatório.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            throw new ArgumentException("Motivo precisa descrever o atendimento (mínimo 10 caracteres).");

        var duracao = ttl ?? TtlPadrao;
        if (duracao > TimeSpan.FromHours(2))
            throw new ArgumentException("TTL máximo é 2 horas.");

        var agora = _relogio.GetUtcNow();
        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var sessao = new SessaoSuporteRegistro
        {
            EmpresaId = empresaId,
            LojaId = lojaId,
            Tecnico = tecnico.Trim(),
            Motivo = motivo.Trim(),
            Escopo = escopo,
            CodigoHash = Hash(codigo),
            TtlMinutos = (int)duracao.TotalMinutes,
            CriadaEm = agora,
            ExpiraEm = agora.Add(duracao),
            Status = StatusSessaoSuporte.AguardandoConsentimento
        };

        _db.Sessoes.Add(sessao);
        _db.Auditoria.Add(new EventoAuditoriaApi
        {
            Acao = "suporte.sessao_criada",
            Detalhe = $"sessao={sessao.Id} tecnico={sessao.Tecnico} escopo={sessao.Escopo} ttl={sessao.TtlMinutos}min",
            Ator = sessao.Tecnico
        });
        _db.SaveChanges();

        return new ResultadoCriacaoSessao(sessao.Id, codigo, sessao.ExpiraEm);
    }

    /// <summary>Chamado pelo app na loja, depois de o operador digitar o código na tela.</summary>
    public ResultadoConsentimento Consentir(Guid sessaoId, string? codigo)
    {
        var sessao = _db.Sessoes.SingleOrDefault(s => s.Id == sessaoId);
        if (sessao is null) return new(false, "sessao_inexistente", null, null);

        var agora = _relogio.GetUtcNow();

        if (sessao.Status == StatusSessaoSuporte.Encerrada)
            return new(false, "sessao_encerrada", null, null);

        if (sessao.Status == StatusSessaoSuporte.Expirada || agora > sessao.ExpiraEm)
        {
            sessao.Status = StatusSessaoSuporte.Expirada;
            _db.SaveChanges();
            return new(false, "sessao_expirada", null, null);
        }

        if (sessao.CodigoUsado || sessao.Status != StatusSessaoSuporte.AguardandoConsentimento)
            return new(false, "codigo_ja_utilizado", null, null);

        if (string.IsNullOrWhiteSpace(codigo) || !HashConfere(codigo.Trim(), sessao.CodigoHash))
            return new(false, "codigo_invalido", null, null);

        var credencial = CredencialAleatoria();

        sessao.CodigoUsado = true;
        sessao.CredencialHash = Hash(credencial);
        sessao.ConsentidaEm = agora;
        sessao.Status = StatusSessaoSuporte.Ativa;
        sessao.ExpiraEm = agora.AddMinutes(sessao.TtlMinutos);

        _db.Auditoria.Add(new EventoAuditoriaApi
        {
            Acao = "suporte.consentido",
            Detalhe = $"sessao={sessao.Id} tecnico={sessao.Tecnico} expira={sessao.ExpiraEm:O}",
            Ator = "operador-local"
        });
        _db.SaveChanges();

        return new(true, null, credencial, sessao.ExpiraEm);
    }

    public bool Encerrar(Guid sessaoId, string ator)
    {
        var sessao = _db.Sessoes.SingleOrDefault(s => s.Id == sessaoId);
        if (sessao is null || sessao.Status == StatusSessaoSuporte.Encerrada) return false;

        sessao.Status = StatusSessaoSuporte.Encerrada;
        sessao.EncerradaEm = _relogio.GetUtcNow();
        sessao.EncerradaPor = string.IsNullOrWhiteSpace(ator) ? "sistema" : ator.Trim();

        _db.Auditoria.Add(new EventoAuditoriaApi
        {
            Acao = "suporte.encerrado",
            Detalhe = $"sessao={sessao.Id} por={sessao.EncerradaPor}",
            Ator = sessao.EncerradaPor
        });
        _db.SaveChanges();
        return true;
    }

    /// <summary>Consulta com aplicação de expiração preguiçosa (não existe sessão "vencida e ativa").</summary>
    public SessaoSuporteRegistro? Obter(Guid sessaoId)
    {
        var sessao = _db.Sessoes.AsNoTracking().SingleOrDefault(s => s.Id == sessaoId);
        if (sessao is null) return null;

        if (sessao.Status is StatusSessaoSuporte.Ativa or StatusSessaoSuporte.AguardandoConsentimento
            && _relogio.GetUtcNow() > sessao.ExpiraEm)
        {
            return new SessaoSuporteRegistro
            {
                Id = sessao.Id,
                EmpresaId = sessao.EmpresaId,
                LojaId = sessao.LojaId,
                Tecnico = sessao.Tecnico,
                Motivo = sessao.Motivo,
                Escopo = sessao.Escopo,
                Status = StatusSessaoSuporte.Expirada,
                CriadaEm = sessao.CriadaEm,
                ExpiraEm = sessao.ExpiraEm,
                ConsentidaEm = sessao.ConsentidaEm,
                EncerradaEm = sessao.EncerradaEm,
                TtlMinutos = sessao.TtlMinutos
            };
        }

        return sessao;
    }

    private static string Hash(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();

    private static bool HashConfere(string valor, string hashEsperado)
    {
        var calculado = SHA256.HashData(Encoding.UTF8.GetBytes(valor));
        byte[] esperado;
        try { esperado = Convert.FromHexString(hashEsperado); }
        catch (FormatException) { return false; }

        return calculado.Length == esperado.Length &&
               CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static string CredencialAleatoria() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
