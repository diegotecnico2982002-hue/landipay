using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;

namespace Landipay.Client.Servicos;

/// <summary>
/// Catálogo de demonstração para uma instalação nova: três produtos com estoque inicial.
/// É dado de exemplo para o caixa poder ser operado logo após o primeiro acesso — não é
/// dado fiscal real e pode ser apagado sem afetar a estrutura. Usado pelo primeiro acesso
/// do cliente e, na Fase 4, pelo instalador.
/// </summary>
public static class CatalogoDemo
{
    public sealed record ItemDemo(string Sku, string CodigoBarras, string Nome, decimal Preco, decimal Estoque);

    public static readonly IReadOnlyList<ItemDemo> Itens = new[]
    {
        new ItemDemo("CAF-500", "7891000053508", "Café Torrado 500g", 18.90m, 20m),
        new ItemDemo("LEI-1L", "7896000000017", "Leite Integral 1L", 5.49m, 60m),
        new ItemDemo("ARR-5K", "7896036090017", "Arroz Tipo 1 5kg", 29.90m, 15m)
    };

    /// <summary>Cria os produtos só se o catálogo estiver vazio (idempotente).</summary>
    public static int Semear(LandipayDbContext db, Guid empresaId, Guid lojaId, Guid usuarioId)
    {
        if (db.Produtos.Any()) return 0;

        var agora = DateTimeOffset.UtcNow;
        var criados = 0;

        foreach (var item in Itens)
        {
            var produto = Produto.Criar(empresaId, item.Sku, item.Nome, item.Preco, "UN", item.CodigoBarras);
            db.Produtos.Add(produto);
            db.SaveChanges();

            db.EstoqueMovimentos.Add(EstoqueMovimento.Criar(
                lojaId, produto.Id, TipoMovimentoEstoque.Entrada, item.Estoque, item.Estoque,
                usuarioId, agora, "Carga inicial (catálogo de demonstração)"));

            criados++;
        }

        db.SaveChanges();
        return criados;
    }
}
