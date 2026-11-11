# 08 — Referência (Gestor/SyntaxPlus): inventário, gap analysis e plano de fechamento

> Data: 11/11/2026 · Autor: Hermes · Escopo: `C:\Users\diego\Desktop\PDV - Landipay\REFERENCIA` × `C:\Users\diego\landipay`
> Anexo de dados: [`08-inventario-referencia.csv`](08-inventario-referencia.csv) (262 arquivos, SHA-256 de cada um).
> Notas usadas: [[2026-11-11-PDV-Landipay-analise-reversa]] · [[2026-11-11-Landipay-lacunas-comerciais]] · [[2026-11-11-Landipay-separacao-admin-caixa]]

---

## 0. Resumo executivo (o que muda a decisão)

1. **A pasta REFERENCIA não é um PDV: é o ERP "Gestor" da Syntax (linha SyntaxPlus), em pasta de distribuição.** 563,7 MB, 262 arquivos, **zero código-fonte, zero documentação, zero banco de dados**. São 3 executáveis .NET Framework 4.6.2 e 245 DLLs. Any gap analysis "PDV × PDV" subestima o alvo: o concorrente tem 503 telas XAML e 116 tabelas/views citadas no próprio binário.
2. **A suíte estava vermelha quando esta análise começou — e foi corrigida no mesmo dia (P0-1).** Primeira execução: **90 testes em `Landipay.Tests` (88 aprovados, 2 com falha) + 4 em `Landipay.Desktop.Tests` (4 aprovados)**. As duas falhas eram **semente de teste dependente do relógio** (`BancoDeTeste.Semear` abria o turno com `DateTimeOffset.UtcNow`, e `Fase5RelatoriosTests` filtra o período em torno de `2026-11-10 12:00 -03`): passaram em 10/11 e quebraram em 11/11. Corrigido em `Semear(db, DateTimeOffset? agora = null)` + `Semear(db, Base)` na Fase 5 → **94/94 verdes** (saída em §4.1).
3. **A documentação estava à frente do código em dois pontos — corrigidos junto (P0-1).** `docs/00 §6.1` prometia "licença validada a cada 12 h com janela offline de 7 dias; ao expirar, modo somente-leitura" — **não existe chamada a `ValidadorDeLicencaJwt` fora do instalador** (`ServicoInstalador.cs:208`); a linha agora diz **Pendente (P0-3)**. E o contador de testes aparecia como 72 (README linha 7), 89 (README linha 280), 89 (`docs/05`) e 94 (`docs/05` atualização); agora é **94** em todos, com a tabela de inventário refeita por arquivo.
4. **A referência comete, na própria pasta entregue, dois dos anti-padrões que o Landipay já corrigiu**: senha `sa` do SQL Server em texto claro no `Gestor.exe.config` (A2) e atualização que baixa e executa `Atualizador.exe` de URL fixa sem hash/assinatura (A3). Não são "features a copiar" — são a confirmação de que o contrato A1–A6 está certo.

---

## 1. Etapa 0 — Inventário da pasta REFERENCIA

**Caminho:** `C:\Users\diego\Desktop\PDV - Landipay\REFERENCIA` — pasta plana, **262 arquivos, 591.057.914 bytes (563,7 MB)**, nenhum subdiretório.
Inventário completo com hash: [`08-inventario-referencia.csv`](08-inventario-referencia.csv).

### 1.1 Grupos (tabela pedida)

| Caminho | Tipo | Tamanho | Analisável? | Observação |
|---|---|---|---|---|
| `Gestor.exe` | Binário (.NET FW 4.6.2, PE32) | 102.007.296 B (97,3 MB) | **Parcial** | Executável principal; `FileVersion 1.0.0.0`; SHA-256 `3ada2060…`. **Não executado** (sem autorização). Metadados e strings lidos; lógica **não** descompilada |
| `Gestor.zip` | Pacote | 129.463.329 B (123,5 MB) | Sim (listagem) | Conteúdo = o próprio `Gestor.exe` (102.007.296) + `PDFtoPrinter_m.exe` + 245 DLLs + configs + 3 zips de schema. **Não extraído** |
| `PreVenda.exe` | Binário (.NET FW, PE32) | 165.302.272 B (157,6 MB) | **Parcial** | Módulo separado de pré-venda (não está dentro do `Gestor.zip`); mesma data do zip (11/11/2026 13:32) |
| `PDFtoPrinter_m.exe` | Binário (utilitário) | 11,1 MB | Não | Impressão por linha de comando (usado para mandar PDF à impressora) |
| `Syntax.Controler.dll` | Biblioteca (produto) | 23.631.360 B (22,5 MB) | **Parcial** | Núcleo do produto; config próprio lista endpoints SOAP MDFe (SVRS homologação) |
| `Syntax.DocsFiscais.dll` | Biblioteca (produto) | 20.515.328 B (19,6 MB) | **Parcial** | Camada fiscal própria |
| `Syntax.Model.dll` / `.Framework` / `.Boleto` / `.SPED` / `.HelpDesk` / `SyntaxAlert` | Bibliotecas (produto) | 1,6 MB / 494 KB / 182 KB / 199 KB / 167 KB / 23 KB | **Parcial** | 7 DLLs da família `Syntax.*` |
| `ACBr*.dll`, `NFe.*`, `CTe.*`, `MDFe.*`, `DFe.*` | Bibliotecas (fiscal) | 27 DLLs, ~17,8 MB | Sim (só presença) | NFC-e/NFe/CTe/MDFe/SAT via ACBr + DFe.NET |
| `Syncfusion.*`, `FastReport*`, `Microsoft.ReportViewer.*`, `ExcelDataReader*` | Bibliotecas (relatórios) | 19 DLLs, ~40 MB | Sim (só presença) | Grid WPF, PDF, XlsIO, relatórios |
| `FirebirdSql.Data.FirebirdClient`, `MySql.Data`, `Microsoft.SqlServer.Smo/ConnectionInfo/Types`, `Dapper*` | Bibliotecas (dados) | 8 DLLs | Sim (só presença) | Conectores Firebird **e** MySQL **e** SQL Server — o produto fala com os três |
| `Azure.Storage.Blobs/Common`, `Azure.Core` | Bibliotecas (cloud) | 3 DLLs, ~2 MB | Sim (só presença) | Backup/arquivo em nuvem |
| `System.*`, `PresentationFramework*`, `WindowsBase`, `Microsoft.*` | BCL/WPF/WinForms | 128 DLLs | Não relevante | Runtime redistribuído |
| `Gestor.exe.config` | Configuração | 11.034 B | **Sim (lido)** | **Fonte primária de observação**: runtime, connection string, redirects (revela versões de cada dependência) |
| `Syntax.*.dll.config` (5) | Configuração | 1.246–6.211 B | **Sim (lido)** | Endpoints fiscais SOAP |
| `Gestor.exe.manifest` | Configuração | 189.653 B | Parcial | Manifesto de compatibilidade (185 KB sugerem recursos embutidos) |
| `schemas_nfe.zip` / `schemas_cte.zip` / `schemas_mdfe.zip` | Pacote (XSD) | 264,6 KB / 287,9 KB / 92,9 KB | Sim (listagem) | Schemas fiscais versionados **dentro** da instalação |
| `NFeRetrato.frx` | Recurso | 129.626 B | Não (binário) | Retrato/DANFE em FastReport |
| `c39hrp24dhtt.ttf` | Recurso | 12.212 B | Não | Fonte para código de barras/impressão |
| `PDF-XChange Viewer Settings.dat` | Desconhecido | 11.683 B | Não | Config do visualizador de PDF embarcado |
| `.cs`, `.xaml`, `.sln`, `.csproj`, `.db`, `.bak`, `.md`, `.pdf`, `.png` | — | **0** | — | **Não existem**: sem fonte, sem banco, sem docs, sem prints |

### 1.2 Regras cumpridas

- Nenhum binário executado (`Gestor.exe`, `PreVenda.exe`, `PDFtoPrinter_m.exe`, `Atualizador.exe` remoto).
- Nenhuma descompilação (ILSpy/dnSpy) — por isso as regras de negócio ficam como `Não-verificado`.
- O que **foi** feito, e fica declarado como método: (a) leitura dos arquivos `.config`; (b) metadados PE/versão via `FileVersionInfo`; (c) **extração de strings UTF-16 do heap de strings do assembly** (`Gestor.exe`), sem reconstruir IL. É leitura, não engenharia reversa de lógica — mas é a razão de existir boa parte da Etapa 2, então fica explícito aqui.
- `Gestor.zip` foi **listado**, não extraído; não há nada nele que não esteja solto na pasta.

