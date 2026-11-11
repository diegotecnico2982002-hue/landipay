using Landipay.Client.Seguranca;

// Valida um código de licença OFFLINE usando o MESMO validador do instalador.
//   validar-licenca "<token>" "<chave publica base64>"
// Útil para suporte: responde se o código é válido, de quem é, e quando expira.

if (args.Length < 2)
{
    Console.Error.WriteLine("uso: validar-licenca \"<token>\" \"<chave-publica-base64>\"");
    return 1;
}

byte[] publica;
try
{
    publica = Convert.FromBase64String(args[1]);
}
catch (FormatException)
{
    Console.Error.WriteLine("chave pública inválida: não é base64.");
    return 1;
}

var resultado = ValidadorDeLicencaJwt.Validar(args[0].Trim(), publica);

Console.WriteLine($"valida          : {(resultado.Valida ? "SIM" : "NÃO")}");
Console.WriteLine($"codigo          : {resultado.Codigo}");
Console.WriteLine($"instalacao      : {resultado.InstalacaoId ?? "-"}");
Console.WriteLine($"empresa         : {resultado.EmpresaId?.ToString() ?? "-"}");
Console.WriteLine($"plano           : {resultado.Plano ?? "-"}");
Console.WriteLine($"expira em       : {resultado.ExpiraEm?.ToLocalTime():dd/MM/yyyy HH:mm}");

return resultado.Valida ? 0 : 2;
