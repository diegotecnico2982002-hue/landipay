# Title
2026-11-11 — PDV Landipay: execução do pipeline de 8 etapas sobre o projeto vivo (auditoria + fechamento de lacunas)

# Summary
O pipeline autônomo (`REFERENCIA\instruções\1.txt..8.txt`) foi executado **de ponta a ponta sobre o
repositório existente**, não sobre uma cópia nova: o projeto real estava em **1.1.0 com 108 testes**,
enquanto o briefing descrevia uma arquitetura de 1.0.0 com 72 testes e 11 projetos de nomes
diferentes. Decisão tomada no início: **não refazer nem renomear nada**; cada gate foi revalidado
contra o estado real, e o que faltava de fato foi produzido.

**Fechado nesta execução (com prova):**

| Lacuna | O que foi feito | Prova |
|---|---|---|
| Scanner de segredos não existia no repo (só o resultado documentado) | `assets/audit-scan.py` — 10 padrões P1–P10, decodifica ASCII e UTF-16LE, `--canary` obrigatório | `_evidence/etapa6-T4-audit-scan.txt` (110 arquivos / 79.517.265 B / 0 achados) + `etapa6-T5-controle-positivo.txt` (**DETECTADO**) |
| Auditoria de Commands órfãos era grep manual | `assets/audit-commands.py` — lê `[RelayCommand]`, resolve `XxxAsync → XxxCommand`, compara com Binding no XAML | `_evidence/etapa5-T5-commands.txt` — **17 declarados, 17 com botão, 0 órfãos** |
| Fluxo de venda só existia em teste, sem cadeia de auditoria auditável | runner de execução real fora do repo (`scratch/e2e-runner`), migrations + Argon2id + `ServicoDeCaixa` + `ServicoDeRelatorios` de produção sobre SQLite novo | `_evidence/etapa5-T3-fluxo-real.txt` + banco `_evidence/gate5-fluxo-real.db` |
| Doc do MSI inexistente | `docs/05-msi.md` com SHA-256 real e ciclo instalar/desinstalar | `_evidence/etapa7-T6-msi-ciclo.txt` |
| Inferências espalhadas | `docs/07-inferencias.md` com o que **não** foi provado, marcado `(inferência)` | — |
| `docs/00-analise-reversa.md` ausente (só o 08 tinha o conteúdo) | doc de pipeline com telas, modelo inferido, catálogo A1–A6 com decisão e mapa de fluxos | `_evidence/{listagem,ilspy-tree,grep-legado}.txt` |
| 10 PNGs do gate com nomes diferentes | mapeados para capturas reais + `mockups/msi-instalado.png` capturado nesta sessão | `docs/mockups/INDEX.md` |

**Números reais no fecho:** build Release **0 erros / 0 avisos** em 13 projetos · **108 testes**
(102 + 6) · MSI `Landipay-1.1.0` **18.808.832 B** · SHA-256
`180B85957701365F09CBA145C37F284FB091364A97BCCEFC6DC87A46BB701A20` · varredura de segredos
**0 achados** com controle positivo detectado · MSI instalado **exit 0** (110 arquivos, ARP x64) e
desinstalado **exit 0** preservando `%APPDATA%\Landipay\landipay.db`.

# Source
- Instruções: `C:\Users\diego\Desktop\PDV - Landipay\REFERENCIA\instruções\{1..8}.txt`
- Repositório: `C:\Users\diego\landipay` (branch `main`, HEAD `8447daf`)
- Legado analisado (somente leitura): `C:\Program Files (x86)\Syntax Tecnologia\Syntax Plus PDV`
  e `Desktop\SyntaxInstaller-descompilado`
- Evidências: `docs/_evidence/` (11 arquivos + 1 banco + 2 capturas de tela)
- Validação única: `tools/final-run.sh` → `docs/_evidence/final-run.txt`
- Nota anterior: [[2026-11-11-PDV-Landipay-analise-reversa]]
- Análise de referência (fonte dos números do legado): [[2026-11-11-PDV-Landipay-analise-reversa]]

# Context
**Por que não refazer.** O briefing foi escrito quando o projeto tinha 72 testes e uma árvore de
projetos que evoluiu: `Landipay.Desktop` foi dividido em `Landipay.Ui` + `Landipay.Pos` +
`Landipay.Admin` (separação de papéis admin × caixa), e os testes viraram
`Landipay.Tests` + `Landipay.Desktop.Tests`. Renomear para casar com o texto do briefing
destruiria um estado verde (108 testes) em troca de conformidade nominal. Optou-se por
**revalidar por capacidade** e registrar o desvio.

**Desvios do briefing, com razão:**
1. **Arquitetura** — 11 projetos de produção com nomes `Ui`/`Pos`/`Admin` em vez de
   `Client`/`Desktop`. Capacidade equivalente, melhor isolamento (a tela de caixa não referencia
   relatórios nem cadastros; há teste de matriz papel × permissão).
2. **Ed25519** — o briefing pede `System.Security.Cryptography.Ed25519`, que **não existe no
   .NET 8** (chegou no 9). Usado **BouncyCastle 2.4** (`Ed25519PrivateKeyParameters`), com o
   teste de "outra chave rejeitada" verde.
3. **Rede em `Backend`** — `ServicoCnpj` usa `HttpClient` (HTTPS, BrasilAPI) dentro do Backend,
   o que o gate G4.7 lia como "0 rede fora da Api". É decisão de produto (o próprio briefing pede
   CNPJ por API com fallback manual) e tem teste de queda para modo manual sem rede.
4. **Contagem de tabelas** — 17 tabelas de domínio + `__EFMigrationsHistory` = 18 objetos no
   SQLite (o gate fala "17 tabelas": confere se a tabela de controle for excluída).
5. **MSI** — `1.1.0`, 110 arquivos, 17,9 MB (o briefing descreve `1.0.0`, 105 arquivos, 17,8 MB).
6. **Numeração de docs** — `00-analise-reversa.md`, `05-msi.md` e `07-inferencias.md` convivem com
   `00-arquitetura.md`, `05-testes-seguranca.md` e `07-design-system.md`; o índice do README
   distingue os dois conjuntos.

**Defeitos conhecidos do briefing — revalidados um a um:** `FecharTurnoCommand` tem botão
(17/17 sem órfão) · 0 `.pdb` no payload · MSI x64 fora do `WOW6432Node` · instalação `perUser`
sem erro 1925 · nenhum SQL concatenado (as duas `migrationBuilder.Sql` são literais) · subtítulo
do instalador não sobrepõe `v1.0` (captura do `instalador-reparo.png`).

**O que não foi feito:** árvore de namespaces dos binários do Gestor (ilspycmd não instala com o
SDK 8.0.428; registrado em `_evidence/ilspy-tree.txt`); nenhum binário do legado foi executado.

# Next Action
1. **Assinar o MSI com Authenticode** (OV/EV) — hoje o SmartScreen avisa em máquina de terceiro;
   ver `docs/07-inferencias.md §1.2`.
2. **Rodar `tools/final-run.sh` no CI** em vez de só build+teste: o gate de segurança e o ciclo de
   MSI hoje são locais (`ci.yml` cobre build e contagem de testes).
3. **Fechar as lacunas de domínio P1/P2** do `docs/08-referencia-e-gaps.md` (entrada de estoque,
   fiscal NFC-e) — são o que separa o Landipay do ERP do legado em cobertura.
4. **Medir relatórios com volume real** (100 mil vendas) para decidir se a coluna de data indexada
   deixa de ser inferência.