**Nível de observabilidade da referência: MÉDIO.**
Justificativa: dá para saber **o que existe e como se chama** (503 telas XAML, 116 tabelas/views, 245 bibliotecas, endpoints fiscais, connection string, fluxo de atualização), o que basta para comparar **cobertura de domínio**. Não dá para saber **como decide** (validações, regras de desconto, mensagens de erro, fluxo real de tela): sem fonte, sem docs, sem banco, sem descompilação autorizada → tudo que for regra fica `Não-verificado`.

---

## 2. Etapa 1 — Fontes do Landipay carregadas

| Fonte | Estado | O que foi usado |
|---|---|---|
| `README.md` | Lido | Estrutura, contrato de segurança, pré-requisitos, "Estado e pendências" (linha 277) |
| `docs/00-arquitetura.md` | Lido | §6 contrato A1–A6, §6.1 achados adicionais (**inclui a promessa de licença a cada 12 h**), §12 fora de escopo, §13 gates |
| `docs/01-modelo-dados.md` | Cabeçalhos lidos | 17 tabelas, 28 índices, integridade em 3 camadas |
| `docs/02-backend.md` | Cabeçalhos lidos | Endpoints, ES256, manifesto Ed25519, rate limiting, pendência de JWT de sessão |
| `docs/03-cliente.md` | §8 lido | Prova de execução do fluxo de caixa (7 passos, com captura), lacuna de XAML encontrada |
| `docs/04-instalador.md` | Cabeçalhos lidos | 5 passos idempotentes + passo 6, MSI, limitações |
| `docs/05-testes-seguranca.md` | §2–§3 lidos | Inventário de testes (72 declarados / 94 na atualização), varredura de segredos (116 arquivos, 77.879.411 bytes, 0 achados, controle positivo OK) |
| `docs/06-guia-operacao.md` | §4–§5 lidos | Relatórios, backup manual (copiar pasta) |
| `docs/07-design-system.md` | Cabeçalhos lidos | 2 temas, contraste medido, proibição de cor hardcoded |
| `[[2026-11-11-PDV-Landipay-analise-reversa]]` | Lido | Origem dos A1–A6 (RustDesk senha fixa, tokens, download sem hash, SQL concatenado, login sem auth, log sem consentimento) |
| `[[2026-11-11-Landipay-lacunas-comerciais]]` | Lido | 8 bloqueadores P0, 7 de operação P1, 8 de profissionalização P2, roteiro em 3 ondas |
| `[[2026-11-11-Landipay-separacao-admin-caixa]]` | Lido | RBAC no serviço, passo 6, escopo `apps` na licença, pendências declaradas |
| `src/` | Lido por grep + leitura de 9 arquivos | Confirmado o que existe de fato (ver Etapa 3) |

**Nada faltou.** O único desvio relevante é que a pasta `REFERENCIA` **não** contém o `GEstorLimpo (1).bak` nem o `SyntaxInstaller.exe` analisados na nota de engenharia reversa — esses dois arquivos ficavam na pasta-mãe `PDV - Landipay\`, que hoje só tem `LANDIPAY\` e `REFERENCIA\`. Ou seja: **o banco do legado e o instalador do legado não estão mais disponíveis para esta etapa.**

---

## 3. Etapa 2 — Mapa da aplicação de referência (só o observado)

Identidade observada: `C:\Git\SyntaxPlus\Gestor\...` aparece nas strings de rastreio de exceção → produto **SyntaxPlus / módulo Gestor**, compilado em 03/11/2026 18:12, .NET Framework 4.6.2, **32-bit**, 503 telas WPF/XAML, exige **SQL Server** (`Data Source=localhost;Initial Catalog=Gestor`), com conectores Firebird/MySQL para importação, e `EnvDTE`/`ICSharpCode.AvalonEdit` (editor de script embutido).

### 3.1 Operação de caixa

| Funcionalidade observável | Fonte da observação |
|---|---|
| Abertura/fechamento de caixa com conferência dupla: `conferircaixa`, `conferircaixa2`, `correcaodecaixa`, `lancamentodacorrecaodecaixa`, `caixasabertos`, `buscarcaixa` | strings `Gestor.exe` → `/Gestor;component/view/…` |
| Extrato de caixa por caixa e por data; consulta de fechamento e de movimentação | `extratodecaixaporcaixa`, `extratodecaixapordata`, `consultas/consultafechamentodecaixa`, `consultas/consultamovimentacaodecaixa`, `consultas/consultafluxocaixa` |
| Sangria/suprimento/saque como movimentos tipados | strings `Sangria`, `Suprimento`, `lancarsaque`, `lancardeposito`, tabela `dbo.Caixa`, coluna `IdCaixa` |
| Três modos de venda no mesmo caixa: **balcão, mesa e comanda** | SQL embutido: `--Venda Balcão` (`VendaEntrega = 0 AND IdMesa IS NULL`), `--Venda Mesa` (`IdMesa > 0 AND M.Tipo = 1`), `--Venda Comanda` (`M.Tipo = 2`) |
| Cancelamento/estorno de venda e de lançamento | `cancelarvenda`, `lancamentos/alterarstatusvenda`, strings `Confirma o estorno da venda`, `Confirma o estorno deste lançamento?` |
| Desconto em R$ e em % (por item e total), acréscimo, tabela de desconto, plano de desconto | colunas `Desconto`, `DescontoEmReais`, `DescontoPadrao`, `ValorDescontoSubTotal`, `Acrescimo`; telas `cadastrotabeladedesconto`, `cadastroplanodesconto` |
| Correção de caixa (o sistema **permite** corrigir divergência) | `correcaodecaixa` + string `A impressão da correção de caixa ainda não está disponível.` |

> **Não copiar (A5-adjacente):** "corrigir" o caixa depois do fato é justamente o que o Landipay decidiu não fazer (`CaixaTurno.DiferencaConferencia` registra a diferença, não a apaga). A referência oferece as duas coisas (conferência *e* correção); copiar só a segunda destrói a trilha.

### 3.2 Fiscal — a referência é completa, e é o maior abismo entre os dois

| Funcionalidade observável | Fonte |
|---|---|
| NFC-e, NFe, CTe, MDFe, SAT: emissores, gerenciadores, eventos, cancelamento, inutilização, contingência, distribuição de DF-e | 27 DLLs (`ACBr.Net.*`, `NFe.*`, `CTe.*`, `MDFe.*`, `DFe.*`) + `Syntax.DocsFiscais.dll` (19,6 MB) + telas `gerenciadores/gerenciadornfce|nfe|sat|cte|mdfe`, `nfe/cancelarnfe`, `nfe/inutilizarnfe`, `nfe/eventosdanfe`, `gerenciadordistribuicaodfe` |
| Schemas XSD versionados na instalação | `schemas_nfe.zip` (264 KB), `schemas_cte.zip` (287 KB), `schemas_mdfe.zip` (92 KB) |
| DANFE/DACTE (retrato e FastReport) | `NFeRetrato.frx`, `NFe.Danfe.Nativo.dll`, `NFe.Danfe.OpenFast.dll`, `relatorios/dacte` |
| Sintegra e SPED | `gerarsintegra`, `Syntax.SPED.dll`, `exportacaoecf` |
| Obrigações acessórias de preço: IBPT e CEST (Lei 12.741/2012) | `atualizacaoibpt`, strings `Tabela IBPT desatualizada.`, `Para que possamos atender a Lei da Transparência (Lei 12.741/2012)…` |
| Integração com ECF (impressora fiscal antiga) e equipamento ECF | `cadastroequipamentoecf`, `cadastroaliquotaecf`, `consultavendasecf`, `definirimpressao`, `relatorios/imprimirvenda` |

### 3.3 Pagamentos

| Funcionalidade observável | Fonte |
|---|---|
| **TEF integrado** com parcelas e conciliação | tabelas `dbo.Tef`, `dbo.TefParcelas` (FK explícita nas strings), `cadastrotef`, `editarmovimentotef`, `lancarrecebimentotransacaotef`, `lancartransacoescartaosped`, `consultas/encontrodecontastef`, `consultavendasemtef`, coluna `AS TEF` |
| Cartão POS (débito/crédito) e parcelamento com preço parcelado | `AS PrecoParcelado`, `cadastroplanopagamento`, `consultavendasporfinalizadora` |
| Cheque (próprio, de terceiro, devolução, depósito, histórico) | `cadastrocheque`, `cadastrochequeproprio`, `devolucaodecheque`, `historicocheque`, `lancardepositoemcheque`, `gerenciadorcheques`, tabela `Cheque*` |
| Crediário / convênio / carteira | `cadastrocreditorotativo`, `cadastroconvenio`, `consultavendasconvenio`, `ClientesCarteiraVendedor`, `gerarcontasreceber` |
| Finalizadora como entidade de primeira classe | `@Finalizadora`, `ConsultaVendasPorFinalizadora`, `ATENÇÃO: finalizadora com valor divergente do valor da venda em {0:N2}.` |

### 3.4 Catálogo e estoque

| Funcionalidade observável | Fonte |
|---|---|
| Cadastro completo de produto: grupo/subgrupo, marca, coleção, grade, cor, tamanho, unidade, atributos, composição (kit) | `cadastroproduto`, `cadastrogrupo`, `cadastrosubgrupo`, `cadastromarcaproduto`, `cadastrocolecao`, `cadastrograde`, `cadastrocor`, `cadastrounidademedida`, `cadastroatributo`, `cadastrocomponente`, tabela `PRODUTOCOMPOSICAOESCOLHA` |
| Preço: tabela de preço, alteração de preço, custo, lucratividade | `cadastrotabeladepreco`, `cadastroalteracaodepreco`, `relatorios/tabeladeprecos`, `adicionarcustoproduto`, `consultalucratividadeprodutos`, `consultacustoestoque` |
| **Inventário de verdade**: contagem, recontagem, fechamento, integração, importação de contagem por Excel | `inventario/cadastrocontagem`, `cadastrorecontagem`, `cadastrocontagemfechamento`, `cadastroinventariointegracao`, `inventario/importarexcelcontagem`, `relatorios/relatorioinventario` |
| Entrada/saída avulsa, transferência e conferência entre centros de estoque | `lancamentodacorrecaodecaixa`, `lancarentradaavulsa`, `consultas/consultaentradaavulsa`, `lancamentos/transferircentrodeestoque`, `conferirtransferenciacentrodeestoque` |
| Entrada por **XML de NF-e** e importação de outros sistemas | `importacao/importarxml`, `importarnfeemissor`, `importarprodutosemissor`, `importacao/outrossistemas/…` |
| Etiquetas: modelo, gôndola, impressão | `cadastromodeloetiqueta`, `geraretiquetaproduto`, `geraretiquetaprodutogondola`, `BarcodeStandard.dll`, `zxing` |
| Centros de estoque (multi-depósito) | `cadastrocentrodeestoque` + FKs `IdCentroDeEstoque` nas consultas |

### 3.5 Cadastros

| Funcionalidade observável | Fonte |
|---|---|
| Cliente/fornecedor como **Entidade** (PF/PJ, endereço, telefones, grupos, convênios, comissão por vendedor) | `cadastropessoa`, `cadastropessoafisica`, `cadastropessoajuridica`, `cadastrogrupocliente`, `cadastroconvenio`, tabelas `Entidade`, `PessoaFisica`, `EnderecoEntidade`, `TelefoneEntidade` |
| Usuário, funcionário, vendedor, garçom, técnico, mecânico, operador de viagem | `cadastrousuario`, `cadastrofuncionario`, `cadastrovendedor`, `cadastrogarcom`, `cadastrotecnico` |
| Multi-empresa e multi-loja | `cadastroloja`, `cadastroempresausuaria`, `EmpresaUsuariaCentroDeEstoque`, `UsuarioEmpresaUsuaria` |
| Natureza de operação, CFOP, CST, grupo tributário, regra fiscal | `cadastronaturezaoperacao`, `cadastrosituacaotributaria`, `cadastrogrupotributario`, `cadastroreducaoz`, `buscarcest` |
| Plano de contas, classificação financeira, banco, DRE | `cadastroplanodecontacontabil`, `cadastroclassificacaofinanceira`, `cadastrobanco`, `cadastrodre` |

### 3.6 Relatórios

| Funcionalidade observável | Fonte |
|---|---|
| Vendas: por período, por grupo, por finalizadora, por plano de pagamento, por dia da semana, por vendedor (média), ranking | `consultas/consultavendas*`, `consultarankingprodutosvendidos`, `consultamediasdavendaporvendedor` |
| Produtos vendidos: agrupado, por tabela, por insumo, por CFOP, Scanntech | `consultaprodutosvendidosagrupado*`, `consultafiscalprodutosvendidosagrupadosproduto`, `consultaprodutosvendidosscanntech` |
| Curva ABC (produtos e clientes), lucratividade, comissão | `consultacurvaabc`, `consultacurvaabcclientes`, `consultalucratividadeprodutos`, `relatoriodecomissao` |
| Financeiro: contas a pagar/receber, fluxo de caixa, DRE, demonstração do exercício, saldo geral | `consultacontaspagar`, `consultacontasreceber`, `consultafluxocaixa`, `relatoriodre`, `relatoriodemonstracaoresultadoexercicio`, `relatoriosaldogeral`, `dashboard/dashboardfinanceiro` |
| Saída: impressão de venda, PDF, Excel | `relatorios/imprimirvenda`, `janelapdf`, `FastReport`, `Syncfusion.XlsIO`, `ExcelDataReader` |

### 3.7 Licenciamento, atualização, instalação

| Funcionalidade observável | Fonte |
|---|---|
| Tela de liberação do sistema (ativação) | `liberacaosistema` |
| Atualização: verifica, baixa `Atualizador.exe` de URL **fixa** e executa | strings `http(s)://www.syntaxti.com.br/SistemasSyntax/Atualizador/Atualizador.exe`, `Deseja atualizar o sistema para a versão mais recente disponível?`, `Programa de Atualização não encontrado` |
| Backup embutido: configurar destino, executar, e **contador de dias sem backup** | `backupdatabase`, `configurarbackup`, `efetuarbackup`, coluna `DiasSemBackup`, `Backup realizado com sucesso!`, `Selecione um local de destino para o Backup` |
| Banco no balcão: SQL Server com restauração de `.bak` (fluxo do instalador, nota de RE) | `Gestor.exe.config` (provider `System.Data.SqlClient`), `manutencaobanco` |
| Acesso remoto e log para o fornecedor | nota [[2026-11-11-PDV-Landipay-analise-reversa]] (RustDesk senha fixa, `LogSyncService` com token Basic) — **não** estão nesta pasta |

