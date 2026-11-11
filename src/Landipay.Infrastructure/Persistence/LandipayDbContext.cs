using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core do PDV. Todas as configurações são explícitas (sem convenção implícita
/// para tamanhos, precisão ou índices), porque o schema é contrato e precisa ser auditável.
/// Nenhuma query deste contexto é montada por concatenação — ver docs/01-modelo-dados.md §7.
/// </summary>
public sealed class LandipayDbContext : DbContext
{
    public LandipayDbContext(DbContextOptions<LandipayDbContext> options) : base(options) { }

    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Loja> Lojas => Set<Loja>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Papel> Papeis => Set<Papel>();
    public DbSet<Permissao> Permissoes => Set<Permissao>();
    public DbSet<UsuarioPapel> UsuarioPapeis => Set<UsuarioPapel>();
    public DbSet<PapelPermissao> PapelPermissoes => Set<PapelPermissao>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<EstoqueMovimento> EstoqueMovimentos => Set<EstoqueMovimento>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<ItemVenda> ItensVenda => Set<ItemVenda>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<CaixaTurno> CaixaTurnos => Set<CaixaTurno>();
    public DbSet<MovimentoCaixa> MovimentosCaixa => Set<MovimentoCaixa>();
    public DbSet<LogAuditoria> Auditoria => Set<LogAuditoria>();
    public DbSet<OutboxMensagem> Outbox => Set<OutboxMensagem>();
    public DbSet<ConfigLicenca> ConfigLicencas => Set<ConfigLicenca>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        ConfigurarEmpresas(b);
        ConfigurarSeguranca(b);
        ConfigurarCatalogo(b);
        ConfigurarVendas(b);
        ConfigurarCaixa(b);
        ConfigurarAuditoria(b);
        ConfigurarLicenca(b);
    }

    // ── Empresa / Loja ────────────────────────────────────────────────────────
    private static void ConfigurarEmpresas(ModelBuilder b)
    {
        b.Entity<Empresa>(e =>
        {
            e.ToTable("Empresas", t => t.HasCheckConstraint("CK_Empresas_CnpjDigitos", "length(Cnpj) = 14"));
            e.HasKey(x => x.Id);
            e.Property(x => x.RazaoSocial).HasMaxLength(200).IsRequired();
            e.Property(x => x.NomeFantasia).HasMaxLength(200).IsRequired();
            e.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
            e.Property(x => x.InscricaoEstadual).HasMaxLength(30);
            e.Property(x => x.Regime).HasConversion<string>().HasMaxLength(40).IsRequired();
            e.Property(x => x.Ativa).IsRequired();
            e.HasIndex(x => x.Cnpj).IsUnique();

            e.OwnsOne(x => x.Endereco, o =>
            {
                o.Property(p => p.Logradouro).HasColumnName("EnderecoLogradouro").HasMaxLength(200).IsRequired();
                o.Property(p => p.Numero).HasColumnName("EnderecoNumero").HasMaxLength(20).IsRequired();
                o.Property(p => p.Complemento).HasColumnName("EnderecoComplemento").HasMaxLength(100);
                o.Property(p => p.Bairro).HasColumnName("EnderecoBairro").HasMaxLength(100).IsRequired();
                o.Property(p => p.Cidade).HasColumnName("EnderecoCidade").HasMaxLength(100).IsRequired();
                o.Property(p => p.Uf).HasColumnName("EnderecoUf").HasMaxLength(2).IsRequired();
                o.Property(p => p.Cep).HasColumnName("EnderecoCep").HasMaxLength(8).IsRequired();
            });
            e.Navigation(x => x.Endereco).IsRequired();
        });

        b.Entity<Loja>(e =>
        {
            e.ToTable("Lojas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(10).IsRequired();
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.FusoHorario).HasMaxLength(60).IsRequired();
            e.Property(x => x.Ativa).IsRequired();
            e.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();
            e.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);

            e.OwnsOne(x => x.Endereco, o =>
            {
                o.Property(p => p.Logradouro).HasColumnName("EnderecoLogradouro").HasMaxLength(200).IsRequired();
                o.Property(p => p.Numero).HasColumnName("EnderecoNumero").HasMaxLength(20).IsRequired();
                o.Property(p => p.Complemento).HasColumnName("EnderecoComplemento").HasMaxLength(100);
                o.Property(p => p.Bairro).HasColumnName("EnderecoBairro").HasMaxLength(100).IsRequired();
                o.Property(p => p.Cidade).HasColumnName("EnderecoCidade").HasMaxLength(100).IsRequired();
                o.Property(p => p.Uf).HasColumnName("EnderecoUf").HasMaxLength(2).IsRequired();
                o.Property(p => p.Cep).HasColumnName("EnderecoCep").HasMaxLength(8).IsRequired();
            });
            e.Navigation(x => x.Endereco).IsRequired();
        });
    }

    // ── Segurança ─────────────────────────────────────────────────────────────
    private static void ConfigurarSeguranca(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.ToTable("Usuarios", t =>
            {
                t.HasCheckConstraint("CK_Usuarios_TentativasNaoNegativas", "TentativasFalhas >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
            e.Property(x => x.Login).HasMaxLength(60).IsRequired();
            e.Property(x => x.SenhaHash).HasMaxLength(255).IsRequired();
            e.Property(x => x.Ativo).IsRequired();
            e.Property(x => x.TentativasFalhas).IsRequired();
            e.HasIndex(x => new { x.EmpresaId, x.Login }).IsUnique();
            e.HasIndex(x => x.LojaId);
            e.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Papel>(e =>
        {
            e.ToTable("Papeis");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(60).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(200);
            e.HasIndex(x => x.Nome).IsUnique();
        });

        b.Entity<Permissao>(e =>
        {
            e.ToTable("Permissoes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(80).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
        });

        b.Entity<UsuarioPapel>(e =>
        {
            e.ToTable("UsuarioPapeis");
            e.HasKey(x => new { x.UsuarioId, x.PapelId });
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Papel>().WithMany().HasForeignKey(x => x.PapelId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PapelPermissao>(e =>
        {
            e.ToTable("PapelPermissoes");
            e.HasKey(x => new { x.PapelId, x.PermissaoId });
            e.HasOne<Papel>().WithMany().HasForeignKey(x => x.PapelId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permissao>().WithMany().HasForeignKey(x => x.PermissaoId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // ── Catálogo ──────────────────────────────────────────────────────────────
    private static void ConfigurarCatalogo(ModelBuilder b)
    {
        b.Entity<Produto>(e =>
        {
            e.ToTable("Produtos", t =>
            {
                t.HasCheckConstraint("CK_Produtos_PrecoNaoNegativo", "PrecoVenda >= 0");
                t.HasCheckConstraint("CK_Produtos_EstoqueMinimoNaoNegativo", "EstoqueMinimo >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(40).IsRequired();
            e.Property(x => x.CodigoBarras).HasMaxLength(20);
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired();
            e.Property(x => x.Unidade).HasMaxLength(6).IsRequired();
            e.Property(x => x.PrecoVenda).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.CustoAtual).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.EstoqueMinimo).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.ControlaEstoque).IsRequired();
            e.Property(x => x.Ativo).IsRequired();
            e.HasIndex(x => new { x.EmpresaId, x.Sku }).IsUnique();
            e.HasIndex(x => new { x.EmpresaId, x.CodigoBarras })
                .IsUnique()
                .HasFilter("\"CodigoBarras\" IS NOT NULL");
            e.HasIndex(x => new { x.EmpresaId, x.Nome });
            e.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EstoqueMovimento>(e =>
        {
            e.ToTable("EstoqueMovimentos", t =>
            {
                t.HasCheckConstraint("CK_EstoqueMovimentos_QuantidadePositiva", "Quantidade > 0");
                t.HasCheckConstraint("CK_EstoqueMovimentos_SaldoNaoNegativo", "SaldoApos >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Quantidade).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.SaldoApos).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.Motivo).HasMaxLength(200);
            e.Ignore(x => x.Sinal);
            e.HasIndex(x => new { x.LojaId, x.ProdutoId, x.OcorridoEm });
            e.HasOne<Loja>().WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ── Vendas ────────────────────────────────────────────────────────────────
    private static void ConfigurarVendas(ModelBuilder b)
    {
        b.Entity<Venda>(e =>
        {
            e.ToTable("Vendas", t =>
            {
                t.HasCheckConstraint("CK_Vendas_DescontoNaoNegativo", "Desconto >= 0");
                t.HasCheckConstraint("CK_Vendas_NumeroPositivo", "Numero > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Numero).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Desconto).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.MotivoCancelamento).HasMaxLength(300);
            e.Ignore(x => x.Subtotal);
            e.Ignore(x => x.Total);
            e.Ignore(x => x.TotalPago);
            e.Ignore(x => x.Troco);
            e.HasIndex(x => new { x.LojaId, x.Numero }).IsUnique();
            e.HasIndex(x => new { x.LojaId, x.Status, x.CriadoEm });
            e.HasIndex(x => x.CaixaTurnoId);
            e.HasOne<Loja>().WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CaixaTurno>().WithMany().HasForeignKey(x => x.CaixaTurnoId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Itens).WithOne().HasForeignKey(i => i.VendaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Pagamentos).WithOne().HasForeignKey(p => p.VendaId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Itens).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_itens");
            e.Navigation(x => x.Pagamentos).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_pagamentos");
        });

        b.Entity<ItemVenda>(e =>
        {
            e.ToTable("ItensVenda", t =>
            {
                t.HasCheckConstraint("CK_ItensVenda_QuantidadePositiva", "Quantidade > 0");
                t.HasCheckConstraint("CK_ItensVenda_PrecoNaoNegativo", "PrecoUnitario >= 0");
                t.HasCheckConstraint("CK_ItensVenda_DescontoNaoNegativo", "Desconto >= 0");
                t.HasCheckConstraint("CK_ItensVenda_TotalNaoNegativo", "Total >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.DescricaoSnapshot).HasMaxLength(200).IsRequired();
            e.Property(x => x.Unidade).HasMaxLength(6).IsRequired();
            e.Property(x => x.Quantidade).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.PrecoUnitario).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.Desconto).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.Total).HasPrecision(18, 4).IsRequired();
            e.HasIndex(x => x.VendaId);
            e.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Pagamento>(e =>
        {
            e.ToTable("Pagamentos", t => t.HasCheckConstraint("CK_Pagamentos_ValorPositivo", "Valor > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Forma).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Valor).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.Autorizacao).HasMaxLength(60);
            e.HasIndex(x => x.VendaId);
        });
    }

    // ── Caixa ─────────────────────────────────────────────────────────────────
    private static void ConfigurarCaixa(ModelBuilder b)
    {
        b.Entity<CaixaTurno>(e =>
        {
            e.ToTable("CaixaTurnos", t =>
            {
                t.HasCheckConstraint("CK_CaixaTurnos_NumeroPositivo", "Numero > 0");
                t.HasCheckConstraint("CK_CaixaTurnos_ValorAberturaNaoNegativo", "ValorAbertura >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ValorAbertura).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.ValorFechamentoInformado).HasPrecision(18, 4);
            e.Property(x => x.ValorFechamentoCalculado).HasPrecision(18, 4);
            e.Ignore(x => x.TotalSangrias);
            e.Ignore(x => x.TotalSuprimentos);
            e.Ignore(x => x.DiferencaConferencia);
            e.HasIndex(x => new { x.LojaId, x.Numero }).IsUnique();
            e.HasIndex(x => new { x.LojaId, x.Status });
            e.HasOne<Loja>().WithMany().HasForeignKey(x => x.LojaId).OnDelete(DeleteBehavior.Restrict);

            e.Navigation(x => x.Movimentos).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_movimentos");
            e.HasMany(x => x.Movimentos).WithOne().HasForeignKey(m => m.CaixaTurnoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MovimentoCaixa>(e =>
        {
            e.ToTable("MovimentosCaixa", t => t.HasCheckConstraint("CK_MovimentosCaixa_ValorPositivo", "Valor > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Valor).HasPrecision(18, 4).IsRequired();
            e.Property(x => x.Motivo).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.CaixaTurnoId);
        });
    }

    // ── Auditoria e outbox ────────────────────────────────────────────────────
    private static void ConfigurarAuditoria(ModelBuilder b)
    {
        b.Entity<LogAuditoria>(e =>
        {
            // Tabela append-only: sem UPDATE/DELETE (triggers na migration).
            e.ToTable("LogAuditoria");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Acao).HasMaxLength(80).IsRequired();
            e.Property(x => x.Entidade).HasMaxLength(60).IsRequired();
            e.Property(x => x.EntidadeId).HasMaxLength(60);
            e.Property(x => x.DadosJson).HasColumnType("TEXT");
            e.Property(x => x.Origem).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Maquina).HasMaxLength(60);
            e.HasIndex(x => x.OcorridoEm);
            e.HasIndex(x => x.UsuarioId);
            e.HasIndex(x => new { x.Entidade, x.EntidadeId });
        });

        b.Entity<OutboxMensagem>(e =>
        {
            e.ToTable("OutboxMensagens", t => t.HasCheckConstraint("CK_Outbox_TentativasNaoNegativas", "Tentativas >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.UltimoErro).HasMaxLength(500);
            e.HasIndex(x => new { x.LojaId, x.EnviadoEm });
        });
    }

    // ── Licença ───────────────────────────────────────────────────────────────
    private static void ConfigurarLicenca(ModelBuilder b)
    {
        b.Entity<ConfigLicenca>(e =>
        {
            e.ToTable("ConfigLicencas", t =>
            {
                t.HasCheckConstraint("CK_ConfigLicencas_ToleranciaNaoNegativa", "ToleranciaOfflineDias >= 0");
                t.HasCheckConstraint("CK_ConfigLicencas_ExpiracaoPosterior", "ExpiraEm > EmitidaEm");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.InstalacaoId).HasMaxLength(64).IsRequired();
            e.Property(x => x.TokenAssinado).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.KeyId).HasMaxLength(40).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.ToleranciaOfflineDias).IsRequired();
            e.Property(x => x.AppsHabilitados).HasMaxLength(40).HasDefaultValue(string.Empty).IsRequired();
            e.HasIndex(x => x.InstalacaoId).IsUnique();
            e.HasOne<Empresa>().WithMany().HasForeignKey(x => x.EmpresaId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
