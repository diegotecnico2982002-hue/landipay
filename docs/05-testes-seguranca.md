# 05 — Testes e segurança

> Fase 5. Este documento não lista intenção: lista **o que foi executado**, com o comando,
> o resultado e o que ainda não está coberto. Onde não há prova, está escrito que não há.

Estado nesta máquina (11/11/2026):

```
dotnet build -c Release        → 13 projetos, 0 erros, 0 avisos
dotnet test                    → 94 aprovados, 0 falhas
varredura de segredos (publish) → 0 achados em 116 arquivos / 77,8 MB
```

---

## 1. Matriz A1–A6 com evidência

Cada anti-padrão do sistema legado, o que o Landipay faz no lugar e **onde isso pode ser verificado**.

| # | Anti-padrão legado | Landipay | Prova |
|---|---|---|---|
| **A1** | Senha fixa de acesso remoto embutida no instalador (RustDesk/AnyDesk) | Nenhum agente remoto é instalado. Suporte é sessão opt-in com código de 6 dígitos, TTL 30 min, uso único, encerramento auditado | `ServicoDeSuporte` + testes `Suporte_*`; instalador não tem canal remoto (grep de `RustDesk`/`AnyDesk`/`Process.Start` = 0) |
| **A2** | Tokens Basic/Bearer hardcoded no binário | Segredo só em variável de ambiente no servidor (`LANDIPAY_ADMIN_KEY`); cliente não fala com terceiros usando chave própria — CNPJ passa pelo backend | Varredura de segredos (§3): 0 achados; `Program.cs` lê só de env |
| **A3** | Baixa e executa binário sem verificar nada | Manifesto assinado **Ed25519** + SHA-256 + `kid`; updater valida antes de gravar, e nunca chama `Process.Start` | `Landipay.Updates` + testes de manifesto adulterado/assinatura inválida |
| **A4** | Connection string por concatenação + SQL interpolado | Config externa (`LANDIPAY_DADOS`, `LANDIPAY_API_DB`) e 100% EF Core parametrizado | Varredura de SQL concatenado no fonte: 0 ocorrências; logs do EF mostram `@p0`, `@__pedido_*` |
| **A5** | "Login" que não autentica | Argon2id (m=64 MB, t=3, p=4), lockout progressivo 5→5 min / 10→30 min, sessão 15 min ociosa / 12 h absoluta, primeiro acesso define a senha | `ServicoDeAutenticacao` + testes de lockout, expiração e senha errada; UI executada (login real) |
| **A6** | Logs enviados ao fornecedor sem consentimento nem TLS | Log local sempre (30 dias), envio **opt-in explícito** e anonimizado; nada sai sem consentimento gravado | `EnvioDeLogOptIn` + testes de consentimento; `consentir` recusa sem opt-in |

---

## 2. Inventário de testes (94)

| Arquivo | Testes | Cobre |
|---|---|---|
| `ModeloDeDadosTests.cs` | 11 | invariantes do domínio: totais, desconto > subtotal, venda sem item, turno fechado, CNPJ |
| `Fase2BackendTests.cs` | 20 | licença ES256, manifesto Ed25519, CNPJ com cache, suporte, JWT expirado/adulterado |
| `Fase3ClienteTests.cs` | 15 | Argon2id, lockout, sessão, caixa (abertura/venda/sangria/fechamento), updater, preferências |
| `Fase4InstaladorTests.cs` | 12 | passos idempotentes, licença offline (4 recusas), admin, auditoria da instalação |
| `Fase4AplicativosTests.cs` | 11 | passo 6: escolha de apps, escopo da licença, idempotência da regravação, negativa auditada |
| `Fase5RelatoriosTests.cs` | 8 | somas do relatório, forma de pagamento, produtos, sangria/suprimento, diferença de caixa, limite de período, loja isolada |
| `SegregacaoAdminCaixaTests.cs` | 11 | matriz papel × permissão, Admin sem caixa, negativa auditada, `apps` por papel |
| `RateLimitingTests.cs` | 2 | 429 com `Retry-After` em política sensível; saúde ilimitada |
| `Landipay.Desktop.Tests` | 4 | primeiro acesso cria admin + semeia catálogo; senhas divergentes; senha curta; login inválido |
| **Total** | **94** | 90 em `Landipay.Tests` + 4 em `Landipay.Desktop.Tests` |

Comando único: `dotnet test -v q --nologo`.

> **Estado da suíte (11/11/2026):** 94/94 verdes, reexecutado depois da correção do P0-1
> (ver `docs/08`). As duas falhas que apareceram nesta data vinham de `BancoDeTeste.Semear`
> gravar o turno com `DateTimeOffset.UtcNow` enquanto a Fase 5 filtra o período em torno de uma
> data fixa — o teste passava no dia da âncora e quebrava no dia seguinte. `Semear` agora aceita
> o instante (`Semear(db, Base)`), e a Fase 5 passa a âncora explicitamente.

### 2.1 O que os testes **não** cobrem