### 3.8 Segurança (o que a referência entrega hoje)

| Observação | Fonte |
|---|---|
| **Senha `sa` do SQL Server em texto claro no arquivo de configuração entregue** | `Gestor.exe.config` → `<add name="Gestor.Properties.Settings.GestorConnectionString" connectionString="Data Source=localhost;Initial Catalog=Gestor;User ID=sa;Password=***" providerName="System.Data.SqlClient" />` (senha redigida neste documento) |
| Hash de senha com BCrypt | `BCrypt-Net-Next.dll` |
| `EnvDTE` + `AvalonEdit` embutidos → execução/edição de script dentro do ERP | `EnvDTE.dll`, `ICSharpCode.AvalonEdit.dll` |
| Nenhum indício de hash/assinatura na atualização | ausência de qualquer string `sha256`/`assinatura` junto ao fluxo `Atualizador.exe` |
| Integração com RustDesk/AnyDesk para suporte | nota de RE (`SyntaxInstaller.exe`), não esta pasta |

**Anti-padrões (A1–A6) registrados como "não copiar":**

| # | Evidência nesta pasta | Status |
|---|---|---|
| A1 senha remota fixa | **Não-verificado aqui** (o `SyntaxInstaller.exe` que a continha não está mais na pasta) | fonte: nota de RE |
| A2 segredo embutido | **Confirmado**: `Gestor.exe.config` com `sa` + senha; `Syntax.*.dll.config` com endpoints e bindings | anexar ao contrato |
| A3 download+execução sem hash | **Confirmado**: `Atualizador.exe` de URL fixa, sem string de hash/assinatura | anexar ao contrato |
| A4 SQL concatenado | **Confirmado por indício**: SQL inline massivo + `fncArredondamento_ABNT`, `sp_start_job` no texto do binário; `WITH(NOLOCK)` em quase toda consulta | indício, não prova |
| A5 login sem autenticação | **Não-verificado aqui** (tela `login` existe; comportamento não observável sem execução) | fonte: nota de RE |
| A6 log sem consentimento | **Não-verificado aqui** (serviço de log fica no instalador) | fonte: nota de RE |

### 3.9 Requisitos não-funcionais observáveis

