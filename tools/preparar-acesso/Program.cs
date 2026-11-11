using Landipay.Client.Seguranca;
using Landipay.Domain.Seguranca;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Prepara a estrutura de acesso em um banco já existente e mostra/concede papéis.
//
//   preparar-acesso                              -> cria permissões/papéis e lista usuários
//   preparar-acesso --admin admteste             -> concede Admin ao login informado
//   preparar-acesso --caixa operador             -> concede OperadorCaixa
//   preparar-acesso --gerente fulano             -> concede Gerente
//   preparar-acesso --banco "C:/.../landipay.db" -> outro arquivo
//
// Idempotente: rodar duas vezes não duplica nada. É o caminho de migração para bases
// criadas antes da separação App Admin × App Caixa.

string? banco = null;
string? papel = null;
string? login = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--banco": banco = args[++i]; break;
        case "--admin": papel = CatalogoDeAcesso.PapelAdmin; login = args[++i]; break;
        case "--gerente": papel = CatalogoDeAcesso.PapelGerente; login = args[++i]; break;
        case "--caixa": papel = CatalogoDeAcesso.PapelOperadorCaixa; login = args[++i]; break;
    }
}

banco ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Landipay", "landipay.db");

if (!File.Exists(banco))
{
    Console.Error.WriteLine($"Banco não encontrado: {banco}");
    return 1;
}

var opcoes = new DbContextOptionsBuilder<LandipayDbContext>()
    .UseSqlite($"Data Source={banco}")
    .EnableSensitiveDataLogging()
    .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information)
    .Options;
using var db = new LandipayDbContext(opcoes);

Console.WriteLine($"banco: {banco}");

SemeadorDeSeguranca.Garantir(db);
Console.WriteLine($"permissões: {db.Permissoes.Count()} | papéis: {db.Papeis.Count()}");

if (papel is not null && login is not null)
{
    var usuario = db.Usuarios.FirstOrDefault(u => u.Login == login.ToLowerInvariant());
    if (usuario is null)
    {
        Console.Error.WriteLine($"usuário '{login}' não existe neste banco.");
        return 2;
    }

    SemeadorDeSeguranca.ConcederPapel(db, usuario.Id, papel);
    Console.WriteLine($"papel {papel} concedido a {login}");
}

Console.WriteLine();
var autorizacao = new ServicoDeAutorizacao(db);
foreach (var u in db.Usuarios.OrderBy(u => u.Login).ToList())
{
    var papeis = autorizacao.PapeisDe(u.Id);
    var apps = papeis.SelectMany(CatalogoDeAcesso.AppsDoPapel).Distinct().ToList();
    var perms = autorizacao.PermissoesDe(u.Id);
    Console.WriteLine($"{u.Login,-14} papéis=[{string.Join(",", papeis)}] apps=[{string.Join(",", apps)}] {perms.Count} permissões");
}

return 0;
