import type { ColumnDef } from '../../headless/useColumnSettings'

export const userColumns: ColumnDef[] = [
  { key: 'userName', title: 'Username' },
  { key: 'email', title: 'Email' },
  { key: 'createdAt', title: 'Created At', visible: false },
]
