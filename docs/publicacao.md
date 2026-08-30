# Publicação e distribuição

O servidor é publicado de três formas por uma tag `vX.Y.Z`
(`.github/workflows/release.yml`):

| Destino          | Como o consumidor usa            | Exige do mantenedor            |
| ---------------- | -------------------------------- | ----------------------------- |
| **NuGet.org**    | `dotnet dnx MCP.Harness`         | policy de Trusted Publishing + var `NUGET_USER` |
| **GHCR**         | `docker run … ghcr.io/…/mcp-harness` | nada (usa o `GITHUB_TOKEN`) |
| **GitHub Release** | baixa o binário do SO             | nada                          |

## Setup único

### 1. NuGet — Trusted Publishing (OIDC, sem API key)

Em **nuget.org/account/trustedpublishing** → *Add*:

| Campo             | Valor            |
| ----------------- | ---------------- |
| Publisher Name    | `mcp-harness-gha` (livre) |
| Package Owner     | seu usuário/org  |
| Repository Owner  | `semog-projects` |
| Repository        | `MCP.Harness`    |
| Workflow File     | `release.yml`    |
| Environment       | *(vazio)*        |
| Package glob      | `MCP.Harness*`   |

Depois, no repo: `Settings → Secrets and variables → Actions → Variables →
New variable` → **`NUGET_USER`** = seu username do nuget.org (não é segredo).
Sem essa variável o job de NuGet só emite um warning e segue (GHCR e Release
continuam). Não há secret de API key.

### 2. GHCR

Nada. Na 1ª publicação o pacote nasce privado; deixe público em
`github.com/orgs/semog-projects/packages` → `mcp-harness` →
*Package settings* → *Change visibility*.

## Publicar uma versão

```bash
git tag v0.1.0
git push origin v0.1.0
```

O workflow: sincroniza a versão no `.mcp/server.json`, `dotnet pack` +
`dotnet nuget push` autenticado por OIDC (`NuGet/login`, todas as RIDs),
`docker buildx` multi-arch para o GHCR, publica os binários por SO e cria a
Release com notas geradas.

Também dá pra rodar manualmente: *Actions → Release → Run workflow* com a
versão.

## Versionamento

A versão vem da tag (`v0.1.0` → `0.1.0`). O `.mcp/server.json` no repo fica
com uma versão de referência; o workflow reescreve na hora do pack. Pré-
lançamento: `v0.2.0-rc.1`.

## Registro MCP (opcional)

Com o `.mcp/server.json` no pacote, o servidor pode ser submetido ao
[MCP Registry](https://github.com/modelcontextprotocol/registry) para ficar
descobrível — o `name` já segue o formato `io.github.semog-projects/MCP.Harness`.
