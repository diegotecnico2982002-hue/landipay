# 00 — Arquitetura do Landipay

> Documento da Fase 0. Estado: aguardando GATE 1.
> Escopo: visão de componentes, stack, contrato de segurança herdado → Landipay e modelo de ameaças.
> O que este documento **não** cobre: detalhamento de tabelas (Fase 1), contratos de API (Fase 2), telas (Fase 3/4).

---

## 1. Objetivo

Construir um PDV desktop **do zero** que cumpra o mesmo papel funcional do instalador legado analisado (implantar um ponto de venda completo em uma loja, com banco, empresa, licença e operação de caixa) **sem herdar nenhum dos seis anti-padrões de segurança** identificados na engenharia reversa.

Referência de entrada: o fluxo observado no `SyntaxInstaller.exe` e o domínio do banco `GestorLimpo` — usados apenas como **especificação de comportamento**. Nenhum código, token, senha, endpoint ou binário do fornecedor é reutilizado.

O que muda de natureza, não só de detalhe: o sistema legado é um **instalador** que configura a máquina; o Landipay é um **produto** com atualização verificada, autenticação real, licença assinada e suporte remoto consentido. Essa diferença é o eixo de todas as decisões abaixo.

---

## 2. Princípios de arquitetura

| # | Princípio | Consequência prática |
|---|---|---|
| P1 | **Nada executa sem verificação** | Todo artefato vindo da rede (manifesto, MSI, ZIP, script) é validado por assinatura + hash antes de tocar o sistema. |
| P2 | **Segredo não mora no binário** | Chaves privadas nunca saem do KMS/HSM; o cliente só carrega chave **pública** de verificação, e credenciais de runtime vêm de secret store. |
| P3 | **Offline é cidadão de primeira classe** | O caixa precisa vender sem internet; licença, CNPJ e sincronização têm caminho local com prazo de tolerância explícito. |
| P4 | **Toda ação sensível é auditável** | Quem entrou, quem abriu caixa, quem autorizou suporte remoto: registro imutável e local-first. |
| P5 | **Menor privilégio por padrão** | O app roda como usuário comum; elevação só no instalador, em etapas declaradas, nunca em background. |
| P6 | **Consentimento explícito** | Nenhum canal de entrada (remoto, telemetria, log) existe sem ato humano registrado. |

---

## 3. Stack e justificativa

| Camada | Escolha | Por quê | Alternativa descartada |
|---|---|---|---|
| Cliente desktop | **.NET 8 (LTS) + WPF + MVVM** (CommunityToolkit.Mvvm) | Periféricos de PDV (balança, gaveta, impressora térmica, PinPad) têm SDK maduro no Windows; WPF resolve DPI/acessibilidade e tem template de UI próprio para o design system. | **Avalonia** — melhor para multi-OS, mas o parque de PDV é Windows e o esforço de integração de periféricos não se paga agora. *(decisão nova LD-02)* |
| Persistência local | **SQLite** via EF Core 8 (`Microsoft.Data.Sqlite`) | Arquivo único, backup trivial, zero administração na loja, funciona 100% offline, WAL para concorrência do caixa. | **SQL Server Express** — foi o que o legado instalava: exige instalação de engine, serviço, patch, e vira superfície de ataque administrativa numa máquina de balcão. *(decisão nova LD-01)* |
| Backend | **ASP.NET Core 8 (minimal APIs)** | Mesma plataforma do cliente, um só pipeline de build; JWT/ES256 nativo; bom encaixe com o manifesto assinado. | Node/Nest — viável, mas duplicaria toolchain sem ganho. |
| Banco central | **PostgreSQL 16** | Licença livre, `jsonb` para payloads de auditoria, réplica lógica simples para relatórios. | SQL Server (custo/licença), MySQL (menos recursos de integridade). |
| ORM | **EF Core 8 + migrations** | Migrations versionadas são o que permite reinstalação idempotente (Fase 4). | Dapper puro — mais controle de SQL, menos estrutura de schema; pode ser adotado pontualmente em relatórios. |
| Log | **Serilog** (sink arquivo local + sink de envio opt-in) | Envio de log é um **plugin** que o usuário liga, não um comportamento embutido. | — |
| Criptografia | **libsodium/`NSec`** (Ed25519, XChaCha20-Poly1305) + **Argon2id** (`Konscious`) | Primitivas modernas, sem invenção caseira. | bcrypt — aceitável, mas Argon2id é o padrão atual para senha. |
| CI/CD | GitHub Actions + **assinatura fora do CI** (chave em KMS) | Pipeline nunca tem a chave privada em claro. | — |

