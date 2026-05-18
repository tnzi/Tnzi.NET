import type { ColumnDef } from '../../../headless/useColumnSettings'

export const threadColumns: ColumnDef[] = [
  { key: 'id',             title: 'columns.id' },
  { key: 'title',          title: 'columns.title' },
  { key: 'userName',       title: 'columns.userName' },
  { key: 'agentName',      title: 'columns.agentName' },
  { key: 'messageCount',   title: 'columns.messageCount' },
  { key: 'totalTokens',    title: 'columns.totalTokens' },
  { key: 'lastActiveTime', title: 'columns.lastActiveTime' },
]
