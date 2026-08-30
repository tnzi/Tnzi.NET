/**
 * The naive-ui floor is written in two places on purpose: `peerDependencies`
 * (what a package manager reads) and `MIN_NAIVE_UI` (what the running app can
 * check). Two copies of a version drift, and this one would drift silently:
 * raising the peer range without touching the constant leaves a boot check that
 * cheerfully approves a version the code cannot actually run on.
 *
 * So the first test here is not about behaviour at all - it binds the constant
 * to the manifest.
 */
import { describe, it, expect, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { MIN_NAIVE_UI, satisfiesMin, checkNaiveUiVersion } from '../../src/plugin/peer-check'

const pkgPath = resolve(dirname(fileURLToPath(import.meta.url)), '../../package.json')
const pkg = JSON.parse(readFileSync(pkgPath, 'utf8')) as {
  peerDependencies: Record<string, string>
}

describe('naive-ui floor', () => {
  it('MIN_NAIVE_UI equals the lower bound declared in peerDependencies', () => {
    const declared = pkg.peerDependencies['naive-ui']
    expect(declared).toBeDefined()
    // Accepts `^2.45.0` / `>=2.45.0` / `2.45.0`; the point is the number.
    const lower = declared.replace(/^[\^~>=\s]+/, '')
    expect(MIN_NAIVE_UI).toBe(lower)
  })
})

describe('satisfiesMin', () => {
  it.each([
    ['2.45.0', true],
    ['2.45.1', true],
    ['2.46.0', true],
    ['3.0.0', true],
    ['2.44.1', false],
    ['2.44.99', false],
    ['1.99.99', false],
  ])('%s -> %s', (actual, expected) => {
    expect(satisfiesMin(actual, MIN_NAIVE_UI)).toBe(expected)
  })

  it('compares numerically, not lexically', () => {
    // '2.9.0' < '2.10.0' numerically but the other way round as strings.
    expect(satisfiesMin('2.10.0', '2.9.0')).toBe(true)
    expect(satisfiesMin('2.9.0', '2.10.0')).toBe(false)
  })
})

describe('checkNaiveUiVersion', () => {
  it('stays silent on an acceptable version', () => {
    const report = vi.fn()
    expect(checkNaiveUiVersion('2.45.1', report)).toBe(true)
    expect(report).not.toHaveBeenCalled()
  })

  it('reports the version, the floor and the symptom on an old one', () => {
    const report = vi.fn()
    expect(checkNaiveUiVersion('2.44.1', report)).toBe(false)
    expect(report).toHaveBeenCalledTimes(1)
    const msg = report.mock.calls[0][0] as string
    // The message has to be actionable on its own: which version is loaded,
    // which is needed, and what the user will otherwise see.
    expect(msg).toContain('2.44.1')
    expect(msg).toContain(MIN_NAIVE_UI)
    expect(msg).toContain('filter is not a function')
    // And why the install did not warn, which is the genuinely surprising part.
    expect(msg).toContain('link')
  })
})
