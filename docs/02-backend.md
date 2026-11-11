# 02 — Backend (ASP.NET Core)

> Documento da Fase 2. Estado: **concluída e verificada** — API no ar, endpoints exercitados com `curl`, 31 testes verdes (20 deles desta fase).
> Código: `src/Landipay.Api` (host), `src/Landipay.Backend` (serviços e dados), `src/Landipay.Updates` (assinatura/validação de manifesto).

---

## 1. Objetivo

Entregar os quatro serviços que o cliente precisa e que no legado eram **o problema**:

| Serviço | O que resolve | Anti-padrão |
|---|---|---|
| Licença assinada (JWT ES256, `kid` rotativo) | substitui "login que não autentica" como controle de uso | A5 |
| Manifesto de update assinado (Ed25519 + SHA-256) | substitui download-e-executa | A3 |
| Proxy de CNPJ com cache e fallback offline | tira a chave de terceiro do binário | A2 |
| Suporte remoto por sessão consentida (TTL 30 min) | substitui senha fixa de acesso remoto | A1 |

---

## 2. Estrutura

```
Landipay.Api        → host mínimo (Minimal APIs), só roteia e autentica
Landipay.Backend    → regras: licença, update, CNPJ, suporte; DbContext próprio
Landipay.Updates    → primitivas de assinatura/validação (compartilhado com o cliente futuro)
```

O host não contém regra de negócio: isso permite testar tudo em memória, sem subir servidor (é o que os 20 testes desta fase fazem).

---

## 3. Endpoints

| Método | Rota | Auth | Função |
|---|---|---|---|
| GET | `/v1/saude` | — | health check |
| POST | `/v1/licencas` | — | emite licença para uma instalação |
| POST | `/v1/licencas/validar` | — | valida token (server-side; o cliente valida offline) |
| GET | `/v1/updates/manifesto?canal=stable` | — | manifesto assinado do release mais recente |
| GET | `/v1/updates/chave-publica` | — | `kid` + chave pública Ed25519 (base64) |
| POST | `/v1/updates/releases` | `X-Admin-Key` | publica release (URL, SHA-256, tamanho, versão mínima) |
| GET | `/v1/cnpj/{cnpj}` | — | dados cadastrais com cache 24h; **503** quando indisponível |
| POST | `/v1/suporte/sessoes` | `X-Admin-Key` | cria sessão e devolve código de 6 dígitos |
| POST | `/v1/suporte/sessoes/{id}/consentir` | código do operador | ativa a sessão e devolve credencial de uso único |
| POST | `/v1/suporte/sessoes/{id}/encerrar` | admin ou cliente | encerra e audita |
| GET | `/v1/suporte/sessoes/{id}` | `X-Admin-Key` | estado da sessão (com expiração aplicada) |

**Chave administrativa:** lida de `LANDIPAY_ADMIN_KEY`; em `Development`, se ausente, é gerada em memória e impressa uma vez no log. Em produção, ausência **derruba o host** — não existe fallback "chave padrão".

---

## 4. Licença — JWT ES256 com rotação

```
POST /v1/licencas  { empresaId, instalacaoId, plano }
→ { token, expiraEm, kid, algoritmo: "ES256" }
```

Cabeçalho real do token emitido em teste:

```json
{"alg":"ES256","kid":"lic-101f375bf556","typ":"JWT"}
```

Claims: `sub` (instalação), `emp` (empresa), `plano`, `jti`, `iss=landipay-api`, `aud=landipay-client`, `nbf`, `exp`.

Decisões:
- **`kid` no cabeçalho + chave pública pinada no cliente**: rotacionar = publicar chave nova e manter a antiga na lista por 90 dias. Não quebra instalação antiga.
- **Validação offline**: o token é autocontido; o cliente verifica assinatura e `exp` sem rede. O servidor só é necessário para **renovar**. Isso é o que permite operar com a internet caída sem virar licença perpétua.
- **`MapInboundClaims = false`** na validação: sem isso o .NET traduz `sub` para `ClaimTypes.NameIdentifier` e o cliente lê `null` — foi exatamente um bug pego por teste nesta fase.
- **Separação de chaves**: `lic-*` (licença) ≠ `upd-*` (manifesto). Comprometer uma não compromete a outra.

Prova em execução:

```
POST /v1/licencas/validar → {"valida":true,"instalacao":"instalacao-loja-01",
                            "empresa":"1111...","plano":"pro","expiraEm":"2027-11-11T06:04:21+00:00"}
```

---

## 5. Manifesto assinado — A3 de ponta a ponta

### 5.1 Formato

```json
{
  "versao": "1.4.0",
  "canal": "stable",
  "url": "https://releases.landipay.example/landipay-1.4.0.zip",
  "sha256": "0d19dd47...a123",
  "tamanho": 23,
  "versaoMinima": "1.0.0",
  "publicadoEm": "2026-11-11T05:57:29.4208741+00:00"
}
```
Envelope: `{ manifesto, assinatura (base64 Ed25519), keyId, algoritmo }`.

### 5.2 Canonicalização

`JsonCanonico.Serializar` fixa: ordem de propriedade declarada, sem espaços, sem cultura, datas em ISO-8601 (`"O"`). Assinador e validador usam **a mesma função** — qualquer divergência derruba a verificação (e isso é bom: falha ruidosa em vez de aceitação silenciosa).

### 5.3 Ordem de verificação no cliente (`ValidadorDeManifesto`)

1. forma — `keyId` presente, URL **HTTPS**, SHA-256 de 64 hex, tamanho > 0 (barato, rejeita lixo antes de criptografia);
2. **assinatura Ed25519** sobre o JSON canônico, com a chave **pinada**;
3. versões — `versao >= versaoMinima` e `instalada <= disponivel` (**anti-downgrade**);
4. **hash do artefato** baixado, em streaming e ao final.

Nenhuma exceção escapa do validador: entrada malformada vira `ResultadoValidacao.Falha(codigo, descricao)`.

### 5.4 Provas

| Cenário | Resultado |
|---|---|
| manifesto íntegro, chave correta | `ok` |
| URL trocada depois da assinatura | `assinatura_invalida` |
| assinatura feita com outra chave | `assinatura_invalida` |
| instalada 2.0.0, manifesto 1.4.0 | `downgrade_bloqueado` |
| `http://` na URL | `url_insegura` (antes da criptografia) |
| artefato com hash diferente | `hash_divergente` |
| manifesto trafega em JSON e volta | assinatura continua válida (teste de ida e volta) |

Resposta real do servidor em execução:

```json
{"manifesto":{...},"assinatura":"E0hQVuU8usThOlGf5TZFxS2nCb6wUJnZP/nRRY8BJpwRaAN7ntt3CVyh51X4z2KGEG2cW5JZVTGKYe/XA5fKCw==","keyId":"upd-7835e1a9ce6c","algoritmo":"Ed25519"}
```

---

## 6. CNPJ — proxy, cache e fallback

- Fonte pública (`brasilapi.com.br/api/cnpj/v1/{cnpj}`), **sem token** — e, se um dia precisar de chave, ela fica aqui, nunca no cliente (A2).
- Cache em memória de **24h** por CNPJ; resposta repetida marca `deCache: true` e não vai à rede (testado: 1 chamada HTTP para 2 consultas).
- Falha de rede/serviço → `503 { offline: true }`. O cliente cai no **preenchimento manual** e o cadastro nunca bloqueia por falta de internet (LD-05).
- CNPJ com menos de 14 dígitos nem chama a API.
- Dado externo é tratado como não confiável: campos truncados, UF normalizada, CEP só dígitos.

---

## 7. Suporte remoto consentido — A1

Fluxo em quatro passos, com o controle **no servidor**:

```
1. técnico abre sessão           POST /v1/suporte/sessoes        (X-Admin-Key)
   → código de 6 dígitos, válido 30 min, uso único

2. operador da loja digita o código na tela    POST .../consentir
   → credencial aleatória (32 bytes), TTL renovado em 30 min

3. sessão termina por encerramento explícito ou por TTL

4. tudo registrado em AuditoriaApi (criada / consentida / encerrada)
```

O que impede o retorno do padrão legado:

| Regra | Implementação |
|---|---|
| Sem senha permanente | credencial aleatória por sessão, nunca reutilizada |
| Código não fica em claro | `CodigoHash` = SHA-256 do código; comparação em tempo constante |
| Uso único | `CodigoUsado` — segundo `consentir` devolve `codigo_ja_utilizado` |
| Expira sozinho | `TtlMinutos` (padrão 30, máximo 120); `Obter` converte para `Expirada` |
| Consentimento explícito | `ConsentidaEm` + evento de auditoria `suporte.consentido` |
| Encerramento auditado | `EncerradaPor` + evento `suporte.encerrado` |
| Sem canal permanente | nada é instalado como serviço auto-start; não existe "config embutida" |

