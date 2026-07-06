/**
 * Invoice bridge — thin adapter over `@tnzi/core`'s admin invoice API
 * (`useAdminInvoiceApi`, wrapping `/admin/invoices/*` exposed by
 * `Tnzi.Payment.Controllers.Admin.DefaultInvoiceAdminController`).
 *
 * Delegates to the canonical `useAdminInvoiceApi` factory in
 * `@tnzi/core/services/payment` (getList / get / send / markAsPaid / cancel).
 * This file keeps the bridge's original public surface (`InvoiceDto` /
 * `InvoiceQueryDto` / `MarkInvoicePaidDto` re-exports + `createInvoiceBridge`)
 * so consuming pages are unaffected.
 *
 * The admin page focuses on the lifecycle ops (mark-paid / cancel / send)
 * — manual creation is more complex (line-items editor) and intentionally
 * routes through the dedicated create flow rather than the table page.
 *
 * Backend note: `InvoiceQueryDto` server-side filters on
 * invoiceNo/type/status/customerEmail/startTime/endTime only — there is no
 * free-text `searchText` parameter, so the page's search box is a no-op
 * server-side (the param is serialised but silently ignored). `status`
 * filters as expected.
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminInvoiceApi,
  type InvoiceDto as CoreInvoiceDto,
  type MarkInvoicePaidDto as CoreMarkInvoicePaidDto,
} from '@tnzi/core/services/payment'
import { unwrapResult as unwrap } from '../_mappers'

// Re-export under the original bridge names consumed by pages.
export type InvoiceDto = CoreInvoiceDto
export type MarkInvoicePaidDto = CoreMarkInvoicePaidDto

/**
 * Page-facing query shape (table page: page + status filter).
 *
 * `status` / `type` are enum member-name strings (the backend serialises and
 * binds enums by name via the global JsonStringEnumConverter); numbers are
 * still accepted for backward-compat.
 */
export interface InvoiceQueryDto {
  pageIndex: number
  pageSize: number
  status?: string | number | null
  type?: string | number | null
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

  const api = useAdminInvoiceApi(client)

  return {
    getList: async (query: InvoiceQueryDto) => {
      const result = unwrap<{ items: InvoiceDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await api.getList({
          pageIndex: query.pageIndex,
          pageSize: query.pageSize,
          status: query.status ?? undefined,
          type: query.type ?? undefined,
        } as never),
      )
      return {
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      }
    },
    getById: async (id: string) =>
      unwrap<InvoiceDto | null>(await api.get(id)),
    send: async (id: string, recipientEmail?: string) => {
      await api.send(id, recipientEmail ? { recipientEmail } : undefined)
    },
    markAsPaid: async (id: string, payload: MarkInvoicePaidDto) => {
      await api.markAsPaid(id, payload)
    },
    cancel: async (id: string, reason: string) => {
      await api.cancel(id, reason)
    },
  }
}
