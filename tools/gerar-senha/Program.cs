using Landipay.Client.Seguranca;

// Gera o hash Argon2id no formato PHC exato que o Landipay grava,
// usando o MESMO hasher da aplicação (m=64MB, t=3, p=4).
var hasher = new Argon2idSenhaHasher();

if (args.Length >= 3 && args[0] == "--verificar")
{
    var ok = hasher.Verificar(args[1], args[2]);
    Console.WriteLine(ok ? "OK" : "FALHA");
    return ok ? 0 : 2;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("uso: gerar-senha \"<senha>\" | --verificar \"<senha>\" \"<hash>\"");
    return 1;
}

var senha = args[0];
var hash = hasher.Hash(senha);

// Confere o que acabou de ser gerado antes de entregar.
if (!hasher.Verificar(senha, hash))
{
    Console.Error.WriteLine("FALHA: o hash gerado não verifica contra a senha.");
    return 2;
}

Console.WriteLine(hash);
return 0;