**Versões fixadas na Fase 1** via `Directory.Packages.props` (central package management).

### 3.1 Identidade visual e temas

O cliente e o instalador usam o design system da Fase 0 (`docs/07-design-system.md`), com **modo escuro como padrão e modo claro disponível como opção**, alternável nas configurações do app e do instalador. Ambos os temas mapeiam **as mesmas chaves semânticas** (`Theme.Dark.xaml` / `Theme.Light.xaml`), e nenhum componente referencia cor concreta — a troca acontece em runtime, por `ResourceDictionary`, sem reiniciar. A preferência é persistida **por usuário/perfil**, não por máquina. Esse modelo não altera stack, ameaças nem o contrato A1–A6; ele apenas garante que a UI inteira (incluindo o instalador) seja temável por token.

---

## 4. Visão de componentes

```
┌──────────────────────────── ESTAÇÃO DA LOJA (Windows) ────────────────────────────┐
│                                                                                    │
│  ┌──────────────────────┐        ┌───────────────────────────────────────────┐     │
│  │ Landipay.Installer   │        │ Landipay.Desktop (WPF, MVVM)              │     │
│  │ (elevado, 1x)        │        │  ├ Login (Argon2id, lockout, timeout)     │     │
│  │ 1 pré-requisitos     │        │  ├ Produtos / Estoque                     │     │
│  │ 2 banco (schema)     │        │  ├ Frente de caixa (venda/pgto/sangria)   │     │
│  │ 3 empresa (CNPJ/manual)│      │  ├ Fechamento de turno / relatórios       │     │
│  │ 4 ativação (licença) │        │  └ Updater + Logging + Assist             │     │
│  │ 5 primeiro admin     │        └───────────────┬───────────────────────────┘     │
│  └──────────┬───────────┘                        │                                 │
│             │ aplica migrations                  │ EF Core                        │
│             ▼                                    ▼                                 │
│        ┌──────────────────────────────────────────────────┐                        │
│        │  landipay.db (SQLite, WAL) + landipay.db-wal     │                        │
│        │  tabelas: empresa, loja, usuario, produto, venda, │                        │
│        │  item_venda, pagamento, caixa_turno, auditoria…   │                        │
│        └──────────────────────────────────────────────────┘                        │
│             │                                       ▲                              │
│             │ fila de sincronização (outbox)        │ pull de catálogo/preço       │
└─────────────┼───────────────────────────────────────┼──────────────────────────────┘
              │  TLS 1.3 + mTLS opcional              │
              ▼                                       │
┌───────────────────────────── NUVEM LANDIPAY ────────┴──────────────────────────────┐
│                                                                                    │
│  ┌────────────────────────────┐   ┌─────────────────────────────────────────────┐  │
│  │ License & Update Service   │   │ Sync / Reporting API                        │  │
│  │  ASP.NET Core 8            │   │  ASP.NET Core 8                             │  │
│  │  • /license/activate       │   │  • /sync/outbox (idempotente, por loja)     │  │
│  │  • /license/refresh        │   │  • /catalog/pull                            │  │
│  │  • /updates/manifest       │   │  • /reports/*                               │  │
│  │  • JWT ES256, kid rotativo │   │  autorização por tenant + loja              │  │
│  └──────────┬─────────────────┘   └──────────────┬──────────────────────────────┘  │
│             │                                    │                                 │
│             ▼                                    ▼                                 │
│  ┌──────────────────┐   ┌──────────────────────────────────────────────────────┐   │
│  │ KMS / HSM        │   │ PostgreSQL 16                                        │   │
│  │ • priv. licença  │   │  tenants, licenças, releases, auditoria de suporte   │   │
│  │ • priv. update   │   └──────────────────────────────────────────────────────┘   │
│  │ (nunca no CI)    │                                                              │
│  └──────────────────┘                                                              │
│                                                                                    │
│  ┌────────────────────────────┐   ┌─────────────────────────────────────────────┐  │
│  │ Object storage (releases)  │   │ Assist (suporte remoto)                     │  │
│  │ artefatos + .sig + SBOM    │   │  • emite credencial de sessão (TTL 30 min)  │  │
│  └────────────────────────────┘   │  • registra consentimento + encerramento    │  │
│                                    └─────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────────────┘
              │                                        ▲
              │ HTTPS (API pública, cache local 24h)    │ consentimento explícito
              ▼                                        │
     ┌────────────────────┐              ┌─────────────────────────────────┐
     │ Cadastro público   │              │ Técnico autorizado (sessão      │
     │ de CNPJ (API)      │              │ nominal, auditada, expira só)   │
     └────────────────────┘              └─────────────────────────────────┘
```

