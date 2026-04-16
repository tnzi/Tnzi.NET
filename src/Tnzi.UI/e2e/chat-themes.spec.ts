import { test, expect } from '@playwright/test'

/**
 * Phase 6.6 — Chat in light + dark mode E2E
 *
 * Runs against the @tnzi/ui-ai playground (port 5174). Verifies:
 * - Chat demo mounts in the default (light) theme
 * - Theme toggle flips the dark class on documentElement
 * - Chat demo still renders correctly after theme switch
 * - Locale toggle works (EN ⇄ 中文)
 */

const UI_AI_BASE = 'http://localhost:5174'

test.describe('ui-ai chat themes', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(`${UI_AI_BASE}/`)
    // Wait for the header + Chat tab button (text-based match since the
    // button accessible name includes an icon child)
    await expect(page.locator('header button').filter({ hasText: 'Chat' }).first()).toBeVisible()
  })

  test('chat demo mounts in light theme by default', async ({ page }) => {
    // documentElement should not have the 'dark' class initially
    const isDark = await page.evaluate(() => document.documentElement.classList.contains('dark'))
    expect(isDark).toBe(false)
    // Chat demo content container should be visible
    await expect(page.locator('main').first()).toBeVisible()
  })

  test('theme toggle flips dark class on documentElement', async ({ page }) => {
    // Theme toggle is the icon button showing moon/sun icons
    const themeButton = page.locator('button').filter({ has: page.locator('.iconify[class*="moon"], .iconify[class*="sun"]') }).first()
    // Fallback: find the second icon-only button in the right-side actions
    const button = (await themeButton.count()) > 0
      ? themeButton
      : page.locator('header button').last()

    await button.click()
    await expect.poll(
      () => page.evaluate(() => document.documentElement.classList.contains('dark')),
      { timeout: 3000 },
    ).toBe(true)

    await button.click()
    await expect.poll(
      () => page.evaluate(() => document.documentElement.classList.contains('dark')),
      { timeout: 3000 },
    ).toBe(false)
  })

  test('chat demo still renders after toggling theme multiple times', async ({ page }) => {
    const button = page.locator('header button').last()
    await button.click()
    await button.click()
    await button.click()
    // The main content area should still be visible
    await expect(page.locator('main').first()).toBeVisible()
  })

  test('locale toggle switches between EN and 中文 labels', async ({ page }) => {
    // Locale button shows "中文" when locale is en, "EN" when locale is zh.
    // Text includes icon whitespace — use a broader match.
    const localeButton = page.locator('header button').filter({ hasText: /中文|^\s*EN\s*$/ }).first()
    await expect(localeButton).toBeVisible()
    const before = (await localeButton.textContent())?.replace(/\s/g, '')
    await localeButton.click()
    const after = (await localeButton.textContent())?.replace(/\s/g, '')
    expect(before).not.toBe(after)
    expect([before, after].sort()).toEqual(['EN', '中文'])
  })
})
