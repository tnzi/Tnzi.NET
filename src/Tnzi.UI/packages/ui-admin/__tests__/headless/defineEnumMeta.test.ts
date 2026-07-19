import { describe, it, expect } from 'vitest'
import { defineEnumMeta } from '../../src/headless/defineEnumMeta'

const t = (k: string): string => ({ 'k.active': '进行中', 'k.closed': '已结' } as Record<string, string>)[k] ?? k

describe('defineEnumMeta', () => {
  it('options resolve labels via labelKey', () => {
    const m = defineEnumMeta<string>(
      [
        { value: 'Active', labelKey: 'k.active', tone: 'success' },
        { value: 'Closed', labelKey: 'k.closed', tone: 'default' },
      ],
      t,
    )
    expect(m.options.value).toEqual([
      { value: 'Active', label: '进行中' },
      { value: 'Closed', label: '已结' },
    ])
  })

  it('label(value) resolves; null / not-in-specs → empty', () => {
    const m = defineEnumMeta<string>([{ value: 'A', label: 'Alpha' }], t)
    expect(m.label('A')).toBe('Alpha')
    expect(m.label(null)).toBe('')
    expect(m.label('missing')).toBe('')
  })

  it('falls back to label then String(value) when no labelKey', () => {
    const m = defineEnumMeta<string>([{ value: 'X', label: 'Ex' }, { value: 'Y' }], t)
    expect(m.label('X')).toBe('Ex')
    expect(m.label('Y')).toBe('Y')
  })

  it('tone(value) returns the tone or undefined', () => {
    const m = defineEnumMeta<string>([{ value: 'A', tone: 'warning' }], t)
    expect(m.tone('A')).toBe('warning')
    expect(m.tone('B')).toBeUndefined()
    expect(m.tone(null)).toBeUndefined()
  })

  it('badgeMapping is keyed by String(value) with type + labelKey', () => {
    const m = defineEnumMeta<number>([{ value: 1, label: 'One', labelKey: 'k.one', tone: 'info' }], t)
    expect(m.badgeMapping['1']).toEqual({ type: 'info', label: 'One', labelKey: 'k.one' })
  })

  it('badgeMapping tone defaults to "default"', () => {
    const m = defineEnumMeta<string>([{ value: 'A', label: 'a' }], t)
    expect(m.badgeMapping['A'].type).toBe('default')
  })
})