Prova em execução (sessão real criada contra o servidor):

```
POST /v1/suporte/sessoes    → {"id":"7800277b-...","codigo":"786066","expiraEm":"2026-11-11T06:27:42Z"}
consentir com 000000        → HTTP 403
consentir com 786066        → {"credencial":"j1uULoia...","expiraEm":"2026-11-11T06:27:43Z"}
consentir de novo 786066    → HTTP 403 (uso único)
encerrar                    → {"encerrada":true}
```

---

## 8. Rate limiting (por IP, janela fixa de 1 minuto)

| Política | Limite | Onde |
|---|---|---|
| `publico` | 60/min | `/licencas`, `/updates/manifesto`, `/updates/chave-publica`, `/cnpj/{cnpj}` |
| `sensivel` | **5/min** | `/licencas/validar`, `/suporte/sessoes/{id}/consentir`, `.../encerrar` |
| `admin` | 30/min | `/updates/releases`, `/suporte/sessoes`, `GET /suporte/sessoes/{id}` |
| — | sem limite | `/v1/saude` (health check precisa responder sempre) |

Resposta ao estourar: **429** com `Retry-After: 60` e corpo `{ "erro": "limite_de_requisicoes" }`.

O caso mais importante é o `sensivel`: o código de suporte tem 6 dígitos (1.000.000 de combinações). Sem limite, um atacante com a sessão em mãos poderia varrer o espaço; com 5/min e TTL de 30 min, o teto é 150 tentativas — **0,015%** do espaço, e a sessão expira antes disso.

Comprovado por teste de integração (`TestServer`, 2 testes): 7 tentativas seguidas em `/consentir` → 5 respostas 403 e 2 respostas 429; `/v1/saude` responde 200 em 70 chamadas seguidas.

---

## 9. Segurança transversal

| Controle | Como |
|---|---|
| Nenhum segredo no código | chave admin por env; chaves de assinatura em arquivo gitignored (`chaves/`), KMS/HSM em produção |
| Sem fallback inseguro | fora de `Development`, ausência de `LANDIPAY_ADMIN_KEY` derruba o host |
| Comparação de chave em tempo constante | `CryptographicOperations.FixedTimeEquals` |
| Entrada externa não confiável | CNPJ parseado defensivamente; JSON inválido → `null`, nunca exceção 500 |
| Auditoria no backend | `AuditoriaApi` com ação, detalhe e ator (append-only por trigger na Fase 5) |
| Erros de validação não vazam stack | `ArgumentException` → 400 com código de erro |

---

## 10. Testes desta fase (33 no total; 22 novos)

| Grupo | Testes |
|---|---|
| Manifesto / A3 | 8 — assinatura válida, adulteração, chave alheia, downgrade, URL insegura, hash divergente, ida-e-volta JSON, publicação via serviço |
| Licença | 3 — emissão com `kid`/claims, token de outra chave, token expirado |
| Suporte / A1 | 5 — código errado, uso único, expiração por TTL (relógio falso), encerramento auditado, TTL acima do limite |
| CNPJ | 3 — preenchimento + cache, indisponibilidade → `null`, CNPJ inválido não chama API |
| Rate limiting | 2 — 429 após 5 tentativas em `/consentir`, health ilimitado |
| Modelo de dados (Fase 1) | 11 — mantidos verdes |

`dotnet test` → **Aprovado: 33, Falha: 0**.

---

## 10. O que mudou em relação ao legado

| Legado | Landipay | Anti-padrão |
|---|---|---|
| Token Basic/Bearer fixo no binário para logs e APIs | nenhuma credencial no cliente; chaves só no servidor | A2 |
| `Updater.exe` vindo em Base64 de um endpoint e executado | manifesto assinado, hash verificado, downgrade bloqueado | A3 |
| Senha fixa de RustDesk + `--config` embutido, aplicada no instalador | sessão consentida com código de uso único e TTL 30 min | A1 |
| "Login" só checava versão e iniciava log | licença assinada + authn real no cliente (Fase 3) | A5 |
| Chave de CNPJ dentro do app | proxy no backend com cache e fallback | A2 |

