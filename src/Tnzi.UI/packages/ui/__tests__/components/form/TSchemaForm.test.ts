import { describe, it, expect } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { provideI18n, resetI18n } from '@tnzi/core/adapters/i18n'
import TSchemaForm, { type FieldRenderer } from '../../../src/components/form/TSchemaForm'

describe('TSchemaForm - custom field renderers (F3)', () => {
  it('uses a custom field renderer for a non-builtin field type', () => {
    const markdown: FieldRenderer = (ctx) => h('div', { class: 'custom-md' }, String(ctx.value))
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'bio', type: 'markdown', label: 'Bio' }],
        model: { bio: 'hello world' },
        fieldRenderers: { markdown },
      },
    })
    expect(wrapper.find('.custom-md').exists()).toBe(true)
    expect(wrapper.find('.custom-md').text()).toContain('hello world')
  })

  it('passes readonly + onUpdate into the custom renderer context', () => {
    let captured: { readonly: boolean; hasOnUpdate: boolean } | null = null
    const probe: FieldRenderer = (ctx) => {
      captured = { readonly: ctx.readonly, hasOnUpdate: typeof ctx.onUpdate === 'function' }
      return h('span', { class: 'probe' }, '')
    }
    mount(TSchemaForm, {
      props: {
        schema: [{ key: 'c', type: 'color', label: 'Color' }],
        model: { c: '#fff' },
        readonly: true,
        fieldRenderers: { color: probe },
      },
    })
    expect(captured).not.toBeNull()
    expect(captured!.readonly).toBe(true)
    expect(captured!.hasOnUpdate).toBe(true)
  })

  it('falls back to rendering the value for unknown types without a renderer', () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'x', type: 'mystery', label: 'X' }],
        model: { x: 'fallback-value' },
      },
    })
    // Must not throw; the value should still surface somewhere (read-only fallback).
    expect(wrapper.html()).toContain('fallback-value')
  })

  it('still renders builtin text fields as a naive input (regression)', () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'name', type: 'text', label: 'Name' }],
        model: { name: 'a' },
      },
    })
    expect(wrapper.find('.n-input').exists()).toBe(true)
  })
})

describe('TSchemaForm - required fields validate, not just mark', () => {
  it('validate() rejects while a required text field is blank and shows the message inline', async () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'name', type: 'text', label: 'Name', required: true }],
        model: { name: '' },
      },
    })
    const ok = await (wrapper.vm as unknown as { validate: () => Promise<boolean> }).validate()
    expect(ok).toBe(false)
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.n-form-item-feedback').text()).toContain('This field is required')
  })

  it('validate() resolves once the required field is filled', async () => {
    const model = { name: '' }
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'name', type: 'text', label: 'Name', required: true }],
        model,
      },
    })
    const vm = wrapper.vm as unknown as { validate: () => Promise<boolean> }
    expect(await vm.validate()).toBe(false)
    model.name = 'Ada'
    await wrapper.vm.$nextTick()
    expect(await vm.validate()).toBe(true)
  })

  it('treats 0 and false as values, and only null / empty as missing', async () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [
          { key: 'qty', type: 'number', label: 'Qty', required: true },
          { key: 'on', type: 'switch', label: 'On', required: true },
          { key: 'kind', type: 'select', label: 'Kind', required: true, options: [{ label: 'A', value: 'a' }] },
        ],
        model: { qty: 0, on: false, kind: null },
      },
    })
    const vm = wrapper.vm as unknown as { validate: () => Promise<boolean> }
    expect(await vm.validate()).toBe(false)
    await wrapper.vm.$nextTick()
    const feedback = wrapper.findAll('.n-form-item-feedback').filter((el) => el.text().length > 0)
    expect(feedback).toHaveLength(1)
    expect(wrapper.findAll('.n-form-item')[2].text()).toContain('This field is required')
  })

  it('validates every section block, not just the first', async () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [
          { key: 'a', type: 'text', label: 'A', required: true, section: 's1' },
          { key: 'b', type: 'text', label: 'B', required: true, section: 's2' },
        ],
        sections: [
          { key: 's1', label: 'One' },
          { key: 's2', label: 'Two' },
        ],
        model: { a: 'filled', b: '' },
      },
    })
    const vm = wrapper.vm as unknown as { validate: () => Promise<boolean> }
    expect(await vm.validate()).toBe(false)
  })

  it('takes the message from the core catalog in the active locale', async () => {
    provideI18n(undefined, 'zh-CN')
    try {
      const wrapper = mount(TSchemaForm, {
        props: {
          schema: [{ key: 'name', type: 'text', label: 'Name', required: true }],
          model: { name: '' },
          // A page translator humanises misses instead of returning empty, so the
          // message must not depend on it.
          translate: (key: string) => key.replace('.', ' '),
        },
      })
      await (wrapper.vm as unknown as { validate: () => Promise<boolean> }).validate()
      await wrapper.vm.$nextTick()
      expect(wrapper.find('.n-form-item-feedback').text()).toContain('此字段为必填项')
    } finally {
      resetI18n()
    }
  })

  it('does not validate a readonly form', async () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'name', type: 'text', label: 'Name', required: true }],
        model: { name: '' },
        readonly: true,
      },
    })
    expect(await (wrapper.vm as unknown as { validate: () => Promise<boolean> }).validate()).toBe(true)
  })

  it('registers with a provided form host so the host can validate slotted forms', async () => {
    const { provideFormHost } = await import('../../../src/headless/form/form-host')
    let host: ReturnType<typeof provideFormHost> | null = null
    const Host = defineComponent({
      setup(_, { slots }) {
        host = provideFormHost()
        return () => h('div', slots.default?.())
      },
    })
    const wrapper = mount(Host, {
      slots: {
        default: () =>
          h(TSchemaForm, {
            schema: [{ key: 'name', type: 'text', label: 'Name', required: true }],
            model: { name: '' },
          }),
      },
    })
    expect(host).not.toBeNull()
    expect(await host!.validate()).toBe(false)
    wrapper.unmount()
    // Once the form is gone the host has nothing left to check.
    expect(await host!.validate()).toBe(true)
  })
})
