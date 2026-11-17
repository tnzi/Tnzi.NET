import { afterEach, describe, expect, it } from 'vitest'
import { AI_APP_ATTRIBUTE, markAiApp } from '../../src/headless/app-root'

const root = () => document.documentElement

afterEach(() => root().removeAttribute(AI_APP_ATTRIBUTE))

describe('markAiApp', () => {
  it('marks <html> and unmarks it on release', () => {
    const release = markAiApp()
    expect(root().hasAttribute(AI_APP_ATTRIBUTE)).toBe(true)
    release()
    expect(root().hasAttribute(AI_APP_ATTRIBUTE)).toBe(false)
  })

  it('counts marks: the attribute goes with the last release', () => {
    const first = markAiApp()
    const second = markAiApp()
    first()
    expect(root().hasAttribute(AI_APP_ATTRIBUTE)).toBe(true)
    second()
    expect(root().hasAttribute(AI_APP_ATTRIBUTE)).toBe(false)
  })

  it('a release called twice counts once', () => {
    const first = markAiApp()
    const second = markAiApp()
    first()
    first()
    expect(root().hasAttribute(AI_APP_ATTRIBUTE)).toBe(true)
    second()
    expect(root().hasAttribute(AI_APP_ATTRIBUTE)).toBe(false)
  })

  it('leaves an attribute the page had before the first mark', () => {
    root().setAttribute(AI_APP_ATTRIBUTE, '')
    const release = markAiApp()
    release()
    expect(root().hasAttribute(AI_APP_ATTRIBUTE)).toBe(true)
  })

  it('does nothing without a document', () => {
    const release = markAiApp(null)
    expect(() => release()).not.toThrow()
  })
})
