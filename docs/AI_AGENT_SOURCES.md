# Fuentes para el monitor de agentes de IA

Investigación hecha para implementar `src/NotchWin.Core/AiAgents/*`. Ninguno de los tres
proveedores (Anthropic/Claude Code, OpenAI/Codex, GitHub/Copilot) expone una API pública para
"cuánto me queda de mi plan personal" — lo que existe es: (a) APIs de administración a nivel
organización, pensadas para billing, no para un usuario individual viendo su propio consumo, o
(b) nada en absoluto, con el propio CLI leyendo su estado local o pegándole a un endpoint interno
no documentado. Esto es consistente con lo que el propio README de Vorssaint dice de su función
"AI agents": sigue a "Claude, Codex, OpenCode y GitHub Copilot" mostrando "plan limits ... where
available" (no siempre disponibles) y aclara explícitamente que "Copilot uses local CLI/app
session logs" — o sea, el propio producto que estamos clonando admite que para Copilot no hay
API, solo logs locales.

## Claude Code

- **Transcripts locales**: `~/.claude/projects/<carpeta-por-proyecto>/<sessionId>.jsonl`. Cada
  línea es un evento; los turnos del asistente traen `message.usage` con
  `input_tokens`/`output_tokens`/`cache_creation_input_tokens`/`cache_read_input_tokens` y
  `message.model`. Esto es lo que usan herramientas de la comunidad como `ccusage` y lo que
  implementa `ClaudeCodeUsageProvider`.
- **Gotcha de deduplicación**: una sesión puede tener líneas repetidas para el mismo mensaje
  (reanudar/forkear una sesión). El provider dedupea por `message.id`.
- **% de plan Pro/Max y su reset**: **no documentado ni expuesto localmente**. El comando
  `/usage` del propio Claude Code sí lo muestra, pero contra un endpoint interno no público. Lo
  único público y documentado es la **Admin API** de Anthropic
  (`/v1/organizations/usage_report/claude_code`, `/v1/organizations/usage_report/messages`,
  `/v1/organizations/cost_report`), que requiere una API key de administrador y es para uso a
  nivel organización — no es la cuota de un plan personal Pro/Max. `AppSettings.AnthropicAdminApiKey`
  queda como hook para quien tenga ese caso de uso, pero no es el camino para un usuario
  individual.

## Codex CLI

- **Rollouts locales**: `$CODEX_HOME/sessions/YYYY/MM/DD/rollout-*.jsonl` (`$CODEX_HOME` por
  defecto es `~/.codex`; cada cuenta extra de ChatGPT/Codex usa su propio `CODEX_HOME`, que es
  justo cómo `CodexUsageProvider` soporta múltiples cuentas).
  Las líneas son eventos `event_msg`; cuando `payload.type == "token_count"`, el payload trae un
  contador **acumulado** de la sesión completa (no incremental: hay que tomar el último valor, no
  sumar todos los eventos) y, en builds recientes del CLI, un objeto `rate_limits` con ventanas
  `primary`/`secondary` (`used_percent`, `window_minutes`, `resets_in_seconds`) — esto es lo que
  alimenta el `/status` del propio CLI.
- **No documentado oficialmente**: el esquema exacto de `token_count`/`rate_limits` no tiene spec
  pública y ha cambiado entre versiones del CLI (algunos builds de 2025 emitían `token_count` sin
  `turn_context`). `CodexUsageProvider.ScanRolloutFile` por eso prueba varias formas conocidas
  (`info.total_token_usage`, `info` plano, el payload plano) y nunca falla por un campo ausente.

## GitHub Copilot

- **Logs locales**: `~/.copilot/session-state/<session-id>/events.jsonl` (o
  `~/.copilot/history-session-state/` en versiones viejas, antes de la v0.0.342). La documentación
  oficial de GitHub describe esta carpeta solo como historial de sesión para `--resume`/`--continue`
  — **no documenta ningún campo de tokens o de uso**.
- **No hay API de cuota personal**: el único endpoint oficial de uso (`GET
  /organizations/{org}/settings/billing/premium_request/usage`) es a nivel organización y requiere
  ser administrador. Existe un endpoint no oficial (`copilot_internal/user`) que terceros usan,
  pero no está documentado, puede desaparecer sin aviso y no encaja con el enfoque "solo archivos
  locales" del resto de este monitor — se decidió no usarlo.
- **Por eso `CopilotUsageProvider` es heurístico**: cuenta cualquier campo numérico cuyo nombre
  contenga "token" en los eventos de hoy, y marca el resultado con
  `AgentUsageSnapshot.IsEstimate = true`. Lo confiable (actividad en vivo, proyecto activo) viene
  de metadata de archivos (fecha de modificación, nombre de carpeta de sesión), no de parsear el
  contenido.

## Catálogo de precios (`PricingCatalog`)

Los "$" que se muestran junto a los tokens son una estimación con una tabla de precios que se
puede editar sin recompilar (`pricing.json` junto a `settings.json`, formato
`{ "modelo": { "InputPerMillion": 3, "OutputPerMillion": 15, ... } }`). Los valores por defecto
embebidos en el código son indicativos y casi seguro están desactualizados para cuando leas esto
— no se reintentó "adivinar" precios de modelos lanzados después del corte de conocimiento de
quien escribió este código. Un modelo que no está en la tabla simplemente no muestra costo
estimado, en vez de mostrar un número inventado.
