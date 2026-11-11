using Landipay.Client.Seguranca;
using Landipay.Domain.Common;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.Seguranca;
using Landipay.Domain.ValueObjects;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Landipay.Client.Servicos;

public sealed record ResultadoPasso(bool Ok, string Mensagem, string? Detalhe = null)
{
    public static ResultadoPasso Sucesso(string mensagem, string? detalhe = null) => new(true, mensagem, detalhe);
    public static ResultadoPasso Falha(string mensagem, string? detalhe = null) => new(false, mensagem, detalhe);
}

public sealed record DadosEmpresa(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    string? InscricaoEstadual,
    RegimeTributario Regime,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Cidade,
    string Uf,
    string Cep,
    string CodigoLoja,
    string NomeLoja);

/// <summary>
/// Passos do instalador, todos <b>idempotentes</b>: rodar de novo sobre uma instalação
/// existente atualiza o que existe em vez de duplicar ou quebrar. É o oposto do legado,
/// que reinstalava engine de banco e reescrevia configuração a cada execução.
///
/// Nada aqui baixa e executa binário: o instalador não tem canal de download embutido.
/// A licença é validada offline com a chave pública pinada.
/// </summary>
public sealed class ServicoInstalador
{
    private readonly LandipayDbContext _db;
    private readonly string _pastaDados;
    private readonly Argon2idSenhaHasher _hasher;
    private readonly TimeProvider _relogio;

    public ServicoInstalador(LandipayDbContext db, string pastaDados,
        Argon2idSenhaHasher? hasher = null, TimeProvider? relogio = null)
    {
        _db = db;
        _pastaDados = pastaDados;
        _hasher = hasher ?? new Argon2idSenhaHasher();
        _relogio = relogio ?? TimeProvider.System;
    }

    /// <summary>Pasta onde ficam banco, preferências e logs desta instalação.</summary>
    public string PastaDeDados => _pastaDados;

    /// <summary>
    /// Trilha de auditoria da instalação. O legado não deixava rastro de quem instalou,
    /// com qual licença e quando — impossível reconstruir depois. Aqui cada passo grava.
    /// </summary>
    private void Auditar(string acao, string entidade, Guid? empresaId = null, Guid? lojaId = null,
        Guid? usuarioId = null, string? entidadeId = null, string? detalhe = null)
    {
        _db.Auditoria.Add(LogAuditoria.Registrar(
            acao, entidade, usuarioId, empresaId, lojaId, entidadeId,
            detalhe is null ? null : JsonSerializer.Serialize(new { detalhe }),
            OrigemLog.Sistema, _relogio.GetUtcNow(), Environment.MachineName));
        _db.SaveChanges();
    }

    // ── 1. Pré-requisitos ────────────────────────────────────────────────────
    public ResultadoPasso VerificarPreRequisitos()
    {
        var problemas = new List<string>();

        if (Environment.Version.Major < 8)
            problemas.Add($"runtime .NET {Environment.Version} (mínimo 8)");

        try
        {
            Directory.CreateDirectory(_pastaDados);
            var teste = Path.Combine(_pastaDados, $".escrita-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(teste, "ok");
            File.Delete(teste);
        }
        catch (Exception ex)
        {
            problemas.Add($"sem permissão de escrita em {_pastaDados} ({ex.GetType().Name})");
        }

        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(_pastaDados));
            if (raiz is not null)
            {
                var livre = new DriveInfo(raiz).AvailableFreeSpace;
                if (livre < 200L * 1024 * 1024)
                    problemas.Add($"espaço livre insuficiente ({livre / 1024 / 1024} MB)");
            }
        }
        catch (IOException) { /* unidade de rede: não bloqueia */ }

