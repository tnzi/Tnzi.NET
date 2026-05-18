import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

interface AuditOperationRow {
  id?: string
  functionName?: string
  permissionName?: string
  userId?: string
  userName?: string
  httpMethod?: string
  startTime?: string
  elapsed?: number
  resultType?: string
}

function methodType(m?: string): 'info' | 'success' | 'warning' | 'error' | 'default' {
  switch (m?.toUpperCase()) {
    case 'GET': return 'info'
    case 'POST': return 'success'
    case 'PUT':
    case 'PATCH': return 'warning'
    case 'DELETE': return 'error'
    default: return 'default'
  }
}

export const auditOperationColumns: ColumnDef<AuditOperationRow>[] = [
  { key: 'functionName', title: 'columns.functionName', width: 220, fixed: 'left' },
  { key: 'permissionName', title: 'columns.permissionName', width: 200 },
  { key: 'userName', title: 'columns.userName', width: 140 },
  {
    key: 'httpMethod',
    title: 'columns.httpMethod',
    width: 90,
    render: (row) =>
      h(TStatusBadge, {
        value: row.httpMethod ?? '?',
        type: methodType(row.httpMethod),
        label: row.httpMethod ?? '—',
      }),
  },
  { key: 'elapsed', title: 'ms', width: 90 },
  {
    key: 'resultType',
    title: 'columns.resultType',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.resultType ?? 'unknown',
        type:
          row.resultType === 'Success' ? 'success' :
          row.resultType === 'Fail' ? 'error' : 'default',
        label: row.resultType ?? '—',
      }),
  },
  {
    key: 'startTime',
    title: 'columns.startTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.startTime }),
  },
]

// Read-only view schema — shown in detail view only, not create/edit
export const auditOperationFormSchema: FormSchemaItem[] = [
  { key: 'functionName',      label: 'Operation',         type: 'text' },
  { key: 'permissionName',    label: 'Permission',        type: 'text' },
  { key: 'userId',            label: 'Operator',          type: 'text' },
  { key: 'ip',                label: 'IP',                type: 'text' },
  { key: 'httpMethod',        label: 'Method',            type: 'text' },
  { key: 'url',               label: 'URL',               type: 'text' },
  { key: 'httpStatusCode',    label: 'Status Code',       type: 'number' },
  { key: 'elapsed',           label: 'Duration (ms)',     type: 'number' },
  { key: 'resultType',        label: 'Result',            type: 'text' },
  { key: 'message',           label: 'Message',           type: 'textarea' },
  { key: 'exception',         label: 'Exception',         type: 'textarea' },
  { key: 'requestParameters', label: 'Request Params',    type: 'textarea' },
  { key: 'responseResult',    label: 'Response',          type: 'textarea' },
]
