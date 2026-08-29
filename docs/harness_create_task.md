# Tool `harness_create_task`

Passo 2 do ciclo de vida do harness: transforma um pedido de trabalho em
Issue rastreada no board.

## Assinatura

```
harness_create_task(
  owner: string, repo: string, title: string, body: string,
  type?: "Task" | "Bug" | "Feature",   // default: "Task"
  storyPoints?: number,                 // omitir se ainda não há estimativa
  assignees?: string[],                 // default: o usuário do token
  projectNumber?: number                // só se o repo tiver mais de um board
)
```

## O que faz

1. Resolve o board do repo e a sprint corrente (iteração que contém hoje).
2. **Dedup**: lista as Issues abertas do repo (dados ao vivo, não a busca —
   que é eventualmente consistente) e procura título idêntico (ignorando
   caixa e espaços nas pontas).
   - **Achou** → não cria nada. Se a Issue não estiver no board, adiciona em
     `Backlog`; se já estiver, não mexe no `Status` dela. Devolve essa Issue.
   - **Não achou** → cria a Issue com o `type` informado.
3. Adiciona ao Project, `Status = Backlog`, `Sprint` = sprint corrente (se
   houver) e `Story Points` (se informado).
4. **Assignees**: define `assignees` (ou, sem eles, o usuário do token —
   `viewer.login`). Na Issue nova vai já no `create`; numa Issue reaproveitada
   só assina se **ninguém** estiver assinado (respeita atribuição manual).

## Saída

Texto com: se criou ou reaproveitou, número/URL da Issue, número do Project
+ id do item + `Status = Backlog`, **Assignees** e a sprint.

## Notas

- A dedup varre até 5 páginas de 100 Issues abertas. Repos com centenas de
  Issues abertas além disso podem furar a dedup — improvável num board de
  sprint.
- A listagem REST do GitHub pode levar 1–3 s para refletir uma Issue
  recém-criada; duas chamadas em milissegundos podem criar duplicata. No uso
  real (humano/agente pedindo de novo) a janela não é problema.
