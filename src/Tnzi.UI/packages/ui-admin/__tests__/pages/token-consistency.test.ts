import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'fs'
import { join, sep } from 'path'

/**
 * Static check: page CSS must use the real `@tnzi/ui` theme tokens, not
 * historical typos like `--tnzi-primary-color` or invented `--t-*` names
 * that fell back to hardcoded #06B6D4 cyan instead of following the user's
 * primary color.
 *
 * Rationale: a regression here re-introduces the issue the user reported —
 * "用量统计页面没有白色背景容器 / 评测页面外边距偏大" — where every
 * custom page used a parallel set of tokens whose fallbacks happened to
 * render as cyan, hiding the fact they never reacted to theme changes.
 *
 * Scope: `src/pages/**` only. `src/components/**` already uses the
 * canonical tokens (shared admin chrome).
 */

const ROOT = join(__dirname, '..', '..', 'src', 'pages')

interface BadToken {
  pattern: RegExp
  reason: string
}

const BANNED_TOKENS: BadToken[] = [
  // -color suffix variants — these have no entry in @tnzi/ui variables.css.
  { pattern: /--tnzi-primary-color(?!-rgb)\b/, reason: 'use --tnzi-primary (no -color suffix)' },
  { pattern: /--tnzi-success-color\b/, reason: 'use --tnzi-success' },
  { pattern: /--tnzi-warning-color\b/, reason: 'use --tnzi-warning' },
  { pattern: /--tnzi-error-color\b/, reason: 'use --tnzi-error' },
  { pattern: /--tnzi-info-color\b/, reason: 'use --tnzi-info' },
  // -base-* variants superseded by the canonical names.
  { pattern: /--tnzi-base-border\b/, reason: 'use --tnzi-border' },
  { pattern: /--tnzi-base-fill\b/, reason: 'use --tnzi-layout-bg or --tnzi-container-bg' },
  { pattern: /--tnzi-font-family-mono\b/, reason: 'use the literal font-stack — no token shipped' },
  // primary-color-suppl was an aliased rgba — replaced by inline rgb(... / alpha).
  { pattern: /--tnzi-primary-color-suppl\b/, reason: 'use rgb(var(--tnzi-primary-rgb) / 0.XX)' },
  // Stray --t-* tokens left over from initial scaffolding (no namespace prefix).
  { pattern: /--t-border\b/, reason: 'use --tnzi-border' },
  { pattern: /--t-surface\b/, reason: 'use --tnzi-container-bg' },
  { pattern: /--t-muted\b/, reason: 'use --tnzi-base-text-muted' },
  { pattern: /--t-danger\b/, reason: 'use --tnzi-error' },
]

function walkVueFiles(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    const s = statSync(full)
    if (s.isDirectory()) walkVueFiles(full, out)
    else if (name.endsWith('.vue')) out.push(full)
  }
  return out
}

describe('page CSS token consistency', () => {
  const files = walkVueFiles(ROOT)

  it('finds Vue files to scan', () => {
    expect(files.length).toBeGreaterThan(40)
  })

  for (const banned of BANNED_TOKENS) {
    it(`no page references ${banned.pattern.source} — ${banned.reason}`, () => {
      const offenders: string[] = []
      for (const file of files) {
        const content = readFileSync(file, 'utf8')
        if (banned.pattern.test(content)) {
          offenders.push(file.split(sep).slice(-3).join('/'))
        }
      }
      expect(offenders, `Files using ${banned.pattern.source}:\n  ${offenders.join('\n  ')}`)
        .toEqual([])
    })
  }
})
