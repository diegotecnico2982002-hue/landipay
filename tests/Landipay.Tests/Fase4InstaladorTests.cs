using Landipay.Backend.Seguranca;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.ValueObjects;
using Landipay.Infrastructure.Persistence;
using Xunit;

namespace Landipay.Tests;

/// <summary>
/// Fase 4 — passos do instalador. Cada teste ataca um requisito: idempotência,
/// ativação offline e recusa de licença inválida.
/// </summary>
public sealed class Fase4InstaladorTests
{
    private static (ServicoInstalador Instalador, LandipayDbContext Db, string Pasta) Criar(BancoDeTeste banco)
    {
        var db = banco.Criar();
        var pasta = Path.Combine(Path.GetTempPath(), $"landipay-inst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        return (new ServicoInstalador(db, pasta), db, pasta);
    }

    private static DadosEmpresa EmpresaPadrao(string cnpj = "12345678000190", string razao = "Mercado Central Ltda") =>
        new(cnpj, razao, "Mercado Central", "123456789", RegimeTributario.SimplesNacional,
            "Rua das Acácias", "1200", null, "Centro", "Campinas", "SP", "13010000", "01", "Loja Centro");

    private static (string Token, byte[] Publica) EmitirLicenca(Guid empresaId, TimeSpan? validade = null)
    {
        using var chaves = ProvedorDeChaves.EmMemoria();
        var emissor = new EmissorDeLicenca(chaves);
        var (token, _, _) = emissor.Emitir(empresaId, "instalacao-teste-01", "pro", validade ?? TimeSpan.FromDays(30));
        return (token, chaves.ChaveLicenca.ExportSubjectPublicKeyInfo());
    }

    // ── 1. Pré-requisitos ────────────────────────────────────────────────────
    [Fact]
    public void Pre_requisitos_passam_em_pasta_gravavel()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Criar(banco);

        var resultado = instalador.VerificarPreRequisitos();

        Assert.True(resultado.Ok, resultado.Detalhe);
        Assert.Contains(".NET", resultado.Detalhe);
    }

    // ── 2. Banco ─────────────────────────────────────────────────────────────
    [Fact]
    public void Preparar_banco_e_idempotente()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Criar(banco);

        var primeira = instalador.PrepararBanco();
        var segunda = instalador.PrepararBanco();

