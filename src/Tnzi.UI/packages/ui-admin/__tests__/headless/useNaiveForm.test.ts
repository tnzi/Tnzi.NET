/**
 * `useNaiveForm` - partial validation contract.
 *
 * `validateFields(paths)` has no in-framework call site (it is a public helper
 * for consuming apps), so nothing else in this suite would notice it breaking.
 * These tests lock the one thing that matters about it: WHAT it hands naive-ui
 * as the filter. naive-ui 2.45 accepts a path array there and selects form
 * items by their `path` prop; the predicate form this replaced filtered RULES
 * by `rule.key`, which silently skipped any field whose rule carried no key.
 */
import { describe, it, expect, vi } from 'vitest'
import type { FormInst } from 'naive-ui'
import { useNaiveForm } from '../../src/headless/useNaiveForm'

function bindForm(validate = vi.fn().mockResolvedValue(undefined)) {
  const form = useNaiveForm()
  form.formRef.value = { validate, restoreValidation: vi.fn() } as unknown as FormInst
  return { form, validate }
}

describe('useNaiveForm', () => {
  it('passes the paths straight through as naive-ui\'s validate filter', async () => {
    const { form, validate } = bindForm()

    await form.validateFields(['account', 'code'])

    expect(validate).toHaveBeenCalledTimes(1)
    const [callback, filter] = validate.mock.calls[0]
    expect(callback).toBeUndefined()
    // An array - NOT a predicate. A predicate would be applied to rules, and a
    // field whose rule has no `key` would then never be validated at all.
    expect(filter).toEqual(['account', 'code'])
    expect(typeof filter).not.toBe('function')
  })

  it('validates every field when no paths are given', async () => {
    const { form, validate } = bindForm()

    await form.validate()

    expect(validate).toHaveBeenCalledWith()
  })

  it('rejects with the naive-ui validation errors', async () => {
    const errors = [[{ message: 'required' }]]
    const { form } = bindForm(vi.fn().mockRejectedValue(errors))

    await expect(form.validateFields(['account'])).rejects.toBe(errors)
  })

  it('throws a named error when the form ref is not bound yet', async () => {
    const form = useNaiveForm()

    await expect(form.validateFields(['account'])).rejects.toThrow('NForm ref not yet bound')
  })
})
