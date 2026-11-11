# 00 — Análise reversa do legado (Syntax Plus PDV / Gestor)

> Data: 11/11/2026 · Escopo: `C:\Program Files (x86)\Syntax Tecnologia\Syntax Plus PDV` (252 arquivos, 637 MB) e a pasta de distribuição `REFERENCIA` (262 arquivos, 563,7 MB).
> Regra cumprida: **nenhum binário do legado foi executado** e nada foi executado com acesso à rede.
> Evidências brutas desta etapa: [`_evidence/listagem.txt`](_evidence/listagem.txt) · [`_evidence/ilspy-tree.txt`](_evidence/ilspy-tree.txt) · [`_evidence/grep-legado.txt`](_evidence/grep-legado.txt)
> Análise profunda (inventário com SHA-256, gap analysis, 262 arquivos): [`08-referencia-e-gaps.md`](08-referencia-e-gaps.md) + [`08-inventario-referencia.csv`](08-inventario-referencia.csv).

Este documento é o **registro de pipeline** da etapa de análise reversa: fixa o que foi
observado por execução real (listagem, metadados de PE, greps), o modelo de dados inferido e
o catálogo A1–A6 com a decisão tomada em cada caso. O detalhamento arquivo-a-arquivo está no
documento 08, que é o anexo de dados desta análise — aqui fica a conclusão operacional.

---

## 1. Inventário de telas

O legado **não traz código-fonte, XAML, banco nem documentação** — é pasta de distribuição
(.NET Framework 4.6.2, 233 DLLs + 3 executáveis). As telas só podem ser conhecidas pelo nome
dos recursos embutidos nos binários.

| Fonte | O que se obtém | Volume observado |
|---|---|---|
| `Syntax.Controler.dll` (22,78 MB) + `Syntax.Model.dll` | nomes de telas WPF/XAML e de comandos | **503 telas XAML** |
| `Syntax.DocsFiscais.dll` (19,56 MB) + `ACBr.Net.*`/`NFe.*`/`CTe.*`/`MDFe.*` | telas e serviços fiscais (NFC-e, NF-e, CT-e, MDF-e, SAT) | 27 DLLs fiscais |
| `SyntaxInstaller.exe` (decompilado em `Desktop\SyntaxInstaller-descompilado`) | fluxo de instalação do produto: 16 namespaces, 72 arquivos `.cs` | árvore completa em `_evidence/ilspy-tree.txt` §C |
| `ConfigMaquina.xml` / `Config.ini` | parâmetros de máquina, impressão, TEF | 14.334 B / 115 B |

**Nome das telas observadas** (amostra do que o binário cita): `cadastrotabeladedesconto`,
`cadastroplanodesconto`, `gerenciadornfce`, `gerenciadornfe`, `gerenciadorsat`,
`gerenciadorcte`, `gerenciadormdfe`, `cancelarnfe`, `inutilizarnfe`, `eventosdanfe`,
`gerenciadordistribuicaodfe`, `inventario/*` (8 telas), `lancarentradaavulsa`,
`importarexcelcontagem`.

> Limite declarado: sem fonte e sem banco, dá para saber **o que existe e como se chama**;
> **não** dá para saber como o legado decide (validações, mensagens, regra de desconto).
> Tudo que dependa disso está marcado como não-verificado em `08 §2`.

---

## 2. Modelo de dados inferido

Inferido de duas fontes independentes: (a) nomes de tabelas/colunas citados nas strings dos
binários — **116 tabelas/views**; (b) a connection string em texto claro do `PreVenda.exe.config`
(`Initial Catalog=…;User ID=…;Password=…`), que confirma **SQL Server** como banco do legado.

O subconjunto que o Landipay precisa reproduzir (mínimo do briefing) e sua correspondência no
schema novo:

| Conceito no legado | Tabela inferida | Tabela no Landipay | PK | FKs | Índice |
|---|---|---|---|---|---|
| Produtos | `Produtos` | `Produtos` | `Id` (GUID) | `EmpresaId` | `IX_Produtos_EmpresaId_Sku`, `IX_Produtos_EmpresaId_CodigoBarras`, `IX_Produtos_EmpresaId_Nome` |
| Categorias | `Categorias` | — (coluna `Categoria` no produto) | — | — | — |
| Vendas | `Vendas` | `Vendas` | `Id` | `LojaId`, `CaixaTurnoId`, `UsuarioId` | `IX_Vendas_LojaId_Numero` (único), `IX_Vendas_LojaId_Status_CriadoEm` |
| Itens de venda | `VendaItens` | `ItensVenda` | `Id` | `VendaId`, `ProdutoId` | `IX_ItensVenda_VendaId`, `IX_ItensVenda_ProdutoId` |
| Turnos de caixa | `Caixa` / `Turnos` | `CaixaTurnos` | `Id` | `LojaId`, `UsuarioId` | `IX_CaixaTurnos_LojaId_Numero`, `IX_CaixaTurnos_LojaId_Status` |
| Formas de pagamento | `FormasPagamento` | enum `FormaPagamento` + `Pagamentos` | `Id` | `VendaId` | `IX_Pagamentos_VendaId` |
| Usuários | `Usuarios` | `Usuarios` | `Id` | `EmpresaId`, `LojaId` | `IX_Usuarios_EmpresaId_Login`, `IX_Usuarios_LojaId` |
| Configuração | `Config` | `ConfigLicencas` + `preferences.json` | `Id` | `EmpresaId` | — |
| Estoque | `Estoque`/`MovimentoEstoque` | `EstoqueMovimentos` | `Id` | `ProdutoId`, `LojaId` | `IX_EstoqueMovimentos_ProdutoId`, `IX_EstoqueMovimentos_LojaId_ProdutoId_OcorridoEm` |
| Auditoria | (ausente) | `LogAuditoria` (append-only por trigger) | `Id` | `EmpresaId`, `LojaId`, `UsuarioId` | `OcorridoEm`, `Entidade+EntidadeId`, `UsuarioId` |

**Divergência consciente:** o legado não tem trilha de auditoria imutável. O Landipay tem
`LogAuditoria` com dois triggers que abortam `UPDATE`/`DELETE` (provado em
`_evidence/etapa3-T3-gate3-sqlite.txt`). Schema real: **17 tabelas de domínio + 1 tabela de
controle de migrations** (`__EFMigrationsHistory`) = 18 objetos.

---

## 3. Catálogo A1–A6

Cada anti-padrão com evidência, trecho e **decisão explícita**. Evidência bruta:
`_evidence/grep-legado.txt` (valores de segredo mascarados — o arquivo vai para o repositório).

| # | Anti-padrão | Evidência no legado | Trecho (mascarado) | Decisão |
|---|---|---|---|---|
| **A1** | Senha fixa de acesso remoto embutida | `Senha.ini` (26 B) na raiz da instalação + `[CONTROLESENHA]` | `[CONTROLESENHA]` / `SENHA=***` | **SUBSTITUIR** — suporte é sessão opt-in com código de 6 dígitos, TTL 30 min, uso único e encerramento auditado (`ServicoDeSuporte`). Nenhum agente remoto é instalado |
| **A2** | Download e execução de binário sem assinatura | `Atualizador.exe` na pasta + URL fixa; nenhum `http://`/`ftp://` em config (0 ocorrências) | — | **SUBSTITUIR** — update assinado com **Ed25519** sobre `{versao, sha256, urlDownload}`; hash do artefato conferido após o download; só HTTPS |
| **A3** | "Login" que não autentica | `Config.ini` guarda `SENHA` e o controle compara texto; não há hash no pacote | `SENHA=***` | **SUBSTITUIR** — **Argon2id** (m=64 MB, t=3, p=4), lockout progressivo, mensagem idêntica para usuário inexistente e senha errada, toda tentativa auditada |
| **A4** | Credenciais em texto claro em config | `PreVenda.exe.config` traz connection string completa; `ConfigMaquina.xml` e `Senha.ini` também casam o grep | `…User ID=***;Password=***…` | **SUBSTITUIR** — SQLite local sem credencial; segredo de servidor só em variável de ambiente (`LANDIPAY_ADMIN_KEY`, `LANDIPAY_LICENCA`) |
| **A5** | Execução de comando sem validação de origem | 5 binários citam acesso remoto (`Syntax.Controler.dll`, `Syntax.HelpDesk.dll`, `Syntax.Framework.dll`, `Syntax.Model.dll`, `MaterialDesignThemes.Wpf.dll` — este último por string de terceiro) | — | **SUBSTITUIR** — `grep rustdesk\|anydesk\|Process.Start\|WebClient\|DownloadFile` em `src/` = **0** (controle em `_evidence/etapa6-T4-auditoria-greps.txt`) |
| **A6** | Outros padrões de risco do pacote | conexão com **três** bancos (SQL Server + Firebird + MySQL); `TrustServerCertificate` no legado; 233 DLLs de terceiros sem inventário de origem | — | **SUBSTITUIR** — dependência única (SQLite + EF Core 8), pacote publicado com 110 arquivos inventariados e varredura de segredos com controle positivo |

