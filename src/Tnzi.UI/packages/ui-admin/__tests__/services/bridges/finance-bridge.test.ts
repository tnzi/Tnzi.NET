import { describe, it, expect, vi } from 'vitest'
import { createFinanceBridge } from '../../../src/services/bridges/finance-bridge'
import type { HttpClient } from '@tnzi/core/http'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

// ---- 失败信封必须抛，不能被当成成功 ----
//
// ★ HttpClient 对业务拒绝（400/409 + `{ succeeded: false }`）**返回失败信封而不 reject**，
//   `unwrapResult` 独用会把信封里的 (null) data 原样交出来。`useCrudPage.submit` 只把
//   **抛出的** 错误送进错误链，所以 bridge 返回 null 就是绿 toast + 关表单 + 列表里没那一行。
//   2026-09-04 横扫改掉了 `unwrap(await api.create(x)) as Dto` 那一种形状，本文件里的
//   `unwrap<T>((await api.create(x)) as never)` 整批留了下来（门禁对多一层括号失明）。
//   页面测试 `vi.mock` 整个 bridge，永远看不见这一层，所以门禁必须在这里。

const REFUSAL = 'Customer code X already exists.'

/** Mock HttpClient: reads succeed, every write answers a refused envelope. */
function refusingClient() {
  const ok = <T>(data: T) => ({ data, succeeded: true, success: true, code: 200, message: '' })
  const refused = () => ({ data: null, succeeded: false, success: false, code: 409, message: REFUSAL })
  return {
    get: vi.fn(async () => ok({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    post: vi.fn(async () => refused()),
    put: vi.fn(async () => refused()),
    delete: vi.fn(async () => refused()),
    // `HttpClient.download` resolves a failed envelope on any non-2xx, with an
    // explicit `data: undefined` (createFailedApiResult) - the shape a bare
    // `unwrap<Blob>` turned into `undefined`.
    download: vi.fn(async () => ({ ...refused(), data: undefined })),
  } as unknown as HttpClient
}

describe('finance-bridge write paths', () => {
  const bridge = createFinanceBridge({ client: refusingClient() })

  // One representative per section that went through bare `unwrap`, plus `end`
  // (the verb the source-scan gate did not list) and `runDue` (no id argument).
  const writes: Array<[string, () => Promise<unknown>]> = [
    ['customers.create', () => bridge.customers.create({ code: 'X', name: 'X' } as never)],
    ['vendors.update', () => bridge.vendors.update('v1', { name: 'X' } as never)],
    ['items.create', () => bridge.items.create({ code: 'X', name: 'X' } as never)],
    ['estimates.createDraft', () => bridge.estimates.createDraft({} as never)],
    ['estimates.update', () => bridge.estimates.update('e1', {} as never)],
    ['estimates.send', () => bridge.estimates.send('e1')],
    ['estimates.accept', () => bridge.estimates.accept('e1')],
    ['estimates.decline', () => bridge.estimates.decline('e1')],
    ['estimates.close', () => bridge.estimates.close('e1')],
    ['estimates.convert', () => bridge.estimates.convert('e1', {} as never)],
    ['purchaseOrders.accept', () => bridge.purchaseOrders.accept('p1')],
    ['invoices.createDraft', () => bridge.invoices.createDraft({} as never)],
    ['invoices.updateDraft', () => bridge.invoices.updateDraft('i1', {} as never)],
    ['invoices.post', () => bridge.invoices.post('i1')],
    ['invoices.voidDoc', () => bridge.invoices.voidDoc('i1')],
    ['bills.post', () => bridge.bills.post('b1')],
    ['expenses.voidDoc', () => bridge.expenses.voidDoc('x1')],
    ['creditMemos.createDraft', () => bridge.creditMemos.createDraft({} as never)],
    ['collaboration.attach', () => bridge.collaboration.attach('invoice', 'i1', {} as never)],
    ['collaboration.postComment', () => bridge.collaboration.postComment('invoice', 'i1', 'hello')],
    ['bankRules.create', () => bridge.bankRules.create({} as never)],
    ['bankRules.update', () => bridge.bankRules.update('r1', {} as never)],
    ['recurring.create', () => bridge.recurring.create({} as never)],
    ['recurring.update', () => bridge.recurring.update('t1', {} as never)],
    ['recurring.pause', () => bridge.recurring.pause('t1')],
    ['recurring.resume', () => bridge.recurring.resume('t1')],
    ['recurring.end', () => bridge.recurring.end('t1')],
    ['recurring.run', () => bridge.recurring.run('t1')],
    ['recurring.runDue', () => bridge.recurring.runDue()],
    // 2026-09-12 second pass: verbs outside the gate's first list, so the 09-04 sweep
    // and fe854d1d both walked past them while converting their neighbours.
    ['journals.reverse', () => bridge.journals.reverse('j1', {} as never)],
    ['rates.refresh', () => bridge.rates.refresh()],
    ['bankFeed.pull', () => bridge.bankFeed.pull('acc1')],
    // Third pass: `suggest` is `POST bank-feed/suggest` gated by `finance.bankFeed.update`.
    // It writes suggested matches (and auto-confirms exact ones when a draft reconciliation
    // is open) and refuses foreign-currency accounts with a 400 - on bare unwrap that
    // refusal surfaced as `Cannot read properties of null (reading 'suggested')`.
    ['bankFeed.suggest', () => bridge.bankFeed.suggest('acc1')],
    ['checks.spoil', () => bridge.checks.spoil({} as never)],
    ['balanceSummary.rebuild', () => bridge.balanceSummary.rebuild()],
  ]

  it.each(writes)('%s rejects with the backend reason on a refused envelope', async (_name, call) => {
    await expect(call()).rejects.toThrow(REFUSAL)
  })

  // Not a write, but a POST whose declared result is non-optional: BankRules.vue assigns
  // the result straight to the test modal, so a refused envelope (403/404) left the
  // modal open and empty with no error. Same read-side decision as defineCrudBridge.
  it('bankRules.test rejects with the backend reason on a refused envelope', async () => {
    await expect(bridge.bankRules.test('r1', { accountId: null })).rejects.toThrow(REFUSAL)
  })

  // Blob downloads: Reports.vue / Checks.vue / EftBatches / statements hand the
  // result straight to `downloadBlob`, so a refusal that resolved `undefined`
  // surfaced as "Failed to execute 'createObjectURL'" instead of the server's
  // reason (row cap, date range, 403). Every Blob path must reject.
  const downloads: Array<[string, () => Promise<unknown>]> = [
    ['reports.exportTrialBalanceCsv', () => bridge.reports.exportTrialBalanceCsv('2026-01-01', '2026-01-31')],
    ['reports.exportBalanceSheetCsv', () => bridge.reports.exportBalanceSheetCsv('2026-01-31')],
    ['reports.exportProfitAndLossCsv', () => bridge.reports.exportProfitAndLossCsv('2026-01-01', '2026-01-31')],
    ['reports.exportGeneralLedgerCsv', () => bridge.reports.exportGeneralLedgerCsv('acc1', '2026-01-01', '2026-01-31')],
    ['reports.exportArAgingCsv', () => bridge.reports.exportArAgingCsv('2026-01-31')],
    ['reports.exportApAgingCsv', () => bridge.reports.exportApAgingCsv('2026-01-31')],
    ['reports.exportTaxSummaryCsv', () => bridge.reports.exportTaxSummaryCsv('2026-01-01', '2026-01-31')],
    ['reports.exportCashFlowCsv', () => bridge.reports.exportCashFlowCsv('2026-01-01', '2026-01-31')],
    ['statements.download', () => bridge.statements.download('customer', 'c1', {} as never)],
    ['checks.templateSpecimen', () => bridge.checks.templateSpecimen('check-cpa006-ca')],
    ['checks.calibration', () => bridge.checks.calibration('acc1')],
    ['checks.exportPositivePay', () => bridge.checks.exportPositivePay('acc1', '2026-01-01', '2026-01-31')],
    ['eftBatches.download', () => bridge.eftBatches.download('b1')],
  ]

  it.each(downloads)('%s rejects with the backend reason on a refused download envelope', async (_name, call) => {
    await expect(call()).rejects.toThrow(REFUSAL)
  })

  it('a refused delete still throws (regression guard for the ensureOk sites)', async () => {
    await expect(bridge.customers.delete(['c1'])).rejects.toThrow(REFUSAL)
    await expect(bridge.estimates.deleteDraft('e1')).rejects.toThrow(REFUSAL)
    await expect(bridge.recurring.delete(['t1'])).rejects.toThrow(REFUSAL)
  })

  it('reconciliations.worksheet rejects a refused read (Save writes the selection back as the whole cleared set)', async () => {
    // A failed read handed over as null left the worksheet empty with Save and
    // Complete live: one click cleared every selected line of the reconciliation.
    const client = refusingClient()
    ;(client.get as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: null, succeeded: false, success: false, code: 500, message: 'Worksheet unavailable',
    })
    await expect(createFinanceBridge({ client }).reconciliations.worksheet('r1')).rejects.toThrow('Worksheet unavailable')

    const ws = { reconciliationId: 'r1', statementEndingBalance: 0, clearedBalance: 0, difference: 0, lines: [] }
    ;(client.get as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({ data: ws, succeeded: true, success: true, code: 200, message: '' })
    await expect(createFinanceBridge({ client }).reconciliations.worksheet('r1')).resolves.toEqual(ws)
  })

  it('a successful envelope still unwraps to the payload', async () => {
    const client = refusingClient()
    ;(client.post as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: { id: 'c1', code: 'X' },
      succeeded: true,
      success: true,
      code: 200,
      message: '',
    })
    const okBridge = createFinanceBridge({ client })
    await expect(okBridge.customers.create({ code: 'X', name: 'X' } as never)).resolves.toEqual({ id: 'c1', code: 'X' })
  })
})

// ---- 读侧：失败的读必须抛，不能被读成「空」 ----
//
// ★ 同一个 HttpClient 对 403 / 404 / 5xx / 超时也是**返回失败信封**（`data: undefined`）。
//   裸 unwrap 把它变成 undefined，再经 `?? []` 就成了一个看起来真实的空答案：核销面板
//   「没有未清单据」、存款 / 支票 / EFT 队列为空、报表像从没跑过、抽屉空白，页面自己的
//   catch 一次也没机会执行。现在每个读都 reject 出服务端的原话。

const READ_REFUSAL = 'You do not have permission to view finance records.'

/** Every verb answers the failed-envelope shape `createFailedApiResult` produces. */
function failingClient() {
  const failed = () => ({ data: undefined, succeeded: false, success: false, code: 403, message: READ_REFUSAL })
  return {
    get: vi.fn(async () => failed()),
    post: vi.fn(async () => failed()),
    put: vi.fn(async () => failed()),
    delete: vi.fn(async () => failed()),
    download: vi.fn(async () => failed()),
  } as unknown as HttpClient
}

/** Every verb answers success with no body. */
function emptyBodyClient() {
  const ok = () => ({ data: null, succeeded: true, success: true, code: 200, message: '' })
  return {
    get: vi.fn(async () => ok()),
    post: vi.fn(async () => ok()),
    put: vi.fn(async () => ok()),
    delete: vi.fn(async () => ok()),
  } as unknown as HttpClient
}

describe('finance-bridge read paths', () => {
  const bridge = createFinanceBridge({ client: failingClient() })
  const q = { pageIndex: 1, pageSize: 20, filters: {} } as never

  const reads: Array<[string, () => Promise<unknown>]> = [
    ['accounts.fetch', () => bridge.accounts.fetch(q)],
    ['accounts.tree', () => bridge.accounts.tree()],
    ['accounts.balances', () => bridge.accounts.balances(['a1'])],
    ['journals.fetch', () => bridge.journals.fetch(q)],
    ['journals.getById', () => bridge.journals.getById('j1')],
    ['rates.fetch', () => bridge.rates.fetch(q)],
    ['fiscalYears.list', () => bridge.fiscalYears.list()],
    ['fiscalYears.getClosingDate', () => bridge.fiscalYears.getClosingDate()],
    ['reports.trialBalance', () => bridge.reports.trialBalance('2026-01-01', '2026-01-31')],
    ['reports.balanceSheet', () => bridge.reports.balanceSheet('2026-01-31')],
    ['reports.profitAndLoss', () => bridge.reports.profitAndLoss('2026-01-01', '2026-01-31')],
    ['reports.generalLedger', () => bridge.reports.generalLedger('a1', '2026-01-01', '2026-01-31')],
    ['reports.arAging', () => bridge.reports.arAging('2026-01-31')],
    ['reports.apAging', () => bridge.reports.apAging('2026-01-31')],
    ['reports.taxSummary', () => bridge.reports.taxSummary('2026-01-01', '2026-01-31')],
    ['reports.cashFlow', () => bridge.reports.cashFlow('2026-01-01', '2026-01-31')],
    ['customers.fetch', () => bridge.customers.fetch(q)],
    ['customers.get', () => bridge.customers.get('c1')],
    ['customers.summary', () => bridge.customers.summary('c1')],
    ['customers.transactions', () => bridge.customers.transactions('c1', {} as never)],
    ['vendors.get', () => bridge.vendors.get('v1')],
    ['vendors.summary', () => bridge.vendors.summary('v1')],
    ['vendors.transactions', () => bridge.vendors.transactions('v1', {} as never)],
    ['items.fetch', () => bridge.items.fetch(q)],
    ['taxes.agencies', () => bridge.taxes.agencies()],
    ['taxes.rates', () => bridge.taxes.rates()],
    ['taxes.codes', () => bridge.taxes.codes()],
    ['collaboration.attachments', () => bridge.collaboration.attachments('invoice', 'i1')],
    ['collaboration.attachmentCounts', () => bridge.collaboration.attachmentCounts('invoice', ['i1'])],
    ['collaboration.comments', () => bridge.collaboration.comments('invoice', 'i1')],
    ['bankRules.fetch', () => bridge.bankRules.fetch(q)],
    ['bankRules.getById', () => bridge.bankRules.getById('r1')],
    ['statements.get', () => bridge.statements.get('customer' as never, 'c1', {} as never)],
    ['statements.dunning', () => bridge.statements.dunning('customer' as never)],
    ['taxReturns.forms', () => bridge.taxReturns.forms()],
    ['taxReturns.get', () => bridge.taxReturns.get('CA', 'GST34', '2026-01-01', '2026-03-31')],
    ['recurring.fetch', () => bridge.recurring.fetch(q)],
    ['recurring.getById', () => bridge.recurring.getById('t1')],
    ['recurring.preview', () => bridge.recurring.preview('t1', 6)],
    ['recurring.previewSchedule', () => bridge.recurring.previewSchedule({} as never, 6)],
    ['recurring.runs', () => bridge.recurring.runs({} as never)],
    ['estimates.fetch', () => bridge.estimates.fetch(q)],
    ['purchaseOrders.getById', () => bridge.purchaseOrders.getById('p1')],
    ['invoices.fetch', () => bridge.invoices.fetch(q)],
    ['invoices.getById', () => bridge.invoices.getById('i1')],
    ['creditMemos.getById', () => bridge.creditMemos.getById('m1')],
    ['deposits.undeposited', () => bridge.deposits.undeposited({} as never)],
    ['reconciliations.fetch', () => bridge.reconciliations.fetch(q)],
    ['reconciliations.getById', () => bridge.reconciliations.getById('r1')],
    ['revaluations.preview', () => bridge.revaluations.preview({} as never)],
    ['settlements.applications', () => bridge.settlements.applications('PaymentEntry' as never, 'p1')],
    ['settlements.openDocuments', () => bridge.settlements.openDocuments('Customer' as never, 'c1')],
    ['bankAccounts.capabilities', () => bridge.bankAccounts.capabilities()],
    ['bankAccounts.fetch', () => bridge.bankAccounts.fetch(q)],
    ['bankAccounts.getById', () => bridge.bankAccounts.getById('b1')],
    ['partyBankAccounts.byParty', () => bridge.partyBankAccounts.byParty('Vendor' as never, 'v1')],
    ['bankFeed.transactions', () => bridge.bankFeed.transactions(q)],
    ['bankFeed.candidates', () => bridge.bankFeed.candidates('t1')],
    ['bankFeed.batches', () => bridge.bankFeed.batches(q)],
    ['checks.queue', () => bridge.checks.queue('b1')],
    ['checks.fetch', () => bridge.checks.fetch(q)],
    ['eftBatches.queue', () => bridge.eftBatches.queue()],
    ['eftBatches.fetch', () => bridge.eftBatches.fetch(q)],
    ['eftBatches.getById', () => bridge.eftBatches.getById('e1')],
    ['receipts.fetch', () => bridge.receipts.fetch(q)],
    ['receipts.getById', () => bridge.receipts.getById('r1')],
  ]

  it.each(reads)('%s rejects with the server reason instead of answering empty', async (_name, call) => {
    await expect(call()).rejects.toThrow(READ_REFUSAL)
  })

  it('checks.templates still answers empty when the rendering module is absent (501 is a deployment fact)', async () => {
    await expect(bridge.checks.templates()).resolves.toEqual([])
  })

  it('a successful answer with no body is still an empty list, not a TypeError', async () => {
    const empty = createFinanceBridge({ client: emptyBodyClient() })
    await expect(empty.customers.fetch(q)).resolves.toMatchObject({ items: [], totalCount: 0 })
    await expect(empty.invoices.fetch(q)).resolves.toMatchObject({ items: [], totalCount: 0 })
    await expect(empty.settlements.openDocuments('Customer' as never, 'c1')).resolves.toEqual([])
    await expect(empty.checks.queue('b1')).resolves.toEqual([])
  })

  it('no read in the bridge goes through the unchecked unwrap', () => {
    const src = readFileSync(join(__dirname, '../../../src/services/bridges/finance-bridge.ts'), 'utf8')
    expect(src).not.toMatch(/\bunwrap(Result|Unchecked)?\s*[<(]/)
  })
})
