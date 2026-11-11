using Landipay.Domain.Common;
using Landipay.Domain.Enums;

namespace Landipay.Domain.Entities;

/// <summary>
/// Venda é a raiz do agregado: itens e pagamentos só existem dentro dela.
/// Todos os totais são calculados em <see cref="decimal"/> e arredondados a 2 casas
/// (nunca <c>double</c>), e o total nunca pode ficar negativo.
/// </summary>
public sealed class Venda : Entity
{
    private readonly List<ItemVenda> _itens = new();
    private readonly List<Pagamento> _pagamentos = new();

    public Guid LojaId { get; private set; }
    public Guid CaixaTurnoId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public long Numero { get; private set; }
    public StatusVenda Status { get; private set; } = StatusVenda.Aberta;
    public decimal Desconto { get; private set; }
    public DateTimeOffset? FinalizadaEm { get; private set; }
    public string? MotivoCancelamento { get; private set; }

    public IReadOnlyCollection<ItemVenda> Itens => _itens.AsReadOnly();
    public IReadOnlyCollection<Pagamento> Pagamentos => _pagamentos.AsReadOnly();

    public decimal Subtotal => _itens.Sum(i => i.Total);
    public decimal TotalPago => _pagamentos.Sum(p => p.Valor);
    public decimal Total => Math.Round(Subtotal - Desconto, 2, MidpointRounding.AwayFromZero);
    public decimal Troco => Math.Round(TotalPago - Total, 2, MidpointRounding.AwayFromZero);

    private Venda() { }

    public static Venda Abrir(Guid lojaId, Guid caixaTurnoId, Guid usuarioId, long numero)
    {
        if (lojaId == Guid.Empty) throw new DomainException("Loja é obrigatória.");
        if (caixaTurnoId == Guid.Empty) throw new DomainException("Turno de caixa é obrigatório.");
        if (usuarioId == Guid.Empty) throw new DomainException("Operador é obrigatório.");
        if (numero <= 0) throw new DomainException("Número da venda deve ser positivo.");

        return new Venda
        {
            LojaId = lojaId,
            CaixaTurnoId = caixaTurnoId,
            UsuarioId = usuarioId,
            Numero = numero
        };
    }

    public ItemVenda AdicionarItem(Produto produto, decimal quantidade, decimal? precoUnitario = null, decimal desconto = 0m)
    {
        if (Status != StatusVenda.Aberta) throw new DomainException("Só é possível adicionar itens em venda aberta.");
        if (produto is null) throw new DomainException("Produto é obrigatório.");
        if (!produto.Ativo) throw new DomainException("Produto inativo não pode ser vendido.");
        if (quantidade <= 0) throw new DomainException("Quantidade deve ser positiva.");
        if (desconto < 0) throw new DomainException("Desconto do item não pode ser negativo.");

        var preco = precoUnitario ?? produto.PrecoVenda;
        if (preco < 0) throw new DomainException("Preço unitário não pode ser negativo.");

        var item = new ItemVenda(Id, produto.Id, _itens.Count + 1, produto.Nome, produto.Unidade,
            quantidade, preco, desconto);

        if (item.Total < 0) throw new DomainException("Total do item não pode ser negativo.");

        _itens.Add(item);
        return item;
    }

    public void RemoverItem(ItemVenda item)
    {
        if (Status != StatusVenda.Aberta) throw new DomainException("Só é possível alterar venda aberta.");
        _itens.Remove(item);
    }

    public void AplicarDesconto(decimal valor)
    {
        if (Status != StatusVenda.Aberta) throw new DomainException("Só é possível alterar venda aberta.");
        if (valor < 0) throw new DomainException("Desconto não pode ser negativo.");
        if (valor > Subtotal) throw new DomainException("Desconto não pode exceder o subtotal.");
        Desconto = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }

    public void RegistrarPagamento(FormaPagamento forma, decimal valor, string? autorizacao = null, DateTimeOffset? quando = null)
    {
        if (Status != StatusVenda.Aberta) throw new DomainException("Só é possível pagar venda aberta.");
        if (_itens.Count == 0) throw new DomainException("Não é possível pagar uma venda sem itens.");
        if (valor <= 0) throw new DomainException("Valor do pagamento deve ser positivo.");

        _pagamentos.Add(new Pagamento(Id, forma, valor, autorizacao, quando ?? DateTimeOffset.UtcNow));
    }

    public void Finalizar(DateTimeOffset quando)
    {
        if (Status != StatusVenda.Aberta) throw new DomainException("Venda não está aberta.");
        if (_itens.Count == 0) throw new DomainException("Venda sem itens não pode ser finalizada.");

        var arredondado = Math.Round(TotalPago, 2, MidpointRounding.AwayFromZero);
        if (arredondado < Total)
            throw new DomainException($"Pagamento insuficiente: pago {TotalPago:0.00}, total {Total:0.00}.");

        Status = StatusVenda.Finalizada;
        FinalizadaEm = quando;
    }

    public void Cancelar(string motivo, DateTimeOffset quando)
    {
        if (Status == StatusVenda.Cancelada) throw new DomainException("Venda já cancelada.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new DomainException("Motivo do cancelamento é obrigatório.");
        if (motivo.Trim().Length < 5) throw new DomainException("Motivo do cancelamento muito curto.");

        Status = StatusVenda.Cancelada;
        MotivoCancelamento = motivo.Trim();
        FinalizadaEm ??= quando;
    }
}

public sealed class ItemVenda : Entity
{
    public Guid VendaId { get; private set; }
    public Guid ProdutoId { get; private set; }
    public int Sequencia { get; private set; }
    public string DescricaoSnapshot { get; private set; } = string.Empty;
    public string Unidade { get; private set; } = "UN";
    public decimal Quantidade { get; private set; }
    public decimal PrecoUnitario { get; private set; }
    public decimal Desconto { get; private set; }
    public decimal Total { get; private set; }

    private ItemVenda() { }

    internal ItemVenda(Guid vendaId, Guid produtoId, int sequencia, string descricao, string unidade,
        decimal quantidade, decimal precoUnitario, decimal desconto)
    {
        VendaId = vendaId;
        ProdutoId = produtoId;
        Sequencia = sequencia;
        DescricaoSnapshot = descricao;
        Unidade = unidade;
        Quantidade = Math.Round(quantidade, 4, MidpointRounding.AwayFromZero);
        PrecoUnitario = Math.Round(precoUnitario, 2, MidpointRounding.AwayFromZero);
        Desconto = Math.Round(desconto, 2, MidpointRounding.AwayFromZero);
        Total = Math.Round(Quantidade * PrecoUnitario - Desconto, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed class Pagamento : Entity
{
    public Guid VendaId { get; private set; }
    public FormaPagamento Forma { get; private set; }
    public decimal Valor { get; private set; }
    public string? Autorizacao { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    private Pagamento() { }

    internal Pagamento(Guid vendaId, FormaPagamento forma, decimal valor, string? autorizacao, DateTimeOffset ocorridoEm)
    {
        if (valor <= 0) throw new DomainException("Valor do pagamento deve ser positivo.");
        VendaId = vendaId;
        Forma = forma;
        Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        Autorizacao = string.IsNullOrWhiteSpace(autorizacao) ? null : autorizacao.Trim();
        OcorridoEm = ocorridoEm;
    }
}
