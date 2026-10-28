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

describe('TSchemaForm - date fields write the shape they read', () => {
  // NDatePicker answers with the local midnight of the picked day in epoch
  // milliseconds. The backend binds date fields to DateTime, which a JSON number
  // is not, so a form that forwarded its model was refused with 400.
  it('writes a picked day back as a local calendar date (YYYY-MM-DD)', async () => {
    const { NDatePicker } = await import('naive-ui')
    const model: Record<string, unknown> = { birthday: '1990-05-17' }
    const wrapper = mount(TSchemaForm, {
      props: { schema: [{ key: 'birthday', type: 'date', label: 'Birthday' }], model },
    })
    const picker = wrapper.findComponent(NDatePicker)
    // The picker is fed the stored calendar date as that local day.
    expect(picker.props('value')).toBe(new Date(1990, 4, 17).getTime())

    picker.vm.$emit('update:value', new Date(2026, 0, 31).getTime())
    expect(model.birthday).toBe('2026-01-31')

    picker.vm.$emit('update:value', null)
    expect(model.birthday).toBeNull()
  })

  it('leaves an untouched value exactly as it arrived', () => {
    const model: Record<string, unknown> = { expiredAt: '2026-12-31T23:59:59Z' }
    mount(TSchemaForm, {
      props: { schema: [{ key: 'expiredAt', type: 'date', label: 'Expires' }], model },
    })
    expect(model.expiredAt).toBe('2026-12-31T23:59:59Z')
  })
})

describe('TSchemaForm - hint placement', () => {
  const longHint = 'Three-letter code. Currency and billing cycle cannot change while the plan has live subscriptions.'

  it('shows an inline hint as a line under the control by default', () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'currency', type: 'text', label: 'Currency', hint: longHint }],
        model: { currency: 'CAD' },
      },
    })
    expect(wrapper.find('.t-form-schema__hint').text()).toBe(longHint)
    expect(wrapper.find('.t-hint').exists()).toBe(false)
  })

  it('puts a popover hint behind an info icon next to the label, not under the control', () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'currency', type: 'text', label: 'Currency', hint: longHint, hintMode: 'popover' }],
        model: { currency: 'CAD' },
      },
    })
    expect(wrapper.find('.t-form-schema__hint').exists()).toBe(false)
    const label = wrapper.find('.t-form-schema__label')
    expect(label.text()).toContain('Currency')
    expect(label.find('.t-hint').attributes('aria-label')).toBe(longHint)
  })

  it('puts a popover section hint next to the heading', () => {
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [{ key: 'price', type: 'number', label: 'Price', section: 'pricing' }],
        sections: [{ key: 'pricing', label: 'Pricing', hint: longHint, hintMode: 'popover' }],
        model: { price: 1 },
      },
    })
    expect(wrapper.find('.t-form-schema__section-hint').exists()).toBe(false)
    expect(wrapper.find('.t-form-schema__section-title .t-hint').attributes('aria-label')).toBe(longHint)
  })
})

describe('TSchemaForm - every control fills its grid cell', () => {
  // naive's number and date inputs are inline and size to their content, so next to a full-width text input
  // in the same row they read as half-width. One width rule for a row: every control fills its cell.
  it('gives number and date inputs the full cell width, like text and select', async () => {
    const { NDatePicker, NInputNumber } = await import('naive-ui')
    const wrapper = mount(TSchemaForm, {
      props: {
        schema: [
          { key: 'price', type: 'number', label: 'Price' },
          { key: 'startsOn', type: 'date', label: 'Starts on' },
        ],
        model: { price: 10, startsOn: null },
      },
    })
    expect(wrapper.findComponent(NInputNumber).classes()).toContain('t-form-schema__fill')
    expect(wrapper.findComponent(NDatePicker).classes()).toContain('t-form-schema__fill')
  })
})