        Assert.True(primeira.Ok, primeira.Detalhe);
        Assert.True(segunda.Ok, segunda.Detalhe);
        Assert.Contains("já estava aplicado", segunda.Mensagem);
        Assert.Equal(17, instalador.ContarTabelas());
    }

    // ── 3. Empresa ───────────────────────────────────────────────────────────
    [Fact]
    public void Configurar_empresa_duas_vezes_nao_duplica_e_atualiza_dados()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);
        instalador.PrepararBanco();

        var primeira = instalador.ConfigurarEmpresa(EmpresaPadrao());
        var segunda = instalador.ConfigurarEmpresa(EmpresaPadrao(razao: "Mercado Central LTDA (nova razão)"));

        Assert.True(primeira.Ok, primeira.Detalhe);
        Assert.True(segunda.Ok, segunda.Detalhe);
        Assert.Contains("já existia", segunda.Mensagem);

        Assert.Equal(1, db.Empresas.Count());
        Assert.Equal(1, db.Lojas.Count());
        Assert.Equal("Mercado Central LTDA (nova razão)", db.Empresas.Single().RazaoSocial);
    }

    [Fact]
    public void Empresa_com_cnpj_invalido_e_recusada()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);

        var resultado = instalador.ConfigurarEmpresa(EmpresaPadrao(cnpj: "123"));

        Assert.False(resultado.Ok);
        Assert.Equal(0, db.Empresas.Count());
    }

    // ── 4. Licença (validação offline) ───────────────────────────────────────
    [Fact]
    public void Licenca_valida_ativa_a_instalacao_sem_servidor()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);
        instalador.PrepararBanco();
        instalador.ConfigurarEmpresa(EmpresaPadrao());

        var empresaId = db.Empresas.Single().Id;
        var (token, publica) = EmitirLicenca(empresaId);

        var resultado = instalador.AtivarLicenca(token, publica);

        Assert.True(resultado.Ok, resultado.Detalhe);
        Assert.Contains("instalação", resultado.Detalhe);
        Assert.Equal(1, db.ConfigLicencas.Count());
    }

    [Fact]
    public void Licenca_assinada_com_outra_chave_e_recusada()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);
        instalador.PrepararBanco();
        instalador.ConfigurarEmpresa(EmpresaPadrao());

        var empresaId = db.Empresas.Single().Id;
        var (token, _) = EmitirLicenca(empresaId);

        using var outraChave = ProvedorDeChaves.EmMemoria();
        var resultado = instalador.AtivarLicenca(token, outraChave.ChaveLicenca.ExportSubjectPublicKeyInfo());

        Assert.False(resultado.Ok);
        Assert.Equal("assinatura_invalida", resultado.Detalhe);
        Assert.Equal(0, db.ConfigLicencas.Count());
    }

    [Fact]
    public void Licenca_expirada_e_recusada()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);
        instalador.PrepararBanco();
        instalador.ConfigurarEmpresa(EmpresaPadrao());

        var empresaId = db.Empresas.Single().Id;
        var (token, publica) = EmitirLicenca(empresaId, TimeSpan.FromMinutes(-10));

        var resultado = instalador.AtivarLicenca(token, publica);

        Assert.False(resultado.Ok);
        Assert.Equal("licenca_expirada", resultado.Detalhe);
    }

    [Fact]
    public void Licenca_de_outra_empresa_e_recusada()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Criar(banco);
        instalador.PrepararBanco();
        instalador.ConfigurarEmpresa(EmpresaPadrao());

        var (token, publica) = EmitirLicenca(Guid.NewGuid());   // empresa que não é a instalada

        var resultado = instalador.AtivarLicenca(token, publica);

        Assert.False(resultado.Ok);
        Assert.Equal("Licença de outra empresa.", resultado.Mensagem);
    }

    [Fact]
    public void Token_adulterado_e_recusado()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);
        instalador.PrepararBanco();
        instalador.ConfigurarEmpresa(EmpresaPadrao());

        var empresaId = db.Empresas.Single().Id;
        var (token, publica) = EmitirLicenca(empresaId);

        // Troca um caractere do payload: a assinatura deixa de fechar.
        var partes = token.Split('.');
        var payloadAdulterado = partes[1].Length > 8
            ? partes[1][..4] + (partes[1][4] == 'A' ? 'B' : 'A') + partes[1][5..]
            : partes[1];
        var tokenAdulterado = $"{partes[0]}.{payloadAdulterado}.{partes[2]}";

        var resultado = instalador.AtivarLicenca(tokenAdulterado, publica);

        Assert.False(resultado.Ok);
        Assert.Contains(resultado.Detalhe, new[] { "assinatura_invalida", "token_malformado", "emissor_invalido", "audiencia_invalida" });
    }

    // ── 5. Administrador ─────────────────────────────────────────────────────
    [Fact]
    public void Administrador_e_criado_uma_vez_e_login_duplicado_e_recusado()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);
        instalador.PrepararBanco();
        instalador.ConfigurarEmpresa(EmpresaPadrao());

        var primeira = instalador.CriarAdministrador("admin", "Landipay#2026");
        var segunda = instalador.CriarAdministrador("admin", "OutraSenha#2026");

        Assert.True(primeira.Ok, primeira.Detalhe);
        Assert.Contains("3 produtos", primeira.Detalhe);
        Assert.False(segunda.Ok);
        Assert.Equal(1, db.Usuarios.Count());
        Assert.Equal(3, db.Produtos.Count());

        // A senha gravada é Argon2id verificável.
        var usuario = db.Usuarios.Single();
        Assert.StartsWith("$argon2id$", usuario.SenhaHash);
        Assert.True(new Argon2idSenhaHasher().Verificar("Landipay#2026", usuario.SenhaHash));
    }

    [Fact]
    public void Administrador_exige_empresa_cadastrada()
    {
        using var banco = new BancoDeTeste();
        var (instalador, _, _) = Criar(banco);
        instalador.PrepararBanco();

        var resultado = instalador.CriarAdministrador("admin", "Landipay#2026");

        Assert.False(resultado.Ok);
        Assert.Contains("Cadastre a empresa", resultado.Mensagem);
    }

    // ── 6. Sequência completa ────────────────────────────────────────────────
    [Fact]
    public void Instalacao_completa_em_sequencia_e_reconhecida_como_existente()
    {
        using var banco = new BancoDeTeste();
        var (instalador, db, _) = Criar(banco);

        Assert.False(instalador.InstalacaoExistente(out _));

        Assert.True(instalador.VerificarPreRequisitos().Ok);
        Assert.True(instalador.PrepararBanco().Ok);
        Assert.True(instalador.ConfigurarEmpresa(EmpresaPadrao()).Ok);

        var empresaId = db.Empresas.Single().Id;
        var (token, publica) = EmitirLicenca(empresaId);
        Assert.True(instalador.AtivarLicenca(token, publica).Ok);
        Assert.True(instalador.CriarAdministrador("admin", "Landipay#2026").Ok);

        Assert.True(instalador.InstalacaoExistente(out var resumo));
        Assert.Contains("1 empresa(s)", resumo);
        Assert.Contains("1 usuário(s)", resumo);
        Assert.Contains("3 produto(s)", resumo);
        Assert.Contains("1 licença(s)", resumo);

        // A instalação deixa rastro auditável de cada passo (o legado não deixava nenhum).
        var acoes = db.Auditoria.OrderBy(a => a.Id).Select(a => a.Acao).ToList();
        Assert.Equal(new[]
        {
            "instalacao.banco", "instalacao.empresa", "instalacao.licenca", "instalacao.administrador"
        }, acoes);
        Assert.All(db.Auditoria.ToList(), a => Assert.False(string.IsNullOrWhiteSpace(a.Maquina)));
    }
}