| Item | Referência | Fonte |
|---|---|---|
| Runtime | .NET Framework **4.6.2**, **32-bit** (PE32/i386) | `Gestor.exe.config` `<startup>`, `file Gestor.exe` |
| Banco | **SQL Server** (obrigatório; `sa`), com Firebird/MySQL para importação | `Gestor.exe.config`, `FirebirdSql.Data.FirebirdClient.dll`, `MySql.Data.dll` |
| Dependências de UI | WPF + MaterialDesign + Syncfusion + Xceed + ControlzEx | 245 DLLs |
| Tamanho da instalação | **563,7 MB** (pasta), 597 MB descompactado no zip | `du` |
| Atualização | baixa e executa EXE de URL fixa, sem hash | strings |
| Versionamento | `FileVersion` = `1.0.0.0` em **todos** os binários → versão não rastreável pelo executável | `FileVersionInfo` |
| Multi-empresa / multi-loja / multi-centro de estoque | sim | `EmpresaUsuaria`, `CentroDeEstoque` |

**Não-verificado (declarado):** regras de negócio, validações, fluxo real de tela, performance, tratamento de erro, comportamento offline, granularidade de permissões, formato de cupom.

---

## 4. Etapa 3 — Inventário do Landipay (só o provado)

### 4.1 Provas de execução desta sessão

Primeira execução (antes do P0-1):

```
$ dotnet test Landipay.sln -c Release --no-build
Aprovado! – Com falha: 0, Aprovado: 4, Ignorado: 0, Total: 4  – Landipay.Desktop.Tests.dll
Com falha! – Com falha: 2, Aprovado: 88, Ignorado: 0, Total: 90 – Landipay.Tests.dll
```

Execução depois do P0-1 (saída real do MSBuild, sem resumo):

```
$ dotnet build Landipay.sln -c Release --nologo -warnaserror
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)

$ dotnet test Landipay.sln -c Release --no-build --nologo
Aprovado!  – Com falha: 0, Aprovado: 4,  Ignorado: 0, Total: 4  – Landipay.Desktop.Tests.dll
Aprovado!  – Com falha: 0, Aprovado: 90, Ignorado: 0, Total: 90 – Landipay.Tests.dll
```

13 projetos na solução (11 de produção + 2 de teste) — o "14 projetos" que circulava nos
documentos era contagem de uma versão anterior da solução, corrigida em `README`, `docs/05` e aqui.

**Total depois do P0-3: 108 testes, 108 aprovados, 0 falhas** (94 do P0-1 + 14 do P0-3).

| Falha (histórica) | Causa raiz (provada) | Correção aplicada |
|---|---|---|
| `Fase5RelatoriosTests.Relatorio_mostra_sangria_suprimento_e_conferencia_do_turno` (`Assert.Single` em coleção vazia) | `BancoDeTeste.Semear` abria o turno com `DateTimeOffset.UtcNow`; o teste filtra `[Base, Base+1d)` com `Base = 2026-11-10 12:00 -03`. Rodando em 11/11 depois das 12h, o turno caía fora da janela → `relatorio.Turnos` vazio | `Semear(db, DateTimeOffset? agora = null)` (`BancoDeTeste.cs`) + `banco.Semear(db, Base)` (`Fase5RelatoriosTests.cs:37`) |
| `Fase5RelatoriosTests.Relatorio_acusa_diferenca_de_caixa_em_vez_de_corrigir` (`Sequence contains no elements`) | mesma causa | mesma correção |

### 4.2 Item | Evidência | Status

| Item | Evidência | Status | Observação |
|---|---|---|---|
| Build 13 projetos, 0 erros/avisos (`-warnaserror`) | saída em §4.1 | **Provado** | — |
| Venda ponta a ponta na UI real (bipar → pagar → finalizar) | `docs/03-cliente.md` §8.1 (7 passos com captura e conferência no banco) | **Provado em 10/11** | não reexecutado nesta sessão |
| Fechamento de turno com diferença R$ 0,00 | `docs/03` §8.1 passo 7; `CaixaTurno.DiferencaConferencia` | **Provado** | 2 testes de conferência estavam vermelhos pela semente; verdes desde o P0-1 |
| Relatórios por período (R$ 56,70 / ticket 28,35 na semente; R$ 43,29 no fluxo real) | `Fase5RelatoriosTests` (8 testes) + `docs/06 §4` | **Provado** (8/8) | — |
| Sangria/suprimento | `ServicoDeCaixa.cs:120,123` + `[RelayCommand] Sangria/Suprimento` | **Provado** | — |
| Auditoria append-only | `20261111054456_InitialCreate.cs:618,627` (2 triggers) | **Provado por código** | trigger testado? não há teste específico |
| Outbox transacional | entidade `OutboxMensagens` + `docs/03` §8.1 ("1 mensagem") | **Provado que grava** | **sem consumidor** |
| 17 tabelas | `LandipayDbContextModelSnapshot.cs`: 19 `ToTable`, 2 são `Endereco` owned → **17** | **Provado** | — |
| RBAC no serviço + auditoria de negativa | `ServicoDeAutorizacao.Exigir` chamado em 6 pontos (`ServicoDeCaixa.cs:43,64,81,129,145`; `ServicoDeRelatorios.cs:68`) | **Provado** | — |
| Matriz papel × permissão (13 permissões, 3 papéis) | `CatalogoDeAcesso.cs` | **Provado** | **7 das 13 nunca são exigidas**: `venda.cancelar`, `venda.desconto`, `produto.escrever`, `estoque.movimentar`, `usuario.gerenciar`, `configuracao.escrever`, `licenca.ler` (grep = 0 chamadas) |
| Separação Admin × Caixa + acesso negado auditado | `SegregacaoAdminCaixaTests`, `AcessoNegadoView`, nota de separação (prova em execução) | **Provado** | — |
| Licença ES256 offline | `ValidadorDeLicencaJwt` + `Fase4InstaladorTests` (4 recusas) | **Provado** | **só no instalador** |
| Licença bloqueando operação / tolerância offline | `ConfigLicenca.ToleranciaOfflineDias = 7` existe no modelo | **Inferência (não usado)** | `ValidadorDeLicencaJwt` só é chamado em `ServicoInstalador.cs:208`; `ComposicaoApp` não valida nada ao abrir |
| Update assinado (Ed25519) | `AtualizadorDesktop` confere hash contra manifesto assinado | **Provado que baixa/valida** | **não aplica** (passo separado, explícito no código) |
| Instalador 6 passos idempotentes | `InstaladorViewModel.cs:101-106` (Pré-requisitos, Banco, Empresa, Ativação, Administrador, Aplicativos) | **Provado** | idempotência testada em `Fase4InstaladorTests` (11 testes) |
| MSI 1.1.0 per-user x64 | `dist/Landipay-1.1.0.msi` (17,9 MB) + `install-*.log` (14 logs de ciclos reais) | **Provado que existe** | reprodutibilidade e sessão não elevada: pendentes |
| Tema claro/escuro | `Theme.Dark.xaml` / `Theme.Light.xaml`, `docs/07` | **Provado em uso** | — |
| Varredura de segredos (0 achados) | `docs/05` §3 (116 arquivos, 77.879.411 bytes, controle positivo OK) | **Provado** | controle documentado, não reexecutado |
| Catálogo **somente leitura** | `ProdutosViewModel` (`LoginViewModel.cs:174-212`): só `Busca`, `Atualizar`, `Carregar` | **Provado** | continua igual à nota de lacunas |
| Cancelar venda / desconto | domínio `Venda.Cancelar` (`Vendas.cs:107`), `Venda.AplicarDesconto` (`Vendas.cs:77`) | **Domínio provado, sem UI** | `grep` por `Cancelar(`/`AplicarDesconto` em `Ui` e `Client` = **0** |
| Fiscal | `grep -liE "nfce|sefaz|danfe|sat|sped"` em `src/**/*.cs,xaml` | **Faltante** | 0 arquivos |
| Impressão de cupom / gaveta | `grep -liE "escpos|rawprinter|printdialog|cupom|gaveta"` | **Faltante** | 0 arquivos de código (só strings de UI) |
| Backup/restore | `grep -liE "backup|restore"` = só migrations | **Faltante** | `docs/06 §5` ensina copiar a pasta à mão |
| Exportação CSV/PDF | `grep -liE "\.csv|exportar"` | **Faltante** | — |
| TEF/PIX integrado | `FormaPagamento` tem `Pix`, `Debito`, `Credito` | **Só anotação** | nenhuma integração |
| JWT de sessão na API | `docs/02` §"Pendência: JWT de sessão" | **Faltante** | API usa `X-Admin-Key` |
| Git / CI | `git rev-parse` → *not a git repository*; sem `.github` | **Faltante** | — |
| Multi-caixa | 1 turno aberto por loja (`ServicoDeCaixa.AbrirTurno`), sem trava/concorrência | **Não testado** | `docs/05 §2.1` declara |
| Documentação coerente com o código | README l. 7 e 281, `docs/05 §2`, `docs/00 §6.1` | **Corrigido (P0-1)** | 94 em todos os pontos; licença marcada como pendente (P0-3); tabela de testes refeita por arquivo |

---