---

## 11. Limitações conhecidas

1. **`EnsureCreated()` em vez de migrations** no backend: schema entra por migrations na Fase 5, junto com os testes de segurança.
2. **Rate limiting por IP** (não por conta): suficiente para o cenário atual, mas atrás de proxy/CDN exige `ForwardedHeaders` configurado — senão todo mundo cai na mesma partição. Entra no guia de operação (Fase 5).
3. **Rotação de chave fica fora do código, por decisão de escopo**: é operação (KMS + lista de `kid` aceitos no cliente), não software desta entrega. O que existe em código é o suporte a múltiplos `kid`.
4. **SQLite** no backend de desenvolvimento; PostgreSQL 16 em produção (o EF troca o provider, o código não muda).
5. **Sem revogação online consultada a cada boot**: licença é offline-tolerante por design; revogação chega na próxima renovação (máx. 7 dias, conforme `ToleranciaOfflineDias` do modelo).


---

## Atualização — Escopo por aplicativo e proteção por permissão

**Estado:** parcial, declarado. O que existe hoje e o que falta está separado abaixo — sem
misturar "desenhado" com "implantado".

### O que a API expõe hoje

`/v1/saude`, `/v1/licencas` (emissão, `X-Admin-Key`), `/v1/update/*`, `/v1/cnpj/*`, `/v1/suporte/*`.
**Não há endpoint de dados de loja** (vendas, catálogo, relatórios) — esses dados vivem no SQLite
local. Consequência honesta: hoje não existe "endpoint de relatório" para responder 403, porque
relatório não passa pela API.

### O que foi implementado de verdade

- **Autorização na camada de serviço do cliente** (`ServicoDeAutorizacao.Exigir`): `ServicoDeCaixa`
  exige `caixa.movimentar`; `ServicoDeRelatorios` exige `relatorio.ler`. Um teste chama o serviço
  direto, sem UI, e comprova a negativa — "esconder botão" não é a proteção.
- **Escopo de aplicativo no login**: `ServicoDeAutenticacao` recebe o `Aplicativo` e recusa
  credencial válida em app não permitido por papel, gravando `acesso.negado`.
- **Licença por instalação** (ES256) validada offline com chave pública pinada.

### Pendência: JWT de sessão na API

Quando houver sync/cloud, o contrato a implementar é:

```
POST /v1/sessao  → { access_token }
claims: sub, papel, perms[], app ("admin"|"pos"), exp, jti
```

- Assinatura ES256 com rotação por `kid` (mesma infraestrutura de `ProvedorDeChaves`).
- Todo endpoint de dados valida `perms` **no servidor**; `app` restringe a superfície
  (`app=pos` não acessa `/v1/relatorios/*`).
- Negativa = `403` + evento `acesso.negado` com `jti`, rota e permissão exigida.
- Licença passa a declarar `apps: ["admin","pos"]` e a API valida esse vetor em cada requisição
  (hoje o teto é o papel do usuário + `apps.json` da estação).

Isso está listado como pendência no README e na nota de construção — não como entregue.


### Implementado depois: claim `apps` na licença

O emissor passou a carregar o escopo de aplicativos na licença:

```json
{ "sub": "<instalacaoId>", "emp": "<empresaId>", "plano": "pro",
  "apps": "[\"pos\"]", "exp": 1825926831, "jti": "..." }
```

- `POST /v1/licencas` aceita `apps: ["admin","pos"]`; ausente = ambos (`AppsPadrao`).
- Código desconhecido é **400 `apps_invalidos`** — não se emite licença mais permissiva por engano.
- A resposta devolve o escopo efetivo (`apps`), e a auditoria da API registra `apps=pos+admin`.
- O validador offline (`ValidadorDeLicencaJwt`) lê o claim e devolve `ResultadoLicenca.Apps`.
  Token antigo sem o claim continua válido e é tratado como "ambos" (compatibilidade explícita).
- Claim presente mas malformado → escopo **vazio** (falha fechada), nunca "libera tudo".

Verificado com `curl` no serviço local: licença `apps:["pos"]` devolveu `apps: ["pos"]` e o payload
decodificado trouxe `apps: ["pos"]`; pedido com `"estoque"` respondeu **400**.
