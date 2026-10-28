import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TJsonEditor from '../../../src/components/inputs/TJsonEditor.vue'

/**
 * The editor is the built-in `json` form field, so whatever it emits is what
 * the form saves. Formatting must only move whitespace: a JSON.parse /
 * JSON.stringify round trip rounds integers past 2^53 (a 19-digit Snowflake id
 * comes back as a different id), collapses duplicate keys and respells numbers.
 */

const SNOWFLAKE = '1234567890123456789'
const STORED = `{"tenantId":${SNOWFLAKE},"ratio":1.50}`

function mountEditor(props: Record<string, unknown> = {}) {
  return mount(TJsonEditor, { props: { value: STORED, ...props } })
}

function lastEmitted(wrapper: ReturnType<typeof mountEditor>): string | undefined {
  const events = wrapper.emitted('update:value') as string[][] | undefined
  return events?.at(-1)?.[0]
}

describe('TJsonEditor', () => {
  it('★ clicking into the box and out again does not rewrite the stored value', async () => {
    const wrapper = mountEditor()
    const textarea = wrapper.find('textarea')
    await textarea.trigger('focus')
    await textarea.trigger('blur')
    expect(wrapper.emitted('update:value')).toBeUndefined()
  })

  it('★ a read-only editor never emits, edited or not', async () => {
    const wrapper = mountEditor({ readonly: true })
    const textarea = wrapper.find('textarea')
    await textarea.trigger('focus')
    await textarea.trigger('blur')
    // An input event still reaching the element (scripted, or a browser that
    // lets one through) must not turn into a write either.
    await textarea.trigger('focus')
    await textarea.setValue('{"a":1}')
    await textarea.trigger('blur')
    ;(wrapper.vm as unknown as { format: () => void }).format()
    ;(wrapper.vm as unknown as { minify: () => void }).minify()
    expect(wrapper.emitted('update:value')).toBeUndefined()
  })

  it('★ auto-format after an edit keeps a 19-digit id exactly as typed', async () => {
    const wrapper = mountEditor({ value: '' })
    const textarea = wrapper.find('textarea')
    await textarea.trigger('focus')
    await textarea.setValue(STORED)
    await wrapper.setProps({ value: STORED })
    await textarea.trigger('blur')

    const out = lastEmitted(wrapper)
    expect(out).toBe(`{\n  "tenantId": ${SNOWFLAKE},\n  "ratio": 1.50\n}`)
    expect(out).not.toContain('1234567890123456800')
  })

  it('★ the Format button keeps big integers, number spellings and duplicate keys', async () => {
    const wrapper = mountEditor({ value: `{"a":${SNOWFLAKE},"a":2,"e":1E3}` })
    ;(wrapper.vm as unknown as { format: () => void }).format()
    expect(lastEmitted(wrapper)).toBe(`{\n  "a": ${SNOWFLAKE},\n  "a": 2,\n  "e": 1E3\n}`)
  })

  it('★ the Minify button keeps big integers', async () => {
    const wrapper = mountEditor({ value: `{\n  "tenantId": ${SNOWFLAKE}\n}` })
    ;(wrapper.vm as unknown as { minify: () => void }).minify()
    expect(lastEmitted(wrapper)).toBe(`{"tenantId":${SNOWFLAKE}}`)
  })

  it('reports invalid JSON on blur without emitting a value', async () => {
    const wrapper = mountEditor({ value: '' })
    const textarea = wrapper.find('textarea')
    await textarea.trigger('focus')
    await textarea.setValue('{"a":')
    await wrapper.setProps({ value: '{"a":' })
    const emittedBefore = (wrapper.emitted('update:value') ?? []).length
    await textarea.trigger('blur')
    expect((wrapper.emitted('update:value') ?? []).length).toBe(emittedBefore)
    expect((wrapper.vm as unknown as { isValid: boolean }).isValid).toBe(false)
  })

  it('leaves an edited value alone when it is already formatted', async () => {
    const formatted = `{\n  "a": 1\n}`
    const wrapper = mountEditor({ value: '' })
    const textarea = wrapper.find('textarea')
    await textarea.trigger('focus')
    await textarea.setValue(formatted)
    await wrapper.setProps({ value: formatted })
    const emittedBefore = (wrapper.emitted('update:value') ?? []).length
    await textarea.trigger('blur')
    expect((wrapper.emitted('update:value') ?? []).length).toBe(emittedBefore)
  })
})
