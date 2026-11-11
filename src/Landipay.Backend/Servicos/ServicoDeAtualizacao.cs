using Landipay.Backend.Dados;
using Landipay.Backend.Seguranca;
using Landipay.Updates;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Backend.Servicos;

public sealed record ManifestoAssinado(ManifestoRelease Manifesto, string Assinatura, string KeyId);

/// <summary>
/// Publicação de releases e emissão do <b>manifesto assinado</b> (A3).
/// O servidor é o único que assina; o cliente é o único que verifica — e verifica antes
/// de baixar, durante o download e antes de executar qualquer coisa.
/// </summary>
public sealed class ServicoDeAtualizacao
{
    private readonly BackendDbContext _db;
    private readonly ProvedorDeChaves _chaves;

    public ServicoDeAtualizacao(BackendDbContext db, ProvedorDeChaves chaves)
    {
        _db = db;
        _chaves = chaves;
    }

    public ReleaseRegistro Publicar(string versao, string canal, string url, string sha256, long tamanho,
        string versaoMinima = "0.0.0")
    {
        if (string.IsNullOrWhiteSpace(versao)) throw new ArgumentException("Versão é obrigatória.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("URL do release precisa ser HTTPS.");
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 precisa ter 64 caracteres hexadecimais.");
        if (tamanho <= 0) throw new ArgumentException("Tamanho precisa ser positivo.");

        var existente = _db.Releases.FirstOrDefault(r => r.Canal == canal && r.Versao == versao);
        if (existente is not null)
        {
            existente.Url = url;
            existente.Sha256 = sha256.ToLowerInvariant();
            existente.Tamanho = tamanho;
            existente.VersaoMinima = versaoMinima;
            existente.PublicadoEm = DateTimeOffset.UtcNow;
            _db.SaveChanges();
            return existente;
        }

        var release = new ReleaseRegistro
        {
            Versao = versao.Trim(),
            Canal = string.IsNullOrWhiteSpace(canal) ? "stable" : canal.Trim(),
            Url = url,
            Sha256 = sha256.ToLowerInvariant(),
            Tamanho = tamanho,
            VersaoMinima = versaoMinima,
            PublicadoEm = DateTimeOffset.UtcNow
        };

        _db.Releases.Add(release);
        _db.Auditoria.Add(new EventoAuditoriaApi
        {
            Acao = "release.publicado",
            Detalhe = $"{release.Canal}/{release.Versao} sha256={release.Sha256}",
            Ator = "api"
        });
        _db.SaveChanges();

        return release;
    }

    /// <summary>Manifesto do release mais recente do canal, já assinado com Ed25519.</summary>
    public ManifestoAssinado? ObterManifesto(string canal = "stable")
    {
        // SQLite não ordena DateTimeOffset no SQL: a ordenação é feita em memória
        // (a tabela de releases é pequena e o índice por canal já filtra).
        var doCanal = _db.Releases.Where(r => r.Canal == canal).ToList();
        var release = doCanal.OrderByDescending(r => r.PublicadoEm).FirstOrDefault();

        if (release is null) return null;

        var manifesto = new ManifestoRelease
        {
            Versao = release.Versao,
            Canal = release.Canal,
            Url = release.Url,
            Sha256 = release.Sha256,
            Tamanho = release.Tamanho,
            VersaoMinima = release.VersaoMinima,
            PublicadoEm = release.PublicadoEm,
            KeyId = _chaves.KidUpdate
        };

        var payload = JsonCanonico.Serializar(manifesto);
        var assinatura = AssinaturaEd25519.Assinar(payload, _chaves.SementeUpdate);

        return new ManifestoAssinado(manifesto, assinatura, _chaves.KidUpdate);
    }
}
