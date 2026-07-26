import { EMPTY_DASH } from '../../../utils/placeholders'
import { h } from 'vue'
import { formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'
import type {
  CreateMcpServerRegistrationDto,
  UpdateMcpServerRegistrationDto,
} from '@tnzi/core/services/ai'

/**
 * McpServers page config - EXTERNAL MCP server registrations (Tab 1 of the
 * rebuilt three-tab MCP page).
 *
 * Backed by entity-driven CRUD on AI_McpServerRegistration. Types come from
 * @tnzi/core/services/ai (McpServerRegistrationDto / Create… / Update…).
 *
 * SEMANTIC NOTE - these are EXTERNAL MCP servers Tnzi connects to as a
 * CLIENT. Tnzi's own self-hosted MCP server (exposing Tnzi's tools to external
 * MCP clients) is surfaced read-only in Tab 2 «This Server», driven by
 * bridge.mcp.getStatus(). The two are intentionally distinct.
 *
 * AuthToken tri-state semantic:
 *   - Create: authToken plaintext (encrypted at rest); optional.
 *   - Update: null/omitted = keep existing cipher; '' = clear; non-empty =
 *     rotate. The form leaves authToken blank in edit mode and uses the
 *     toUpdateDto adapter to drop the field when blank.
 */
export const mcpServerColumns: ColumnDef[] = [
  { key: 'name', title: 'columns.name', primary: true },
  { key: 'transport', title: 'columns.transport' },
  { key: 'serverUrl', title: 'columns.serverUrl' },
  { key: 'authType', title: 'columns.authType' },
  { key: 'priority', title: 'columns.priority' },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isEnabled),
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  { key: 'hasAuthToken', title: 'columns.hasAuthToken' },
  {
    key: 'lastModificationTime',
    title: 'columns.lastModificationTime',
    width: 160,
    render: (row) =>
      formatDateTime(
        (row as { lastModificationTime?: string | null }).lastModificationTime,
        { fallback: EMPTY_DASH },
      ),
  },
]

/**
 * Allowed transports for runtime-registered servers. stdio is intentionally
 * absent - the backend rejects it (stdio = spawning a local process, which is
 * only allowed via deployment configuration AI:Mcp, never via the admin UI).
 */
export const mcpTransportOptions: Array<{ label: string; value: string }> = [
  { label: 'SSE', value: 'sse' },
  { label: 'Streamable HTTP', value: 'streamable-http' },
  { label: 'HTTP', value: 'http' },
]

export const mcpAuthTypeOptions: Array<{ label: string; value: string }> = [
  { label: 'None', value: 'none' },
  { label: 'Bearer', value: 'bearer' },
  { label: 'API Key', value: 'api-key' },
  { label: 'OAuth', value: 'oauth' },
]

/**
 * Maps an MCP transport string to a mdi icon name for the external-server
 * card grid. sse/http = a network stream. The stdio case only renders for
 * legacy rows created before the registry became HTTP-only.
 */
export function mcpTransportIcon(transport: string | null | undefined): string {
  switch ((transport ?? '').toLowerCase()) {
    case 'stdio':
      return 'mdi:console-line'
    case 'sse':
      return 'mdi:rss'
    case 'streamable-http':
    case 'http':
      return 'mdi:web'
    default:
      return 'mdi:server-network'
  }
}

/**
 * Form schema - HTTP-family transports only; the backend rejects stdio
 * registrations, so no command/arguments fields exist anymore.
 */
export const mcpServerFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'serverUrl',
    labelKey: 'form.serverUrl',
    label: 'Server URL',
    type: 'text',
    required: true,
    placeholder: 'https://...',
  },
  {
    key: 'transport',
    labelKey: 'form.transport',
    label: 'Transport',
    type: 'select',
    options: mcpTransportOptions,
    required: true,
  },
  { key: 'authType', labelKey: 'form.authType', label: 'Auth Type', type: 'select', options: mcpAuthTypeOptions },
  {
    key: 'authToken',
    labelKey: 'form.authToken',
    label: 'Auth Token',
    type: 'text',
    placeholder: 'Leave blank on edit to keep existing token',
  },
  { key: 'priority', labelKey: 'form.priority', label: 'Priority', type: 'number', min: 0 },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'tags',
    labelKey: 'form.tags',
    label: 'Tags',
    type: 'textarea',
    placeholder: 'One tag per line. Stored verbatim.',
  },
]

/**
 * Form -> CreateMcpServerRegistrationDto adapter. Trims string fields and
 * normalizes blank-strings to null so backend validation handles the
 * required/optional distinction consistently.
 */
export function toCreateMcpServerDto(form: Record<string, unknown>): CreateMcpServerRegistrationDto {
  const authToken = String(form.authToken ?? '').trim()
  return {
    name: String(form.name ?? '').trim(),
    serverUrl: String(form.serverUrl ?? '').trim(),
    transport: String(form.transport ?? '').trim(),
    authType: (form.authType as string | undefined) || null,
    authToken: authToken || null,
    priority: (form.priority as number | undefined) ?? 0,
    isEnabled: (form.isEnabled as boolean | undefined) ?? true,
    description: (form.description as string | undefined) || null,
    tags: (form.tags as string | undefined) || null,
  }
}

/**
 * Form -> UpdateMcpServerRegistrationDto adapter. Tri-state authToken:
 * blank means "keep existing", so we omit the field entirely; a non-empty
 * value rotates the encrypted token.
 */
export function toUpdateMcpServerDto(form: Record<string, unknown>): UpdateMcpServerRegistrationDto {
  const authTokenRaw = String(form.authToken ?? '')
  const dto: UpdateMcpServerRegistrationDto = {
    name: (form.name as string | undefined) ?? null,
    serverUrl: (form.serverUrl as string | undefined) ?? null,
    transport: (form.transport as string | undefined) ?? null,
    authType: (form.authType as string | undefined) ?? null,
    priority: (form.priority as number | undefined) ?? null,
    isEnabled: (form.isEnabled as boolean | undefined) ?? null,
    description: (form.description as string | undefined) ?? null,
    tags: (form.tags as string | undefined) ?? null,
  }
  if (authTokenRaw.trim().length > 0) {
    dto.authToken = authTokenRaw
  }
  return dto
}
