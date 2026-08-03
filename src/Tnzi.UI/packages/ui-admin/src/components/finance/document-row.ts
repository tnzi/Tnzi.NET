/**
 * The row shape and status vocabulary shared by every finance-document surface.
 *
 * These live in the component layer, not next to the pages, because
 * `TDocumentCard` renders them: a component reaching up into `pages/finance/`
 * for the contract it displays inverts the layering and makes `./components`
 * unusable without the page layer. The five document pages import the same
 * declarations back through `pages/finance/document-config`, which re-exports
 * them.
 */
import { FinanceDocumentStatus } from '../../services/bridges/finance-bridge'

/** All-optional row shape shared by the five document list pages. */
export interface FinanceDocRow {
  id?: string
  number?: string | null
  status?: FinanceDocumentStatus
  docDate?: string
  dueDate?: string | null
  customerName?: string | null
  vendorName?: string | null
  partyName?: string | null
  // 行内下游动作要把往来方交接给下一张单据（收款 / 付款），所以行里必须带 id。
  customerId?: string | null
  vendorId?: string | null
  partyId?: string | null
  currency?: string
  total?: number
  amount?: number
  appliedTotal?: number
  memo?: string | null
  creationTime?: string
}

/** FinanceDocumentStatus → badge meta（键为后端 PascalCase 成员名；label 为页面作用域 i18n 键）。 */
export const DOC_STATUS_META: Record<
  string,
  { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label: string }
> = {
  [FinanceDocumentStatus.Draft]: { type: 'warning', label: 'status.draft' },
  [FinanceDocumentStatus.Posted]: { type: 'success', label: 'status.posted' },
  [FinanceDocumentStatus.PartiallyPaid]: { type: 'info', label: 'status.partiallyPaid' },
  [FinanceDocumentStatus.Paid]: { type: 'default', label: 'status.paid' },
  [FinanceDocumentStatus.Voided]: { type: 'error', label: 'status.voided' },
}