### 4.1 Responsabilidades

| Componente | Faz | Não faz |
|---|---|---|
| `Landipay.Installer` | Aplica schema, cadastra empresa, ativa licença, cria primeiro admin. Idempotente. | Não instala engine de banco, não configura acesso remoto, não baixa nada sem validar assinatura. |
| `Landipay.Desktop` | Operação de caixa e retaguarda, offline-first, com outbox de sincronização. | Não contém segredo, não abre porta de entrada, não envia log sem opt-in. |
| `License & Update Service` | Emite/valida licença (JWT ES256), publica manifesto assinado de releases. | Não guarda dado de venda do cliente; não tem acesso ao banco da loja. |
| `Sync/Reporting API` | Recebe outbox, entrega catálogo, serve relatórios consolidados. | Não inicia conexão para dentro da loja. |
| `Assist` | Emite credencial **por sessão**, registra consentimento e encerramento. | Não provisiona canal permanente nem senha fixa. |
| KMS/HSM | Guarda chaves privadas de assinatura e de licença. | Nunca expõe chave privada a runner de CI ou a desenvolvedor. |

---

## 5. Fronteiras de confiança

| Zona | Confiança | Regra de travessia |
|---|---|---|
| Z1 — Processo do cliente | **Baixa** (máquina de balcão, físico acessível) | Banco local cifrado em repouso (SQLCipher) + credencial de usuário; nenhum segredo de longo prazo em disco. |
| Z2 — Instalador elevado | **Média** (roda uma vez, com admin) | Só aceita payload assinado; toda escrita declarada e logada. |
| Z3 — Nuvem Landipay | **Alta** | Autenticação mútua de serviço, autorização por tenant, auditoria de toda chamada administrativa. |
| Z4 — KMS | **Máxima** | Acesso só por papel IAM dedicado; assinatura via API; chave nunca exportável. |
| Z5 — Terceiros (CNPJ) | **Nenhuma** | Resposta é dado não confiável: validada, cacheada, nunca executada. |

**Fluxo crítico de dados — atualização:**
`CI publica artefato` → `job de assinatura (KMS) gera manifesto.sig` → `storage` → `cliente consulta /updates/manifest` → **valida assinatura com chave pública pinada** → baixa artefato → **valida SHA-256** → aplica → registra em auditoria.

Nenhum passo pode ser pulado: o cliente trata "sem manifesto válido" como "sem atualização", nunca como "atualizar mesmo assim".

---

## 6. Contrato de segurança: herdado → Landipay

Esta tabela é o documento central do projeto. Cada linha é uma obrigação verificável nas fases seguintes.

