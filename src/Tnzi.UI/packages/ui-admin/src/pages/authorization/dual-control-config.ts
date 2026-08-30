/**
 * Dual-control config - columns and the status vocabulary for the four-eyes
 * approval queue.
 *
 * Backend: `Tnzi.Authorization`'s `DefaultDualControlAdminController`
 * (`/admin/dual-control`).
 */
import { DualControlStatus, type DualControlRequestDto } from '@tnzi/core/services/authorization'
import type { ColumnDef } from '../../headless/useColumnSettings'

type StatusTone = 'success' | 'warning' | 'error' | 'info' | 'default'

/**
 * A request's state as a colour.
 *
 * `Rejected` is `warning`, not `error`: a refusal is the control working, not a
 * malfunction. Reserving red for actual faults is what keeps operators reading
 * red at all. `Cancelled` is `default` - the requester simply withdrew.
 */
export function dualControlStatusTone(status?: DualControlStatus | null): StatusTone {
  switch (status) {
    case DualControlStatus.Approved: return 'success'
    case DualControlStatus.Pending: return 'info'
    case DualControlStatus.Rejected: return 'warning'
    case DualControlStatus.Cancelled: return 'default'
    default: return 'default'
  }
}

/** Status filter options, in lifecycle order rather than alphabetical. */
export const DUAL_CONTROL_STATUS_OPTIONS = [
  DualControlStatus.Pending,
  DualControlStatus.Approved,
  DualControlStatus.Rejected,
  DualControlStatus.Cancelled,
] as const

/**
 * A permit is spent, not just approved.
 *
 * `isUsable` is computed server-side (approved AND unconsumed AND unexpired) and
 * it is the only honest answer to "can this still be acted on" - an `Approved`
 * row on its own says nothing, because approval decays with time and is spent on
 * use. The page must never derive this from `status` alone.
 */
export function isSpent(row: Pick<DualControlRequestDto, 'status' | 'isUsable'>): boolean {
  return row.status === DualControlStatus.Approved && !row.isUsable
}

/**
 * Columns feed `useCrudPage`'s column settings and the mobile card fallback;
 * the page itself renders request rows rather than a grid.
 */
export const dualControlColumns: ColumnDef<Partial<DualControlRequestDto>>[] = [
  { key: 'operation', title: 'columns.operation', minWidth: 200, primary: true },
  { key: 'targetId', title: 'columns.targetId', width: 140 },
  { key: 'status', title: 'columns.status', width: 120 },
  { key: 'requesterName', title: 'columns.requester', width: 160 },
  { key: 'approverName', title: 'columns.approver', width: 160 },
  { key: 'creationTime', title: 'columns.requestedAt', width: 160 },
  { key: 'expiresAt', title: 'columns.expiresAt', width: 160 },
]
