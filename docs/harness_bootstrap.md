# Tool `harness_bootstrap`

Cria o board (GitHub Project v2) de um repositório a partir do
Project-template padronizado do harness e o vincula ao repositório.

## Assinatura

```
harness_bootstrap(owner: string, repo: string, title?: string)
```

| Parâmetro | Obrigatório | Default              | Descrição                              |
| --------- | ----------- | ------------------- | -------------------------------------- |
| `owner`   | sim         | —                   | Owner do repo alvo (org ou usuário).    |
| `repo`    | sim         | —                   | Nome do repositório alvo.               |
| `title`   | não         | `"<repo> Sprints"`  | Título do Project criado.               |

Template de origem (configurável):

| Config                    | Env                       | Default          |
| ------------------------- | ------------------------- | ---------------- |
| `Harness:TemplateOwner`   | `HARNESS_TEMPLATE_OWNER`  | `semog-projects` |
| `Harness:TemplateNumber`  | `HARNESS_TEMPLATE_NUMBER` | `7`              |

## O que faz

1. Lista os Projects v2 já vinculados ao repo. **Se já houver um** (único, ou
   um cujo título termina em "Sprints"), não cria nada e reporta o estado
   atual — a tool é **idempotente**.
2. Resolve o template (`GetProjectByNumberAsync`), o node id do owner alvo e
   o node id do repositório.
3. `copyProjectV2` — copia o template para o owner alvo com o título dado.
4. `linkProjectV2ToRepository` — vincula o Project novo ao repositório.
5. Re-resolve o board criado e valida: campos `Status` / `Sprint` /
   `Story Points` presentes, e se o calendário de iterações do `Sprint`
   parece herdado do template (datas no passado).

## Saída

Texto com: se criou ou já existia, número e URL do Project, campos, sprint
atual, lista de sprints não concluídas e eventuais avisos.

## Diferenças em relação ao `scripts/bootstrap.sh`

| Aspecto            | `bootstrap.sh`                                  | `harness_bootstrap`                                    |
| ----------------- | ---------------------------------------------- | ---------------------------------------------------- |
| Dependência        | binário `gh` autenticado (escopo `project`)     | só o token (`GITHUB_TOKEN` / config); nada de `gh`   |
| Mecanismo          | `gh project copy` + `gh project link`           | mutations `copyProjectV2` + `linkProjectV2ToRepository` |
| Idempotência       | nenhuma — sempre cria um Project novo           | detecta board já vinculado e não duplica              |
| Owner alvo         | aceita org ou usuário                           | idem (resolve org, senão usuário)                     |
| Validação          | imprime aviso fixo sobre o calendário de ciclos | valida campos obrigatórios + heurística de calendário |
| Descoberta da URL  | monta a URL na mão (`orgs/` vs `users/`)         | usa a URL devolvida pela API                          |

Comportamento equivalente: ambos partem do mesmo template, produzem um
Project com `Status` / `Sprint` / `Story Points` e o vinculam ao repo. O
`bootstrap.sh` segue no repo como referência e para uso sem o servidor MCP.
