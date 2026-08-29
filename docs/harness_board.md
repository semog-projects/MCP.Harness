# Tool `harness_board` / Resource `harness://board/...`

Passo 1 do skill: ler o board antes de criar qualquer coisa.

## Tool

```
harness_board(owner: string, repo: string, sprint?: string, projectNumber?: number)
```

- `sprint` vazio → sprint corrente (a iteração que contém hoje). Se não
  houver corrente, mostra **todos** os itens (`sprint = "(todas)"` na saída).
- Agrupa por `Status` na ordem das opções do board; valores fora do padrão
  viram colunas extras no fim.
- Cada item: `#número`, título, link, `Story Points`, assignees, e o estado
  quando não estiver `open`.
- Soma `Story Points` por coluna e no total.

Saída: Markdown com uma seção por coluna.

## Resources

| URI                              | Repo                                   |
| -------------------------------- | ------------------------------------- |
| `harness://board/{owner}/{repo}` | o do template                          |
| `harness://board/current`        | `Harness:DefaultRepo` / `HARNESS_DEFAULT_REPO` (`owner/repo`) |

Payload: o mesmo snapshot, em **JSON** (`projectNumber`, `projectUrl`,
`sprint`, `columns[]` com `status` / `items[]` / `storyPoints`, mais
`itemCount` e `storyPoints` no topo). Sempre a sprint corrente — para outra
sprint use a tool.

Sem `Harness:DefaultRepo` configurado, `harness://board/current` devolve um
JSON `{ "error": "..." }` apontando para o template.
