# 01 — Modelo de dados

> Documento da Fase 1. Estado: **concluída e verificada** (migrations aplicadas, 11 testes verdes).
> Código: `src/Landipay.Domain` (entidades e invariantes) e `src/Landipay.Infrastructure` (mapeamento EF Core + migrations).
> Números deste documento foram lidos do banco gerado, não estimados: **17 tabelas, 156 colunas, 28 índices (10 únicos), 19 check constraints, 17 chaves estrangeiras, 2 triggers**.

---

## 1. Objetivo

Modelar o domínio do PDV Landipay de forma que **os desvios perigosos do legado sejam fisicamente impossíveis**, não apenas "evitados por disciplina":

- nenhuma query montada por concatenação (A4) → o mapeamento é tipado e o único acesso a SQL cru é parametrizado e testado;
- auditoria que não pode ser reescrita (append-only no banco);
- dinheiro em `decimal`, com invariantes na entidade, não na tela;
- chaves geradas no cliente (offline-first), sem coordenação com servidor.

---

## 2. Convenções

| Convenção | Decisão | Motivo |
|---|---|---|
| Chave primária | `Guid` (v7-like, gerada no cliente) | Duas lojas offline não podem colidir ao sincronizar. Exceção: `LogAuditoria` usa `long` sequencial, para ordem cronológica estável local. |
| Datas | `DateTimeOffset` em UTC | Apresentação converte pelo `Loja.FusoHorario`. Nada de `DateTime.Now` no domínio. |
| Dinheiro e quantidade | `decimal` com precisão **18,4** | Nunca `float`/`double`. Arredondamento explícito (`MidpointRounding.AwayFromZero`) na entidade. |
| Enum | gravado como **string** | Legibilidade em auditoria e relatório; `'Finalizada'` em vez de `2`. |
| Nome de tabela | plural, PascalCase (`Vendas`, `ItensVenda`) | Estável para relatórios e para o DBA. |
| Exclusão | lógica (`Ativo`/`Desativar()`) em cadastros | Venda cancelada é **estado**, não exclusão: o histórico é evidência. |
| Descrição em item de venda | **snapshot** (`DescricaoSnapshot`) | Se o produto for renomeado amanhã, o cupom de ontem continua correto. |
| Eventos para a nuvem | **outbox** transacional | O caixa não depende de rede; a nuvem consome a fila. |

---

## 3. Diagrama de entidades

```
                         ┌──────────────┐
                         │   Empresa    │  Cnpj UNIQUE, Endereco (owned)
                         └──────┬───────┘
              ┌─────────────────┼──────────────────┬─────────────────┐
              │                 │                  │                 │
        ┌─────▼─────┐    ┌──────▼──────┐    ┌──────▼──────┐   ┌──────▼────────┐
        │   Loja    │    │  Usuario    │    │  Produto    │   │ ConfigLicenca │
        │ (Emp,Cod) │    │(Emp,Login)  │    │ (Emp,Sku)   │   │ InstalacaoId  │
        └─────┬─────┘    └──────┬──────┘    └──────┬──────┘   └───────────────┘
              │                 │                  │
              │          ┌──────▼───────┐   ┌──────▼────────────┐
              │          │ UsuarioPapel │   │ EstoqueMovimento  │
              │          └──────┬───────┘   │ (Loja,Produto,Em) │
              │                 │           └───────────────────┘
       ┌──────▼───────┐  ┌──────▼──────┐
       │ CaixaTurno   │  │   Papel     │──┐
       │ (Loja,Num)   │  └─────────────┘  │  ┌───────────────┐
       └──────┬───────┘                   └──│ PapelPermissao│
              │                              └───────┬───────┘
       ┌──────▼────────┐                             │
       │ MovimentoCaixa│                        ┌────▼──────┐
       │ (Sangria/Supr)│                        │ Permissao │
       └───────────────┘                        └───────────┘

       ┌──────────────┐  1..N   ┌────────────┐  1..N  ┌──────────┐
       │    Venda     │────────▶│  ItemVenda │───────▶│ Produto  │
       │ (Loja,Numero)│         └────────────┘        └──────────┘
       │  Status      │  1..N   ┌────────────┐
       │  Desconto    │────────▶│  Pagamento │
       └──────┬───────┘         └────────────┘
              │
       ┌──────▼────────────┐        ┌──────────────────┐
       │ OutboxMensagem    │        │   LogAuditoria   │  ← APPEND-ONLY
       │ (fila de sync)     │        │ (triggers no BD) │
       └───────────────────┘        └──────────────────┘
```

