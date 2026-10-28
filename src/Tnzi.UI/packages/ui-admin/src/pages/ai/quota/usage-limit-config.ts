/**
 * Sibling config of the usage-limits page. The users list renders as row cards
 * (each user is read one at a time: who, how much of each window, any own
 * settings), so the columns only feed the list engine's column settings.
 */
import type { ColumnDef } from '../../../headless/useColumnSettings'
import type { UserUsageSummaryDto } from '@tnzi/core/services/ai'

export const usageUserColumns: ColumnDef<UserUsageSummaryDto>[] = [
  { key: 'userId', title: 'columns.user', minWidth: 220 },
  { key: 'windows', title: 'columns.windows', minWidth: 260 },
  { key: 'lastUsedAt', title: 'columns.lastUsedAt', minWidth: 160 },
]
