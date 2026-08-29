# Tool `harness_move_task`

Passo 3 do ciclo de vida: mover o `Status` da task conforme o trabalho
progride.

## Assinatura

```
harness_move_task(
  owner: string, repo: string, issueNumber: number, status: string,
  projectNumber?: number
)
```

`status` é casado sem diferenciar caixa contra as opções do campo `Status`
do board (`Backlog`, `Todo`, `Doing`, `Done`).

## O que faz

1. Resolve o board e valida `status` contra as opções do campo `Status`.
   Opção inválida → erro `❌` listando as opções válidas; nenhuma mutation é
   enviada.
2. Localiza o item da Issue no board (erro se ela não estiver lá).
3. Lê o `Status` atual e grava o novo.

**Não fecha a Issue** quando o alvo é `Done` — isso é
`harness_complete_task`.

## Saída

`✅ #<n>: Status <de> → <para> (Project #<k>, item <id>)`
