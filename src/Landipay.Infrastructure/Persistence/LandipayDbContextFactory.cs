using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Landipay.Infrastructure.Persistence;

/// <summary>
/// Fábrica usada apenas pelo <c>dotnet ef</c> (design-time).
/// A connection string vem de variável de ambiente — nenhum segredo no código
/// (A2). Em runtime o contexto é montado por DI a partir do secret store.
/// </summary>
public sealed class LandipayDbContextFactory : IDesignTimeDbContextFactory<LandipayDbContext>
{
    private const string CadeiaDesenvolvimento = "Data Source=landipay-dev.db";

    public LandipayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LANDIPAY_DB")
                               ?? CadeiaDesenvolvimento;

        var options = new DbContextOptionsBuilder<LandipayDbContext>()
            .UseSqlite(connectionString)
            .EnableDetailedErrors()
            .Options;

        return new LandipayDbContext(options);
    }
}
