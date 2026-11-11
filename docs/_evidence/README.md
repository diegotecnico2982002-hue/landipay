# `docs/_evidence/` — evidências brutas do pipeline

Saídas de comando, sem edição, na ordem em que foram produzidas. Cada arquivo é a resposta a um
item de gate; o mapa item → arquivo está em [`pipeline-log.txt`](pipeline-log.txt) e em
[`../2026-11-11-PDV-Landipay-implementacao.md`](../2026-11-11-PDV-Landipay-implementacao.md).

| Arquivo | Etapa / gate | O que prova |
|---|---|---|
| `listagem.txt` | 1 / G1.2 | 252 arquivos da instalação do legado (somente leitura) |
| `ilspy-tree.txt` | 1 / G1.2 | metadados de PE reais + árvore de namespaces do instalador decompilado; registra que o `ilspycmd` não instala no SDK 8.0.428 |
| `grep-legado.txt` | 1 / G1.4 | A1/A2/A4 no legado — **valores de segredo mascarados** de propósito |
| `etapa2-T1-build.txt` | 2 / G2.1 | build Release: 13 projetos, 0 avisos, 0 erros |
| `etapa2-T5-temas.txt` | 2 / G2.5 | 23 `x:Key` em cada tema, 0 diferenças |
| `etapa3-T2-test.txt` | 3 / G3.2 | suíte completa (102 + 6) |
| `etapa3-T3-gate3-sqlite.txt` | 3 / G3.3–G3.6 | tabelas, triggers, ABORT em UPDATE/DELETE, rollback de venda+outbox |
| `etapa4-T1-gate4-gate2.txt` | 4 / G4.7 | greps de rede no Backend e cor literal em XAML |
| `etapa4-T2-licenca-update.txt` | 4 / G4.2 | 40 testes de licença/update + algoritmos usados |
| `etapa5-T3-fluxo-real.txt` | 5 / G5.3 | execução real do fluxo: admin, login, turno, venda, fechamento, relatório, auditoria |
| `gate5-fluxo-real.db` | 5 / G5.3 | banco SQLite gerado por aquela execução (auditável com `sqlite3`) |
| `etapa5-T5-commands.txt` | 5 / G5.5 | 17 Commands declarados, 17 com botão, 0 órfãos |
| `etapa6-T4-auditoria-greps.txt` | 2/6 | chave privada, rede e SQL concatenado |
| `etapa6-T4-audit-scan.txt` / `.json` | 6 / G6.4 | 110 arquivos, 79.517.265 bytes, **0 achados** |
| `etapa6-T5-controle-positivo.txt` / `.json` | 6 / G6.4 | canary `password=CANARY123` **detectado** |
| `etapa7-T6-msi-ciclo.txt` | 6 / G6.6 | desinstalar → conferir preservação → reinstalar |
| `final-run.txt` | final / GF.7 | build + testes + varredura + ciclo de MSI numa única execução |
| `pipeline-log.txt` | todas | log consolidado, gate por gate |

## Sobre credenciais nestes arquivos

- `gate5-fluxo-real.db` contém o usuário `diego.guilherme` com hash **Argon2id** de uma senha de
  teste criada pelo runner (`Landipay#2026`) para exercitar o login real. Não é credencial de
  produção e o banco não sai desta pasta.
- `grep-legado.txt` preserva o **nome** das chaves encontradas no legado (A4) e mascara os valores:
  o arquivo é versionado, e uma senha real em repositório vira o problema que a etapa 1 mandou
  corrigir.
- `audit-scan.json` nunca registra o segredo completo, só os 6 primeiros caracteres da ocorrência.
