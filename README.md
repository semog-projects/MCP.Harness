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
| `harness_create_task`  | ✅     | Cria a Issue, adiciona ao Project, `Status = Backlog`, `Sprint` atual e `Assignees` (default: usuário do token). Dedup por título. |
| `harness_move_task`    | ✅     | Move o `Status` de um item (`Backlog` / `Todo` / `Doing` / `Done`); valida a opção. |
| `harness_complete_task`| ✅     | Define `Status = Done` e fecha a Issue com `state_reason = completed`. Idempotente. |
| `harness_board`        | ✅     | Snapshot da sprint (default: corrente), agrupado por `Status` com soma de `Story Points`. |

### Resources

| Resource                            | Estado | Conteúdo                                             |
| ----------------------------------- | ------ | -------------------------------------------------- |
| `harness://board/{owner}/{repo}`    | ✅     | Snapshot JSON da sprint corrente do board do repo.  |
| `harness://board/current`           | ✅     | Idem, para o repo padrão (`Harness:DefaultRepo`).   |
| `harness://config`                  | ✅     | Configuração efetiva + fonte do token (sem o valor). |

## Stack

- **.NET 10** / C#
- SDK oficial [`ModelContextProtocol`](https://github.com/modelcontextprotocol/csharp-sdk) para C#
- Transporte **stdio** (padrão para Claude Code); HTTP/SSE opcional
- Acesso ao GitHub via **GraphQL** (Projects v2) + **REST** (Issues),
  autenticando com PAT (`GITHUB_TOKEN`) ou com o token do `gh` CLI

## Configuração

Duas fontes, nesta ordem de precedência: **variáveis de ambiente** →
**`appsettings.json`** (ao lado do binário) → defaults.

| Env                       | Chave (`appsettings.json`) | Default            | Uso                                          |
| ------------------------- | -------------------------- | ----------------- | -------------------------------------------- |
| `GITHUB_TOKEN` / `GH_TOKEN` | `GitHub:Token`           | —                 | PAT com escopos `repo`, `project`, `read:org` |
| —                         | `GitHub:RestBaseUrl`       | `https://api.github.com/` | base REST (troque para GitHub Enterprise) |
| —                         | `GitHub:GraphQlUrl`        | `https://api.github.com/graphql` | endpoint GraphQL              |
| `HARNESS_TEMPLATE_OWNER`  | `Harness:TemplateOwner`    | `semog-projects`  | dono do Project-template v2                   |
| `HARNESS_TEMPLATE_NUMBER` | `Harness:TemplateNumber`   | `7`               | número do Project-template v2                 |
| `HARNESS_DEFAULT_REPO`    | `Harness:DefaultRepo`      | —                 | `owner/repo` do resource `harness://board/current` |

O token nunca é logado nem exposto. O resource **`harness://config`** mostra
a configuração efetiva e a *fonte* do token (`env GITHUB_TOKEN`, `gh CLI`, …),
nunca o valor. Ver [`docs/configuracao.md`](docs/configuracao.md).

Tools já implementadas:
[`harness_bootstrap`](docs/harness_bootstrap.md) (assinatura e diferenças
vs `scripts/bootstrap.sh`),
[`harness_create_task`](docs/harness_create_task.md),
[`harness_move_task`](docs/harness_move_task.md),
[`harness_complete_task`](docs/harness_complete_task.md) e
[`harness_board`](docs/harness_board.md).

Erros de domínio (status inválido, Issue fora do board, token sem escopo…)
voltam como texto `❌ …` no resultado da tool, não como falha crua.

### Registrar no Claude Code

**Consumir sem clonar o repo** — pacote NuGet via `dnx` (precisa do .NET 10 SDK):

```jsonc
// .mcp.json (na raiz do repo que vai usar o harness)
{
  "mcpServers": {
    "harness": {
      "command": "dotnet",
      "args": ["dnx", "MCP.Harness", "--yes"],
      "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
    }
  }
}
```

**Sem .NET na máquina** — imagem de container (GHCR):

```jsonc
{
  "mcpServers": {
    "harness": {
      "command": "docker",
      "args": ["run", "-i", "--rm", "-e", "GITHUB_TOKEN",
               "ghcr.io/semog-projects/mcp-harness:latest"]
    }
  }
}
```

Ou baixe o binário do SO na [página de Releases](https://github.com/semog-projects/MCP.Harness/releases).
Publicação: [`docs/publicacao.md`](docs/publicacao.md).

**Durante o desenvolvimento deste repo** — `dotnet run`:

```jsonc
// .mcp.json (na raiz do repo que vai usar o harness)
{
  "mcpServers": {
    "harness": {
      "command": "dotnet",
      "args": ["run", "--project", "/caminho/para/MCP.Harness/src/MCP.Harness/MCP.Harness.csproj"],
      "env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" }
    }
  }
}
```

Build local self-contained: `dotnet publish src/MCP.Harness/MCP.Harness.csproj
-c Release -r <RID> -o <destino>` e aponte `command` para `<destino>/mcp-harness`.
O `appsettings.json` publicado ao lado do binário carrega
`Harness:TemplateOwner`, `Harness:DefaultRepo`, etc. sem env vars.

### Passo a passo num repo novo

1. Gere um PAT com escopos `repo`, `project`, `read:org` → `export GITHUB_TOKEN=…`.
2. Adicione o `.mcp.json` acima na raiz do repo.
3. Rode o harness uma vez: tool `harness_bootstrap` com `owner`/`repo` do repo
   novo — cria o Project v2 e vincula.
4. A partir daí, `harness_create_task` / `harness_move_task` /
   `harness_complete_task` / `harness_board`.

## Troubleshooting

| Sintoma                                                        | Causa provável / correção                                                  |
| ------------------------------------------------------------- | ------------------------------------------------------------------------- |
| `❌ … token sem permissão` / `token inválido ou expirado`      | PAT sem escopo `project` ou `read:org`, ou expirado. Gere outro.          |
| `❌ … rate limit do GitHub atingido`                          | Espere o horário do reset informado na mensagem.                          |
| `❌ Nenhum Project v2 vinculado a …`                          | Rode `harness_bootstrap` primeiro.                                        |
| `❌ … template #7 … pode estar desconfigurado`               | `HARNESS_TEMPLATE_OWNER`/`NUMBER` apontam para um Project sem os campos padrão. |
| `❌ Issue #N não está no board`                              | Use `harness_create_task` (ou adicione a Issue ao Project na UI).          |
| Nenhum token encontrado                                       | `export GITHUB_TOKEN=…`, ou `gh auth login`, ou preencha `GitHub:Token`.   |
| `harness://board/current` devolve `{ "error": … }`           | Defina `HARNESS_DEFAULT_REPO=owner/repo` ou use `harness://board/{owner}/{repo}`. |

## Desenvolvimento

```bash
dotnet build
dotnet test                                    # unidade
HARNESS_IT=1 GITHUB_TOKEN=$(gh auth token) \
  dotnet test --filter Category=Integration    # ponta-a-ponta (GitHub real)
dotnet run --project src/MCP.Harness
```

## Estrutura

```
src/MCP.Harness/           # host do servidor MCP: tools, resources, appsettings.json, .mcp/server.json
src/MCP.Harness.GitHub/    # cliente GitHub (GraphQL Projects v2 + REST Issues) + serviços do harness
tests/MCP.Harness.Tests/   # testes de unidade e integração
docs/                      # uma página por tool + configuracao.md + publicacao.md
Dockerfile                 # imagem stdio (GHCR)
.github/workflows/         # ci.yml (build+test) · release.yml (tag v* → NuGet/GHCR/Release)
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