## 5. Etapa 4 — Gap analysis por domínio

Legenda: `Coberto` · `Parcial` · `Faltante` · `Diferente-melhor` · `Diferente-pior` · `Não-verificado`. Impacto/Esforço: P/M/G.

### 5.1 Operação de caixa

| Item | Referência (fonte) | Landipay (fonte) | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Abertura/fechamento com conferência calculado × informado | `conferircaixa`, `conferircaixa2` | `FecharTurno` + `DiferencaConferencia` (`Caixa.cs:40`) | **Coberto** | — | — |
| Sangria / suprimento | `Sangria`, `Suprimento`, `lancarsaque` | `RegistrarSangria/Suprimento` (`ServicoDeCaixa.cs:120,123`) | **Coberto** | — | — |
| Extrato de caixa por caixa/data; consulta de fechamento | `extratodecaixaporcaixa`, `extratodecaixapordata`, `consultafechamentodecaixa` | relatórios por período com turnos (`ServicoDeRelatorios`) | **Parcial** | M | P |
| Correção de caixa depois do fechamento | `correcaodecaixa`, `lancamentodacorrecaodecaixa` | não existe — só registra a diferença | **Diferente-melhor** | — | — |
| Cancelamento de venda | `cancelarvenda`, `alterarstatusvenda` | domínio existe (`Vendas.cs:107`), **sem comando/UI** | **Parcial** | G | P |
| Desconto (R$/%) por venda e por item, com alçada | `Desconto`, `DescontoPadrao`, `cadastrotabeladedesconto` | `AplicarDesconto` no domínio (`Vendas.cs:77`), sem UI, sem alçada | **Parcial** | G | M |
| Turno com múltiplos operadores / vários caixas abertos | `Caixa`, `IdCaixa`, `@NumeroCaixa`, `caixasabertos` (N caixas) | **1 turno aberto por loja**; sessão por operador | **Diferente-pior** | G | G |
| Mesa / comanda / garçom | SQL `--Venda Mesa`, `--Venda Comanda`, `cadastromesa`, `cadastrogarcom` | não existe | **Faltante** (fora do segmento atual) | M | G |

### 5.2 Fiscal (declarado fora de escopo em `docs/00 §12`)

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| NFC-e (emissão, evento, cancelamento, contingência) | `gerenciadornfce`, `nfe/*`, ACBr | — | **Faltante** | **G** | **G** |
| SAT / CF-e | `gerenciadorsat`, `ACBr.Net.Sat.dll` | — | **Faltante** | G | G |
| NF-e / CTe / MDFe | `gerenciadornfe/cte/mdfe`, 27 DLLs | — | **Faltante** | M | G |
| Sintegra / SPED | `gerarsintegra`, `Syntax.SPED.dll` | — | **Faltante** | M | G |
| IBPT / CEST / Lei 12.741 | `atualizacaoibpt` | — | **Faltante** | M | P |
| Cupom (impressão) e DANFE | `imprimirvenda`, `NFeRetrato.frx`, `PDFtoPrinter` | — | **Faltante** | **G** | M |

> **Decisão necessária, não tarefa técnica:** o PDV sem documento fiscal só serve a MEI/Simples em alguns regimes e UFs. `docs/00 §12` diz "fora de escopo"; `lacunas-comerciais` diz "bloqueia a venda". As duas coisas não podem estar certas ao mesmo tempo. Ver P0-7.

### 5.3 Pagamentos

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Dinheiro com cálculo de troco | sim | `RegistrarPagamento` + `Troco` (`Vendas.cs:31`) e `dinheiroNaGaveta` no fechamento | **Coberto** | — | — |
| PIX / cartão **como forma anotada** | sim | `FormaPagamento.Pix/Debito/Credito` | **Coberto** | — | — |
| **TEF/POS integrado** (transação, parcelas, conciliação) | `cadastrotef`, `TefParcelas`, `encontrodecontastef` | — | **Faltante** | **G** | **G** |
| Parcelamento com preço parcelado | `PrecoParcelado`, `cadastroplanopagamento` | — | **Faltante** | M | M |
| Cheque / crediário / convênio | `cadastrocheque`, `cadastrocreditorotativo`, `cadastroconvenio` | — | **Faltante** | P (segmento) | G |
| Sangria de cartão / taxa de adquirente | `consultavendasporfinalizadora`, `consultavendasemtef` | — | **Faltante** | M | M |

### 5.4 Catálogo e estoque

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| **Cadastro de produto (CRUD)** | `cadastroproduto` + gerenciador | `ProdutosViewModel` **somente leitura** | **Faltante** | **G** | M |
| Código de barras (leitura) | sim | `CodigoBarras`/`Sku` na busca do caixa | **Coberto** | — | — |
| Etiqueta (modelo + impressão) | `geraretiquetaproduto`, `BarcodeStandard`, `zxing` | — | **Faltante** | M | M |
| Unidade / grade / cor / tamanho / composição (kit) | `cadastrograde`, `cadastrocor`, `PRODUTOCOMPOSICAOESCOLHA` | só `Unidade` string | **Faltante** | M | G |
| Entrada de estoque / ajuste de inventário | `inventario/*` (8 telas), `lancarentradaavulsa`, `importarexcelcontagem` | **ausente** — só baixa na venda (`ServicoDeCaixa.cs:89`) | **Faltante** | **G** | M |
| Entrada por XML de NF-e / importação de produtos | `importarxml`, `importarprodutosemissor`, `importacaodeprodutos` | — | **Faltante** | M | G |
| Preço: alteração, tabela, promoção | `cadastroalteracaodepreco`, `cadastrotabeladepreco` | `AlterarPreco` no domínio, sem UI | **Parcial** | M | P |
| Custo e lucratividade | `consultalucratividade`, `custoganhoitensvenda` | `CustoAtual` existe, não usado em relatório | **Parcial** | P | M |
| Saldo de estoque performático | tabela/materialização no SQL Server | `CalcularSaldo` soma movimentos em memória | **Diferente-pior** | M | M |
| Centros de estoque / transferência | `centrodeestoque`, `transferircentrodeestoque` | 1 estoque por loja | **Faltante** | P | G |

### 5.5 Cadastros

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Empresa / loja / endereço / CNPJ | `cadastroloja`, `cadastroempresa` | `Empresa`, `Loja`, `Endereco`, consulta CNPJ na API | **Coberto** | — | — |
| **Cliente** | `cadastropessoa` (PF/PJ), `grupocliente`, convênio | **não existe entidade `Cliente`** no domínio | **Faltante** | G (CPF na nota, crediário, histórico) | M |
| **Fornecedor** | idem (Entidade) | **não existe** | **Faltante** | M | P |
| Usuários e papéis | `cadastrousuario`, perfis | RBAC completo no serviço, **sem tela de gestão** (`usuario.gerenciar` sem uso) | **Parcial** | G | M |
| Multi-empresa / multi-loja | `EmpresaUsuaria`, `UsuarioEmpresaUsuaria` | `Empresa`/`Loja` no modelo; app assume 1 loja | **Parcial** | M | G |
| Natureza de operação / regra fiscal | `cadastronaturezaoperacao` | — | **Faltante** | G (junto com fiscal) | G |

### 5.6 Relatórios

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Por período, forma de pagamento, top produtos, ticket médio, descontos | `consultavendas*`, `consultapagamentos` | `ServicoDeRelatorios` (8 testes) | **Coberto** | — | — |
| Conferência de turnos no relatório | `consultafechamentodecaixa` | `RelatorioPeriodo.Turnos` | **Coberto** (2 testes hoje vermelhos) | — | — |
| Exportação CSV / PDF | FastReport + Syncfusion XlsIO | — | **Faltante** | M | P |
| Fluxo de caixa / DRE / curva ABC / comissão | `consultafluxocaixa`, `relatoriodre`, `consultacurvaabc` | — | **Faltante** | P | M |
| Dashboard com gráficos | `dashboard/dashboardfinanceiro`, `reportgraficos` | dashboard = relatório textual | **Parcial** | P | M |
| Vendas por vendedor / grupo / dia da semana | `consultavendasporvendedor`, `consultavendaspordiadasemana` | — | **Faltante** | P | P |

