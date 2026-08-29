# MCP.Harness

Servidor **MCP (Model Context Protocol)** em **.NET 10** que expõe o *sprint
harness* de engenharia como ferramentas para qualquer cliente MCP (Claude
Code, IDEs, agentes). Em vez de depender de scripts soltos (`bootstrap.sh`) e
de convenções que vivem apenas no `CLAUDE.md`, o MCP.Harness entrega o ciclo
de vida de tarefas — bootstrap do board, criação de Issues e transição de
Status — como *tools* versionadas, testáveis e reaproveitáveis entre repos.

## Contexto — o que é o sprint harness

O harness organiza todo trabalho não-trivial em torno de um **GitHub Project
(v2)** por repositório, com campos padronizados:

| Campo          | Tipo            | Valores                              |
| -------------- | --------------- | ------------------------------------ |
| `Status`       | single select   | `Backlog`, `Todo`, `Doing`, `Done`   |
| `Sprint`       | iteration       | ciclo/iteração atual (14 dias)       |
| `Story Points` | number          | estimativa de esforço                |

Ciclo de vida de uma task:

1. **Criação** — Issue real + item no Project, `Status = Backlog`.
2. **Início** — `Status = Todo` e, ao começar de fato, `Status = Doing`.
3. **Execução** — commits referenciam a Issue (`refs #N` / `Closes #N`).
4. **Conclusão** — `Status = Done` e Issue fechada. Trabalho interrompido
   fica em `Doing` com o estado registrado no corpo da Issue.

A **Issue é a fonte de verdade** — nunca arquivos `.md` soltos no repo.

## O que o servidor expõe

### Tools

| Tool                   | Estado | Descrição                                                                     |
| ---------------------- | ------ | --------------------------------------------------------------------------- |
| `harness_bootstrap`    | ✅     | Cria o Project v2 a partir do template padronizado e vincula ao repo (porta do `bootstrap.sh`). |
| `harness_create_task`  | ✅     | Cria a Issue, adiciona ao Project, `Status = Backlog` e a `Sprint` atual. Dedup por título. |
| `harness_move_task`    | ✅     | Move o `Status` de um item (`Backlog` / `Todo` / `Doing` / `Done`); valida a opção. |
| `harness_complete_task`| 🔜     | Define `Status = Done` e fecha a Issue com `state_reason = completed`.         |
| `harness_board`        | 🔜     | Lê os itens da sprint atual, com `Status`, `Story Points` e link da Issue.     |

### Resources

| Resource                   | Conteúdo                                            |
| -------------------------- | -------------------------------------------------- |
| `harness://board/current`  | Snapshot em JSON do board da sprint corrente.       |
| `harness://config`         | Configuração efetiva (owner/número do template).    |

## Stack

- **.NET 10** / C#
- SDK oficial [`ModelContextProtocol`](https://github.com/modelcontextprotocol/csharp-sdk) para C#
- Transporte **stdio** (padrão para Claude Code); HTTP/SSE opcional
- Acesso ao GitHub via **GraphQL** (Projects v2) + **REST** (Issues),
  autenticando com PAT (`GITHUB_TOKEN`) ou com o token do `gh` CLI

## Configuração

Variáveis de ambiente:

| Variável                  | Default           | Uso                                          |
| ------------------------- | ----------------- | -------------------------------------------- |
| `GITHUB_TOKEN`            | —                 | PAT com escopos `repo`, `project`, `read:org` |
| `HARNESS_TEMPLATE_OWNER`  | `semog-projects`  | dono do Project-template v2 (`Harness:TemplateOwner`) |
| `HARNESS_TEMPLATE_NUMBER` | `7`               | número do Project-template v2 (`Harness:TemplateNumber`) |

Tools já implementadas:
[`harness_bootstrap`](docs/harness_bootstrap.md) (assinatura e diferenças
vs `scripts/bootstrap.sh`),
[`harness_create_task`](docs/harness_create_task.md) e
[`harness_move_task`](docs/harness_move_task.md).

Erros de domínio (status inválido, Issue fora do board, token sem escopo…)
voltam como texto `❌ …` no resultado da tool, não como falha crua.

### Registrar no Claude Code

```jsonc
// .mcp.json
{
  "mcpServers": {
    "harness": {
      "command": "dotnet",
      "args": ["run", "--project", "src/MCP.Harness/MCP.Harness.csproj"],
      "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
    }
  }
}
```

Em produção, publique um binário (`dotnet publish -c Release`) e aponte
`command` para o executável.

## Desenvolvimento

```bash
dotnet build
dotnet test
dotnet run --project src/MCP.Harness
```

## Estrutura (planejada)

```
src/MCP.Harness/           # host do servidor MCP + definição das tools
src/MCP.Harness.GitHub/    # cliente GitHub (GraphQL Projects v2 + REST Issues)
tests/MCP.Harness.Tests/   # testes de unidade e integração
scripts/bootstrap.sh       # script legado — referência para a tool harness_bootstrap
```

### Camada de acesso ao GitHub

`src/MCP.Harness.GitHub` isola toda a conversa com o GitHub e é registrada
com `services.AddHarnessGitHub(configuration)`:

- `IssuesClient` (REST) — criar/ler/fechar Issue, comentar.
- `ProjectsV2Client` (GraphQL) — resolver o board de um repo, ler campos e
  opções (`Status` / `Sprint` / `Story Points`), adicionar item, atualizar
  valor de campo (single-select, iteration, number) e remover item.
- `GitHubClient` — fachada com o atalho `PlaceOnBoardAsync` (add + status +
  sprint + pontos).
- `GitHubTokenProvider` — resolve o token: `GitHub:Token` → `GITHUB_TOKEN` /
  `GH_TOKEN` → `gh auth token`. Erros da API viram `GitHubApiException` com
  mensagem acionável (escopo faltando, rate limit, 404).

Os testes de integração (`Category=Integration`) batem no GitHub real e só
rodam com `HARNESS_IT=1` e `GITHUB_TOKEN` no ambiente:

```bash
HARNESS_IT=1 GITHUB_TOKEN=$(gh auth token) dotnet test --filter Category=Integration
```

## Relação com o `bootstrap.sh`

`scripts/bootstrap.sh` copia um Project-template v2 (com os campos
`Status` / `Sprint` / `Story Points` já configurados) para um novo owner e
vincula ao repositório, usando o `gh` CLI. A tool `harness_bootstrap`
replica exatamente esse fluxo pela API do GitHub, sem depender do `gh`
instalado na máquina do cliente:

1. `gh project copy <template> --source-owner … --target-owner …`
   → mutation `copyProjectV2`
2. `gh project link <n> --owner … --repo …`
   → mutation `linkProjectV2ToRepository`
3. valida o campo `Sprint` (iteration) e reporta o calendário de ciclos.

## Roadmap

**Sprint 1 (atual)** — fundação: scaffold do projeto, camada de acesso ao
GitHub, tool de bootstrap e CRUD de tasks.

Board: <https://github.com/orgs/semog-projects/projects/9>