**Agregado de venda:** `Venda` é a raiz. `ItemVenda` e `Pagamento` não existem fora dela (FK `Cascade`), e o construtor é `internal` — nenhum código de fora do agregado cria um item solto.

---

## 4. Dicionário de dados

### 4.1 Empresa / Loja

| Tabela | Coluna | Tipo | Regra |
|---|---|---|---|
| `Empresas` | `Id` | TEXT (Guid) | PK |
| | `RazaoSocial`, `NomeFantasia` | TEXT(200) | obrigatório |
| | `Cnpj` | TEXT(14) | **UNIQUE**, só dígitos, `CHECK length(Cnpj) = 14` |
| | `InscricaoEstadual` | TEXT(30) | opcional |
| | `Regime` | TEXT(40) | enum como string |
| | `Endereco*` (7 colunas) | TEXT | owned type `Endereco`, obrigatório |
| `Lojas` | `Codigo` | TEXT(10) | **UNIQUE com `EmpresaId`** |
| | `FusoHorario` | TEXT(60) | padrão `America/Sao_Paulo` |

### 4.2 Segurança

| Tabela | Coluna | Tipo | Regra |
|---|---|---|---|
| `Usuarios` | `Login` | TEXT(60) | **UNIQUE com `EmpresaId`**, normalizado minúsculo |
| | `SenhaHash` | TEXT(255) | PHC string do Argon2id — **jamais senha em claro** |
| | `TentativasFalhas` | INTEGER | `CHECK >= 0` |
| | `BloqueadoAte` | TEXT (datetime) | lockout progressivo (5 falhas → 5 min; 10 → 30 min) |
| `Papeis` | `Nome` | TEXT(60) | **UNIQUE** |
| `Permissoes` | `Codigo` | TEXT(80) | **UNIQUE** (ex.: `venda.cancelar`) |
| `UsuarioPapeis` / `PapelPermissoes` | PK composta | — | N:N com `Cascade` |

### 4.3 Catálogo

| Tabela | Coluna | Tipo | Regra |
|---|---|---|---|
| `Produtos` | `Sku` | TEXT(40) | **UNIQUE com `EmpresaId`** |
| | `CodigoBarras` | TEXT(20) | **UNIQUE com `EmpresaId`**, filtrado `IS NOT NULL` |
| | `PrecoVenda`, `CustoAtual`, `EstoqueMinimo` | decimal(18,4) | `CHECK >= 0` |
| `EstoqueMovimentos` | `Quantidade` | decimal(18,4) | `CHECK > 0` (sinal vem do `Tipo`) |
| | `SaldoApos` | decimal(18,4) | `CHECK >= 0` — saldo negativo é erro de operação, não "estado válido" |
| | `VendaId` | Guid? | preenchido quando o movimento nasce de venda |

### 4.4 Vendas

| Tabela | Coluna | Tipo | Regra |
|---|---|---|---|
| `Vendas` | `Numero` | INTEGER | **UNIQUE com `LojaId`**, `CHECK > 0` |
| | `Status` | TEXT(20) | `Aberta` / `Finalizada` / `Cancelada` |
| | `Desconto` | decimal(18,4) | `CHECK >= 0`, nunca maior que o subtotal (domínio) |
| | `MotivoCancelamento` | TEXT(300) | obrigatório para cancelar, mínimo 5 caracteres |
| `ItensVenda` | `Quantidade` | decimal(18,4) | `CHECK > 0` |
| | `PrecoUnitario`, `Desconto`, `Total` | decimal(18,4) | `CHECK >= 0` nos três |
| | `DescricaoSnapshot` | TEXT(200) | nome do produto no momento da venda |
| `Pagamentos` | `Valor` | decimal(18,4) | `CHECK > 0` |
| | `Forma` | TEXT(20) | `Dinheiro`/`Debito`/`Credito`/`Pix`/`Vale` |

Subtotal e total **não são colunas**: derivam dos itens (`Subtotal = Σ itens`, `Total = Subtotal − Desconto`). Isso elimina a classe de bug "total gravado diverge da soma dos itens". Relatórios agregam por SQL quando necessário (PostgreSQL, Fase 2).

### 4.5 Caixa

| Tabela | Coluna | Tipo | Regra |
|---|---|---|---|
| `CaixaTurnos` | `Numero` | INTEGER | **UNIQUE com `LojaId`** |
| | `ValorAbertura` | decimal(18,4) | `CHECK >= 0` |
| | `ValorFechamentoInformado` / `Calculado` | decimal(18,4) | ambos gravados; a diferença é o achado de auditoria |
| `MovimentosCaixa` | `Tipo` | TEXT(20) | `Sangria` / `Suprimento` |
| | `Valor` | decimal(18,4) | `CHECK > 0` |
| | `Motivo` | TEXT(200) | obrigatório |

