import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

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

function methodType(method?: string): 'info' | 'success' | 'warning' | 'error' | 'default' {
  switch (method?.toUpperCase()) {
    case 'GET': return 'info'
    case 'POST': return 'success'
    case 'PUT':
    case 'PATCH': return 'warning'
    case 'DELETE': return 'error'
    default: return 'default'
  }
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
    fixed: 'left',
    render: (row) =>
      h(TStatusBadge, {
        value: row.method ?? '?',
        type: methodType(row.method),
        label: row.method ?? '—',
      }),
  },
  { key: 'path', title: 'columns.path' },
  {
    key: 'statusCode',
    title: 'columns.statusCode',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.statusCode ?? 0,
        type: statusType(row.statusCode),
        label: row.statusCode ? String(row.statusCode) : '—',
      }),
  },
  { key: 'responseTime', title: 'columns.responseTime', width: 130 },
  { key: 'ipAddress', title: 'columns.ipAddress', width: 140 },
  { key: 'userName', title: 'columns.userName', width: 140 },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

// Read-only view schema — form is shown for detail view only, not create/edit
export const accessLogFormSchema: FormSchemaItem[] = [
  { key: 'path',        label: 'Path',        type: 'text' },
  { key: 'method',      label: 'Method',      type: 'text' },
  { key: 'statusCode',  label: 'Status Code', type: 'number' },
  { key: 'responseTime', label: 'Duration',   type: 'number' },
  { key: 'requestBody', label: 'Payload',     type: 'textarea' },
  { key: 'responseBody', label: 'Response',   type: 'textarea' },
]
