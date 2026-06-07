import { h } from 'vue'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'
import TStatusBadge from '../../../components/display/TStatusBadge.vue'

/**
 * Phase 5 Task 5.11 — McpServers page config.
 *
 * Backed by entity-driven CRUD on AI_McpServerRegistration table. Types come
 * from @tnzi/core/services/ai (McpServerRegistrationDto / Create... /
 * Update... / McpServerTestResultDto, added by Stage 3b backend prereq commit
 * 95a5401a).
 *
 * SEMANTIC NOTE — these are EXTERNAL MCP servers Tnzi connects to as a
 * client. Self-hosted MCP server settings (Tnzi exposing its own tools as an
 * MCP server) live in appsettings.json under McpServerOptions and are not
 * editable from this page. The page header callout reinforces this so admins
 * don't conflate the two.
 *
 * AuthToken tri-state semantic (per Stage 3b):
 *   - Create: authToken plaintext (encrypted at rest); optional.
 *   - Update: null/omitted = keep existing cipher; '' = clear; non-empty =
 *     rotate. The form leaves authToken blank in edit mode and uses the
 *     toUpdateDto adapter to drop the field when blank, matching the
 *     Providers apiKey pattern.
 *
 * Arguments / Tags serialization:
 *   Both fields are stored as a single string column on the entity (the
 *   backend treats them as opaque text blobs that downstream tooling can
 *   parse). The form accepts a newline-separated value for ergonomics; the
 *   page-level adapter joins/splits with '\n'. Operators who need JSON
 *   semantics can paste a JSON literal — the backend stores it verbatim.
 */
export const mcpServerColumns: ColumnDef[] = [
  { key: 'name', title: 'columns.name' },
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
  { key: 'lastModificationTime', title: 'columns.lastModificationTime' },
]

export const mcpTransportOptions: Array<{ label: string; value: string }> = [
  { label: 'stdio', value: 'stdio' },
  { label: 'SSE', value: 'sse' },
  { label: 'Streamable HTTP', value: 'streamable-http' },
]

export const mcpAuthTypeOptions: Array<{ label: string; value: string }> = [
  { label: 'None', value: 'none' },
  { label: 'Bearer', value: 'bearer' },
  { label: 'API Key', value: 'api-key' },
  { label: 'OAuth', value: 'oauth' },
]

/**
 * Form schema — `command` field uses the visible() predicate so it only
 * appears when transport === 'stdio'. TFormSchemaRenderer supports the
 * predicate (form-schema.ts line 56).
 */
export const mcpServerFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'serverUrl', labelKey: 'form.serverUrl', label: 'Server URL', type: 'text', required: true, placeholder: 'https://... or stdio://...' },
  { key: 'transport', labelKey: 'form.transport', label: 'Transport', type: 'select', options: mcpTransportOptions, required: true },
  {
    key: 'command',
    labelKey: 'form.command', label: 'Command (stdio only)',
    type: 'text',
    placeholder: 'e.g. node ./mcp-server.js',
    visible: (model) => (model.transport as string | undefined) === 'stdio',
  },
  {
    key: 'arguments',
    labelKey: 'form.arguments', label: 'Arguments',
    type: 'textarea',
    placeholder: 'One argument per line. Stored verbatim — JSON literals also accepted.',
  },
  { key: 'authType', labelKey: 'form.authType', label: 'Auth Type', type: 'select', options: mcpAuthTypeOptions },
  {
    key: 'authToken',
    labelKey: 'form.authToken', label: 'Auth Token',
    type: 'text',
    placeholder: 'Leave blank on edit to keep existing token',
  },
  { key: 'priority', labelKey: 'form.priority', label: 'Priority', type: 'number', min: 0 },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'tags',
    labelKey: 'form.tags', label: 'Tags',
    type: 'textarea',
    placeholder: 'One tag per line. Stored verbatim.',
  },
]

/**
 * i18n keys (Task 5.16 will sweep into tnzi.admin.modules.ai.mcp.*):
 *
 * Page meta:
 *   modules.ai.mcp.pageTitle
 *   modules.ai.mcp.headerNote
 *   modules.ai.mcp.testConnection
 *   modules.ai.mcp.testing
 *   modules.ai.mcp.testSuccess
 *   modules.ai.mcp.testFailure
 *   modules.ai.mcp.authTokenHint
 *
 * Columns:
 *   modules.ai.mcp.columns.name
 *   modules.ai.mcp.columns.transport
 *   modules.ai.mcp.columns.serverUrl
 *   modules.ai.mcp.columns.authType
 *   modules.ai.mcp.columns.priority
 *   modules.ai.mcp.columns.isEnabled
 *   modules.ai.mcp.columns.hasAuthToken
 *   modules.ai.mcp.columns.lastModificationTime
 *
 * Form fields:
 *   modules.ai.mcp.form.name
 *   modules.ai.mcp.form.serverUrl
 *   modules.ai.mcp.form.transport
 *   modules.ai.mcp.form.command
 *   modules.ai.mcp.form.arguments
 *   modules.ai.mcp.form.authType
 *   modules.ai.mcp.form.authToken
 *   modules.ai.mcp.form.priority
 *   modules.ai.mcp.form.isEnabled
 *   modules.ai.mcp.form.description
 *   modules.ai.mcp.form.tags
 */
