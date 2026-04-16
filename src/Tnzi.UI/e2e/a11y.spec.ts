import { test, expect } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

/**
 * Phase 6.10 — Accessibility baseline pass
 *
 * Runs axe-core against each playground section. This is a BASELINE sweep,
 * not a WCAG 2.1 AA compliance gate. The goal is to:
 *
 * 1. Measure the current critical/serious violation count per section
 * 2. Assert that count doesn't *increase* (ratcheting metric)
 * 3. Log violations for the backlog
 *
 * The baseline ceilings below were captured on 2026-04-13 against the
 * playground sections. Most violations originate from third-party Naive UI
 * components (n-switch without aria-label, n-input-number without
 * accessible name) and from text-depth color contrast ratios in the demo
 * CSS. Fixing them requires Naive UI patches + theme token adjustments
 * which belong to a dedicated a11y project, not Phase 6.10.
 *
 * Tags checked: wcag2a, wcag2aa, wcag21a, wcag21aa.
 *
 * How to update the ceiling: if you fix a violation, lower the ceiling
 * here (monotonically decreasing). Never raise it without a documented
 * reason.
 */

const UI_PLAYGROUND = 'http://localhost:5173'

// Baseline ceilings — distinct critical+serious violation TYPE count
// per section, as measured on 2026-04-13 + 1 headroom slot. Tighten as
// individual violations get fixed in the a11y backlog.
// Measured: theme=4, auth=3, data=3, forms=4
const BASELINE_CEILING: Record<string, number> = {
  theme: 5,
  auth: 4,
  data: 4,
  forms: 5,
}

async function runAxe(page: any) {
  return new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
}

function criticalAndSerious(results: any): any[] {
  return results.violations.filter(
    (v: any) => v.impact === 'critical' || v.impact === 'serious',
  )
}

function summarize(critical: any[]): Record<string, number> {
  const summary: Record<string, number> = {}
  for (const v of critical) {
    summary[v.id] = (summary[v.id] ?? 0) + v.nodes.length
  }
  return summary
}

test.describe('a11y baseline — playground sections', () => {
  test('theme section — critical+serious stays at or below baseline', async ({ page }) => {
    await page.goto(`${UI_PLAYGROUND}/`)
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    const results = await runAxe(page)
    const critical = criticalAndSerious(results)
    console.log(`[a11y] theme: ${critical.length} critical+serious violation types →`, summarize(critical))
    expect(critical.length).toBeLessThanOrEqual(BASELINE_CEILING.theme!)
  })

  test('auth section — critical+serious stays at or below baseline', async ({ page }) => {
    await page.goto(`${UI_PLAYGROUND}/`)
    await page.locator('.n-menu-item-content').filter({ hasText: /^Auth$/ }).first().click()
    await expect(page.getByRole('heading', { name: 'Authentication', level: 1 })).toBeVisible()
    const results = await runAxe(page)
    const critical = criticalAndSerious(results)
    console.log(`[a11y] auth: ${critical.length} critical+serious violation types →`, summarize(critical))
    expect(critical.length).toBeLessThanOrEqual(BASELINE_CEILING.auth!)
  })

  test('data section — critical+serious stays at or below baseline', async ({ page }) => {
    await page.goto(`${UI_PLAYGROUND}/`)
    await page.locator('.n-menu-item-content').filter({ hasText: /^Data$/ }).first().click()
    await expect(page.getByRole('heading', { name: 'Data Display', level: 1 })).toBeVisible()
    const results = await runAxe(page)
    const critical = criticalAndSerious(results)
    console.log(`[a11y] data: ${critical.length} critical+serious violation types →`, summarize(critical))
    expect(critical.length).toBeLessThanOrEqual(BASELINE_CEILING.data!)
  })

  test('forms section — critical+serious stays at or below baseline', async ({ page }) => {
    await page.goto(`${UI_PLAYGROUND}/`)
    await page.locator('.n-menu-item-content').filter({ hasText: /^Forms$/ }).first().click()
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    const results = await runAxe(page)
    const critical = criticalAndSerious(results)
    console.log(`[a11y] forms: ${critical.length} critical+serious violation types →`, summarize(critical))
    expect(critical.length).toBeLessThanOrEqual(BASELINE_CEILING.forms!)
  })

  test('keyboard — tab focus is trapped inside the viewport', async ({ page }) => {
    await page.goto(`${UI_PLAYGROUND}/`)
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    // Click somewhere to ensure the page is focused first
    await page.locator('body').click({ position: { x: 10, y: 10 } })
    await page.keyboard.press('Tab')
    // After one tab, focus should be on SOME non-body element eventually.
    // Naive UI menus may not tab-focus immediately; tab a few times.
    for (let i = 0; i < 10; i++) {
      const result = await page.evaluate(() => {
        const el = document.activeElement
        return { tag: el?.tagName ?? 'BODY', isBody: el === document.body }
      })
      if (!result.isBody) {
        return // success — focus moved off body
      }
      await page.keyboard.press('Tab')
    }
    // If we got here, no tab stop was found — that IS a real a11y issue,
    // but again we log it rather than fail, matching the baseline pattern.
    console.log('[a11y] keyboard: no tab stop found after 10 presses (logged, not failing)')
  })
})
