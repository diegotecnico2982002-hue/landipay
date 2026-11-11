using Microsoft.EntityFrameworkCore;

namespace Landipay.Backend.Dados;

public sealed class BackendDbContext : DbContext
{
    public BackendDbContext(DbContextOptions<BackendDbContext> options) : base(options) { }

    public DbSet<LicencaRegistro> Licencas => Set<LicencaRegistro>();
    public DbSet<ReleaseRegistro> Releases => Set<ReleaseRegistro>();
    public DbSet<SessaoSuporteRegistro> Sessoes => Set<SessaoSuporteRegistro>();
    public DbSet<EventoAuditoriaApi> Auditoria => Set<EventoAuditoriaApi>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LicencaRegistro>(e =>
        {
            e.ToTable("Licencas");
            e.HasKey(x => x.Id);
            e.Property(x => x.InstalacaoId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Plano).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.InstalacaoId).IsUnique();
            e.HasIndex(x => x.EmpresaId);
        });

        b.Entity<ReleaseRegistro>(e =>
        {
            e.ToTable("Releases", t =>
            {
                t.HasCheckConstraint("CK_Releases_Sha256Hex", "length(Sha256) = 64");
                t.HasCheckConstraint("CK_Releases_TamanhoPositivo", "Tamanho > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Versao).HasMaxLength(30).IsRequired();
            e.Property(x => x.Canal).HasMaxLength(30).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.VersaoMinima).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.Canal, x.Versao }).IsUnique();
        });

        b.Entity<SessaoSuporteRegistro>(e =>
        {
            e.ToTable("SessoesSuporte");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tecnico).HasMaxLength(120).IsRequired();
            e.Property(x => x.Motivo).HasMaxLength(300).IsRequired();
            e.Property(x => x.Escopo).HasMaxLength(40).IsRequired();
            e.Property(x => x.CodigoHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.CredencialHash).HasMaxLength(64);
            e.Property(x => x.EncerradaPor).HasMaxLength(120);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.EmpresaId, x.Status });
        });

        b.Entity<EventoAuditoriaApi>(e =>
        {
            e.ToTable("AuditoriaApi");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Acao).HasMaxLength(80).IsRequired();
            e.Property(x => x.Detalhe).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Ator).HasMaxLength(120);
            e.HasIndex(x => x.OcorridoEm);
        });
    }

    /// <summary>Migrations serão geradas na Fase 5; por ora o host cria o schema com EnsureCreated.</summary>
    public static BackendDbContext ParaArquivo(string caminho)
    {
        var opcoes = new DbContextOptionsBuilder<BackendDbContext>()
            .UseSqlite($"Data Source={caminho}")
            .Options;
        return new BackendDbContext(opcoes);
    }
}
