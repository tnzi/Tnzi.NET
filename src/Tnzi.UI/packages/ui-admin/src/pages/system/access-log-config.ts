import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'
import { methodTone as methodType } from '../_shared/http-method'

interface AccessLogRow {
  id?: string
  path?: string
  method?: string
  statusCode?: number
  responseTime?: number
  ipAddress?: string
  userName?: string
  creationTime?: string
  requestBody?: string
  responseBody?: string
}

function statusType(code?: number): 'success' | 'info' | 'warning' | 'error' | 'default' {
  if (!code) return 'default'
  if (code < 300) return 'success'
  if (code < 400) return 'info'
  if (code < 500) return 'warning'
  return 'error'
}

export const accessLogColumns: ColumnDef<AccessLogRow>[] = [
  {
    key: 'method',
    title: 'columns.method',
    width: 90,
    render: (row) =>
      h(TStatusBadge, {
        value: row.method ?? '?',
        type: methodType(row.method),
        label: row.method ?? EMPTY_DASH,
      }),
  },
  { key: 'path', title: 'columns.path', minWidth: 200 },
  {
    key: 'statusCode',
    title: 'columns.statusCode',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.statusCode ?? 0,
        type: statusType(row.statusCode),
        label: row.statusCode ? String(row.statusCode) : EMPTY_DASH,
      }),
  },
  {
    key: 'responseTime',
    title: 'columns.responseTime',
    width: 120,
    render: (row) => (row.responseTime != null ? `${row.responseTime} ms` : EMPTY_DASH),
  },
  { key: 'ipAddress', title: 'columns.ipAddress', minWidth: 120 },
  { key: 'userName', title: 'columns.userName', minWidth: 120 },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 150,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

// Read-only view schema - form is shown for detail view only, not create/edit.
// Field set mirrors backend AccessLog entity (request/response body are NOT
// persisted server-side, so we surface the geo + UA fields instead, which the
// AccessLogMiddleware actually populates).
/**
 * Three blocks, because a log entry answers three separate questions: what was
 * requested, who requested it, and from what. Flat, eleven fields read as one
 * undifferentiated column and the reader has to re-derive that grouping on
 * every entry they open.
 */
export const accessLogFormSections: FormSchemaSection[] = [
  { key: 'request', labelKey: 'admin.shared.formSections.value', label: 'Request', icon: 'mdi:swap-horizontal' },
  { key: 'who', labelKey: 'admin.shared.formSections.identity', label: 'Origin', icon: 'mdi:account-outline' },
  { key: 'client', labelKey: 'admin.shared.formSections.connection', label: 'Client', icon: 'mdi:devices' },
]

export const accessLogFormSchema: FormSchemaItem[] = [
  { key: 'method', labelKey: 'form.method', label: 'Method', type: 'text', section: 'request' },
  { key: 'path', labelKey: 'form.path', label: 'Path', type: 'text', span: 'full', section: 'request' },
  { key: 'statusCode', labelKey: 'form.statusCode', label: 'Status Code', type: 'number', section: 'request' },
  { key: 'responseTime', labelKey: 'form.responseTime', label: 'Duration (ms)', type: 'number', section: 'request' },
  { key: 'userName', labelKey: 'form.userName', label: 'User', type: 'text', section: 'who' },
  { key: 'ipAddress', labelKey: 'form.ipAddress', label: 'IP Address', type: 'text', section: 'who' },
  { key: 'ipFullAddress', labelKey: 'form.ipFullAddress', label: 'IP Location', type: 'text', section: 'who' },
  { key: 'uaBrowser', labelKey: 'form.uaBrowser', label: 'Browser', type: 'text', section: 'client' },
  { key: 'uaOperatingSystem', labelKey: 'form.uaOperatingSystem', label: 'OS', type: 'text', section: 'client' },
  { key: 'uaDeviceType', labelKey: 'form.uaDeviceType', label: 'Device Type', type: 'text', section: 'client' },
  { key: 'userAgent', labelKey: 'form.userAgent', label: 'User Agent', type: 'textarea', section: 'client' },
]