### 4.6 Auditoria, outbox e licença

| Tabela | Detalhe |
|---|---|
| `LogAuditoria` | `long Id`, `Acao`, `Entidade`, `EntidadeId`, `DadosJson`, `Origem` (`Sistema`/`Operador`/`SuporteRemoto`), `UsuarioId`, `Maquina`, `OcorridoEm`. **Append-only por trigger.** |
| `OutboxMensagens` | `Tipo`, `PayloadJson`, `EnviadoEm`, `Tentativas` (`CHECK >= 0`), `UltimoErro`. Índice `(LojaId, EnviadoEm)` para varredura da fila. |
| `ConfigLicencas` | `InstalacaoId` **UNIQUE**, `TokenAssinado` (opaco), `KeyId`, `ExpiraEm`, `ToleranciaOfflineDias` (padrão 7). `CHECK ExpiraEm > EmitidaEm`. |

---

## 5. Índices (28, dos quais 10 únicos)

| Índice único | Protege contra |
|---|---|
| `Empresas.Cnpj` | empresa duplicada |
| `Lojas(EmpresaId, Codigo)` | duas "loja 01" na mesma empresa |
| `Usuarios(EmpresaId, Login)` | login duplicado |
| `Produtos(EmpresaId, Sku)` | SKU repetido |
| `Produtos(EmpresaId, CodigoBarras)` filtrado | dois produtos com o mesmo código de barras |
| `Vendas(LojaId, Numero)` / `CaixaTurnos(LojaId, Numero)` | número de venda/turno repetido |
| `Papeis.Nome` / `Permissoes.Codigo` / `ConfigLicencas.InstalacaoId` | — |

Índices não únicos relevantes: `Produtos(EmpresaId, Nome)` (busca no caixa), `Vendas(LojaId, Status, CriadoEm)` (fechamento de turno), `EstoqueMovimentos(LojaId, ProdutoId, OcorridoEm)` (extrato), `OutboxMensagens(LojaId, EnviadoEm)` (fila), `LogAuditoria(OcorridoEm)`, `LogAuditoria(UsuarioId)`.

---

## 6. Integridade — três camadas

1. **Domínio** (testável sem banco): `Venda.Finalizar` recusa pagamento insuficiente; `AdicionarItem` recusa produto inativo; `CaixaTurno.Fechar` não aceita turno já fechado; `Usuario` aplica lockout progressivo.
2. **Banco** (sobrevive a bug de aplicação, script manual ou acesso direto ao `.db`): 19 `CHECK`, 10 `UNIQUE`, 17 `FOREIGN KEY` com `DeleteBehavior.Restrict` em cadastros (não se apaga empresa com venda) e `Cascade` apenas dentro do agregado (venda → itens/pagamentos).
3. **Triggers**: `LogAuditoria` recusa `UPDATE` e `DELETE` com `RAISE(ABORT)`.

```sql
-- gerado na migration InitialCreate (Up)
CREATE TRIGGER trg_LogAuditoria_BloqueiaUpdate
BEFORE UPDATE ON LogAuditoria
BEGIN
    SELECT RAISE(ABORT, 'LogAuditoria e append-only: UPDATE nao permitido');
END;
```

**Por que trigger e não "a aplicação não faz update":** a trilha de auditoria é a evidência de quem cancelou venda e quem abriu suporte remoto. Se ela pode ser reescrita por um `UPDATE` (bug, DBA, atacante com acesso ao arquivo), ela não vale nada.

---

## 7. A4 — zero SQL concatenado

**Como o EF garante:** toda consulta é expressão tipada (`db.Produtos.Where(p => p.Sku == sku)`) que o provider traduz em SQL **com parâmetros** (`@p0`). Nenhuma interpolação de string acontece no caminho.

**Regra para SQL cru:** só `ExecuteSqlRaw`/`FromSqlRaw` **com placeholders `{0}`** (que o EF converte em `DbParameter`) — nunca `$"..."`. Os dois casos de SQL cru hoje são a migration (DDL, não recebe entrada de usuário) e os testes negativos.

**Prova automatizada** (`ModeloDeDadosTests.Nome_de_produto_com_payload_de_injecao_e_gravado_literalmente`):

