/**
 * TSegmented's icon size: 16px by default, or `--tnzi-ai-segmented-icon-size`
 * from the group or an element around it; the options' height, an icon-only
 * option's width and the dot and running ring on its corner follow it.
 *
 * happy-dom computes neither `var()` nor `calc()`, so the sizes are worked out
 * here from the component's own stylesheet: each length the rules below declare
 * is evaluated with the icon size given (or not), the option's box for `%`.
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const sfc = readFileSync(resolve(__dirname, '../../src/components/layout/TSegmented.vue'), 'utf8')
const style = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '')

function declaration(selector: string, property: string): string {
  const body = Array.from(style.matchAll(/([^{}]+)\{([^{}]*)\}/g)).find((m) => m[1]!.trim() === selector)?.[2]
  if (body === undefined) throw new Error(`no rule for ${selector}`)
  const value = new RegExp(`(?:^|;)\\s*${property.replace(/[-]/g, '\\-')}\\s*:\\s*([^;]+)`).exec(body)?.[1]
  if (value === undefined) throw new Error(`no ${property} in ${selector}`)
  return value.replace(/\s+/g, ' ').trim()
}

/* What the group's custom properties resolve to: its own declarations, over
   what it inherits (`inherited`, the consumer's). */
function groupVars(inherited: Record<string, string>): Record<string, string> {
  const vars: Record<string, string> = { ...inherited }
  for (const name of ['--t-segmented-pad', '--t-segmented-icon']) vars[name] = declaration('.t-segmented', name)
  return vars
}

/* A small evaluator for the lengths these rules use: px and % numbers, plain
   numbers, + - * /, parentheses, calc(), max(), var() with a fallback. */
function evaluate(expression: string, vars: Record<string, string>, percentOf = 0): number {
  let at = 0
  const text = expression
  const space = () => {
    while (text[at] === ' ') at += 1
  }
  const args = (): string[] => {
    const parts: string[] = []
    let depth = 0
    let start = at
    for (; at < text.length; at += 1) {
      const c = text[at]
      if (c === '(') depth += 1
      else if (c === ')') {
        if (depth === 0) break
        depth -= 1
      } else if (c === ',' && depth === 0) {
        parts.push(text.slice(start, at))
        start = at + 1
      }
    }
    parts.push(text.slice(start, at))
    at += 1
    return parts.map((p) => p.trim())
  }
  const primary = (): number => {
    space()
    if (text[at] === '(') {
      at += 1
      const value = sum()
      space()
      at += 1
      return value
    }
    const fn = /^(calc|max|var)\(/.exec(text.slice(at))
    if (fn) {
      at += fn[0].length
      const list = args()
      if (fn[1] === 'calc') return evaluate(list[0]!, vars, percentOf)
      if (fn[1] === 'max') return Math.max(...list.map((a) => evaluate(a, vars, percentOf)))
      const [name, ...fallback] = list
      const value = vars[name!] ?? fallback.join(',')
      if (!value) throw new Error(`${name} is not set and has no fallback`)
      return evaluate(value, vars, percentOf)
    }
    const number = /^-?\d+(?:\.\d+)?(px|%)?/.exec(text.slice(at))
    if (!number) throw new Error(`cannot read "${text.slice(at)}" in "${text}"`)
    at += number[0].length
    const value = Number.parseFloat(number[0])
    return number[1] === '%' ? (value / 100) * percentOf : value
  }
  const product = (): number => {
    let value = primary()
    for (space(); text[at] === '*' || text[at] === '/'; space()) {
      const op = text[at]
      at += 1
      const next = primary()
      value = op === '*' ? value * next : value / next
    }
    return value
  }
  function sum(): number {
    let value = product()
    for (space(); text[at] === '+' || text[at] === '-'; space()) {
      const op = text[at]
      at += 1
      const next = product()
      value = op === '+' ? value + next : value - next
    }
    return value
  }
  return sum()
}

function sizes(inherited: Record<string, string>) {
  const vars = groupVars(inherited)
  const icon = evaluate(declaration('.t-segmented__option :deep(svg)', 'width'), vars)
  const height = evaluate(declaration('.t-segmented__option', 'height'), vars)
  const width = evaluate(declaration('.t-segmented__option.is-icon', 'width'), vars)
  const pad = evaluate(vars['--t-segmented-pad']!, vars)
  const corner = (selector: string) => ({
    top: evaluate(declaration(selector, 'top'), vars, height),
    right: evaluate(declaration(selector, 'right'), vars, width),
  })
  return {
    icon,
    iconHeight: evaluate(declaration('.t-segmented__option :deep(svg)', 'height'), vars),
    height,
    group: height + 2 * pad,
    width,
    /* The icon is centred in the option: where its top and right edges are. */
    iconTop: (height - icon) / 2,
    iconRight: (width - icon) / 2,
    dot: corner('.t-segmented__option.is-icon .t-segmented__dot'),
    running: corner('.t-segmented__option.is-icon .t-segmented__running'),
  }
}

describe('TSegmented icon size', () => {
  it('is 16px by default, in the 26px options of a 30px group, an icon-only option 32px wide', () => {
    const s = sizes({})
    expect(s.icon).toBe(16)
    expect(s.iconHeight).toBe(16)
    expect(s.height).toBe(26)
    expect(s.group).toBe(30)
    expect(s.width).toBe(32)
    expect(s.dot).toEqual({ top: 5, right: 7 })
    expect(s.running).toEqual({ top: 3, right: 5 })
  })

  it('follows --tnzi-ai-segmented-icon-size: 20px icons in 28px options, 34px wide, a 32px group', () => {
    const s = sizes({ '--tnzi-ai-segmented-icon-size': '20px' })
    expect(s.icon).toBe(20)
    expect(s.iconHeight).toBe(20)
    expect(s.height).toBe(28)
    expect(s.group).toBe(32)
    expect(s.width).toBe(34)
  })

  it('keeps the dot and the running ring on the icon corner whatever its size', () => {
    for (const size of ['16px', '20px', '24px', '12px']) {
      const s = sizes({ '--tnzi-ai-segmented-icon-size': size })
      expect(s.dot.top, size).toBe(s.iconTop)
      expect(s.dot.right, size).toBe(s.iconRight - 1)
      expect(s.running.top, size).toBe(s.iconTop - 2)
      expect(s.running.right, size).toBe(s.iconRight - 3)
    }
  })

  it('never squeezes an option around a larger icon, and keeps the toolbar size for smaller ones', () => {
    for (const size of [12, 14, 16, 18, 20, 24, 28]) {
      const s = sizes({ '--tnzi-ai-segmented-icon-size': `${size}px` })
      expect(s.height - s.icon, `${size}px`).toBeGreaterThanOrEqual(8)
      expect(s.width - s.icon, `${size}px`).toBeGreaterThanOrEqual(14)
      expect(s.height, `${size}px`).toBeGreaterThanOrEqual(26)
      expect(s.width, `${size}px`).toBeGreaterThanOrEqual(32)
    }
  })

  it('takes the consumer value from the group or around it: the component never sets the public property itself', () => {
    expect(declaration('.t-segmented', '--t-segmented-icon')).toBe('var(--tnzi-ai-segmented-icon-size, 16px)')
    expect(style).not.toMatch(/--tnzi-ai-segmented-icon-size\s*:/)
    expect(declaration('.t-segmented__option :deep(svg)', 'width')).toBe('var(--t-segmented-icon)')
    expect(declaration('.t-segmented__option :deep(svg)', 'height')).toBe('var(--t-segmented-icon)')
  })
})
