/**
 * Invoice bridge — wraps `/admin/invoices/*` exposed by
 * `Tnzi.Payment.Controllers.Admin.DefaultInvoiceAdminController`.
 *
 * The admin page focuses on the lifecycle ops (mark-paid / cancel / send)
 * — manual creation is more complex (line-items editor) and intentionally
 * routes through the dedicated create flow rather than the table page.
 */
import type { HttpClient } from '@tnzi/core/http'

export interface InvoiceDto {
  id: string
  invoiceNo: string
  type: number
  status: number
  amount: number
  currency: string
  taxAmount: number
  discountAmount: number
  dueAmount: number
  paidAmount: number
  customerName: string
  customerEmail: string
  customerCompany?: string | null
  invoiceDate: string
  dueDate?: string | null
  paidDate?: string | null
  pdfFileUrl?: string | null
  notes?: string | null
  creationTime: string
}

export interface InvoiceQueryDto {
  pageIndex: number
  pageSize: number
  status?: number | null
  type?: number | null
  searchText?: string | null
}

export interface MarkInvoicePaidDto {
  /** Amount actually settled — must be > 0 (backend RangeAttribute). */
  paidAmount: number
  /** Free-text note attached to the settlement (e.g. payment ref / channel). */
  remark?: string | null
}

export interface InvoiceBridgeDeps {
  client?: HttpClient
}

export interface InvoiceBridge {
  getList(query: InvoiceQueryDto): Promise<{ items: InvoiceDto[]; totalCount: number; pageIndex: number; pageSize: number }>
  getById(id: string): Promise<InvoiceDto | null>
  send(id: string, recipientEmail?: string): Promise<void>
  markAsPaid(id: string, payload: MarkInvoicePaidDto): Promise<void>
  cancel(id: string, reason: string): Promise<void>
}

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
}

export function createInvoiceBridge(deps: InvoiceBridgeDeps = {}): InvoiceBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createInvoiceBridge: no HttpClient provided'))
    return {
      getList: noOp as never,
      getById: noOp as never,
      send: noOp as never,
      markAsPaid: noOp as never,
      cancel: noOp as never,
    }
  }

  return {
    getList: async (query: InvoiceQueryDto) => {
      const params = new URLSearchParams({
        pageIndex: String(query.pageIndex),
        pageSize: String(query.pageSize),
      })
      if (query.status != null) params.set('status', String(query.status))
      if (query.type != null) params.set('type', String(query.type))
      if (query.searchText) params.set('searchText', query.searchText)
      const result = unwrap<{ items: InvoiceDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await client.get(`/admin/invoices?${params.toString()}`),
      )
      return {
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      }
    },
    getById: async (id: string) =>
      unwrap<InvoiceDto | null>(await client.get<InvoiceDto>(`/admin/invoices/${encodeURIComponent(id)}`)),
    send: async (id: string, recipientEmail?: string) => {
      await client.post(`/admin/invoices/${encodeURIComponent(id)}/send`, recipientEmail ? { recipientEmail } : {})
    },
    markAsPaid: async (id: string, payload: MarkInvoicePaidDto) => {
      await client.post(`/admin/invoices/${encodeURIComponent(id)}/mark-paid`, payload)
    },
    cancel: async (id: string, reason: string) => {
      await client.post(`/admin/invoices/${encodeURIComponent(id)}/cancel`, { reason })
    },
  }
}
