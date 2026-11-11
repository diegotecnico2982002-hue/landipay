using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Landipay.Updates;

/// <summary>
/// Ed25519 puro (BouncyCastle, sem dependência nativa). Escolhido em vez de ECDSA P-256
/// porque a assinatura é determinística, curta (64 bytes) e não depende de RNG no momento
/// de assinar — menos coisa para dar errado no job de release.
/// </summary>
public static class AssinaturaEd25519
{
    public const int TamanhoSemente = 32;
    public const int TamanhoChavePublica = 32;
    public const int TamanhoAssinatura = 64;

    public static (byte[] SementePrivada, byte[] ChavePublica) GerarPar()
    {
        var rng = new Org.BouncyCastle.Security.SecureRandom();
        var priv = new Ed25519PrivateKeyParameters(rng);
        return (priv.GetEncoded(), priv.GeneratePublicKey().GetEncoded());
    }

    public static byte[] ChavePublicaDe(byte[] sementePrivada)
    {
        ValidarTamanho(sementePrivada, TamanhoSemente, "semente privada");
        return new Ed25519PrivateKeyParameters(sementePrivada, 0).GeneratePublicKey().GetEncoded();
    }

    public static string Assinar(byte[] payload, byte[] sementePrivada)
    {
        ValidarTamanho(sementePrivada, TamanhoSemente, "semente privada");
        var assinador = new Ed25519Signer();
        assinador.Init(true, new Ed25519PrivateKeyParameters(sementePrivada, 0));
        assinador.BlockUpdate(payload, 0, payload.Length);
        return Convert.ToBase64String(assinador.GenerateSignature());
    }

    /// <summary>
    /// Retorna false para qualquer entrada inválida (assinatura truncada, base64 quebrado,
    /// chave errada). Nunca lança por dado vindo da rede.
    /// </summary>
    public static bool Verificar(byte[] payload, string assinaturaBase64, byte[] chavePublica)
    {
        if (string.IsNullOrWhiteSpace(assinaturaBase64)) return false;
        if (chavePublica is null || chavePublica.Length != TamanhoChavePublica) return false;

        byte[] assinatura;
        try { assinatura = Convert.FromBase64String(assinaturaBase64); }
        catch (FormatException) { return false; }

        if (assinatura.Length != TamanhoAssinatura) return false;

        try
        {
            var verificador = new Ed25519Signer();
            verificador.Init(false, new Ed25519PublicKeyParameters(chavePublica, 0));
            verificador.BlockUpdate(payload, 0, payload.Length);
            return verificador.VerifySignature(assinatura);
        }
        catch
        {
            return false;
        }
    }

    private static void ValidarTamanho(byte[] valor, int esperado, string nome)
    {
        if (valor is null || valor.Length != esperado)
            throw new ArgumentException($"{nome} deve ter {esperado} bytes.", nameof(valor));
    }
}
