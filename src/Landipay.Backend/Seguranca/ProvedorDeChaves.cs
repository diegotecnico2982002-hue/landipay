using System.Security.Cryptography;
using System.Text.Json;
using Landipay.Updates;

namespace Landipay.Backend.Seguranca;

/// <summary>
/// Custódia de chaves. Em produção isto é um KMS/HSM: a chave privada nunca sai de lá e
/// a assinatura é uma chamada de API. Aqui, para desenvolvimento e teste, as chaves são
/// geradas localmente e gravadas em arquivo fora do repositório (<c>chaves/</c> é gitignored).
///
/// Duas chaves, dois propósitos (A3): <c>lic-*</c> assina licença (ES256), <c>upd-*</c>
/// assina manifesto de update (Ed25519). Comprometer uma não compromete a outra.
/// </summary>
public sealed class ProvedorDeChaves : IDisposable
{
    private const string ArquivoChaves = "chaves.json";

    public ECDsa ChaveLicenca { get; }
    public string KidLicenca { get; }
    public byte[] SementeUpdate { get; }
    public byte[] ChavePublicaUpdate { get; }
    public string KidUpdate { get; }

    private ProvedorDeChaves(ECDsa chaveLicenca, string kidLicenca, byte[] sementeUpdate, byte[] chavePublicaUpdate, string kidUpdate)
    {
        ChaveLicenca = chaveLicenca;
        KidLicenca = kidLicenca;
        SementeUpdate = sementeUpdate;
        ChavePublicaUpdate = chavePublicaUpdate;
        KidUpdate = kidUpdate;
    }

    /// <summary>Par efêmero em memória — usado em testes e em ambiente de desenvolvimento.</summary>
    public static ProvedorDeChaves EmMemoria()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (semente, publica) = AssinaturaEd25519.GerarPar();
        return new ProvedorDeChaves(ecdsa, KidDe(ecdsa, "lic"), semente, publica, KidDeBytes(publica, "upd"));
    }

    /// <summary>Carrega do diretório ou cria na primeira execução (arquivo 0600 por convenção do SO).</summary>
    public static ProvedorDeChaves CarregarOuCriar(string pasta)
    {
        Directory.CreateDirectory(pasta);
        var caminho = Path.Combine(pasta, ArquivoChaves);

        if (File.Exists(caminho))
        {
            var dto = JsonSerializer.Deserialize<ChavesDto>(File.ReadAllText(caminho))
                      ?? throw new InvalidOperationException("Arquivo de chaves inválido.");

            var pkcs8 = Convert.FromBase64String(dto.LicencaPkcs8);
            var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);

            var semente = Convert.FromBase64String(dto.UpdateSeed);
            var publica = AssinaturaEd25519.ChavePublicaDe(semente);

            if (publica.Length != AssinaturaEd25519.TamanhoChavePublica)
                throw new InvalidOperationException("Chave pública Ed25519 inválida no arquivo de chaves.");

            return new ProvedorDeChaves(ecdsa, dto.KidLicenca, semente, publica, dto.KidUpdate);
        }

        var novo = EmMemoria();
        var conteudo = new ChavesDto
        {
            KidLicenca = novo.KidLicenca,
            LicencaPkcs8 = Convert.ToBase64String(novo.ChaveLicenca.ExportPkcs8PrivateKey()),
            KidUpdate = novo.KidUpdate,
            UpdateSeed = Convert.ToBase64String(novo.SementeUpdate)
        };
        File.WriteAllText(caminho, JsonSerializer.Serialize(conteudo, new JsonSerializerOptions { WriteIndented = true }));
        return novo;
    }

    /// <summary>Chave pública do manifesto em base64 (é o que se embarca/pina no cliente).</summary>
    public string ChavePublicaUpdateBase64 => Convert.ToBase64String(ChavePublicaUpdate);

    /// <summary>Chave pública ES256 em SPKI base64 — usada para validar licença offline.</summary>
    public string ChavePublicaLicencaBase64 => Convert.ToBase64String(ChaveLicenca.ExportSubjectPublicKeyInfo());

    public void Dispose() => ChaveLicenca.Dispose();

    private static string KidDe(ECDsa chave, string prefixo) =>
        KidDeBytes(chave.ExportSubjectPublicKeyInfo(), prefixo);

    private static string KidDeBytes(byte[] material, string prefixo) =>
        $"{prefixo}-{Convert.ToHexString(SHA256.HashData(material))[..12].ToLowerInvariant()}";

    private sealed class ChavesDto
    {
        public string KidLicenca { get; set; } = string.Empty;
        public string LicencaPkcs8 { get; set; } = string.Empty;
        public string KidUpdate { get; set; } = string.Empty;
        public string UpdateSeed { get; set; } = string.Empty;
    }
}
