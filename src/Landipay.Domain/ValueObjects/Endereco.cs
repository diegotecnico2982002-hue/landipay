using Landipay.Domain.Common;

namespace Landipay.Domain.ValueObjects;

/// <summary>
/// Endereço como objeto de valor (owned type no EF Core — não tem tabela própria).
/// Campos livres são limitados por tamanho para não virar depósito de lixo vindo da API de CNPJ.
/// </summary>
public sealed class Endereco
{
    public string Logradouro { get; private set; } = string.Empty;
    public string Numero { get; private set; } = string.Empty;
    public string? Complemento { get; private set; }
    public string Bairro { get; private set; } = string.Empty;
    public string Cidade { get; private set; } = string.Empty;
    public string Uf { get; private set; } = string.Empty;
    public string Cep { get; private set; } = string.Empty;

    private Endereco() { } // EF Core

    public Endereco(string logradouro, string numero, string? complemento, string bairro, string cidade, string uf, string cep)
    {
        if (string.IsNullOrWhiteSpace(logradouro)) throw new DomainException("Logradouro é obrigatório.");
        if (string.IsNullOrWhiteSpace(cidade)) throw new DomainException("Cidade é obrigatória.");

        var ufNormalizada = (uf ?? string.Empty).Trim().ToUpperInvariant();
        if (ufNormalizada.Length != 2) throw new DomainException("UF deve ter 2 letras.");

        Logradouro = logradouro.Trim();
        Numero = (numero ?? string.Empty).Trim();
        Complemento = string.IsNullOrWhiteSpace(complemento) ? null : complemento.Trim();
        Bairro = (bairro ?? string.Empty).Trim();
        Cidade = cidade.Trim();
        Uf = ufNormalizada;
        Cep = new string((cep ?? string.Empty).Where(char.IsDigit).ToArray());
    }

    public Endereco Vazio() => new("Não informado", "S/N", null, "Centro", "Não informado", "SP", "00000000");
}