| # | Comportamento herdado (fato verificado na engenharia reversa) | Risco concreto | Controle Landipay | Onde será implementado | Anti-padrão |
|---|---|---|---|---|---|
| **A1** | Senha de acesso remoto fixa no binário (`RustDeskInstallService.cs:14`), aplicada junto com um `--config` embutido de ID/servidor | Qualquer portador do executável abre sessão remota em qualquer loja instalada; sem registro de quem entrou | Credencial **por sessão**, gerada no servidor, TTL 30 min, uso único; consentimento na tela com nome do técnico e escopo; kill switch local; auditoria de início/fim; nenhum canal configurado na instalação | `Assist` (Fase 2) + tela de consentimento (Fase 3) | A1 |
| **A2** | Tokens Basic/Bearer e chave de API embutidos (`LogSyncService.cs:29`, `ExternalService.cs:50,67`, `CNPJaService.cs:20`) | Qualquer um com o exe usa a cota/identidade do fornecedor; rotação impossível sem recompilar | Segredos só em secret store do servidor (env/Azure Key Vault/DPAPI para o que for local); cliente nunca fala com terceiros usando chave própria — passa pelo backend; chaves rotacionáveis com `kid` | Fase 2 (config), Fase 5 (varredura de strings no build) | A2 |
| **A3** | Download e execução de `Updater.exe` (Base64), MSI, ZIP e scripts `.sql` sem hash nem assinatura | Quem controlar o domínio/CDN executa código como administrador nas lojas | **Manifesto assinado** (Ed25519): versão, URL, tamanho, SHA-256, `minVersion`, `kid`; cliente valida assinatura → hash → só então executa; recusa e audita qualquer divergência; chave pública pinada no binário | Fase 2 (emissão), Fase 3 (updater), Fase 5 (teste automatizado de rejeição) | A3 |
| **A4** | Connection string por concatenação (`DataAccess.cs:23`) e `INSERT` com nome/endereço interpolados (`DataAccess.cs:125`) | Injeção de SQL a partir de dado vindo da API de CNPJ ou da tela | Connection string em config externa (secret store); **100% das queries parametrizadas** (EF Core + `SqliteParameter`); validação de entrada; nenhuma string SQL montada por concatenação | Fase 1 (modelo/repositórios), Fase 5 (revisão + teste) | A4 |
| **A5** | "Login" sem autenticação: `LoginViewModel` só verifica versão e inicia log; qualquer senha entra | Acesso total ao caixa e à retaguarda sem credencial; nenhuma rastreabilidade de operador | **Argon2id** (m=64MB, t=3, p=4) com salt por usuário; lockout progressivo (5 falhas → 5 min, 10 → 30 min); sessão com timeout de inatividade (15 min) e absoluto (12 h); autorização por papel; auditoria de login e de ação | Fase 1 (entidades), Fase 2 (auth), Fase 3 (telas) | A5 |
| **A6** | Envio de log/sessão ao fornecedor sem consentimento, com AnyDesk ID coletado silenciosamente | Vazamento de dado operacional e de identificador de máquina sem base legal; sem criptografia garantida | Log **local-first** (Serilog, rotação, retenção 30 dias); envio é **opt-in** explícito, por evento, com prévia do que será enviado; TLS 1.3 obrigatório; anonimização de PII antes do envio | Fase 3 (sink) + Fase 2 (endpoint) | A6 |

### 6.1 Achados adicionais (não cobertos por A1–A6, mas corrigidos)

| Achado | Controle |
|---|---|
| Instalador grava `SAPWD` em `Config.ini` em texto claro e instala engine de banco no balcão | Não há engine instalada (LD-01); credencial de banco local é derivada de DPAPI + salt, nunca escrita em ini |
| `LoginView` sem verificação de licença: app funcionava com licença expirada | **Pendente (P0-3).** Especificado: validação a cada 12 h com janela offline de 7 dias e modo somente-leitura ao expirar. Hoje só o instalador valida (`ServicoInstalador.cs:208`) — o app abre sem consultar a licença. Rastreio em `docs/08` (P0-3) |
| Sem rate limit em endpoints públicos de update | Limite por IP + por `lojaId`, com backoff |
| Sem SBOM/reprodutibilidade | `dotnet sbom` no pipeline + build determinístico |

---

## 7. Modelo de ameaças — STRIDE

### 7.1 Instalador (`Landipay.Installer`)

| Ameaça | Cenário concreto | Mitigação | Residual |
|---|---|---|---|
| **S**poofing | Instalador falso distribuído por link de terceiro | Binário do instalador é Authenticode-assinado (EV); instalador exibe publisher + thumbprint; verificação de manifesto antes de qualquer payload | Usuário pode ignorar aviso do SmartScreen (educação no doc 06) |
| **T**ampering | Payload trocado no storage | Manifesto Ed25519 + SHA-256 validados antes de aplicar; falha = aborta e audita | Compromisso do KMS (aceito, fora de escopo) |
| **R**epudiation | "Não fui eu que instalei com esse CNPJ" | Trilha de auditoria assinada por etapa, com usuário Windows + timestamp + hash do payload | Estação sem relógio confiável — usa timestamp do servidor |
| **I**nformation disclosure | `Config.ini` com senha de `sa` legível | Sem engine de banco; nenhum segredo em arquivo; banco local cifrado | Acesso físico com admin ainda lê o `.db` desbloqueado em memória |
| **D**enial of service | Instalador baixa 2 GB e falha no fim | Download resumível, checagem de espaço e de hash antes de aplicar, rollback transacional de migration | — |
| **E**levation of privilege | Instalador roda elevado e executa qualquer coisa da pasta atual | Nenhuma execução de binário de diretório de trabalho; caminho absoluto; `UseShellExecute=false`; só assina/aplica payloads com hash conhecido | Elevação ainda é necessária — declarada e logada |

