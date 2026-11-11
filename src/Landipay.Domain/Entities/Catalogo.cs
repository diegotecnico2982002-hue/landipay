using Landipay.Domain.Common;
using Landipay.Domain.Enums;

namespace Landipay.Domain.Entities;

public sealed class Produto : Entity
{
    public Guid EmpresaId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string? CodigoBarras { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string Unidade { get; private set; } = "UN";
    public decimal PrecoVenda { get; private set; }
    public decimal CustoAtual { get; private set; }
    public bool ControlaEstoque { get; private set; } = true;
    public decimal EstoqueMinimo { get; private set; }
    public bool Ativo { get; private set; } = true;

    private Produto() { }

    public static Produto Criar(Guid empresaId, string sku, string nome, decimal precoVenda,
        string unidade = "UN", string? codigoBarras = null, bool controlaEstoque = true, decimal estoqueMinimo = 0m)
    {
        if (empresaId == Guid.Empty) throw new DomainException("Empresa é obrigatória.");
        if (string.IsNullOrWhiteSpace(sku)) throw new DomainException("SKU é obrigatório.");
        if (string.IsNullOrWhiteSpace(nome)) throw new DomainException("Nome do produto é obrigatório.");
        if (precoVenda < 0) throw new DomainException("Preço de venda não pode ser negativo.");
        if (estoqueMinimo < 0) throw new DomainException("Estoque mínimo não pode ser negativo.");

        return new Produto
        {
            EmpresaId = empresaId,
            Sku = sku.Trim(),
            Nome = nome.Trim(),
            PrecoVenda = ArredondarDinheiro(precoVenda),
            Unidade = string.IsNullOrWhiteSpace(unidade) ? "UN" : unidade.Trim().ToUpperInvariant(),
            CodigoBarras = string.IsNullOrWhiteSpace(codigoBarras) ? null : codigoBarras.Trim(),
            ControlaEstoque = controlaEstoque,
            EstoqueMinimo = estoqueMinimo
        };
    }

    public void AlterarPreco(decimal novoPreco)
    {
        if (novoPreco < 0) throw new DomainException("Preço de venda não pode ser negativo.");
        PrecoVenda = ArredondarDinheiro(novoPreco);
    }

    public void AtualizarCusto(decimal custo)
    {
        if (custo < 0) throw new DomainException("Custo não pode ser negativo.");
        CustoAtual = ArredondarDinheiro(custo);
    }

    public void Desativar() => Ativo = false;
    public void Reativar() => Ativo = true;

    internal static decimal ArredondarDinheiro(decimal valor) =>
        Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    internal static decimal ArredondarQuantidade(decimal valor) =>
        Math.Round(valor, 4, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Movimento de estoque. Quantidade é sempre positiva; o <see cref="Tipo"/> define o sentido.
/// O saldo é gravado no movimento para permitir reconstrução e auditoria sem recalcular histórico.
/// </summary>
public sealed class EstoqueMovimento : Entity
{
    public Guid LojaId { get; private set; }
    public Guid ProdutoId { get; private set; }
    public TipoMovimentoEstoque Tipo { get; private set; }
    public decimal Quantidade { get; private set; }
    public decimal SaldoApos { get; private set; }
    public Guid? VendaId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string? Motivo { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    private EstoqueMovimento() { }

    public static EstoqueMovimento Criar(Guid lojaId, Guid produtoId, TipoMovimentoEstoque tipo,
        decimal quantidade, decimal saldoApos, Guid usuarioId, DateTimeOffset ocorridoEm, string? motivo = null, Guid? vendaId = null)
    {
        if (lojaId == Guid.Empty) throw new DomainException("Loja é obrigatória.");
        if (produtoId == Guid.Empty) throw new DomainException("Produto é obrigatório.");
        if (quantidade <= 0) throw new DomainException("Quantidade do movimento deve ser positiva.");
        if (saldoApos < 0) throw new DomainException("Saldo final não pode ser negativo.");

        return new EstoqueMovimento
        {
            LojaId = lojaId,
            ProdutoId = produtoId,
            Tipo = tipo,
            Quantidade = Produto.ArredondarQuantidade(quantidade),
            SaldoApos = Produto.ArredondarQuantidade(saldoApos),
            UsuarioId = usuarioId,
            OcorridoEm = ocorridoEm,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim(),
            VendaId = vendaId
        };
    }

    /// <summary>+1 para entradas, -1 para saídas.</summary>
    public int Sinal => Tipo is TipoMovimentoEstoque.Entrada
        or TipoMovimentoEstoque.Devolucao
        or TipoMovimentoEstoque.Ajuste ? 1 : -1;
}
