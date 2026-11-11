using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Landipay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Empresas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RazaoSocial = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NomeFantasia = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Cnpj = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    InscricaoEstadual = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Regime = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EnderecoLogradouro = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EnderecoNumero = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EnderecoComplemento = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EnderecoBairro = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EnderecoCidade = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EnderecoUf = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    EnderecoCep = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Ativa = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresas", x => x.Id);
                    table.CheckConstraint("CK_Empresas_CnpjDigitos", "length(Cnpj) = 14");
                });

            migrationBuilder.CreateTable(
                name: "LogAuditoria",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LojaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Acao = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Entidade = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EntidadeId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    DadosJson = table.Column<string>(type: "TEXT", nullable: true),
                    Origem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Maquina = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogAuditoria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LojaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    EnviadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Tentativas = table.Column<int>(type: "INTEGER", nullable: false),
                    UltimoErro = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMensagens", x => x.Id);
                    table.CheckConstraint("CK_Outbox_TentativasNaoNegativas", "Tentativas >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Papeis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Sistema = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Papeis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigLicencas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalacaoId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TokenAssinado = table.Column<string>(type: "TEXT", nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EmitidaEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UltimaValidacaoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ToleranciaOfflineDias = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigLicencas", x => x.Id);
                    table.CheckConstraint("CK_ConfigLicencas_ExpiracaoPosterior", "ExpiraEm > EmitidaEm");
                    table.CheckConstraint("CK_ConfigLicencas_ToleranciaNaoNegativa", "ToleranciaOfflineDias >= 0");
                    table.ForeignKey(
                        name: "FK_ConfigLicencas_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Lojas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    FusoHorario = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EnderecoLogradouro = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EnderecoNumero = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EnderecoComplemento = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EnderecoBairro = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EnderecoCidade = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EnderecoUf = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    EnderecoCep = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Ativa = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lojas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lojas_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Produtos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CodigoBarras = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Unidade = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    PrecoVenda = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CustoAtual = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ControlaEstoque = table.Column<bool>(type: "INTEGER", nullable: false),
                    EstoqueMinimo = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produtos", x => x.Id);
                    table.CheckConstraint("CK_Produtos_EstoqueMinimoNaoNegativo", "EstoqueMinimo >= 0");
                    table.CheckConstraint("CK_Produtos_PrecoNaoNegativo", "PrecoVenda >= 0");
                    table.ForeignKey(
                        name: "FK_Produtos_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LojaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SenhaHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    TentativasFalhas = table.Column<int>(type: "INTEGER", nullable: false),
                    BloqueadoAte = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UltimoAcessoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SenhaAlteradaEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ExigeTrocaDeSenha = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                    table.CheckConstraint("CK_Usuarios_TentativasNaoNegativas", "TentativasFalhas >= 0");
                    table.ForeignKey(
                        name: "FK_Usuarios_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PapelPermissoes",
                columns: table => new
                {
                    PapelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermissaoId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PapelPermissoes", x => new { x.PapelId, x.PermissaoId });
                    table.ForeignKey(
                        name: "FK_PapelPermissoes_Papeis_PapelId",
                        column: x => x.PapelId,
                        principalTable: "Papeis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PapelPermissoes_Permissoes_PermissaoId",
                        column: x => x.PermissaoId,
                        principalTable: "Permissoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CaixaTurnos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LojaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Numero = table.Column<long>(type: "INTEGER", nullable: false),
                    AbertoPorUsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AbertoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValorAbertura = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FechadoPorUsuarioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FechadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ValorFechamentoInformado = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ValorFechamentoCalculado = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaixaTurnos", x => x.Id);
                    table.CheckConstraint("CK_CaixaTurnos_NumeroPositivo", "Numero > 0");
                    table.CheckConstraint("CK_CaixaTurnos_ValorAberturaNaoNegativo", "ValorAbertura >= 0");
                    table.ForeignKey(
                        name: "FK_CaixaTurnos_Lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "Lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EstoqueMovimentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LojaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Quantidade = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SaldoApos = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VendaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstoqueMovimentos", x => x.Id);
                    table.CheckConstraint("CK_EstoqueMovimentos_QuantidadePositiva", "Quantidade > 0");
                    table.CheckConstraint("CK_EstoqueMovimentos_SaldoNaoNegativo", "SaldoApos >= 0");
                    table.ForeignKey(
                        name: "FK_EstoqueMovimentos_Lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "Lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstoqueMovimentos_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioPapeis",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PapelId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioPapeis", x => new { x.UsuarioId, x.PapelId });
                    table.ForeignKey(
                        name: "FK_UsuarioPapeis_Papeis_PapelId",
                        column: x => x.PapelId,
                        principalTable: "Papeis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsuarioPapeis_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosCaixa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CaixaTurnoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Valor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosCaixa", x => x.Id);
                    table.CheckConstraint("CK_MovimentosCaixa_ValorPositivo", "Valor > 0");
                    table.ForeignKey(
                        name: "FK_MovimentosCaixa_CaixaTurnos_CaixaTurnoId",
                        column: x => x.CaixaTurnoId,
                        principalTable: "CaixaTurnos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vendas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LojaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CaixaTurnoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Numero = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Desconto = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FinalizadaEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendas", x => x.Id);
                    table.CheckConstraint("CK_Vendas_DescontoNaoNegativo", "Desconto >= 0");
                    table.CheckConstraint("CK_Vendas_NumeroPositivo", "Numero > 0");
                    table.ForeignKey(
                        name: "FK_Vendas_CaixaTurnos_CaixaTurnoId",
                        column: x => x.CaixaTurnoId,
                        principalTable: "CaixaTurnos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendas_Lojas_LojaId",
                        column: x => x.LojaId,
                        principalTable: "Lojas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItensVenda",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProdutoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequencia = table.Column<int>(type: "INTEGER", nullable: false),
                    DescricaoSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Unidade = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    Quantidade = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Desconto = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensVenda", x => x.Id);
                    table.CheckConstraint("CK_ItensVenda_DescontoNaoNegativo", "Desconto >= 0");
                    table.CheckConstraint("CK_ItensVenda_PrecoNaoNegativo", "PrecoUnitario >= 0");
                    table.CheckConstraint("CK_ItensVenda_QuantidadePositiva", "Quantidade > 0");
                    table.CheckConstraint("CK_ItensVenda_TotalNaoNegativo", "Total >= 0");
                    table.ForeignKey(
                        name: "FK_ItensVenda_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensVenda_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pagamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Forma = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Valor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Autorizacao = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagamentos", x => x.Id);
                    table.CheckConstraint("CK_Pagamentos_ValorPositivo", "Valor > 0");
                    table.ForeignKey(
                        name: "FK_Pagamentos_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaixaTurnos_LojaId_Numero",
                table: "CaixaTurnos",
                columns: new[] { "LojaId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaixaTurnos_LojaId_Status",
                table: "CaixaTurnos",
                columns: new[] { "LojaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigLicencas_EmpresaId",
                table: "ConfigLicencas",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigLicencas_InstalacaoId",
                table: "ConfigLicencas",
                column: "InstalacaoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_Cnpj",
                table: "Empresas",
                column: "Cnpj",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstoqueMovimentos_LojaId_ProdutoId_OcorridoEm",
                table: "EstoqueMovimentos",
                columns: new[] { "LojaId", "ProdutoId", "OcorridoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_EstoqueMovimentos_ProdutoId",
                table: "EstoqueMovimentos",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_ProdutoId",
                table: "ItensVenda",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_VendaId",
                table: "ItensVenda",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_LogAuditoria_Entidade_EntidadeId",
                table: "LogAuditoria",
                columns: new[] { "Entidade", "EntidadeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LogAuditoria_OcorridoEm",
                table: "LogAuditoria",
                column: "OcorridoEm");

            migrationBuilder.CreateIndex(
                name: "IX_LogAuditoria_UsuarioId",
                table: "LogAuditoria",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Lojas_EmpresaId_Codigo",
                table: "Lojas",
                columns: new[] { "EmpresaId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_CaixaTurnoId",
                table: "MovimentosCaixa",
                column: "CaixaTurnoId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMensagens_LojaId_EnviadoEm",
                table: "OutboxMensagens",
                columns: new[] { "LojaId", "EnviadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_VendaId",
                table: "Pagamentos",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_Papeis_Nome",
                table: "Papeis",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PapelPermissoes_PermissaoId",
                table: "PapelPermissoes",
                column: "PermissaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Permissoes_Codigo",
                table: "Permissoes",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_CodigoBarras",
                table: "Produtos",
                columns: new[] { "EmpresaId", "CodigoBarras" },
                unique: true,
                filter: "\"CodigoBarras\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_Nome",
                table: "Produtos",
                columns: new[] { "EmpresaId", "Nome" });

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_EmpresaId_Sku",
                table: "Produtos",
                columns: new[] { "EmpresaId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuarioPapeis_PapelId",
                table: "UsuarioPapeis",
                column: "PapelId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_EmpresaId_Login",
                table: "Usuarios",
                columns: new[] { "EmpresaId", "Login" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_LojaId",
                table: "Usuarios",
                column: "LojaId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_CaixaTurnoId",
                table: "Vendas",
                column: "CaixaTurnoId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_LojaId_Numero",
                table: "Vendas",
                columns: new[] { "LojaId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_LojaId_Status_CriadoEm",
                table: "Vendas",
                columns: new[] { "LojaId", "Status", "CriadoEm" });

            // ── LogAuditoria é append-only ───────────────────────────────────────
            // Triggers no banco, não só "convenção da aplicação": garantia que sobrevive
            // a bug, script manual ou acesso direto ao arquivo SQLite.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_LogAuditoria_BloqueiaUpdate
                BEFORE UPDATE ON LogAuditoria
                BEGIN
                    SELECT RAISE(ABORT, 'LogAuditoria e append-only: UPDATE nao permitido');
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_LogAuditoria_BloqueiaDelete
                BEFORE DELETE ON LogAuditoria
                BEGIN
                    SELECT RAISE(ABORT, 'LogAuditoria e append-only: DELETE nao permitido');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfigLicencas");

            migrationBuilder.DropTable(
                name: "EstoqueMovimentos");

            migrationBuilder.DropTable(
                name: "ItensVenda");

            migrationBuilder.DropTable(
                name: "LogAuditoria");

            migrationBuilder.DropTable(
                name: "MovimentosCaixa");

            migrationBuilder.DropTable(
                name: "OutboxMensagens");

            migrationBuilder.DropTable(
                name: "Pagamentos");

            migrationBuilder.DropTable(
                name: "PapelPermissoes");

            migrationBuilder.DropTable(
                name: "UsuarioPapeis");

            migrationBuilder.DropTable(
                name: "Produtos");

            migrationBuilder.DropTable(
                name: "Vendas");

            migrationBuilder.DropTable(
                name: "Permissoes");

            migrationBuilder.DropTable(
                name: "Papeis");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "CaixaTurnos");

            migrationBuilder.DropTable(
                name: "Lojas");

            migrationBuilder.DropTable(
                name: "Empresas");
        }
    }
}
