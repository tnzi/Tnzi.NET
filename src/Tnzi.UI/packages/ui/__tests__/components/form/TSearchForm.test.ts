import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TSearchForm from '../../../src/components/form/TSearchForm.vue'

/**
 * Enter in the keyword box searches, except the Enter that commits an IME
 * candidate: the model still holds the text from before the composition at
 * that point, so searching then ran a query for a half-typed keyword.
 */
describe('TSearchForm', () => {
  it('searches on Enter', async () => {
    const wrapper = mount(TSearchForm, { props: { modelValue: 'invoice' } })
    await wrapper.find('input').trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('search')).toEqual([['invoice']])
    wrapper.unmount()
  })

  it('does not search on the Enter that commits an IME candidate', async () => {
    const wrapper = mount(TSearchForm, { props: { modelValue: 'fa' } })
    await wrapper.find('input').trigger('keydown', { key: 'Enter', isComposing: true })
    await wrapper.find('input').trigger('keydown', { key: 'Enter', keyCode: 229 })
    expect(wrapper.emitted('search')).toBeUndefined()
    wrapper.unmount()
  })
})
