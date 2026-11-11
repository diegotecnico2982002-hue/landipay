namespace Landipay.Domain.Common;

/// <summary>
/// Base de todas as entidades persistidas. PK em <see cref="Guid"/> porque o PDV
/// opera offline e precisa gerar identificadores sem coordenação com o servidor
/// (duas lojas não podem colidir ao sincronizar).
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>Sempre UTC. A apresentação converte para o fuso da loja.</summary>
    public DateTimeOffset CriadoEm { get; protected set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Violação de invariante de domínio. Nunca representa erro de infraestrutura.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
