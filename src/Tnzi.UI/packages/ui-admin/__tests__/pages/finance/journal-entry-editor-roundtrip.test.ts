import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import JournalEntryEditor from '../../../src/pages/finance/components/JournalEntryEditor.vue'

/**
 * A journal draft update rebuilds every line from the payload. A line's party
 * and dimension tags (set by an integration or the API: "this accrual is for
 * customer X, cost centre Y") have no control on this editor, so the editor
 * has to carry them, or saving a typo fix in the memo strips the tags the
 * party ledger and dimension reports read.
 */
const draft = {
  id: 'je1',
  status: 'Draft',
  postingDate: '2026-07-31',
  memo: 'Month-end accrual',
  currency: 'CAD',
  exchangeRate: 0,
  totalDebit: 0,
  totalCredit: 0,
  txnTotalDebit: 500,
  txnTotalCredit: 500,
  lines: [
    {
      id: 'l1', lineNumber: 1, accountId: 'a-ar', debit: 0, credit: 0, txnDebit: 500, txnCredit: 0, currency: 'CAD',
      memo: 'Accrued fee', partyType: 'Customer', partyId: 'c-42', dimensions: '{"costCentre":"CC-7"}',
    },
    {
      id: 'l2', lineNumber: 2, accountId: 'a-rev', debit: 0, credit: 0, txnDebit: 0, txnCredit: 500, currency: 'CAD',
      memo: null, partyType: null, partyId: null, dimensions: null,
    },
  ],
}

function mountEditor() {
  const updateDraft = vi.fn(async () => ({ id: 'je1' }))
  const bridge = { journals: { updateDraft, createDraft: vi.fn(), post: vi.fn() } }
  const wrapper = mount(JournalEntryEditor, {
    props: {
      entry: draft as never,
      accountOptions: [{ label: 'AR', value: 'a-ar' }, { label: 'Revenue', value: 'a-rev' }],
      bridge: bridge as never,
    },
  })
  return { vm: wrapper.vm as unknown as { memo: string; lines: Array<{ memo: string }>; save: (post: boolean) => Promise<void> }, updateDraft }
}

describe('JournalEntryEditor round-trips line tags it does not show', () => {
  it('sends each line its party and dimension tags back on save', async () => {
    const { vm, updateDraft } = mountEditor()
    await flushPromises()

    vm.memo = 'Month-end accrual (July)'
    await vm.save(false)

    expect(updateDraft).toHaveBeenCalledTimes(1)
    const payload = (updateDraft.mock.calls[0] as unknown as [string, { memo: string; lines: unknown[] }])[1]
    expect(payload.memo).toBe('Month-end accrual (July)')
    expect(payload.lines).toEqual([
      expect.objectContaining({ accountId: 'a-ar', debit: 500, partyType: 'Customer', partyId: 'c-42', dimensions: '{"costCentre":"CC-7"}' }),
      expect.objectContaining({ accountId: 'a-rev', credit: 500, partyType: null, partyId: null, dimensions: null }),
    ])
  })

  it('does not hand a removed line its tags to the line that moves up', async () => {
    const { vm, updateDraft } = mountEditor()
    await flushPromises()

    // Tags travel with the line, not with its position: after removing the
    // tagged first line, the revenue line must not inherit customer c-42.
    ;(vm as unknown as { lines: unknown[] }).lines.splice(0, 1)
    ;(vm as unknown as { lines: Array<{ debit: number | null; credit: number | null }> }).lines.push({ accountId: 'a-ar', debit: 500, credit: null, memo: '' } as never)
    await vm.save(false)

    const payload = (updateDraft.mock.calls[0] as unknown as [string, { lines: Array<{ accountId: string; partyId: string | null }> }])[1]
    expect(payload.lines.map((l) => [l.accountId, l.partyId ?? null])).toEqual([['a-rev', null], ['a-ar', null]])
  })
})