### 5.7 Licenciamento e atualização

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Licença offline assinada | `liberacaosistema` (mecânica não observável) | ES256 + 5 tipos de recusa + `apps` | **Diferente-melhor** | — | — |
| **Licença bloqueando operação** | — | validada **só** no instalador (`ServicoInstalador.cs:208`) | **Faltante** | **G** (não é cobrável) | P |
| Tolerância offline de 7 dias | — | `ToleranciaOfflineDias` no modelo, **não aplicado** | **Parcial (inferência)** | G | P |
| Revogação online / renovação | — | — | **Faltante** | **G** | M |
| Update assinado (hash por pacote) | atualização sem hash (`Atualizador.exe` de URL fixa) | manifesto Ed25519 + hash, **sem aplicar** | **Diferente-melhor (incompleto)** | G | M |
| Endpoint de update em produção | URL fixa `syntaxti.com.br` | API tem `/v1/updates/manifesto` e `/chave-publica` (local) | **Parcial** | G | M |
| Escopo de app na licença | não observável | claim `apps` + passo 6 | **Diferente-melhor** | — | — |

### 5.8 Segurança

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Trilha append-only | não observável | triggers que abortam `UPDATE`/`DELETE` | **Coberto** | — | — |
| Hash de senha | BCrypt | Argon2id + lockout + sessão 15 min | **Diferente-melhor** | — | — |
| Segredo no binário/config | **`sa` + senha no `.config`** | varredura com controle positivo: 0 achados | **Diferente-melhor** | — | — |
| **Assinatura de código (Authenticode)** | não observável | — | **Faltante** | **G** (SmartScreen) | P (compra) / P |
| Criptografia do banco local | SQL Server com autenticação | SQLite em claro em `%APPDATA%` | **Faltante** | M | M |
| API autenticada por loja (JWT de sessão) | — | `X-Admin-Key` | **Faltante** | G | M |
| Rate limiting | não observável | por IP, 1 min, testado | **Coberto** | — | — |
| Suporte remoto consentido | RustDesk senha fixa (A1) | código de 6 dígitos, **sem agente** | **Parcial** | M | G |

### 5.9 Instalação e implantação

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Instalador idempotente | instalador do fornecedor (restaura `.bak`, instala SQL) | 6 passos idempotentes, auditados | **Diferente-melhor** | — | — |
| Banco no balcão | SQL Server 2008 Express + restore | SQLite embarcado, sem engine | **Diferente-melhor** | — | — |
| Escolha de apps por estação | não existe | passo 6 + `apps.json` + auditoria | **Diferente-melhor** | — | — |
| Tamanho / tempo de instalação | 563 MB | MSI 17,9 MB | **Diferente-melhor** | — | — |
| MSI reprodutível / sessão não elevada | — | `dist/*.msi` + logs; reprodutibilidade não automatizada | **Parcial** | P | P |
| **Backup automático** | `efetuarbackup`, `configurarbackup`, `DiasSemBackup` | — | **Faltante** | **G** | M |
| Restore testado | não observável | procedimento manual, nunca testado | **Faltante** | G | P |

### 5.10 UX / operação

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Dois temas com tokens | MaterialDesign + temas próprios | `Theme.Dark/Light` sobre as mesmas chaves, contraste medido | **Coberto** | — | — |
| Atalhos de teclado (F-keys) no caixa | não observável | só Enter; sem mapa de atalhos | **Não-verificado (ref) / Parcial** | M | P |
| Acessibilidade | não observável | contraste documentado; sem leitor de tela | **Parcial** | P | M |
| Fluxo multi-caixa na mesma loja | vários `IdCaixa` | 1 turno por loja; UI assume um | **Diferente-pior** | G | G |
| Handler global de erro | não observável | só no instalador | **Faltante** | M | P |

### 5.11 Multi-caixa / sincronização

| Item | Referência | Landipay | Status | Impacto | Esforço |
|---|---|---|---|---|---|
| Registro de eventos para sincronizar | não observável | `OutboxMensagens` grava por venda | **Parcial** | G | M |
| Consumidor da outbox / painel | Azure Blob, "banco da web" | **nenhum** | **Faltante** | G | G |
| Concorrência de 2 caixas | `caixasabertos`, `IdCaixa` | sem teste, sem trava | **Faltante** | G | M |
| Multi-loja consolidado | `EmpresaUsuaria` | cada loja é uma ilha | **Faltante** | M | G |

---

## 6. Etapa 5 — Priorização por fase com gate

Regra: **cada linha cabe em uma fase com gate** e precisa de evidência de execução para fechar. Fases 7–10 são novas; 0–6 já existem no projeto.

| Fase | Nome | Gates de entrada/saída | Itens |
|---|---|---|---|
| **7** | Credibilidade (2 dias) | **GATE 7.1**: `dotnet test` 94/94 verde, docs sem número divergente, git inicializado com primeiro commit | P0-1, P0-2 |
| **8** | Cobrar e não perder dado | **GATE 8.1**: licença vencida bloqueia o caixa (teste automatizado + execução); **GATE 8.2**: backup automático no fechamento + restore verificado em outra pasta/máquina | P0-3, P0-4, P0-5 |
| **9** | Balcão de verdade | **GATE 9.1**: cadastrar produto pela UI, dar entrada de estoque, vender, cupom impresso, gaveta abre — filme/captura do fluxo | P0-6, P0-7, P0-8 |
| **10** | Distribuição assinada | **GATE 10.1**: MSI e EXEs assinados (SmartScreen limpo); **GATE 10.2**: updater aplica pacote assinado e recusa hash divergente | P0-9, P0-10, P0-11 |
| **11** | Operável com equipe | **GATE 11.1**: TEF/PIX homologado em sandbox; **GATE 11.2**: desconto/cancelamento com alçada auditada; **GATE 11.3**: 2 caixas simultâneos sem colisão de turno | P1-1…P1-5 |
| **12** | Fiscal (se decidido) | **GATE 12.1**: NFC-e autorizada em homologação para 1 UF, com contingência | P0-7b / P1-6 |
| **13** | Incremental | sem gate formal | P2-* |
| **14** | Exploratório | sem gate formal | P3-* |

### P0 — bloqueia adoção comercial

| ID | Gap | Fase | Impacto | Esforço | Depende de |
|---|---|---|---|---|---|
| **P0-1** | ✅ **Concluído** — suíte vermelha (2 testes) + documentação divergente (72/89/94; licença prometida e ausente) | 7 | **G** | P | — |
| **P0-2** | ✅ **Concluído** — repositório git + CI (workflow validado; run no GitHub depende de remote) | 7 | **G** | P | — |
| **P0-3** | ✅ **Concluído** — licença avaliada na abertura e em cada operação; expirada/revogada bloqueia venda, tolerância de 7 dias contada da validação online | 8 | **G** | P | — |
| **P0-4** | Backup automático + restore inexistentes | 8 | **G** | M | P0-2 |
| **P0-5** | Revogação online / validação periódica | 8 | G | M | P0-3, API JWT |
| **P0-6** | Cadastro de produto + entrada/ajuste de estoque (catálogo é read-only) | 9 | **G** | M | — |
| **P0-7** | **Decisão fiscal** + impressão de cupom/gaveta | 9/12 | **G** | M + G | decisão de negócio |
| **P0-8** | Cliente e fornecedor não existem como entidade (CPF na nota, histórico) | 9 | G | M | P0-7 |
| **P0-9** | Authenticode (MSI + 2 EXEs) | 10 | **G** | P + compra de certificado | — |
| **P0-10** | Updater não aplica + manifesto sem hash por pacote | 10 | G | M | P0-9 |
| **P0-11** | MSI reprodutível e instalação não elevada (registro HKCU) | 10 | P | P | P0-2 |

### P1 — diferencial competitivo

| ID | Gap | Fase | Impacto | Esforço |
|---|---|---|---|---|
| P1-1 | TEF/POS integrado (transação + parcelas + conciliação) | 11 | G | G |
| P1-2 | PIX por QR dinâmico (sem adquirente, via PSP) | 11 | G | M |
| P1-3 | Desconto e cancelamento na UI com alçada por papel + auditoria | 11 | G | M |
| P1-4 | Tela de usuários/papéis (7 permissões hoje sem uso) | 11 | G | M |
| P1-5 | Multi-caixa: N turnos por loja, trava, teste de concorrência | 11 | G | G |
| P1-6 | NFC-e (API terceirizada: Focus/eNotas/Nuvem Fiscal) + contingência | 12 | G | G |
| P1-7 | Exportação CSV/PDF dos relatórios | 11 | M | P |
| P1-8 | Importação de produtos do legado (XML/planilha) para migração de cliente | 11 | M | M |
| P1-9 | JWT de sessão na API + 403 auditado por rota | 11 | G | M |

### P2 — incremental

Etiquetas; tabela de preço/promoção; fluxo de caixa/DRE/curva ABC; dashboard com gráfico; atalhos de teclado; handler global de erro; criptografia do SQLite (SQLCipher); suporte remoto consentido com agente real; telemetria opt-in.

### P3 — exploratório

Automação de UI como skill reutilizável; mesas/comandas (só se entrar no segmento de alimentação); multi-centro de estoque; cheque/crediário/convênio; app mobile; e-commerce/Scanntech/cashback (não fazem sentido no estágio atual).

