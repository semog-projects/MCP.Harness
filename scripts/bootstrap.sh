#!/usr/bin/env bash
# bootstrap.sh — cria um GitHub Project (v2) para um repo novo, copiando os
# campos padronizados (Status, Sprint, Story Points) de um Project-template
# já configurado, em vez de recriar cada campo do zero.
#
# Requisitos: gh CLI autenticado (gh auth login) com escopo 'project'.
# Requisito único, feito uma vez: um Project marcado como template
# (gh project mark-template) com os campos Status/Sprint/Story Points já
# configurados — inclusive o campo Sprint (Iteration), que só dá pra criar
# pela UI (ver README.md).
#
# Uso:
#   ./bootstrap.sh <owner> <repo> ["Título do Project"]
#
# Configuração do template (ajuste aqui uma vez, ou exporte as env vars):
#   TEMPLATE_OWNER — dono do Project-template (default: semog-projects)
#   TEMPLATE_NUMBER — número do Project-template (default: 5)
#
# Exemplo:
#   ./bootstrap.sh semog-projects children_tasks "children_tasks Sprints"
#   TEMPLATE_NUMBER=7 ./bootstrap.sh fernando-dev fundobase

set -euo pipefail

OWNER="${1:?informe o owner (usuário ou organização)}"
REPO="${2:?informe o nome do repositório}"
TITLE="${3:-"$REPO Sprints"}"

TEMPLATE_OWNER="${TEMPLATE_OWNER:-semog-projects}"
TEMPLATE_NUMBER="${TEMPLATE_NUMBER:-7}"

echo "==> Copiando o template #$TEMPLATE_NUMBER ($TEMPLATE_OWNER) para $OWNER"
PROJECT_NUMBER=$(gh project copy "$TEMPLATE_NUMBER" \
  --source-owner "$TEMPLATE_OWNER" \
  --target-owner "$OWNER" \
  --title "$TITLE" \
  --format json | jq -r '.number')
echo "    Project #$PROJECT_NUMBER criado em $OWNER, com Status/Sprint/Story Points já configurados."

echo "==> Vinculando Project ao repositório"
gh project link "$PROJECT_NUMBER" --owner "$OWNER" --repo "$REPO"

echo ""
echo "Concluído. Project #$PROJECT_NUMBER pronto em:"
echo "  https://github.com/$( [ "$(gh api /users/$OWNER --jq .type 2>/dev/null || echo User)" = "Organization" ] && echo orgs || echo users)/$OWNER/projects/$PROJECT_NUMBER"
echo ""
echo "Confira o campo 'Sprint' (Iteration): a cópia às vezes preserva o"
echo "calendário de ciclos do template em vez de começar um novo — se as"
echo "datas não fizerem sentido para este repo, ajuste manualmente na UI."
echo ""
echo "O CLAUDE.md e o SKILL.md já valem para este repo automaticamente"
echo "(ficam em ~/.claude/, não precisam ser copiados). Confirme apenas que"
echo "o GitHub MCP Server está conectado com os toolsets 'issues' e 'projects'."
