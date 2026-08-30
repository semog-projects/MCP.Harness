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

1. **Já vinculado ao repo?** Se sim, não cria nada e reporta — idempotente.
2. **Existe no owner mas não vinculado?** (link falhou antes, ou o board foi
   criado fora do harness.) Procura nos Projects do owner um com o **título
   exato** esperado; se achar, tenta (re)vincular e devolve esse — **não cria
   um segundo**.
3. **Senão, cria:** resolve o template, copia (`copyProjectV2`) para o owner
   alvo, e vincula (`linkProjectV2ToRepository`).
4. Re-resolve o board e valida: campos `Status` / `Sprint` / `Story Points`
   presentes; calendário de `Sprint` herdado do template (datas no passado).

O vínculo é **best-effort**: se `linkProjectV2ToRepository` falhar por
permissão (típico de PAT **fine-grained** — a mutation costuma exigir um
token **clássico** com scope `project`), o Project **não é descartado**. A
saída marca `NÃO vinculado` e traz o comando manual:

```
gh project link <número> --owner <owner> --repo <repo>
```

(ou UI do repo → *Projects* → *Link a project*). Rode `harness_bootstrap` de
novo depois — o passo 2 reconhece o board e confirma o vínculo.

## Saída

`✅ Board criado e vinculado` / `⚠️ Board criado, mas NÃO vinculado` /
`ℹ️ Board já vinculado` / `⚠️ Board já existe mas NÃO está vinculado`, mais
número e URL do Project, campos, sprint atual, lista de sprints e avisos.

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

> Owner de organização e de usuário são suportados. A resolução sonda
> `organization` e `user` na mesma query GraphQL; o GitHub reporta a
> alternativa inexistente como erro parcial `NOT_FOUND`, que é tolerado
> quando há `data` utilizável.