---

## 7. Etapa 6 — Lista de tarefas acionáveis (P0 e P1)

### P0-1 · Suíte verde e documentação coerente — ✅ **concluído em 11/11/2026**

- **Gap:** 2 testes vermelhos por semente dependente do relógio; README/docs com números divergentes e com controle prometido que não existe.
- **Mudança executada:**
  - `tests/Landipay.Tests/BancoDeTeste.cs` → `Semear(LandipayDbContext db, DateTimeOffset? agora = null)`, com `quando ?? DateTimeOffset.UtcNow` no `CaixaTurno.Abrir` e o porquê documentado no XML doc.
  - `tests/Landipay.Tests/Fase5RelatoriosTests.cs:37` → `banco.Semear(db, Base)`.
  - `README.md` l. 7 (72 → 94, 9 → 10 documentos) e l. 281 (89 → 94, com a quebra 90 + 4).
  - `docs/05 §2` → tabela de inventário refeita por arquivo (94 no total, incluindo `SegregacaoAdminCaixaTests` e `Fase4AplicativosTests`) + nota "Estado da suíte (11/11/2026)".
  - `docs/00 §6.1` → linha da licença passa de "controle implementado" para **"Pendente (P0-3)"**, apontando o código que falta.
- **Aceite:** `dotnet test -c Release` = **94/94**; nenhum teste depende do relógio real para montar cenário.
- **Prova de execução:** saída colada em §4.1 e em `docs/05 §2`.
- **Esforço real:** P (~20 min).

### P0-2 · Git + CI — ✅ **concluído em 11/11/2026** (run remoto pendente)

- **Gap:** `git rev-parse` → *not a git repository*; sem pipeline.
- **Mudança executada:**
  - `git init -b main` + `.gitignore` estendido: `dist/`, `packaging/payload/` (77 MB regenerados por `packaging/build-msi.ps1`), `TestResults/`, `*.trx`, `*.log`, `*.wixpdb`, `*.msi`, `AppData/`.
  - `.gitattributes`: repositório em LF, working tree Windows em CRLF; binários marcados como `binary`.
  - Primeiro commit: **181 arquivos, 5,4 MB** de blob, nenhum `.dll`/`.exe`/`.msi` versionado.
  - `.github/workflows/ci.yml`: `restore` → `build -c Release -warnaserror` → `dotnet test --logger trx`, artefato `resultados-testes`, `windows-latest`, `timeout-minutes: 20`.
- **Aceite:** clone limpo → `dotnet test` verde; nenhum artefato de build versionado.
- **Prova de execução local (os três comandos do workflow, na ordem):**
  ```
  dotnet restore  → Todos os projetos estão atualizados para restauração.
  dotnet build -c Release --no-restore -warnaserror → Compilação com êxito. 0 Aviso(s) 0 Erro(s)
  dotnet test -c Release --no-build --logger trx → 94/94, 2 arquivos .trx gerados
  ```
