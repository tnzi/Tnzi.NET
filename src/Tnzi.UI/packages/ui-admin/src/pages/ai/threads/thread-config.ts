import { h } from 'vue'
import { formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { FormSchemaItem } from '../../_shared/form-schema'

type DateLike = string | null | undefined

/**
 * Threads list columns. Threads are log-style data produced by conversations,
 * so the page renders a table (not cards). The Agent column prefers the
 * resolved `agentName` (added to the list DTO server-side) and falls back to
 * a truncated `agentId` for rows the backend hasn't enriched. Timestamps go
 * through `formatDateTime` (locale-aware, null-safe `—` fallback).
 */
export const threadColumns: ColumnDef[] = [
  { key: 'title', title: 'columns.title', width: 220, primary: true, ellipsis: { tooltip: true } },
  {
    key: 'agentName',
    title: 'columns.agent',
    width: 200,
    ellipsis: { tooltip: true },
    render: (row) => {
      const name = (row as { agentName?: string | null }).agentName
      if (name) return name
      const id = (row as { agentId?: string | null }).agentId
      if (!id) return '—'
      const full = String(id)
      const short = full.length > 8 ? `${full.slice(0, 8)}…` : full
      return h('span', { title: full, class: 'font-mono' }, short)
    },
  },
  { key: 'messageCount', title: 'columns.messageCount', width: 110, align: 'right' },
  {
    key: 'lastActivityTime',
    title: 'columns.lastActivityTime',
    width: 170,
    render: (row) => formatDateTime(row.lastActivityTime as DateLike, { fallback: '—' }),
  },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 170,
    render: (row) => formatDateTime(row.creationTime as DateLike, { fallback: '—' }),
  },
]

/**
 * Search fields. Free-text keyword maps to `ThreadListQueryDto.keyword`
 * (matches on title); the optional Agent ID narrows by `agentId`.
 */
export const threadSearchFields: FormSchemaItem[] = [
  {
    key: 'keyword',
    labelKey: 'search.keyword',
    label: 'Keyword',
    type: 'text',
    placeholderKey: 'search.keywordHint',
  },
  {
    key: 'agentId',
    labelKey: 'search.agentId',
    label: 'Agent ID',
    type: 'text',
    placeholderKey: 'search.agentIdHint',
  },
]