- Clique físico e renderização de XAML: validados por execução manual com captura de tela (Fases 3, 4 e 5), não por teste automatizado.
- Concorrência real de dois caixas na mesma loja (não há teste de carga nem teste multi-instância).
- Falha de energia no meio de uma venda (a transação do EF cobre o caso lógico, não foi simulada queda de energia).
- Restore de backup do SQLite em outra máquina (procedimento documentado, não testado).

---

## 3. Varredura de segredos no binário

Critério de aceite declarado no README desde a Fase 0: **zero segredo no binário**.

### 3.1 Método

1. `dotnet publish` em Release do cliente e do instalador (self-contained não publicado; framework-dependent).
2. Varredura byte a byte de **todos** os arquivos publicados, com 10 padrões: chave privada PEM, `password=`/`pwd=`, `Bearer` literal, `Basic` literal, chave AWS, `sk-…`, PAT GitHub, JWT (`eyJ…`), atribuição de senha/token literal, URL com credencial embutida (`scheme://user:pass@`).

### 3.2 Resultado

```
arquivos varridos: 116
bytes varridos   : 77.879.411
achados          : 0
```

Nenhum JWT, nenhuma chave privada, nenhuma connection string com senha, nenhuma credencial embutida em URL.

### 3.3 Controle positivo (o scanner funciona?)

Varredura que não acha nada pode estar simplesmente quebrada. Por isso o mesmo scanner foi
apontado para um arquivo com segredo plantado (`Senha = "SenhaFixaDoLegado2019"`) e **detectou**.
Resultado: `controle positivo detectado: True`. O zero da seção anterior é ausência de segredo,
não ausência de detector.

### 3.4 Segunda camada: código-fonte

Varredura de `src/**/*.cs,xaml` por senha/token literal e IP fixo: 94 ocorrências, **todas**
literais de versão (`1.0.0.0`, `8.0.34.0`) em arquivos gerados em `obj/` — casamento falso do
padrão de IPv4. Zero atribuição de senha ou token.

Exceção declarada: `LandipayDbContextFactory` tem `Data Source=landipay-dev.db` como *fallback*
de design-time (`dotnet ef`). Não é credencial, não vai para o binário do cliente (projeto
Infrastructure é referenciado pelo Desktop, mas a fábrica só é usada pela CLI do EF) e não abre
porta em runtime.

---

## 4. Rate limiting (medido, não configurado no papel)

| Política | Limite | Onde | Teste |
|---|---|---|---|
| `publico` | 60/min por IP | licença, CNPJ, update, saúde | — |
| `sensivel` | 5/min por IP | `/v1/suporte/*`, `/v1/licencas/validar` | `RateLimitingTests` (7 POSTs → 5×403 + 2×429) |
| `admin` | 30/min por IP | rotas com `X-Admin-Key` | — |
| saúde | ilimitado | `/v1/saude` | `RateLimitingTests` (70 GETs → 70×200) |

Resposta de bloqueio: HTTP 429 + `Retry-After: 60` + `{"erro":"limite_de_requisicoes"}`.
Justificativa do `sensivel` em 5/min: o código de suporte tem 6 dígitos (1.000.000 combinações);
com 5 tentativas/min e TTL de 30 min, o teto é 150 tentativas = 0,015% do espaço.

---

## 5. Modelo de ameaças (STRIDE) — resultado

Resumo do que foi mitigado; o modelo completo está em `docs/00-arquitetura.md` §6.

| Ameaça | Vetor no legado | Mitigação Landipay | Residual |
|---|---|---|---|
| **S**poofing | login que aceita qualquer senha | Argon2id + lockout + auditoria de falha | roubo de senha por phishing (fora do escopo do app) |
| **T**ampering | ZIP baixado e executado | manifesto Ed25519 + SHA-256 validados antes de executar | comprometimento da chave privada do emissor |
| **R**epudiation | nenhuma trilha de quem fez o quê | `LogAuditoria` append-only (triggers abortam UPDATE/DELETE) | operador usando login de outro (há trilha, não há biometria) |
| **I**nformation disclosure | logs com dados da loja enviados ao fornecedor | log local; envio opt-in + anonimização | disco da loja sem criptografia (BitLocker é política de TI) |
| **D**enial of service | — | rate limiting por política; caixa funciona offline | DDoS no backend de licença |
| **E**levation of privilege | instalador elevava para instalar engine | `asInvoker`, sem serviço, sem engine; papéis e permissões no domínio | conta Windows do operador comprometida |

### 5.1 Riscos residuais assumidos (conscientes)

1. **Sem Authenticode** no executável/instalador — depende de certificado de assinatura de código.
2. **Licença sem revogação online** — o cliente valida offline; revogação exige próxima conexão (`ToleranciaOfflineDias` já previsto).
3. **SQLite local sem criptografia** — proteção de disco é responsabilidade do sistema operacional/loja.
4. **Backup não automatizado** — o procedimento é copiar o arquivo com o app fechado (guia de operação).
5. **Relatórios carregam o período em memória** — decisão consciente: o EF sobre SQLite não traduz `DateTimeOffset` para SQL, e filtro traduzido pela metade devolveria número errado. Para o volume de uma loja é irrelevante; acima de ~centenas de milhares de vendas/mês, o caminho é coluna de data indexada.

