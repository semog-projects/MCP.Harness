# Tool `harness_complete_task`

Passo 4 do ciclo de vida: concluir a task.

## Assinatura

```
harness_complete_task(
  owner: string, repo: string, issueNumber: number,
  comment?: string, projectNumber?: number
)
```

## O que faz

1. Resolve o board e localiza o item da Issue (erro se ela não estiver no
   board).
2. Grava `Status = Done` (sempre — barato e idempotente).
3. Se a Issue **ainda estiver aberta**:
   - posta `comment` (se dado) como comentário de encerramento;
   - fecha a Issue com `state_reason = completed`.
4. Se a Issue **já estiver fechada**: para por aí — não recomenta nem
   re-fecha.

## Saída

- Aberta → `✅ #<n> concluída: Status = Done e Issue fechada (completed) [· comentário postado]`
- Já fechada → `ℹ️ #<n> já estava fechada. Garanti o Status = Done`
