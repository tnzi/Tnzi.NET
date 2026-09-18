import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'fs'
import { join, sep } from 'path'

/**
 * Static gate for the overlay provider.
 *
 * `TOverlayTheme` is the one provider every overlay renders under: it resets
 * the naive theme to the global light/dark mode (so a dialog opened from a
 * dark-card page does not come out dark under a light app) and defaults every
 * button and form control inside to `small` (so a dialog's density does not
 * depend on whether its author remembered `size="small"`). `TModalShell` and
 * `TDrawerShell` go through it; a hand-rolled `NModal` / `NDrawer` has to be
 * wrapped in it by hand.
 *
 * The rule "wrap it" was written down in 2026-08 and, with nothing enforcing
 * it, produced sixteen bare overlays by 2026-09 - five with no provider at all
 * (the theme drawer, the command palette, the chat dialog family, the file
 * preview, the image cropper). Nothing breaks when one is missed: the overlay
 * renders, its controls are just medium and, from a dark card, dark. It only
 * reads as wrong next to the dialog that opened before it.
 */

/**
 * Both packages: `@tnzi/ui` owns the shells and the cropper modal, this one
 * owns everything else. `@tnzi/ui-ai` is deliberately NOT scanned - its
 * overlays take their mode from `useAiNaiveTheme(isDark)`, the chat shell's
 * own switch, and `TOverlayTheme` would reset them to whatever the `@tnzi/ui`
 * theme context says instead (a light modal over a dark chat).
 */
const ROOTS = [
  join(__dirname, '..', '..', 'src'),
  join(__dirname, '..', '..', '..', 'ui', 'src'),
]

/**
 * Files that may pass `:dense="false"` (theme reset without the small control
 * default), each with the reason. The opt-out exists for exactly one shape:
 * a host that mounts a whole PAGE on a floating surface, where the page-level
 * control sizes must survive. Anywhere else it is a dialog quietly opting back
 * into the per-author inconsistency the default ends.
 */
const MAY_OPT_OUT_OF_DENSITY = new Set([
  // The desktop layout's window host: a page in a window, not an overlay.
  '@tnzi/ui-admin/components/desktop/TDesktopWindowHost.vue',
])

const OVERLAY_TAG = /<(NModal|NDrawer)\b/g
const PROVIDER_OPEN = /<TOverlayTheme\b[^>]*?(\/?)>/g
const PROVIDER_CLOSE = /<\/TOverlayTheme>/g

/**
 * The template half of an SFC, comments out. A doc comment that shows
 * `<template #overlays><NDrawer …/></template>` as an example is prose, and a
 * `<script>` full of strings is not markup either.
 */
function template(file: string): string {
  return readFileSync(file, 'utf8')
    .replace(/<script\b[\s\S]*?<\/script>/g, ' ')
    .replace(/<style\b[\s\S]*?<\/style>/g, ' ')
    .replace(/<!--[\s\S]*?-->/g, ' ')
}

function walk(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) walk(full, out)
    else if (name.endsWith('.vue')) out.push(full)
  }
  return out
}

/** `@tnzi/ui/components/form/TImageUpload.vue` - package-qualified, split on the LAST `src`. */
function relative(file: string): string {
  const parts = file.split(sep)
  const at = parts.lastIndexOf('src')
  if (at === -1) return parts.join('/')
  return `@tnzi/${parts[at - 1]}/${parts.slice(at + 1).join('/')}`
}

/**
 * Every overlay tag in the markup, with the number of `<TOverlayTheme>` it
 * sits inside. Depth is counted in source order - an open before the tag
 * raises it, a close before the tag lowers it - which is what "wrapped in"
 * means for a template.
 */
function overlays(markup: string): Array<{ tag: string; depth: number; offset: number }> {
  const events: Array<{ at: number; delta: number }> = []
  for (const m of markup.matchAll(PROVIDER_OPEN)) {
    // A self-closing `<TOverlayTheme />` wraps nothing.
    if (m[1] !== '/') events.push({ at: m.index, delta: 1 })
  }
  for (const m of markup.matchAll(PROVIDER_CLOSE)) events.push({ at: m.index, delta: -1 })
  events.sort((a, b) => a.at - b.at)

  return [...markup.matchAll(OVERLAY_TAG)].map((m) => {
    const depth = events.filter((e) => e.at < m.index).reduce((d, e) => d + e.delta, 0)
    return { tag: m[1], depth, offset: m.index }
  })
}

describe('overlay chrome', () => {
  const files = ROOTS.flatMap((root) => walk(root))
  const found = files.flatMap((file) =>
    overlays(template(file)).map((o) => ({ ...o, file: relative(file) })),
  )

  // The scan has to be looking at something: sixteen files carried a bare
  // overlay when this was written, and a scan that finds none has lost its
  // roots or its regex, not its offenders.
  it('finds the overlays it is meant to check', () => {
    expect(files.length).toBeGreaterThan(100)
    expect(found.length).toBeGreaterThanOrEqual(18)
    expect(found.map((o) => o.file)).toContain('@tnzi/ui/components/overlay/TModalShell.vue')
  })

  it('every NModal / NDrawer renders inside a TOverlayTheme', () => {
    const bare = found.filter((o) => o.depth === 0).map((o) => `${o.file} <${o.tag}>`)
    expect(
      bare,
      `Wrap these in <TOverlayTheme> (or go through TModalShell / TDrawerShell), ` +
        `otherwise their controls stay medium and they inherit the content-area ` +
        `theme through the Teleport: ${bare.join(', ')}`,
    ).toEqual([])
  })

  it('only the listed hosts turn the control density off', () => {
    const optingOut = files
      .filter((f) => /:dense="false"/.test(template(f)))
      .map(relative)
      .filter((f) => !MAY_OPT_OUT_OF_DENSITY.has(f))
    expect(
      optingOut,
      `\`:dense="false"\` is for a host that mounts a PAGE, not for a dialog that wants ` +
        `medium controls - set \`size\` on its NForm instead: ${optingOut.join(', ')}`,
    ).toEqual([])
  })

  it('the opt-out list stays honest (each entry really opts out)', () => {
    const present = new Set(files.map(relative))
    for (const entry of MAY_OPT_OUT_OF_DENSITY) {
      expect(present.has(entry), `${entry} no longer exists`).toBe(true)
      const file = files.find((f) => relative(f) === entry)!
      expect(/:dense="false"/.test(template(file)), `${entry} no longer opts out`).toBe(true)
    }
  })
})