```csharp
const string payload = "'; DROP TABLE Produtos; --";
db.Produtos.Add(Produto.Criar(empresa.Id, "INJ-001", payload, 1.00m));
db.SaveChanges();

var carregado = db.Produtos.Single(p => p.Sku == "INJ-001");
Assert.Equal(payload, carregado.Nome);      // gravado literalmente
Assert.Equal(2, db.Produtos.Count());        // tabela intacta
```

Contraste com o legado (`DataAccess.cs:125`), que montava `INSERT` com `'" + config.Enterprise.company.name + "'`: um nome de empresa com apóstrofo quebrava a query, e um nome malicioso executava comando.

---

## 8. Connection string e segredos (A2)

| Camada | Hoje (Fase 1) | Fase 2+ |
|---|---|---|
| Design-time (`dotnet ef`) | `LANDIPAY_DB` (env) ou `Data Source=landipay-dev.db` | idem — nunca arquivo versionado com senha |
| Runtime (cliente) | — | secret store do SO (DPAPI), arquivo de preferências **sem** segredo |
| Backend | — | variáveis de ambiente + Key Vault |

Não existe senha de banco no código nem em `.ini`. O banco local do PDV é SQLite cifrado (SQLCipher) a partir da Fase 3 — a chave vem de DPAPI por usuário.

---

## 9. Migrations

```bash
# criar nova migration
dotnet ef migrations add <Nome> -p src/Landipay.Infrastructure -o Persistence/Migrations

# aplicar
dotnet ef database update          # usa LANDIPAY_DB ou o dev padrão

# inspecionar o SQL que será executado
dotnet ef migrations script
```

Estado atual: **1 migration** (`20261111054456_InitialCreate`), aplicada, idempotente (`__EFMigrationsHistory` registra a versão; reaplicar não faz nada). Os triggers de auditoria entram no `Up()` via `migrationBuilder.Sql(...)` — SQLite não tem DDL declarativo para isso, então fica explícito e versionado junto.

---

## 10. Papéis e permissões iniciais (seed na Fase 4)

| Papel | Permissões (exemplos) |
|---|---|
| Administrador | tudo, incluindo `usuario.gerenciar`, `licenca.visualizar` |
| Gerente | `venda.cancelar`, `caixa.sangria`, `relatorio.ver`, `produto.editar` |
| Operador | `venda.registrar`, `caixa.abrir`, `caixa.fechar`, `produto.consultar` |

A autorização é verificada por permissão (`venda.cancelar`), nunca por nome de papel no código — o papel é configuração, a permissão é contrato.

---

## 11. O que mudou em relação ao legado

| Legado (verificado na análise) | Landipay | Anti-padrão |
|---|---|---|
| `INSERT` montado por concatenação com dados da API de CNPJ | EF Core tipado + 100% parametrizado; teste de injeção automatizado | A4 |
| Connection string concatenada na mão (`Server=...;User Id=...`) | config externa / secret store; nunca em texto no repositório | A4 |
| Senha `sa` gravada em `Config.ini` | não existe engine de banco instalado na loja (LD-01); SQLite cifrado | A4 / achado adicional |
| Log de sessão enviado ao fornecedor sem controle | `LogAuditoria` local append-only com origem declarada (`SuporteRemoto`) | A6 |
| Sem rastro de quem cancelou venda | venda cancelada guarda motivo + autor + timestamp; auditoria imutável | A6 |
| Instalador sem versionamento de schema | migrations versionadas e idempotentes | A3 (deploy verificável) |

---

## 12. Testes (resultado real)

`dotnet test` → **Aprovado: 11, Falha: 0**, ~1 s.

| # | Teste | O que prova |
|---|---|---|
| 1 | `Migrations_criam_as_17_tabelas_e_os_triggers_de_auditoria` | estrutura real do banco |
| 2 | `Venda_ponta_a_ponta_persiste_itens_pagamentos_e_totais` | fluxo de venda com totais corretos |
| 3 | `Venda_sem_pagamento_suficiente_nao_finaliza` | invariante de domínio |
| 4 | `Produto_inativo_nao_entra_na_venda` | regra de catálogo |
| 5 | `Sku_duplicado_na_mesma_empresa_viola_indice_unico` | unicidade no banco |
| 6 | `Check_constraint_do_banco_bloqueia_numero_de_venda_invalido` | CHECK mesmo com INSERT cru |
| 7 | `Auditoria_e_append_only_bloqueia_update_e_delete` | triggers de imutabilidade |
| 8 | `Nome_de_produto_com_payload_de_injecao_e_gravado_literalmente` | **A4** |
| 9 | `Cinco_falhas_bloqueiam_por_5_minutos_e_dez_por_30` | **A5** (lockout no domínio) |
| 10 | `Licenca_expirada_sobrevive_apenas_dentro_da_tolerancia_offline` | janela offline explícita |
| 11 | `Movimento_de_estoque_guarda_saldo_com_quatro_casas` | precisão decimal |