### 7.2 Updater (cliente)

| Ameaça | Cenário | Mitigação | Residual |
|---|---|---|---|
| **S**poofing | DNS/CDN sequestrado devolve manifesto falso | Assinatura Ed25519 com chave pública **pinada** no binário; sem confiar em CA para integridade | — |
| **T**ampering | Manifesto válido aponta URL que serve binário trocado | Hash do artefato **dentro** do manifesto assinado (não em header HTTP); validação em streaming antes de gravar em `Program Files` | — |
| **R**epudiation | Atualização aplicada sem registro | Log local + evento de auditoria com versão origem/destino e `kid` usado | — |
| **I**nformation disclosure | Manifesto revela roadmap interno | Manifesto só expõe versão estável, canal e hash | — |
| **D**enial of service | Downgrade forçado para versão vulnerável | `minVersion` no manifesto + contador anti-rollback monotônico local | Reinstalação limpa zera o contador (aceito) |
| **E**levation | Pacote "assinado" com conteúdo malicioso | Separação de papéis: chave de **update** ≠ chave de **licença**; assinatura só por job dedicado com aprovação dupla | Compromisso de insiders (processo, não código) |

### 7.3 Acesso remoto (`Assist`)

| Ameaça | Cenário | Mitigação | Residual |
|---|---|---|---|
| **S**poofing | "Técnico" liga pedindo para instalar acesso remoto | Sessão só nasce de um pedido **iniciado pelo cliente** com código exibido na tela; técnico precisa do código + identidade no painel | Engenharia social (treinamento, doc 06) |
| **T**ampering | Sessão usada para alterar dados fora do escopo | Escopo declarado (somente-leitura / suporte), comandos destrutivos exigem re-consentimento; toda ação registrada no `LogAuditoria` | — |
| **R**epudiation | Técnico nega acesso | Registro com identidade nominal, horário, duração, escopo, IP e encerramento assinado | — |
| **I**nformation disclosure | Técnico vê dado de cliente fora do necessário | Blur de PII por padrão durante a sessão; gravação só com opt-in | Visualização humana ainda vê o que está na tela |
| **D**enial of service | Sessão esquecida aberta | TTL 30 min + encerramento automático por inatividade (5 min) + botão "encerrar agora" sempre visível | — |
| **E**levation | Sessão vira acesso permanente | Credencial de uso único, não reutilizável; nada é instalado como serviço auto-start; revogação no servidor invalida na hora | — |

---

## 8. Cadeia de suprimentos

1. **Duas chaves, dois propósitos.** `license-*` assina licença; `update-*` assina manifesto. Comprometer uma não compromete a outra.
2. **Chave privada só no KMS.** CI produz artefato e pede assinatura; nunca vê a chave.
3. **Pinning.** O cliente embarca as chaves públicas (`kid` + chave), com lista de rotação e chave de emergência.
4. **Rotação.** `kid` no manifesto; cliente aceita os `N` `kid`s da lista pinada; chave nova publicada com 90 dias de antecedência.
5. **Revogação.** Lista de `kid` revogados embarcada no próprio release seguinte; manifesto assinado por `kid` revogado é recusado.
6. **Proveniência.** `dotnet sbom` (CycloneDX) anexado a cada release; build determinístico (`Deterministic=true`, `ContinuousIntegrationBuild=true`).
7. **Sem `curl | sh`.** Nenhum script remoto é executado; scripts SQL são dados, aplicados por migrations versionadas.

---

## 9. Modelo de dados — visão de alto nível

Detalhamento completo (dicionário, tipos, índices, integridade) fica em `01-modelo-dados.md`, Fase 1. Aqui, apenas o mapa:

```
Empresa 1─N Loja 1─N CaixaTurno 1─N Venda 1─N ItemVenda
                                 └─N Pagamento
Loja 1─N Usuario N─N Papel N─N Permissao
Produto 1─N Estoque (movimentos: entrada, saída, ajuste, perda)
* ─ LogAuditoria (append-only)
ConfigLicenca (1 por instalação)
```

