import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TMessageComposer from '../../../src/components/chat/TMessageComposer.vue'

const IconStub = {
  name: 'Icon',
  props: ['icon', 'width'],
  template: '<span class="icon-stub" :data-icon="icon" />',
}

function mountComposer(disabled = false) {
  return mount(TMessageComposer, {
    props: { disabled },
    global: {
      stubs: { Icon: IconStub },
    },
  })
}

describe('TMessageComposer', () => {
  it('clicking Send emits "send" with the textarea text and then clears it', async () => {
    const wrapper = mountComposer()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Hello world')
    await wrapper.find('.t-composer__send').trigger('click')
    const emitted = wrapper.emitted('send')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual(['Hello world'])
    // textarea should be cleared after send
    expect(textarea.element.value).toBe('')
  })

  it('pressing Enter (without Shift) triggers send and clears textarea', async () => {
    const wrapper = mountComposer()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Via enter key')
    await textarea.trigger('keydown', { key: 'Enter', shiftKey: false })
    const emitted = wrapper.emitted('send')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual(['Via enter key'])
    expect(textarea.element.value).toBe('')
  })

  it('pressing Shift+Enter does NOT send', async () => {
    const wrapper = mountComposer()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Line one')
    await textarea.trigger('keydown', { key: 'Enter', shiftKey: true })
    expect(wrapper.emitted('send')).toBeFalsy()
  })

  it('sending empty or whitespace-only text is a no-op', async () => {
    const wrapper = mountComposer()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('   ')
    await wrapper.find('.t-composer__send').trigger('click')
    expect(wrapper.emitted('send')).toBeFalsy()
  })

  it('does not emit send when disabled', async () => {
    const wrapper = mountComposer(true)
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Should not send')
    await wrapper.find('.t-composer__send').trigger('click')
    expect(wrapper.emitted('send')).toBeFalsy()
  })
})