---

## 6. Como reproduzir esta auditoria

```bash
# 1. build e testes
dotnet build -c Release -v q --nologo
dotnet test  -v q --nologo

# 2. publicar e varrer
OUT="C:/temp/pub-auditoria"
dotnet publish src/Landipay.Desktop   -c Release -o "$OUT/desktop"   --nologo
dotnet publish src/Landipay.Installer -c Release -o "$OUT/installer" --nologo
# (script de varredura: python, padrões da seção 3.1, com controle positivo)

# 3. conferir ausência de canal remoto / execução de binário
grep -rniE "rustdesk|anydesk|Process\.Start|WebClient|DownloadFile" src/ --include=*.cs
```

Resultado esperado do passo 3: **nenhuma** ocorrência em código de produção
(`Process.Start` existe apenas em teste/ferramenta, se existir).


---

## Atualização — Testes de segregação App Admin × Caixa

**Suíte:** 89 testes verdes naquele momento (`85` em `Landipay.Tests` + `4` em `Landipay.Desktop.Tests`; hoje **94/94**), build de
14 projetos com 0 erros e 0 avisos.

### `SegregacaoAdminCaixaTests` (serviço, sem UI)

| Teste | O que prova |
|---|---|
| `OperadorCaixa_nao_le_relatorio` | `ServicoDeRelatorios.Gerar` lança `AcessoNegadoException` com código `relatorio.ler` |
| `Admin_nao_opera_caixa` | `ServicoDeCaixa.RegistrarMovimento` nega `caixa.movimentar` ao papel Admin |
| `OperadorCaixa_movimenta_caixa` | mesma chamada passa para quem tem o papel |
| `Admin_lanca_movimento` | permissão explícita de `caixa.movimentar` libera (papel composto) |
| `Gerente_entra_nos_dois_apps` | `PodeAcessarApp` = Admin + Pos |
| `Operador_nao_entra_no_admin` | login no app Admin falha com papel de caixa |

Ponto central: os testes chamam **o serviço direto**, sem passar por ViewModel — é a prova de que
esconder botão não é o controle de acesso.

### `Fase4AplicativosTests` (instalador)

| Teste | O que prova |
|---|---|
| `Sem_licenca_nao_configura_estacao` | passo 6 exige licença; nada é gravado |
| `Nenhum_app_marcado_e_recusado` | validação de escolha vazia |
| `Somente_caixa_grava_estacao_pos` | `apps.json` com `Tipo=Pos` e `["pos"]` + máquina |
| `Escolha_e_auditada_com_o_tipo_da_estacao` | evento `instalacao.aplicativos` com descrição e máquina |
| `Reconfigurar_substitui_sem_duplicar_arquivo` | idempotência: um único `apps.json`, escolha substituída |
| `Reparo_nao_pede_senha_quando_administrador_ja_existe` | reparo não recria usuário nem exige senha |

### Verificação manual (executada, com evidência)

1. Admin (`admteste`, papel Admin) autenticou no **Landipay Admin** → dashboard de relatórios.
2. Admin tentou autenticar no **Landipay Caixa** → negado na tela e registrado:
   `acesso.negado {"app":"Pos","papeis":"Admin","motivo":"login"}`.
3. Operador (`operador`, papel OperadorCaixa) autenticou no **Caixa** → cabeçalho sem Catálogo e
   sem Relatórios.
4. Instalador em reparo → passo 6 escolhendo "somente Admin" → `apps.json` gravado e
   `instalacao.aplicativos` na auditoria.

### Checklist atualizado

- [x] Segregação Admin × Caixa validada **no serviço**, não só na UI
- [x] Sessão carrega o escopo de aplicativo (`Aplicativo` + `Papeis`)
- [x] Auditoria de acesso negado (`acesso.negado`)
- [x] Instalador oferece e aplica a escolha de aplicativos (passo 6)
- [x] Reinstalação idempotente; adicionar o segundo app não quebra o primeiro
- [x] Mockups das 5 telas novas em escuro **e** claro (capturados do binário)
- [ ] JWT de sessão na API com `perms`/`app` (API ainda não expõe dados de loja)
- [ ] Licença declarando `apps` e validação por requisição
- [ ] Manifesto de update com hash por pacote (dois binários)


### Testes do escopo de licença (5 novos, 94 no total)

| Teste | O que prova |
|---|---|
| `Licenca_sem_claim_apps_libera_os_dois` | compatibilidade: token legado não restringe |
| `Licenca_so_de_caixa_impede_habilitar_admin` | recusa + `apps.json` não gravado + auditoria `instalacao.aplicativos.negado` |
| `Licenca_so_de_caixa_permite_estacao_de_caixa` | escolha dentro do escopo passa |
| `Estacao_mista_e_recusada_quando_licenca_e_so_de_admin` | "ambos" fora do escopo é recusado |
| `Licenca_grava_o_escopo_no_banco` | normalização (duplicata/caixa alta) e gravação em `AppsHabilitados` |

Total: **94 testes verdes** (90 + 4).