**Fluxos funcionais preservados** (não são risco, são produto): bipar código de barras,
desconto por item, sangria/suprimento, fechamento com conferência de gaveta, relatório por
período, catálogo com estoque. Esses foram **PRESERVADOS** na implementação.

---

## 4. Mapa de fluxos do usuário

Reproduzidos no Landipay e verificados por execução real (`_evidence/etapa5-T3-fluxo-real.txt`,
banco auditável `_evidence/gate5-fluxo-real.db`):

| Fluxo | Passos | Onde está | Prova |
|---|---|---|---|
| **Primeiro acesso** | base vazia → tela de criação de admin (Argon2id) → 3 produtos demo (café R$ 18,90 / leite R$ 5,49 / arroz R$ 29,90 com estoque) → login | `LoginViewModel`, `CatalogoDemo` | runner: `[2] catálogo demo: 3 produtos criados` |
| **Abrir turno** | informa valor inicial → `caixa.aberto` na auditoria | `ServicoDeCaixa.AbrirTurno` | `[3] caixa.aberto {"valor":100,00}` |
| **Venda** | bipa código → item na lista → escolhe forma de pagamento → finaliza → baixa estoque + `Outbox` + `venda.finalizada` | `ServicoDeCaixa.FinalizarVenda` | estoque 20 → 19; `[4] venda.finalizada {"numero":1,"total":18.9}` |
| **Fechar turno** | informa valor contado → calcula gaveta (abertura + dinheiro − sangria + suprimento) → diferença → `caixa.fechado` | `ServicoDeCaixa.FecharTurno` | `[5] calculado 118,90 / informado 118,90 / diferença 0,00` |
| **Relatório** | período → faturamento, ticket médio, quebra por forma, top produtos, conferência de turnos (somas no serviço) | `ServicoDeRelatorios` | `[6] 43,29 / 14,43 / Dinheiro 37,80 / PIX 5,49` |

Ordem da trilha observada (a que o gate exige): `login.ok → caixa.aberto → venda.finalizada →
login.ok → caixa.fechado`.

---

## 5. O que a análise mudou no produto

1. Nenhum canal remoto, nenhum auto-update sem assinatura, nenhuma senha em config.
2. Licença **offline** (ES256/P-256) e update **Ed25519** — o legado dependia de URL fixa.
3. Auditoria append-only por **trigger de banco**, não por convenção de código.
4. Instalação **perUser** com modo reparo idempotente (o legado reinstalava por cima sem rastro).

Itens de cobertura ainda ausentes frente ao ERP do legado (entrada de estoque, fiscal NFC-e,
multi-loja) estão classificados em `08 §4` como **Faltante/Fora de escopo 1.0** — nada disso
foi marcado como entregue.