---

## 13. Limitações conhecidas (registradas, não escondidas)

1. **SQLite guarda `decimal` como TEXT.** Aritmética de dinheiro acontece em `decimal` na aplicação (correto), mas `ORDER BY`/`SUM` direto em coluna decimal no SQLite não é confiável. Relatórios agregados rodam no PostgreSQL (Fase 2), onde a coluna é `numeric(18,4)`.
2. **Sem controle de concorrência otimista** nesta fase. O cenário real é um único terminal por caixa; `Versao`/`RowVersion` entra na Fase 3 se houver escrita concorrente (ex.: retaguarda + caixa simultâneos).
3. **Sem filtro global de soft delete.** `Ativo` é explícito nas consultas; um filtro global pode entrar na Fase 3, mas esconde registros de auditoria se mal configurado — preferi explícito.
4. **`LogAuditoria` sem particionamento.** Para o volume de uma loja (dezenas de milhares de linhas/ano) não é problema; arquivamento para a nuvem entra na Fase 2 via outbox.


---

## Atualização — Permissões e escopo de aplicativo

**Estado:** implementada e verificada (migrations aplicadas; 89 testes verdes naquele momento — hoje a suíte é **94/94**, ver `docs/05 §2`).

### Tabelas novas

| Tabela | Conteúdo | Regra |
|---|---|---|
| `Permissoes` | catálogo de códigos (`relatorio.ler`, `produto.escrever`, …) | `Codigo` único, minúsculo |
| `Papeis` | Admin, Gerente, OperadorCaixa (`Sistema = 1`) | `Nome` único |
| `PapelPermissoes` | papel × permissão | PK composta, cascade |
| `UsuarioPapeis` | usuário × papel | PK composta, cascade |

### Matriz de permissões (implementada em `CatalogoDeAcesso`)

| Permissão | Admin | Gerente | OperadorCaixa |
|---|:--:|:--:|:--:|
| `relatorio.ler` | ✅ | ✅ | ❌ |
| `produto.ler` | ✅ | ✅ | ✅ |
| `produto.escrever` | ✅ | ✅ | ❌ |
| `estoque.movimentar` | ✅ | ✅ | ❌ |
| `venda.criar` | ❌ | ✅ | ✅ |
| `venda.cancelar` | ✅ | ✅ | ❌ |
| `caixa.abrir` | ❌ | ✅ | ✅ |
| `caixa.fechar` | ❌ | ✅ | ✅ |
| `caixa.movimentar` | ❌ | ✅ | ✅ |
| `usuario.gerenciar` | ✅ | ❌ | ❌ |
| `configuracao.escrever` | ✅ | ❌ | ❌ |
| `licenca.ler` | ✅ | ❌ | ❌ |
| `empresa.editar` | ✅ | ❌ | ❌ |

**Decisão de política:** o Admin **não opera a frente de caixa**. Segregação de funções preservada;
quem precisa das duas coisas recebe os dois papéis (Admin + Gerente), e o evento fica auditado.

### Escopo de aplicativo na sessão

`SessaoUsuario` carrega `Aplicativo` (`Admin`/`Pos`) além de `UsuarioId`, `Login`, `Nome`, `Papeis`.
`ServicoDeAutorizacao.PodeAcessarApp(usuarioId, app)` cruza papéis → apps permitidos:

| Papel | Apps |
|---|---|
| Admin | Admin |
| Gerente | Admin + Pos |
| OperadorCaixa | Pos |

Login no app errado é **negado e auditado** (`acesso.negado`, com `app`, `papeis` e `motivo`).

### Auditoria de negação

`LogAuditoria.Acao = "acesso.negado"`, `DadosJson = {"app":"Pos","papeis":"Admin","motivo":"login"}`.
`AcessoNegadoException` é lançada pelo serviço e a UI apenas exibe o código da permissão que faltou.

### Estação e turno

`CaixaTurno` já registra abertura/fechamento por loja e usuário. A capacidade da estação é
registrada em `apps.json` (`Tipo`: Admin/Pos/Mista, `Aplicativos`, `DefinidaEm`, `Maquina`) —
arquivo, não tabela, porque descreve a máquina e não a operação; a decisão que importa para
auditoria é o evento `instalacao.aplicativos`.
