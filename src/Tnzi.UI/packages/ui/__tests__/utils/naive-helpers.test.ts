import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
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

    it('system theme defers to matchMedia (dark)', () => {
      window.matchMedia = vi.fn().mockReturnValue({ matches: true }) as any
      applyThemeToDOM('system')
      expect(document.documentElement.classList.contains('dark')).toBe(true)
    })

    it('system theme defers to matchMedia (light)', () => {
      window.matchMedia = vi.fn().mockReturnValue({ matches: false }) as any
      applyThemeToDOM('system')
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
