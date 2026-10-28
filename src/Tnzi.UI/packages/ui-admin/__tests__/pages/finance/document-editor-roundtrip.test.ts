import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import DocumentEditor, { type DocumentEditorPayload, type EditableDocument } from '../../../src/pages/finance/components/DocumentEditor.vue'

/**
 * A draft update replaces the whole document: the backend writes
 * `ExchangeRate = input.ExchangeRate ?? 0` and rebuilds every line from the
 * payload. Fields this editor does not show (a manual exchange rate, a line's
 * manual tax amount) therefore have to be carried from the loaded draft, or a
 * reviewer fixing a memo silently re-rates the document and recomputes the tax
 * a vendor printed on it.
 *
 * A carried value is only right while what it was set against is unchanged: a
 * rate belongs to its currency, a tax override to its line's amount and tax
 * code. Once the operator changes those, the carried value is dropped and the
 * backend falls back to the rate table / the tax code - the inputs they can see.
 */
type EditorVm = {
  currency: string
  lines: Array<{ quantity: number | null; unitPrice: number | null; amount: number | null; taxCodeId: string | null; memo?: string }>
  memo: string
  save: (post: boolean) => Promise<void>
}

const foreignBill: EditableDocument = {
  id: 'b1',
  partyId: 'v1',
  docDate: '2026-07-01',
  currency: 'USD',
  exchangeRate: 1.3512,
  memo: 'Imported from the vendor portal',
  lines: [
    { itemId: null, description: 'Consulting', accountId: 'a1', quantity: 1, unitPrice: 100, amount: 100, taxCodeId: 'hst', taxAmount: 12.99 },
    { itemId: null, description: 'Travel', accountId: 'a2', quantity: 2, unitPrice: 25, amount: 50, taxCodeId: 'hst', taxAmount: null },
  ],
}

function mountEditor(entry: EditableDocument, kind: 'sales' | 'expense' = 'sales') {
  const onSave = vi.fn(async (_payload: DocumentEditorPayload, _post: boolean) => {})
  const wrapper = mount(DocumentEditor, {
    props: {
      kind,
      entry,
      partyLabel: 'Vendor',
      // The expense editor's vendor is optional, as on the Expenses page.
      partyOptional: kind === 'expense',
      partyOptions: [{ label: 'Vendor', value: 'v1' }],
      accountOptions: [{ label: 'Paid from', value: 'bank' }],
      taxCodeOptions: [{ label: 'HST', value: 'hst' }, { label: 'GST', value: 'gst' }],
      onSave,
    },
  })
  return { vm: wrapper.vm as unknown as EditorVm, onSave }
}

function sent(onSave: ReturnType<typeof vi.fn>): DocumentEditorPayload {
  return (onSave.mock.calls[0] as [DocumentEditorPayload, boolean])[0]
}

describe('DocumentEditor round-trips the fields it does not show', () => {
  it('carries the manual exchange rate and line tax overrides through an unrelated edit', async () => {
    const { vm, onSave } = mountEditor(foreignBill)
    await flushPromises()

    vm.memo = 'Checked against the PDF'
    await vm.save(false)

    expect(onSave).toHaveBeenCalledTimes(1)
    const payload = sent(onSave)
    expect(payload.memo).toBe('Checked against the PDF')
    expect(payload.exchangeRate).toBe(1.3512)
    expect(payload.lines.map((l) => l.taxAmount)).toEqual([12.99, null])
  })

  it('drops a tax override once its line amount or tax code changes', async () => {
    const { vm, onSave } = mountEditor(foreignBill)
    await flushPromises()

    vm.lines[0]!.unitPrice = 110
    await vm.save(false)
    expect(sent(onSave).lines[0]!.taxAmount).toBeNull()

    onSave.mockClear()
    vm.lines[0]!.unitPrice = 100
    vm.lines[0]!.taxCodeId = 'gst'
    await vm.save(false)
    expect(sent(onSave).lines[0]!.taxAmount).toBeNull()
  })

  it('drops the manual rate once the currency changes', async () => {
    const { vm, onSave } = mountEditor(foreignBill)
    await flushPromises()

    vm.currency = 'eur'
    await vm.save(false)

    expect(sent(onSave).currency).toBe('EUR')
    expect(sent(onSave).exchangeRate).toBeNull()
  })

  it('keeps the rate when the currency is only re-typed in another case', async () => {
    const { vm, onSave } = mountEditor(foreignBill)
    await flushPromises()

    vm.currency = ' usd '
    await vm.save(false)

    expect(sent(onSave).exchangeRate).toBe(1.3512)
  })

  it('sends no rate for a draft that resolves it at posting', async () => {
    // A stored 0 is "look it up in the rate table when posted", not a rate.
    const { vm, onSave } = mountEditor({ ...foreignBill, exchangeRate: 0 })
    await flushPromises()

    await vm.save(false)

    expect(sent(onSave).exchangeRate).toBeNull()
  })

  it('carries an expense line override the same way', async () => {
    const expense: EditableDocument = {
      id: 'e1',
      partyId: null,
      paidFromAccountId: 'bank',
      docDate: '2026-07-01',
      currency: 'CAD',
      lines: [{ description: 'Fuel', accountId: 'a3', amount: 80, taxCodeId: 'hst', taxAmount: 9.2 }],
    }
    const { vm, onSave } = mountEditor(expense, 'expense')
    await flushPromises()

    await vm.save(false)
    expect(sent(onSave).lines[0]!.taxAmount).toBe(9.2)

    onSave.mockClear()
    vm.lines[0]!.amount = 90
    await vm.save(false)
    expect(sent(onSave).lines[0]!.taxAmount).toBeNull()
  })
})

// "Save draft" and "Save & post" each had their own busy flag, so a second
// click before the first save returned started another save: on a new
// document, two drafts.
describe('DocumentEditor saves one at a time', () => {
  it('★ a second click while a save is in flight does not start another save', async () => {
    const { vm, onSave } = mountEditor({ ...foreignBill, id: undefined } as EditableDocument)
    await flushPromises()
    let release: () => void = () => {}
    onSave.mockImplementationOnce(() => new Promise<void>((r) => { release = r }))

    const first = vm.save(false)
    const second = vm.save(true)
    release()
    await Promise.all([first, second])

    expect(onSave).toHaveBeenCalledTimes(1)
  })
})
