import { describe, it, expect } from 'vitest'
import { h } from 'vue'
import { mount } from '@vue/test-utils'
import TSchemaForm, { type FieldRenderer } from '../../../src/components/form/TSchemaForm'

describe('TSchemaForm — custom field renderers (F3)', () => {
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