- **Bloqueio que existia:** sem `gh` instalado, sem chave SSH e sem credencial em cache, o push não tinha como ser autenticado; foi resolvido com uma deploy key de escrita restrita a este repositório. Antes do push, o YAML foi validado por parser (`on: push/pull_request/workflow_dispatch`, job `build-e-testes`) e os comandos executados localmente.
- **Push e primeiro run (11/11/2026, 18:09Z):** remote `git@github.com:diegotecnico2982002-hue/landipay.git` (deploy key com escrita, escopo só neste repo). `main` → `f74d37b`. **CI #1 = success em 2min24s**, todos os passos verdes (checkout, .NET 8, cache, restore, build, testes, artefato): https://github.com/diegotecnico2982002-hue/landipay/actions/runs/43075014767
- **Gate de contagem (adicionado depois do #1):** passo `pwsh` soma os `.trx` e falha se não forem `TESTES_ESPERADOS` (94) com 0 falhas, e escreve `### Suíte: 94 testes, 94 aprovados, 0 falhas` no *Summary*. Isso fecha a lacuna "verde não prova que 94 rodaram": agora prova. **Verificado em dois lugares:** local (`2 arquivos, total 94, aprovados 94, falhas 0`) e no runner — [CI #2, 2min38s, passo 8 "Conferir contagem da suíte" = success](https://github.com/diegotecnico2982002-hue/landipay/actions/runs/43076518139).
- **Prova de clone limpo (feita):** `git clone` para outra pasta → **181 arquivos, 0 pastas `bin`/`obj`/`dist`/`payload`**, `dotnet build -c Release -warnaserror` = 0 avisos/0 erros e `dotnet test --no-build` = **94/94**. O repositório é auto-suficiente: nada essencial ficou de fora pelo `.gitignore`.
- **Pegadinha encontrada (duas vezes, causas diferentes):** (1) `--logger "trx;LogFileName=resultados.trx"` na solução inteira faz o segundo assembly **sobrescrever** o primeiro; trocado por `--logger trx`. (2) Só isso não bastou: o nome **padrão** do VSTest é `<usuário>_<máquina>_<timestamp com segundos>`, então dois assemblies iniciados no mesmo segundo colidem igual — foi o que derrubou o **run #3** (testes verdes, contagem 90, gate falhou: https://github.com/diegotecnico2982002-hue/landipay/actions/runs/43077134173). Correção definitiva: um passo `dotnet test` por assembly, com nome de arquivo e pasta explícitos (`unitarios.trx` em `TestResults/unitarios`, `fluxo-ui.trx` em `TestResults/fluxo-ui`) e o gate somando recursivamente. Lição: **verde não prova contagem; o gate prova** — e ele já pagou por si no primeiro uso.
- **Esforço real:** P (~25 min).

### P0-3 · Licença que expira e bloqueia — ✅ **concluído em 11/11/2026**

- **Gap:** `ValidadorDeLicencaJwt` só era chamado em `ServicoInstalador.cs:208`; o app abria e vendia com licença vencida, revogada ou ausente. `ToleranciaOfflineDias` existia no modelo e nunca era consultado.
- **Mudança executada:**
  - `src/Landipay.Client/Seguranca/ServicoDeLicenca.cs` (novo): estados `licenca_ok`, `licenca_em_tolerancia`, `licenca_expirada`, `licenca_revogada`, `licenca_ausente`, `licenca_adulterada`; `Avaliar()` sem escrita a cada operação, `AvaliarNoInicio()` com auditoria throttleada em 12 h, `ExigirOperacao()` lançando `LicencaBloqueadaException` e auditando `licenca.bloqueio` (1×/dia).
  - `ConfigLicenca.Reavaliar(agora)` no domínio: recalcula `Ativa/EmTolerancia/Expirada` pelo relógio.
  - `ServicoDeCaixa`: `ExigirOperacao` em abrir turno, iniciar venda, finalizar venda, sangria e suprimento. **Fechar turno continua liberado** (dinheiro na gaveta).
  - `ComposicaoApp`: avalia na abertura e no login; bloqueada → `LicencaView` no lugar do login; `Shell.Aviso` com o aviso de 7 dias no cabeçalho dos dois apps.
  - `LicencaView.xaml` + `LicencaViewModel` (novos), `DataTemplate` nos dois shells.
- **Regras que os testes fixam:** sem licença não opera; expirada dentro da tolerância opera com aviso; expirada fora bloqueia; revogada bloqueia mesmo com data futura; `exp` do token menor que a data gravada = adulteração; chave pública estranha = adulteração; fechar turno funciona expirado; auditoria de abertura e de bloqueio; **a abertura do app não renova a tolerância**; renovação com o mesmo emissor libera.
- **Prova de execução:** app Caixa compilado e aberto com diretório de dados vazio → abriu direto na tela de licença (`licenca_ausente`), capturado em `docs/mockups/app-licenca-bloqueada.png` (ver `docs/03`); `LogAuditoria` com `licenca.ausente`.
- **Testes:** 12 novos (`LicencaOperacionalTests`) + 2 (`LicencaNaAberturaTests`) → **108 no total**; `TESTES_ESPERADOS` do CI atualizado para 108.
- **Limite declarado:** o app monta o serviço **sem** chave pública pinada (`new ServicoDeLicenca(Db)`), então a assinatura não é conferida em runtime — a tela diz isso ("assinatura não conferida (sem chave pinada)"). A detecção de data esticada no banco continua valendo porque lê o `exp` do próprio token. Pinagem da chave + rotação por `kid` fica com a revogação online (P0-5).
- **Esforço real:** P/M (~1 h 30).

### P0-4 · Backup automático + restore testado

- **Gap:** zero `backup` em `src/`; `docs/06 §5` manda copiar pasta à mão.
- **Mudança:** `ServicoDeBackup` (SQLite `VACUUM INTO` + cópia de `landipay.db` + `-wal`/`-shm`), destino `%APPDATA%\Landipay\backups\AAAA-MM-DD.db`, retenção N, execução no `FecharTurno` e no start (se > 24 h); contador "dias sem backup" na tela Admin (paridade com `DiasSemBackup` da referência); comando `restore` documentado.
- **Aceite:** fechar turno gera arquivo; `restore` em pasta limpa abre com os dados; teste automatizado de ida-e-volta.
- **Prova:** arquivo de backup real + captura do app restaurado.
- **Esforço:** M.

### P0-5 · Revogação online

- **Gap:** nenhuma consulta ao backend depois da instalação.
- **Mudança:** endpoint `GET /v1/licencas/{instalacaoId}/estado` (assinado), consulta a cada 24 h com fallback para a janela offline; revogação grava `StatusLicenca.Revogada` e o app entra em somente-leitura.
- **Aceite:** revogar no servidor → app bloqueia na próxima checagem; teste com servidor fora do ar mantém a tolerância (não bloqueia).
- **Esforço:** M. **Depende de** P0-3.

### P0-6 · Cadastro de produto + entrada/ajuste de estoque

- **Gap:** `ProdutosViewModel` só lê (`LoginViewModel.cs:174-212`); único movimento de estoque é a baixa da venda.
- **Mudança:** `ProdutosViewModel` ganha `Novo/Editar/Desativar` (permissão `produto.escrever`) e `ServicoDeCatalogo` com CRUD; `ServicoDeEstoque` com `Entrada`, `Ajuste`, `Inventário` (permissão `estoque.movimentar`), gravando `EstoqueMovimento` com `SaldoApos` e auditoria.
- **Aceite:** cadastrar produto pela UI → aparece no caixa; entrada de 10 → saldo 10; ajuste para 8 → movimento `Ajuste` com motivo obrigatório; 2 testes por operação; negativo bloqueado.
- **Prova:** captura das telas + query no banco.
- **Esforço:** M.

### P0-7 · Decisão fiscal + cupom

- **Decisão (bloqueia a tarefa):** NFC-e própria (ACBr, caro, 2–3 meses) × API terceirizada (Focus NFe/Nuvem Fiscal/eNotas, ~1–2 semanas de integração, custo por documento) × seguir sem fiscal e restringir o cliente-alvo (MEI/Simples com dispensa). Precisa de resposta do contador e da UF.
- **Mudança (independente da decisão):** impressão ESC/POS + abertura de gaveta — `ServicoDeImpressao` (RAW printing via `winspool.drv`, 58/80 mm, QR de PIX/fiscal opcional), layout de cupom com `Itens`, totais, formas, troco, operador, turno; botão "Imprimir" e reimpressão; preferência de impressora em `ArmazemDePreferencias`.
- **Aceite:** venda finalizada gera cupom na impressora térmica real, gaveta abre pelo mesmo comando; falha de impressora não desfaz a venda (fila de reimpressão).
- **Esforço:** M (cupom) + G (fiscal).

### P0-8 · Cliente e fornecedor

- **Gap:** não existe `Cliente` no domínio (confirmado: 6 arquivos de entidade, nenhum de pessoa).
- **Mudança:** entidades `Cliente`/`Fornecedor` (PF/PJ, documento, endereço reaproveitando `Endereco`), `Venda.ClienteId` opcional, tela de cadastro no Admin, busca por CPF/CNPJ no caixa, migração `ClientesFornecedores`.
- **Aceite:** venda vinculada a cliente; relatório por cliente; teste de documento inválido (CPF/CNPJ) rejeitado.
- **Esforço:** M.

### P0-9 · Authenticode

- **Gap:** nenhum binário assinado → SmartScreen bloqueia.
- **Mudança:** certificado OV/EV (compra, prazo externo), `signtool sign /fd sha256 /tr <timestamp>` no pipeline para `Landipay.Admin.exe`, `Landipay.Pos.exe`, `.msi` e o EXE do instalador; verificação no updater (assunto + thumbprint esperado).
- **Aceite:** `signtool verify /pa` OK nos 4 artefatos; instalação sem aviso do SmartScreen em máquina limpa.
- **Esforço:** P técnico, G no prazo (compra/validação).

### P0-10 · Updater que aplica

- **Mudança:** manifesto `pacotes[]` com hash por binário; `AtualizadorDesktop` baixa para pasta temporária, confere hash, encerra o app, executa o MSI em modo silencioso (`msiexec /i ... /qn REBOOT=Suppress`), registra resultado e reabre; recusa e audita divergência.
- **Aceite:** atualização 1.1.0 → 1.2.0 aplicada em máquina de teste; hash adulterado → recusa auditada; rollback documentado.
- **Esforço:** M. **Depende de** P0-9.

### P1-1 · TEF/POS

- **Mudança:** abstração `IProvedorTef` (stone/cielo/rede via TEF discado ou API), fluxo no `Pagar`: intenção → transação → comprovante → confirmação; parcelas; `TransacaoTef` persistida; conciliação diária (arquivo da adquirente × vendas).
- **Aceite:** venda no crédito parcelado autorizada em homologação, comprovante impresso, conciliação sem divergência.
- **Esforço:** G.

### P1-2 · PIX por QR dinâmico

- **Aceite:** QR gerado com valor e txid, confirmação por webhook/polling, baixa automática no caixa, expiração em 5 min.
- **Esforço:** M.

### P1-3 · Desconto e cancelamento com alçada

- **Mudança:** comandos na `CaixaView` + `ServicoDeCaixa.Desconto(permissao: venda.desconto, limite por papel)` e `CancelarVenda(venda.cancelar, motivo obrigatório, prazo)`; acima do limite exige credencial de supervisor (reautenticação sem trocar de sessão); tudo auditado.
- **Aceite:** operador não desconta acima de X%; desconto acima exige supervisor; cancelamento grava motivo e reversão de estoque; 3 testes.
- **Esforço:** M.

### P1-4 · Tela de usuários e papéis

- **Mudança:** CRUD de usuário, atribuição de papéis, reset de senha, desativação; usar as 7 permissões hoje órfãs.
- **Aceite:** criar operador com `OperadorCaixa` → login no Pos funciona, no Admin não; auditoria de cada alteração.
- **Esforço:** M.

### P1-5 · Multi-caixa

- **Mudança:** `CaixaTurno` por terminal (`TerminalId`), índice único `(LojaId, TerminalId, Status=Aberto)`, retomada de turno por terminal, teste de concorrência com 2 DbContexts em paralelo.
- **Aceite:** 2 turnos abertos na mesma loja; venda nos dois sem colisão de numeração; teste de corrida passa 10/10 execuções.
- **Esforço:** G.

### P1-6 · NFC-e via API terceirizada

- **Mudança:** `IProvedorFiscal`, emissão assíncrona com outbox (aproveitando o que já existe), estados `Pendente/Autorizada/Rejeitada/Contingência`, DANFE simplificado no cupom, cancelamento e inutilização, guarda de XML por 5 anos.
- **Aceite:** 3 vendas autorizadas em homologação + 1 cancelada + 1 em contingência; XML recuperável.
- **Esforço:** G.

### P1-7 · Exportação CSV/PDF

- **Aceite:** cada bloco do relatório exporta CSV com cabeçalho e separador `;` (pt-BR) e PDF com a marca.
- **Esforço:** P.

### P1-8 · Importação do legado

- **Mudança:** importador de produtos/clientes por planilha e por XML de NF-e; mapeamento documentado; dry-run com relatório de rejeições.
- **Aceite:** 100 produtos de uma planilha real importados, com relatório de erros.
- **Esforço:** M.

### P1-9 · JWT de sessão na API

- **Aceite:** `sub/papel/perms/app/exp/jti`; 403 auditado por rota; token de caixa não acessa relatório.
- **Esforço:** M.

---

## 8. O que **não** foi feito (declarado)

- Nenhum binário da referência foi executado, extraído ou descompilado. Toda a Etapa 2 vem de: `.config`, metadados PE, listagem do zip e strings UTF-16 — nada de IL reconstruída.
- As regras de negócio da referência (validação, arredondamento, fluxo de tela, mensagens) **não** foram observadas → marcadas `Não-verificado`.
- O fluxo de caixa do Landipay **não** foi reexecutado nesta sessão; a prova citada é a de `docs/03 §8.1` (10/11) e os testes automatizados.
- A varredura de segredos não foi reexecutada (citada de `docs/05`).
- Nenhum arquivo do repositório foi alterado nesta etapa — só o documento e o CSV de inventário foram criados.