Regras transversais que já valem como arquitetura:
- **`LogAuditoria` é append-only** (sem `UPDATE`/`DELETE` na aplicação; trigger de bloqueio na Fase 1).
- **Valores monetários em `decimal(18,4)`**; nunca `float`.
- **Toda tabela de negócio tem `LojaId`** — base para autorização por tenant/loja nas APIs.
- **Outbox de sincronização** como tabela própria: a venda é local-first e a nuvem é consumidora, nunca fonte de verdade do caixa.

---

## 10. Decisões de arquitetura (ADR resumido)

| ID | Decisão | Justificativa | Consequência |
|---|---|---|---|
| AD-01 | SQLite como banco local | Elimina engine administrativa na loja, backup atômico, offline real | Sem recursos de SQL Server (jobs, procedures) — relatórios pesados vão para a nuvem |
| AD-02 | WPF em vez de Avalonia | Periféricos e parque Windows | Sem multi-OS no cliente |
| AD-03 | Manifesto Ed25519 separado de JWT de licença | Separação de chaves e de ciclos de vida | Duas superfícies de verificação para manter |
| AD-04 | Licença JWT ES256 com janela offline de 7 dias | Caixa não pode parar por rede | Janela precisa ser monitorada; renovação em background |
| AD-05 | Suporte remoto como feature do produto, não do instalador | A1 só se resolve tirando do instalador o poder de abrir porta | Precisa de serviço `Assist` na Fase 2 |
| AD-06 | Log local-first com envio opt-in | A6 | Suporte perde telemetria passiva; ganha confiança e base legal |
| AD-07 | Backend passa na frente do cliente para CNPJ | A2 (chave de terceiro nunca no cliente) | Um hop extra; cache local de 24h compensa |
| AD-08 | Dois temas (escuro padrão, claro opcional) sobre o mesmo conjunto de tokens | Contraste e conforto variam por ambiente de loja; identidade preservada no escuro | Todo componente precisa usar `DynamicResource`; cor literal em XAML passa a ser erro de build |

---

## 11. Decisões novas Landipay (marcadas explicitamente)

> Regra 9 do briefing: o que o legado não revelou (ou revelou mal) é proposto aqui e fica marcado como **decisão nova**, não como "equivalente herdado".

| ID | Decisão nova | Por que não dá para "herdar" |
|---|---|---|
| **LD-01** | Não instalar engine de banco na loja; SQLite embarcado | O legado instalava SQL Server 2008 Express e gravava a senha `sa` em `Config.ini`. Não é questão de versão: a arquitetura de engine administrativo no balcão é o problema. |
| **LD-02** | Cliente WPF (Windows-only) | O legado é WPF; não houve evidência de requisito multi-OS. Multi-OS fica como decisão futura, não como dívida silenciosa. |
| **LD-03** | Suporte remoto deixa de ser instalado pelo instalador e passa a ser função do app (consentimento + TTL) | O legado embutia senha e config no instalador — não há "layout herdado" a preservar aqui, é redefinição de produto. |
| **LD-04** | Instalador idempotente com rollback transacional de migration | O legado não tinha noção de reinstalação; comportamento é novo e precisa ser especificado (Fase 4). |
| **LD-05** | CNPJ via backend com cache 24h + fallback manual | Mantém o comportamento funcional (preencher empresa por CNPJ) mas move a chave de terceiro para o servidor. |
| **LD-06** | Layout das telas é design próprio no tema Landipay | O legado tinha 17 `.baml` não recuperados (limitação registrada na análise). Nenhuma tela é "réplica"; são telas novas, com os campos que o fluxo exige. |

---

## 12. Riscos residuais e fora de escopo

**Riscos residuais aceitos nesta fase:**
- Compromisso do KMS/HSM derrota a cadeia de assinatura (mitigação: processo, dupla aprovação, auditoria de assinatura).
- Máquina de balcão com administrador local malicioso pode ler o banco desbloqueado (mitigação parcial: disco cifrado pelo SO, sessão curta).
- Engenharia social para obter código de sessão de suporte (mitigação: treinamento, doc 06).

**Fora de escopo (explícito):**
- Emissão fiscal (NFC-e/SAT) — o domínio observado não a detalha; seria fase própria.
- Integração com TEF/PinPad de fornecedores específicos.
- App mobile / gestor na nuvem para o lojista.
- Migração de dados do sistema legado (importador de `GestorLimpo`) — possível fase futura, precisa de decisão de negócio.

---

## 13. Critérios de aceite da Fase 0

