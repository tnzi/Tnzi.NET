import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

/**
 * A list page offers the default keyword box only where its list endpoint can
 * act on a keyword.
 *
 * `mapQueryToListRequest` sends the box as `keyword`. A backend query DTO with
 * no such property drops it during binding, so the search returns the same
 * unfiltered page and reads as "no such record" or "the filter is broken". The
 * pages below list paged endpoints whose query DTO has no keyword: they pass
 * `:show-default-search="false"` and keep their Advanced fields. A page that
 * loads a whole list filters it in the page instead (see `keyword-filter.ts`).
 *
 * The second test reads the backend DTO: when one of them gains a keyword, it
 * fails and the page should offer the box again.
 */

const packageRoot = resolve(__dirname, '..', '..')
const repoRoot = resolve(packageRoot, '..', '..', '..', '..')

interface PagedListWithoutKeyword {
  page: string
  dto: string
  dtoFile: string
}

const PAGES: PagedListWithoutKeyword[] = [
  { page: 'identity/LoginLogs.vue', dto: 'LoginLogQueryDto', dtoFile: 'src/Tnzi.Identity/Dtos/LoginLogDtos.cs' },
  { page: 'authorization/DualControl.vue', dto: 'DualControlQueryDto', dtoFile: 'src/Tnzi.Authorization/Dtos/DualControlDtos.cs' },
  { page: 'audit/RecordAccess.vue', dto: 'RecordAccessQueryDto', dtoFile: 'src/Tnzi.Audit/Dtos/RecordAccessDtos.cs' },
  { page: 'audit/Destruction.vue', dto: 'DataDestructionQueryDto', dtoFile: 'src/Tnzi.Audit/Dtos/DataDestructionDtos.cs' },
  { page: 'notification/Subscriptions.vue', dto: 'NotificationPreferenceQueryDto', dtoFile: 'src/Tnzi.Notification/Dtos/NotificationPreferenceDtos.cs' },
  { page: 'notification/Devices.vue', dto: 'PushDeviceQueryDto', dtoFile: 'src/Tnzi.Notification.Push/Dtos/PushDeviceDtos.cs' },
  { page: 'ai/quota/Quotas.vue', dto: 'UsageMeterQueryDto', dtoFile: 'src/Tnzi.AI/Dtos/UsageLimitDtos.cs' },
  { page: 'ai/cli/CliRuns.vue', dto: 'CliRunQueryDto', dtoFile: 'src/Tnzi.AI/Dtos/CliAgentDtos.cs' },
  { page: 'storage/Chunks.vue', dto: 'FileChunkAuditQueryDto', dtoFile: 'src/Tnzi.Storage.Workspace/Dtos/StorageAuditDtos.cs' },
  { page: 'storage/Versions.vue', dto: 'FileVersionAuditQueryDto', dtoFile: 'src/Tnzi.Storage.Workspace/Dtos/StorageAuditDtos.cs' },
  { page: 'payment/Orders.vue', dto: 'PaymentQueryDto', dtoFile: 'src/Tnzi.Payment/Dtos/PaymentDtos.cs' },
  { page: 'payment/Refunds.vue', dto: 'RefundQueryDto', dtoFile: 'src/Tnzi.Payment/Dtos/RefundDtos.cs' },
  { page: 'payment/Subscriptions.vue', dto: 'SubscriptionQueryDto', dtoFile: 'src/Tnzi.Payment.Subscriptions/Dtos/SubscriptionDtos.cs' },
  { page: 'payment/Promotions.vue', dto: 'PromotionQueryDto', dtoFile: 'src/Tnzi.Payment.Promotions/Dtos/PromotionDtos.cs' },
  { page: 'payment/Invoices.vue', dto: 'PaymentInvoiceQueryDto', dtoFile: 'src/Tnzi.Payment.Billing/Dtos/InvoiceDtos.cs' },
  { page: 'finance/Transfers.vue', dto: 'TransferQueryDto', dtoFile: 'src/Tnzi.Finance/Dtos/TransferDtos.cs' },
  { page: 'finance/EftBatches.vue', dto: 'EftBatchQueryDto', dtoFile: 'src/Tnzi.Finance.Banking/Dtos/EftDtos.cs' },
]

/** The body of `class <name>` (between its braces) and the base type it names. */
function classOf(source: string, name: string): { base: string | null; body: string } {
  const header = new RegExp(String.raw`\bclass\s+${name}\b\s*(?::\s*([A-Za-z0-9_.]+))?[^{]*\{`)
  const match = header.exec(source)
  if (!match) throw new Error(`class ${name} not found`)
  let depth = 1
  let index = match.index + match[0].length
  const start = index
  while (depth > 0 && index < source.length) {
    if (source[index] === '{') depth++
    else if (source[index] === '}') depth--
    index++
  }
  return { base: match[1] ?? null, body: source.slice(start, index - 1) }
}

const KEYWORD_PROPERTY = /\bpublic\s+[\w<>?[\], ]+\s+(Keyword|Keywords|SearchText|Search)\s*\{/

describe('list pages offer the keyword box only where the endpoint takes one', () => {
  it.each(PAGES)('$page hides the keyword box ($dto has no keyword)', ({ page }) => {
    const source = readFileSync(resolve(packageRoot, 'src', 'pages', page), 'utf8')
    expect(source).toMatch(/:show-default-search="false"/)
  })

  it.each(PAGES)('$dto still has no keyword property', ({ dto, dtoFile }) => {
    const source = readFileSync(resolve(repoRoot, dtoFile), 'utf8')
    const { base, body } = classOf(source, dto)
    // The base must be the plain paged query (no keyword either); any other base
    // has to be read before this entry can stay in the list.
    expect(base).toBe('PagedQueryDto')
    expect(body).not.toMatch(KEYWORD_PROPERTY)
  })

  it('the property scan sees a keyword when one is declared (positive control)', () => {
    const { body } = classOf(
      'public class SampleQueryDto : PagedQueryDto\n{\n    public string? Keyword { get; set; }\n}\n',
      'SampleQueryDto',
    )
    expect(body).toMatch(KEYWORD_PROPERTY)
  })
})