        return problemas.Count == 0
            ? ResultadoPasso.Sucesso("Pré-requisitos atendidos.",
                $".NET {Environment.Version} · pasta gravável · espaço livre ok")
            : ResultadoPasso.Falha("Pré-requisitos pendentes.", string.Join(" · ", problemas));
    }

    // ── 2. Banco de dados ────────────────────────────────────────────────────
    public ResultadoPasso PrepararBanco()
    {
        try
        {
            var antes = ContarTabelas();
            _db.Database.Migrate();
            var depois = ContarTabelas();

            Auditar("instalacao.banco", "Instalacao", detalhe: $"{depois} tabelas");

            return ResultadoPasso.Sucesso(
                antes == 0 ? "Schema aplicado." : "Schema já estava aplicado (nada a fazer).",
                $"{depois} tabelas · migração idempotente");
        }
        catch (Exception ex)
        {
            return ResultadoPasso.Falha("Falha ao preparar o banco.", ex.Message);
        }
    }

    public int ContarTabelas()
    {
        var conexao = _db.Database.GetDbConnection();
        var abriu = conexao.State != System.Data.ConnectionState.Open;
        if (abriu) conexao.Open();

        try
        {
            using var cmd = conexao.CreateCommand();
            // Consulta fixa ao catálogo do SQLite (introspecção, não modelável no EF).
            // Sem interpolação e sem parâmetro vindo de fora: não há entrada — logo não há injeção.
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
        finally { if (abriu) conexao.Close(); }
    }

    // ── 3. Empresa e loja ────────────────────────────────────────────────────
    public ResultadoPasso ConfigurarEmpresa(DadosEmpresa dados)
    {
        try
        {
            var cnpj = Empresa.NormalizarCnpj(dados.Cnpj);
            if (cnpj.Length != 14) return ResultadoPasso.Falha("CNPJ inválido.", "precisa de 14 dígitos");

            var endereco = new Endereco(dados.Logradouro, dados.Numero, dados.Complemento,
                dados.Bairro, dados.Cidade, dados.Uf, dados.Cep);

            var empresa = _db.Empresas.FirstOrDefault(e => e.Cnpj == cnpj);
            var criada = empresa is null;

            if (empresa is null)
            {
                empresa = Empresa.Criar(dados.RazaoSocial, dados.NomeFantasia ?? dados.RazaoSocial,
                    cnpj, dados.Regime, endereco, dados.InscricaoEstadual);
                _db.Empresas.Add(empresa);
            }
            else
            {
                empresa.AtualizarDadosCadastrais(dados.RazaoSocial, dados.NomeFantasia ?? dados.RazaoSocial,
                    dados.InscricaoEstadual, endereco);
            }

            _db.SaveChanges();

            var loja = _db.Lojas.FirstOrDefault(l => l.EmpresaId == empresa.Id && l.Codigo == dados.CodigoLoja);
            if (loja is null)
            {
                _db.Lojas.Add(Loja.Criar(empresa.Id, dados.CodigoLoja, dados.NomeLoja, endereco));
                _db.SaveChanges();
            }

            Auditar("instalacao.empresa", "Empresa", empresa.Id, loja?.Id,
                entidadeId: empresa.Id.ToString(), detalhe: criada ? "criada" : "atualizada");

            return ResultadoPasso.Sucesso(
                criada ? "Empresa cadastrada." : "Empresa já existia — dados atualizados.",
                $"{empresa.RazaoSocial} · CNPJ {cnpj} · loja {dados.CodigoLoja}");
        }
        catch (DomainException ex)
        {
            return ResultadoPasso.Falha("Dados da empresa inválidos.", ex.Message);
        }
        catch (DbUpdateException ex)
        {
            return ResultadoPasso.Falha("Não foi possível gravar a empresa.", ex.InnerException?.Message ?? ex.Message);
        }
    }

    // ── 4. Ativação (offline) ────────────────────────────────────────────────
    public ResultadoPasso AtivarLicenca(string token, byte[] chavePublicaSpki)
    {
        var resultado = ValidadorDeLicencaJwt.Validar(token, chavePublicaSpki, _relogio.GetUtcNow());

        if (!resultado.Valida)
            return ResultadoPasso.Falha("Licença recusada.", resultado.Codigo);

        if (resultado.EmpresaId is null)
            return ResultadoPasso.Falha("Licença sem empresa.", "claim 'emp' ausente");

        var empresaExiste = _db.Empresas.Any(e => e.Id == resultado.EmpresaId.Value);
        if (!empresaExiste)
            return ResultadoPasso.Falha("Licença de outra empresa.",
                "o CNPJ instalado não corresponde ao da licença");

        var instalacaoId = resultado.InstalacaoId ?? Guid.NewGuid().ToString("N");

        var escopo = resultado.Apps is { Count: > 0 } ? resultado.Apps : new[] { "admin", "pos" };

        var registro = _db.ConfigLicencas.FirstOrDefault(l => l.InstalacaoId == instalacaoId);
        if (registro is null)
        {
            _db.ConfigLicencas.Add(ConfigLicenca.Registrar(
                resultado.EmpresaId.Value, instalacaoId, token, "lic-api",
                _relogio.GetUtcNow(), resultado.ExpiraEm!.Value, apps: escopo));
        }
        else
        {
            registro.RegistrarValidacao(_relogio.GetUtcNow(), resultado.ExpiraEm, token);
            // Renovação pode mudar o escopo: a licença nova é a fonte da verdade.
            registro.DefinirApps(escopo);
        }

        _db.SaveChanges();

        Auditar("instalacao.licenca", "Licenca", resultado.EmpresaId,
            entidadeId: instalacaoId,
            detalhe: $"expira {resultado.ExpiraEm:yyyy-MM-dd} · apps {string.Join("+", escopo)}");

        return ResultadoPasso.Sucesso("Licença ativada.",
            $"instalação {instalacaoId} · plano {resultado.Plano} · expira {resultado.ExpiraEm:yyyy-MM-dd} · " +
            $"apps {string.Join("+", escopo)}");
    }

    // ── 5. Primeiro administrador ────────────────────────────────────────────
    /// <summary>Já existe administrador nesta base? (reparo não pede senha de novo)</summary>
    public bool AdminExistente() => _db.Usuarios.Any(u => u.Ativo);

    public ResultadoPasso CriarAdministrador(string login, string senha)
    {
        if (string.IsNullOrWhiteSpace(login))
            return ResultadoPasso.Falha("Login é obrigatório.");
        if (senha is null || senha.Length < 10)
            return ResultadoPasso.Falha("Senha muito curta.", "mínimo de 10 caracteres");

        var empresa = _db.Empresas.FirstOrDefault();
        if (empresa is null)
            return ResultadoPasso.Falha("Cadastre a empresa antes do administrador.");

        var loja = _db.Lojas.FirstOrDefault(l => l.EmpresaId == empresa.Id);
        var normalizado = login.Trim().ToLowerInvariant();

        if (_db.Usuarios.Any(u => u.Login == normalizado))
            return ResultadoPasso.Falha("Já existe um usuário com esse login.",
                "escolha outro ou redefina a senha no sistema");

        try
        {
            var usuario = Usuario.Criar(empresa.Id, login.Trim(), normalizado, _hasher.Hash(senha), loja?.Id);
            usuario.DefinirSenha(usuario.SenhaHash, _relogio.GetUtcNow(), exigirTroca: false);

            _db.Usuarios.Add(usuario);
            _db.SaveChanges();

            Auditar("instalacao.administrador", "Usuario", empresa.Id, loja!.Id, usuario.Id,
                usuario.Id.ToString(), $"login={usuario.Login}");

            var produtos = CatalogoDemo.Semear(_db, empresa.Id, loja!.Id, usuario.Id);

            return ResultadoPasso.Sucesso("Administrador criado.",
                produtos > 0 ? $"{produtos} produtos de demonstração no catálogo" : "catálogo já existia");
        }
        catch (DomainException ex)
        {
            return ResultadoPasso.Falha("Não foi possível criar o administrador.", ex.Message);
        }
    }

    // ── 6. Aplicativos da estação ────────────────────────────────────────────
    /// <summary>
    /// Registra quais aplicativos esta estação pode abrir. O MSI entrega os dois executáveis
    /// (Opção A); a escolha aqui define o papel da máquina e fica auditada.
    ///
    /// A licença é o teto: se ela declara apps, a estação não pode habilitar app fora do escopo.
    /// </summary>
    public ResultadoPasso SelecionarAplicativos(EscolhaDeAplicativos escolha)
    {
        if (!escolha.Algum)
            return ResultadoPasso.Falha("Escolha ao menos um aplicativo.",
                "selecione Admin, Caixa ou ambos");

        // Mais de uma licença pode existir (reparo, renovação): vale a mais recente.
        // SQLite não ordena DateTimeOffset no SQL — ordena em memória.
        var licenca = _db.ConfigLicencas.ToList().OrderByDescending(l => l.EmitidaEm).FirstOrDefault();
        if (licenca is null)
            return ResultadoPasso.Falha("Ative a licença antes de escolher os aplicativos.");

        // A licença é o teto: habilitar app fora do escopo é recusado, não silenciado.
        var permitidos = licenca.Apps();
        var foraDoEscopo = escolha.Habilitados
            .Where(a => !permitidos.Contains(a.ToString().ToLowerInvariant()))
            .ToList();

        if (foraDoEscopo.Count > 0)
        {
            var nomes = string.Join(" e ", foraDoEscopo.Select(CatalogoDeAcesso.NomeDoApp));
            var escopo = string.Join(" + ", permitidos.Select(p =>
                p == "admin" ? "Admin" : "Caixa"));

            Auditar("instalacao.aplicativos.negado", "Estacao",
                detalhe: $"licença permite {escopo}; pedido {escolha.Descricao}");

            return ResultadoPasso.Falha(
                $"A licença desta instalação não habilita {nomes}.",
                $"licença permite: {escopo}");
        }

        var caminho = Path.Combine(_pastaDados, "apps.json");
        var config = new ConfiguracaoDeEstacao(escolha.Tipo,
            escolha.Habilitados.Select(a => a.ToString().ToLowerInvariant()).ToList(),
            _relogio.GetUtcNow(), Environment.MachineName);

        File.WriteAllText(caminho, JsonSerializer.Serialize(config,
            new JsonSerializerOptions { WriteIndented = true }));

        Auditar("instalacao.aplicativos", "Estacao", detalhe: escolha.Descricao);

        return ResultadoPasso.Sucesso($"Estação configurada: {escolha.Descricao}.",
            $"{escolha.Habilitados.Count} aplicativo(s) habilitado(s) nesta máquina");
    }

    /// <summary>Escopo que a licença ativa permite nesta instalação (vazio se não houver licença).</summary>
    public IReadOnlyList<string> AppsDaLicenca() =>
        _db.ConfigLicencas.ToList().OrderByDescending(l => l.EmitidaEm).FirstOrDefault()?.Apps()
        ?? Array.Empty<string>();

    /// <summary>Configuração já gravada (reinstalação mostra a escolha anterior).</summary>
    public ConfiguracaoDeEstacao? EstacaoConfigurada()
    {
        var caminho = Path.Combine(_pastaDados, "apps.json");
        if (!File.Exists(caminho)) return null;

        try
        {
            return JsonSerializer.Deserialize<ConfiguracaoDeEstacao>(File.ReadAllText(caminho));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Estado da instalação ─────────────────────────────────────────────────
    /// <summary>
    /// Dados da empresa já instalada, para pré-preencher o formulário numa reinstalação
    /// (reparo). Sem isso, "reinstalar" obrigaria a redigitar tudo — e o operador acabaria
    /// criando cadastro duplicado com CNPJ formatado diferente.
    /// </summary>
    public DadosEmpresa? EmpresaInstalada()
    {
        var empresa = _db.Empresas.FirstOrDefault();
        if (empresa is null) return null;

        var loja = _db.Lojas.FirstOrDefault(l => l.EmpresaId == empresa.Id);
        var endereco = empresa.Endereco;

        return new DadosEmpresa(
            empresa.Cnpj, empresa.RazaoSocial, empresa.NomeFantasia, empresa.InscricaoEstadual,
            empresa.Regime,
            endereco?.Logradouro ?? string.Empty, endereco?.Numero ?? string.Empty,
            endereco?.Complemento, endereco?.Bairro ?? string.Empty,
            endereco?.Cidade ?? string.Empty, endereco?.Uf ?? string.Empty,
            endereco?.Cep ?? string.Empty,
            loja?.Codigo ?? "01", loja?.Nome ?? "Loja 01");
    }

    public bool InstalacaoExistente(out string resumo)
    {
        var empresas = _db.Empresas.Count();
        if (empresas == 0) { resumo = "nenhuma instalação anterior"; return false; }

        var usuarios = _db.Usuarios.Count();
        var produtos = _db.Produtos.Count();
        var licencas = _db.ConfigLicencas.Count();
        resumo = $"{empresas} empresa(s) · {usuarios} usuário(s) · {produtos} produto(s) · {licencas} licença(s)";
        return true;
    }
}
