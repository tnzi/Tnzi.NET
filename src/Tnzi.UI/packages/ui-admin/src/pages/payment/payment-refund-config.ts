import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

/**
 * Payment Refund page config — aligned with RefundDto
 * (2026-04-14 Plan C unstub).
 *
 * Backend fields (RefundDto):
 *   id, refundNo, tradeNo, paymentId, paymentNo, refundAmount, currency,
 *   reason, status, approverId, approveTime, completedTime, creationTime,
 *   lastModificationTime
 *
 * Row actions "Approve" / "Reject" map to POST /admin/refunds/{id}/approve
 * with approved=true/false.
 */
export const refundColumns: ColumnDef[] = [
  { key: 'refundNo',      title: 'Refund No' },
  { key: 'paymentNo',     title: 'Payment No' },
  { key: 'refundAmount',  title: 'Amount' },
  { key: 'currency',      title: 'Currency' },
  { key: 'reason',        title: 'Reason' },
  { key: 'status',        title: 'Status' },
  { key: 'creationTime',  title: 'Requested At' },
  { key: 'completedTime', title: 'Completed At' },
  { key: 'approverId',    title: 'Approver', visible: false },
  { key: 'approveTime',   title: 'Approved At', visible: false },
]

export const refundFormSchema: FormSchemaItem[] = [
  { key: 'refundNo',     label: 'Refund No',    type: 'text' },
  { key: 'paymentNo',    label: 'Payment No',   type: 'text' },
  { key: 'refundAmount', label: 'Amount',       type: 'number', required: true },
  { key: 'reason',       label: 'Reason',       type: 'textarea', required: true },
  { key: 'status',       label: 'Status',       type: 'text' },
]