- [x] Diagrama de componentes com fronteiras de confiança
- [x] Tabela comparativa herdado → Landipay com A1–A6 e justificativa por linha
- [x] Stack escolhida com alternativas descartadas
- [x] STRIDE para instalador, updater e acesso remoto
- [x] Decisões novas marcadas (LD-01…LD-06)
- [ ] **Aprovação no GATE 1** ← bloqueia Fase 1


---

## Atualização — Separação App Admin × App Caixa

**Estado:** implementada e verificada em execução (build 14 projetos / 0 erros; 89 testes verdes naquele momento — a suíte hoje é **94/94**, ver `docs/05 §2`).

### Dois aplicativos, um backend, um banco

| Item | Admin | Caixa (Pos) |
|---|---|---|
| Executável | `Landipay.Admin.exe` | `Landipay.Pos.exe` |
| Tela inicial | Dashboard de relatórios | Frente de caixa |
| Navegação | Início · Catálogo · Relatórios · Tema · Sair | Tema · Sair |
| Telas carregáveis | Login, Relatórios, Produtos, Acesso negado | Login, Caixa, Acesso negado |
| Papéis aceitos | Admin, Gerente | Gerente, OperadorCaixa |

A UI compartilhada vive em `Landipay.Ui` (biblioteca WPF). Os dois executáveis são **shells finos**:
cada um monta o `ComposicaoApp` com um `Aplicativo` (`Admin` ou `Pos`), e é isso que decide a
navegação inicial, o escopo do login e os `DataTemplate`s disponíveis. Não existe um terceiro
modo "tudo junto" — o executável antigo (`Landipay.Desktop`) foi removido.

### Decisão de implantação: Opção A com capacidade de estação

**Escolhido: Opção A** (o MSI entrega os dois executáveis, cada um com atalho próprio), **combinada
com o registro da capacidade da máquina** (Opção B como metadado, não como instalação parcial).

- *Segurança*: o pacote não muda por estação, então a superfície de atualização é uma só; o
  controle real é autenticação + autorização no serviço — a escolha da estação nunca é o que
  impede alguém de abrir o outro app.
- *Operação*: a loja pode instalar uma vez e depois mudar de ideia (habilitar o Caixa numa máquina
  que só tinha Admin) sem reinstalar arquivos.
- *Suporte*: um único MSI para diagnosticar; a capacidade fica auditada (`instalacao.aplicativos`).
- *Atualização*: ambos os binários vêm no mesmo pacote, com hash por arquivo no manifesto.

O registro (`apps.json` no diretório de dados) é **conveniência e rastreabilidade**, não segurança.
Um cliente malicioso que edite o arquivo não ganha nada: o serviço continua negando pelo papel.

### Papéis como cidadãos de primeira classe

`Admin`, `Gerente`, `OperadorCaixa` — definidos em `Landipay.Domain.Seguranca.CatalogoDeAcesso`,
persistidos em `Papeis`/`Permissoes`/`PapelPermissoes`/`UsuarioPapeis`, e exigidos **no serviço**
(`ServicoDeAutorizacao.Exigir`), nunca só na tela. Matriz completa em `docs/01-modelo-dados.md`.

### Diagrama

```
        ┌──────────────────┐        ┌──────────────────┐
        │  Landipay.Admin  │        │   Landipay.Pos   │
        │  (WinExe)        │        │   (WinExe)       │
        └────────┬─────────┘        └────────┬─────────┘
                 │  ambos referenciam        │
                 └──────────┬────────────────┘
                    ┌───────▼────────┐
                    │  Landipay.Ui   │  Views · ViewModels · ComposicaoApp(Aplicativo)
                    └───────┬────────┘
                    ┌───────▼────────┐
                    │ Landipay.Client│  autenticação · autorização · caixa · relatórios
                    └───────┬────────┘
                    ┌───────▼────────┐        ┌──────────────────┐
                    │  Infra (EF)    │───────▶│ SQLite landipay.db│
                    └────────────────┘        └──────────────────┘
```

### Pendência declarada

O JWT de sessão com `sub/papel/perms/app/exp/jti` e os endpoints protegidos por permissão
pertencem à API (Fase 2). Hoje a API expõe apenas licença, update, CNPJ e suporte — **não há
endpoint de dados de loja para proteger**. Quando o sync existir, o modelo a implementar é o do
`docs/02-backend.md` (seção "Pendência: JWT de sessão na API").
