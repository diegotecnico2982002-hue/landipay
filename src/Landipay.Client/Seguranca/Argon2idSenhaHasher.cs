using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Landipay.Client.Seguranca;

public interface ISenhaHasher
{
    string Hash(string senha);
    bool Verificar(string senha, string hashArmazenado);
}

/// <summary>
/// Argon2id com parâmetros de produção (m=64 MB, t=3, p=4), salt aleatório por senha e
/// comparação em tempo constante. Formato PHC string, o mesmo que a coluna
/// <c>Usuarios.SenhaHash</c> do modelo de dados espera.
///
/// Elimina A5: a senha nunca é comparada em texto, nem guardada de forma reversível.
/// </summary>
public sealed class Argon2idSenhaHasher : ISenhaHasher
{
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;
    private const int MemoriaKb = 65536;   // 64 MB
    private const int Iteracoes = 3;
    private const int Paralelismo = 4;

    public string Hash(string senha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senha);

        var salt = RandomNumberGenerator.GetBytes(TamanhoSalt);
        var hash = Derivar(senha, salt, MemoriaKb, Iteracoes, Paralelismo);

        return $"$argon2id$v=19$m={MemoriaKb},t={Iteracoes},p={Paralelismo}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string senha, string hashArmazenado)
    {
        if (string.IsNullOrEmpty(senha) || string.IsNullOrWhiteSpace(hashArmazenado)) return false;

        // $argon2id$v=19$m=65536,t=3,p=4$<salt>$<hash>
        var partes = hashArmazenado.Split('$');
        if (partes.Length != 6 || partes[1] != "argon2id") return false;

        if (!TentarParametros(partes[3], out var memoria, out var iteracoes, out var paralelismo)) return false;

        byte[] salt, esperado;
        try
        {
            salt = Convert.FromBase64String(partes[4]);
            esperado = Convert.FromBase64String(partes[5]);
        }
        catch (FormatException) { return false; }

        var calculado = Derivar(senha, salt, memoria, iteracoes, paralelismo);
        return calculado.Length == esperado.Length &&
               CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static byte[] Derivar(string senha, byte[] salt, int memoriaKb, int iteracoes, int paralelismo)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(senha))
        {
            Salt = salt,
            MemorySize = memoriaKb,
            Iterations = iteracoes,
            DegreeOfParallelism = paralelismo
        };
        return argon.GetBytes(TamanhoHash);
    }

    private static bool TentarParametros(string texto, out int memoria, out int iteracoes, out int paralelismo)
    {
        memoria = iteracoes = paralelismo = 0;
        foreach (var parte in texto.Split(','))
        {
            var kv = parte.Split('=');
            if (kv.Length != 2 || !int.TryParse(kv[1], out var valor)) return false;
            switch (kv[0])
            {
                case "m": memoria = valor; break;
                case "t": iteracoes = valor; break;
                case "p": paralelismo = valor; break;
            }
        }
        return memoria > 0 && iteracoes > 0 && paralelismo > 0;
    }
}
