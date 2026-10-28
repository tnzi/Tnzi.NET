import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { NForm, NFormItem, type FormInst } from 'naive-ui'
import type { IFormRule } from '@tnzi/core'
import {
  convertToMenuOptions,
  convertFormRule,
  convertFormRules,
  applyThemeToDOM,
  applyLanguageToDOM,
} from '../../src/utils/naive-helpers'

describe('utils/naive-helpers', () => {
  describe('convertToMenuOptions', () => {
    it('maps key/label/disabled', () => {
      const result = convertToMenuOptions([
        { key: 'a', label: 'Alpha', disabled: true },
        { key: 'b', label: 'Beta', disabled: false },
      ] as any)
      expect(result).toEqual([
        { key: 'a', label: 'Alpha', disabled: true },
        { key: 'b', label: 'Beta', disabled: false },
      ])
    })

    it('filters out hidden items', () => {
      const result = convertToMenuOptions([
        { key: 'a', label: 'Alpha' },
        { key: 'b', label: 'Beta', hidden: true },
      ] as any)
      expect(result.map((r) => r.key)).toEqual(['a'])
    })

    it('recursively converts children', () => {
      const result = convertToMenuOptions([
        { key: 'parent', label: 'P', children: [{ key: 'child', label: 'C' }] },
      ] as any)
      expect(result[0]!.children).toEqual([{ key: 'child', label: 'C', disabled: undefined }])
    })

    it('omits children when empty', () => {
      const result = convertToMenuOptions([
        { key: 'a', label: 'A', children: [] },
      ] as any)
      expect(result[0]).not.toHaveProperty('children')
    })
  })

  describe('convertFormRule', () => {
    it('copies simple fields', () => {
      const rule = convertFormRule({ required: true, message: 'req', min: 1, max: 10, pattern: /x/, trigger: 'blur' } as any)
      expect(rule.required).toBe(true)
      expect(rule.message).toBe('req')
      expect(rule.min).toBe(1)
      expect(rule.max).toBe(10)
      expect(rule.pattern).toBeInstanceOf(RegExp)
      expect(rule.trigger).toBe('blur')
    })

    it('omits undefined fields', () => {
      const rule = convertFormRule({} as any)
      expect(rule.required).toBeUndefined()
      expect(rule.message).toBeUndefined()
    })

    it('wraps custom validator: true → passes silently', async () => {
      const validator = vi.fn().mockResolvedValue(true)
      const rule = convertFormRule({ validator } as any)
      await expect(rule.validator!({} as any, 'val', () => {})).resolves.toBeUndefined()
      expect(validator).toHaveBeenCalledWith('val')
    })

    it('wraps custom validator: false → throws with message', async () => {
      const validator = vi.fn().mockResolvedValue(false)
      const rule = convertFormRule({ validator, message: 'bad' } as any)
      await expect(rule.validator!({} as any, 'v', () => {})).rejects.toThrow('bad')
    })

    it('wraps custom validator: false without message → generic text', async () => {
      const validator = vi.fn().mockResolvedValue(false)
      const rule = convertFormRule({ validator } as any)
      await expect(rule.validator!({} as any, 'v', () => {})).rejects.toThrow('Validation failed')
    })

    it('wraps custom validator: string result → throws with that string', async () => {
      const validator = vi.fn().mockResolvedValue('specific error')
      const rule = convertFormRule({ validator } as any)
      await expect(rule.validator!({} as any, 'v', () => {})).rejects.toThrow('specific error')
    })
  })

  describe('convertFormRule: a required value of only whitespace is missing (the backend [Required] refuses it)', () => {
    // Run the converted rule through a real NForm: async-validator decides
    // which checks run from the rule's shape (a `validator` shadows `required`,
    // `min`, `max` and `pattern`), so asserting fields on the rule proves nothing.
    async function errorsFor(rule: IFormRule, value: unknown): Promise<string[]> {
      const formRef = ref<FormInst | null>(null)
      const wrapper = mount(defineComponent({
        setup: () => () =>
          h(NForm, { ref: formRef, model: { v: value }, rules: convertFormRules({ v: [rule] }) }, () =>
            h(NFormItem, { path: 'v', label: 'V' }, () => h('span'))),
      }))
      try {
        await formRef.value!.validate()
        return []
      } catch (errors) {
        return (errors as Array<Array<{ message?: string }>>).flat().map((e) => e.message ?? '')
      } finally {
        wrapper.unmount()
      }
    }

    it.each([
      ['a bare required rule', { required: true, message: 'req' }],
      ['a required rule with a trigger', { required: true, message: 'req', trigger: 'blur' }],
      ['a required rule with a length range', { required: true, message: 'req', min: 2, max: 10 }],
      ['a required rule with a pattern', { required: true, message: 'req', pattern: /^[\s\w]+$/ }],
      ['a required rule with a custom validator', { required: true, message: 'req', validator: () => true }],
    ] as Array<[string, IFormRule]>)('%s refuses a whitespace-only value', async (_case, rule) => {
      expect(await errorsFor(rule, '   ')).toEqual(['req'])
      expect(await errorsFor(rule, ' \t\n ')).toEqual(['req'])
      // Positive control: a real value with spaces around it passes the same rule.
      expect(await errorsFor(rule, ' ok ')).toEqual([])
    })

    it('a required rule with a custom validator refuses an empty value (async-validator never read `required` next to a validator)', async () => {
      const rule: IFormRule = { required: true, message: 'req', validator: () => true }
      expect(await errorsFor(rule, '')).toEqual(['req'])
      expect(await errorsFor(rule, null)).toEqual(['req'])
      expect(await errorsFor(rule, [])).toEqual(['req'])
    })

    it('a bare required rule still treats an empty list as missing and 0 / false as values', async () => {
      const rule: IFormRule = { required: true, message: 'req' }
      expect(await errorsFor(rule, [])).toEqual(['req'])
      expect(await errorsFor(rule, ['a'])).toEqual([])
      expect(await errorsFor(rule, 0)).toEqual([])
      expect(await errorsFor(rule, false)).toEqual([])
    })

    it('a required rule with a length range still checks the length', async () => {
      const rule: IFormRule = { required: true, message: 'req', min: 3 }
      expect(await errorsFor(rule, 'ab')).toEqual(['req'])
      expect(await errorsFor(rule, 'abc')).toEqual([])
    })

    it('a required rule with a pattern still checks the pattern, and a validator still runs', async () => {
      const patterned: IFormRule = { required: true, message: 'req', pattern: /^\d+$/ }
      expect(await errorsFor(patterned, 'abc')).toEqual(['req'])
      expect(await errorsFor(patterned, '123')).toEqual([])
      expect(await errorsFor({ required: true, validator: (v) => v === 'ok' || 'not ok' }, 'no')).toEqual(['not ok'])
    })

    it('a required rule without a message keeps the form default messages', async () => {
      expect(await errorsFor({ required: true }, '  ')).toEqual(['v is required'])
      expect(await errorsFor({ required: true }, '')).toEqual(['v is required'])
      expect(await errorsFor({ required: true, pattern: /^\d+$/ }, 'abc')).toEqual(['v value abc does not match pattern /^\\d+$/'])
    })

    it('an optional rule accepts a whitespace-only value', async () => {
      expect(await errorsFor({ max: 10 }, '   ')).toEqual([])
    })
  })

  describe('convertFormRules', () => {
    it('converts a field-map of rules', () => {
      const result = convertFormRules({
        name: [{ required: true, message: 'req' } as any],
        email: [{ pattern: /@/ } as any],
      })
      expect(result.name).toHaveLength(1)
      expect(result.name![0]!.required).toBe(true)
      expect(result.email).toHaveLength(1)
      expect(result.email![0]!.pattern).toBeInstanceOf(RegExp)
    })
  })

  describe('applyThemeToDOM', () => {
    let originalMatchMedia: typeof window.matchMedia
    beforeEach(() => {
      originalMatchMedia = window.matchMedia
      document.documentElement.classList.remove('dark')
    })
    afterEach(() => {
      window.matchMedia = originalMatchMedia
    })

    it('adds dark class when theme=dark', () => {
      applyThemeToDOM('dark')
      expect(document.documentElement.classList.contains('dark')).toBe(true)
    })

    it('removes dark class when theme=light', () => {
      document.documentElement.classList.add('dark')
      applyThemeToDOM('light')
      expect(document.documentElement.classList.contains('dark')).toBe(false)
    })

    it('auto theme defers to matchMedia (dark)', () => {
      window.matchMedia = vi.fn().mockReturnValue({ matches: true }) as any
      applyThemeToDOM('auto')
      expect(document.documentElement.classList.contains('dark')).toBe(true)
    })

    // Starts from the dark class already set, so a broken 'auto' resolution
    // cannot pass by simply never adding it.
    it('auto theme defers to matchMedia (light)', () => {
      document.documentElement.classList.add('dark')
      window.matchMedia = vi.fn().mockReturnValue({ matches: false }) as any
      applyThemeToDOM('auto')
      expect(document.documentElement.classList.contains('dark')).toBe(false)
    })
  })

  describe('applyLanguageToDOM', () => {
    it('sets documentElement.lang', () => {
      applyLanguageToDOM('zh-CN')
      expect(document.documentElement.lang).toBe('zh-CN')
      applyLanguageToDOM('en-US')
      expect(document.documentElement.lang).toBe('en-US')
    })
  })
})
