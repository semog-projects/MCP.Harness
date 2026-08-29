# Configuração e empacotamento

## Fontes de configuração

Precedência: **variáveis de ambiente** → **`appsettings.json`** (copiado ao
lado do binário) → defaults do código.

O host é `Host.CreateApplicationBuilder` com `ContentRootPath =
AppContext.BaseDirectory` — o `appsettings.json` é lido do diretório do
binário, não do cwd, então funciona iniciado de qualquer lugar (via
`.mcp.json`).

### Chaves

```jsonc
{
  "GitHub": {
    "Token": "",                       // prefira a env GITHUB_TOKEN
    "RestBaseUrl": "https://api.github.com/",
    "GraphQlUrl": "https://api.github.com/graphql",
    "AllowGhCliTokenFallback": true    // permite 'gh auth token' como última opção
  },
  "Harness": {
    "TemplateOwner": "semog-projects",
    "TemplateNumber": 7,
    "DefaultRepo": ""                  // owner/repo -> resource harness://board/current
  }
}
```

### Env vars equivalentes

| Env                        | Chave                     |
| -------------------------- | ------------------------- |
| `GITHUB_TOKEN` / `GH_TOKEN` | `GitHub:Token` (fallback do provider) |
| `HARNESS_TEMPLATE_OWNER`   | `Harness:TemplateOwner`    |
| `HARNESS_TEMPLATE_NUMBER`  | `Harness:TemplateNumber`   |
| `HARNESS_DEFAULT_REPO`     | `Harness:DefaultRepo`      |

Chaves aninhadas padrão do .NET também valem: `GitHub__RestBaseUrl=…`.

## Resolução do token

`GitHubTokenProvider`, em ordem:

1. `GitHub:Token` (config explícita)
2. env `GITHUB_TOKEN`, depois `GH_TOKEN`
3. `gh auth token` (se `AllowGhCliTokenFallback` e o binário existir)

Só a **descoberta** toca o `gh` — nenhuma operação do servidor usa o binário.
Sem token, a primeira chamada que precisa do GitHub falha com mensagem
acionável.

## `harness://config`

Resource JSON com a configuração efetiva e a **fonte** do token
(`config (GitHub:Token)`, `env GITHUB_TOKEN`, `env GH_TOKEN`, `gh CLI` ou
`nenhuma`) — nunca o valor.

## Empacotamento

```bash
# self-contained (não exige .NET na máquina alvo)
dotnet publish src/MCP.Harness/MCP.Harness.csproj -c Release \
  -r <RID> --self-contained -o <destino>
# RIDs: linux-x64, linux-arm64, osx-arm64, osx-x64, win-x64

# framework-dependent (menor; exige .NET 10 runtime)
dotnet publish src/MCP.Harness/MCP.Harness.csproj -c Release -o <destino>
```

O `appsettings.json` vai junto (`CopyToOutputDirectory=PreserveNewest`).
`.mcp.json` aponta `command` para `<destino>/mcp-harness`.
