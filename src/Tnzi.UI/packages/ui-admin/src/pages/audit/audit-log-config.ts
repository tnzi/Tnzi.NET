import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

interface AuditLogRow {
  id?: string
  userId?: string
  userName?: string
  functionName?: string
  url?: string
  httpMethod?: string
  ip?: string
  startTime?: string
  resultType?: 'Success' | 'Fail' | string
  elapsed?: number
}

export const auditLogColumns: ColumnDef<AuditLogRow>[] = [
  { key: 'userName', title: 'columns.userName', width: 160, fixed: 'left' },
  { key: 'functionName', title: 'columns.functionName', width: 200 },
  { key: 'url', title: 'columns.url' },
  { key: 'ip', title: 'columns.ip', width: 140 },
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
  { key: 'elapsed', title: 'columns.elapsed', width: 130 },
  {
    key: 'startTime',
    title: 'columns.startTime',
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.startTime }),
  },
]

// Read-only view schema — shown in detail view only, not create/edit
export const auditLogFormSchema: FormSchemaItem[] = [
  { key: 'userId',             label: 'User',             type: 'text' },
  { key: 'functionName',       label: 'Action',           type: 'text' },
  { key: 'url',                label: 'Resource',         type: 'text' },
  { key: 'httpMethod',         label: 'Method',           type: 'text' },
  { key: 'ip',                 label: 'IP',               type: 'text' },
  { key: 'resultType',         label: 'Result',           type: 'text' },
  { key: 'message',            label: 'Message',          type: 'textarea' },
  { key: 'exception',          label: 'Exception',        type: 'textarea' },
  { key: 'requestParameters',  label: 'Request Params',   type: 'textarea' },
]
