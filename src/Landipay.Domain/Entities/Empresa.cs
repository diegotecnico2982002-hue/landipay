using Landipay.Domain.Common;
using Landipay.Domain.Enums;
using Landipay.Domain.ValueObjects;

namespace Landipay.Domain.Entities;

public sealed class Empresa : Entity
{
    public string RazaoSocial { get; private set; } = string.Empty;
    public string NomeFantasia { get; private set; } = string.Empty;
    public string Cnpj { get; private set; } = string.Empty;
    public string? InscricaoEstadual { get; private set; }
    public RegimeTributario Regime { get; private set; } = RegimeTributario.SimplesNacional;
    public Endereco Endereco { get; private set; } = null!;
    public bool Ativa { get; private set; } = true;

    private Empresa() { }

    public static Empresa Criar(string razaoSocial, string nomeFantasia, string cnpj,
        RegimeTributario regime, Endereco endereco, string? inscricaoEstadual = null)
    {
        if (string.IsNullOrWhiteSpace(razaoSocial))
            throw new DomainException("Razão social é obrigatória.");

        var cnpjNormalizado = NormalizarCnpj(cnpj);
        if (cnpjNormalizado.Length != 14)
            throw new DomainException("CNPJ deve ter 14 dígitos.");

        return new Empresa
        {
            RazaoSocial = razaoSocial.Trim(),
            NomeFantasia = string.IsNullOrWhiteSpace(nomeFantasia) ? razaoSocial.Trim() : nomeFantasia.Trim(),
            Cnpj = cnpjNormalizado,
            InscricaoEstadual = string.IsNullOrWhiteSpace(inscricaoEstadual) ? null : inscricaoEstadual.Trim(),
            Regime = regime,
            Endereco = endereco ?? throw new DomainException("Endereço é obrigatório.")
        };
    }

    public void AtualizarDadosCadastrais(string razaoSocial, string nomeFantasia, string? inscricaoEstadual, Endereco endereco)
    {
        if (string.IsNullOrWhiteSpace(razaoSocial))
            throw new DomainException("Razão social é obrigatória.");
        RazaoSocial = razaoSocial.Trim();
        NomeFantasia = string.IsNullOrWhiteSpace(nomeFantasia) ? RazaoSocial : nomeFantasia.Trim();
        InscricaoEstadual = string.IsNullOrWhiteSpace(inscricaoEstadual) ? null : inscricaoEstadual.Trim();
        Endereco = endereco ?? throw new DomainException("Endereço é obrigatório.");
    }

    public void Desativar() => Ativa = false;
    public void Reativar() => Ativa = true;

    /// <summary>Só dígitos. Formatação é responsabilidade da UI.</summary>
    public static string NormalizarCnpj(string? cnpj) =>
        new string((cnpj ?? string.Empty).Where(char.IsDigit).ToArray());
}

public sealed class Loja : Entity
{
    public Guid EmpresaId { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public string FusoHorario { get; private set; } = "America/Sao_Paulo";
    public Endereco Endereco { get; private set; } = null!;
    public bool Ativa { get; private set; } = true;

    private Loja() { }

    public static Loja Criar(Guid empresaId, string codigo, string nome, Endereco endereco,
        string fusoHorario = "America/Sao_Paulo")
    {
        if (empresaId == Guid.Empty) throw new DomainException("Empresa é obrigatória.");
        if (string.IsNullOrWhiteSpace(codigo)) throw new DomainException("Código da loja é obrigatório.");
        if (string.IsNullOrWhiteSpace(nome)) throw new DomainException("Nome da loja é obrigatório.");

        return new Loja
        {
            EmpresaId = empresaId,
            Codigo = codigo.Trim(),
            Nome = nome.Trim(),
            Endereco = endereco ?? throw new DomainException("Endereço é obrigatório."),
            FusoHorario = string.IsNullOrWhiteSpace(fusoHorario) ? "America/Sao_Paulo" : fusoHorario.Trim()
        };
    }

    public void Desativar() => Ativa = false;
}
