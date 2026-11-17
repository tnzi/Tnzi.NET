// @vitest-environment node
/**
 * Convention gate: the controls stylesheet (`@tnzi/ui-ai/controls.css`) holds
 * what a page built from `@tnzi/ui-ai/controls` alone needs, and nothing of the
 * other domains.
 *
 * It exists so that a small window (a desktop app's settings panel) does not
 * load the whole package stylesheet: the conversation, the sidebar, markdown,
 * code highlighting and the workflow canvas. What it must hold: the light and
 * dark tokens, the defaults of the five `--tnzi-*` variables the accent reads
 * (there is no `@tnzi/ui` stylesheet on such a page), the global control
 * classes, the tooltip, and the scoped styles of every component the controls
 * entry reaches, under the same `data-v-*` attribute the main build renders.
 *
 * The checks on `dist/` need `pnpm build` first, as the other built-output gates.
 */
import { describe, expect, it } from 'vitest'
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const read = (path: string) => readFileSync(resolve(pkgRoot, path), 'utf8')
const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, ' ')

/* The global sheets a page of controls is used with, and nothing else. */
const SHEET_IMPORTS = [
  './tnzi-defaults.css',
  './scrollbars.css',
  '../components/overlay/tooltip.css',
  '../components/overlay/dialog.css',
  '../components/controls/controls.css',
  './tokens.css',
]

/*
 * Root classes of components outside the controls entry that may still appear
 * in the sheet, each with the reason. Anything else from another component's
 * root class is another domain's style leaking in.
 */
const ALLOWED_FOREIGN_CLASSES: Record<string, string> = {
  // A hook of the shared thin-scrollbar rule (styles/scrollbars.css), which
  // styles the scrollbars inside the chat shell; it carries none of its look.
  't-chat-app': 'scrollbar hook in styles/scrollbars.css',
}

/* Classes in the controls' templates that are hooks with no style of their own, each with its use. */
const HOOK_CLASSES: Record<string, string> = {
  't-select-field__menu': "TSelect's list, found by its own script",
  't-search-select__menu': "TSearchSelect's list box, a hook for a page's own rules",
  't-state': "TStateView's three states, styled by their modifier classes",
}

function sfcFiles(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) sfcFiles(full, out)
    else if (name.endsWith('.vue')) out.push(full)
  }
  return out
}

/** The files the controls and dialogs entries reach through relative imports. */
function controlsGraph(): Set<string> {
  const files = new Set<string>()
  const queue = [resolve(pkgRoot, 'src/controls.ts'), resolve(pkgRoot, 'src/dialogs.ts')]
  while (queue.length) {
    const file = queue.shift()!
    if (files.has(file)) continue
    files.add(file)
    const source = readFileSync(file, 'utf8')
    // Runtime imports only: a type import brings no component, and no style, into the build.
    const specifiers = [
      ...[...source.matchAll(/(?:^|\n)\s*(?:import|export)(\s+type)?\s[^'"]*?from\s*['"](\.[^'"]+)['"]/g)]
        .filter((m) => !m[1])
        .map((m) => m[2]!),
      ...[...source.matchAll(/(?:^|\n)\s*import\s*['"](\.[^'"]+)['"]/g)].map((m) => m[1]!),
      ...[...source.matchAll(/\bimport\(\s*['"](\.[^'"]+)['"]\s*\)/g)].map((m) => m[1]!),
    ]
    for (const specifier of specifiers) {
      const base = resolve(dirname(file), specifier)
      const next = [base, `${base}.ts`, `${base}.vue`, join(base, 'index.ts')].find((c) => existsSync(c) && statSync(c).isFile())
      if (next) queue.push(next)
    }
  }
  return files
}

/** The class on a component's root element (the first `class="..."` in its template). */
function rootClass(file: string): string | null {
  const template = /<template>([\s\S]*)<\/template>/.exec(readFileSync(file, 'utf8'))?.[1] ?? ''
  return /\sclass="([a-z][\w-]*)/.exec(template)?.[1] ?? null
}

describe('controls stylesheet: sources', () => {
  it('is built and exported', () => {
    const manifest = JSON.parse(read('package.json'))
    expect(manifest.exports['./controls.css']).toEqual({ import: './dist/controls.css' })
    expect(manifest.scripts.build).toContain('vite build -c vite.controls-css.config.ts')
    const config = read('vite.controls-css.config.ts')
    expect(config).toContain('src/controls-stylesheet.ts')
    expect(config).toContain("cssFileName: 'controls'")
  })

  it('builds the controls and dialogs entries and the controls sheet only', () => {
    const entry = read('src/controls-stylesheet.ts')
    expect(entry).toMatch(/import '\.\/styles\/controls-sheet\.css'/)
    expect(entry).toMatch(/export \* from '\.\/controls'/)
    expect(entry).toMatch(/export \* from '\.\/dialogs'/)
    const specifiers = [...entry.matchAll(/(?:from|import)\s+['"]([^'"]+)['"]/g)].map((m) => m[1])
    expect(specifiers.sort()).toEqual(['./controls', './dialogs', './styles/controls-sheet.css'])
  })

  it('imports the global sheets of the controls and nothing else', () => {
    const imports = [...stripComments(read('src/styles/controls-sheet.css')).matchAll(/@import\s+['"]([^'"]+)['"]/g)].map((m) => m[1])
    expect(imports).toEqual(SHEET_IMPORTS)
  })

  it('ships the defaults of every accent variable of @tnzi/ui the tokens read', () => {
    // The others they read (radius, durations, easing) carry fallbacks of
    // their own; the accent's fallbacks cannot be right for every accent.
    const tokens = stripComments(read('src/styles/tokens.css'))
    const needed = new Set([...tokens.matchAll(/var\((--tnzi-(?:on-)?primary[a-z0-9-]*)/g)].map((m) => m[1]))
    const defaults = stripComments(read('src/styles/tnzi-defaults.css'))
    expect(defaults).toMatch(/:where\(:root\)\s*\{/)
    expect([...needed].sort()).toEqual(['--tnzi-on-primary', '--tnzi-primary', '--tnzi-primary-rgb', '--tnzi-primary-text-on-dark', '--tnzi-primary-text-on-light'])
    for (const name of needed) expect(defaults, name).toMatch(new RegExp(`${name}:`))
  })
})

describe('controls stylesheet: built (needs `pnpm build` first)', () => {
  const css = stripComments(read('dist/controls.css'))
  const graph = controlsGraph()

  it('holds the tokens in both modes, the --tnzi-* defaults, the control classes and the tooltip', () => {
    expect(css).toMatch(/:root\s*\{[^}]*--tnzi-ai-accent:/)
    expect(css).toMatch(/\.dark,\s*\[data-theme=['"]?dark['"]?\]\s*\{[^}]*--tnzi-ai-bg:/)
    expect(css).toMatch(/:where\(:root\)\s*\{[^}]*--tnzi-primary:/)
    for (const selector of ['.t-input', '.t-select', '.t-check', '.t-tooltip', '.t-ai-scroll']) {
      expect(css, selector).toContain(selector)
    }
  })

  it('holds the overlays the controls open: dialog chrome and tokens, menus, popovers, toasts', () => {
    expect(css).toMatch(/:root\s*\{[^}]*--tnzi-ai-dialog-width-md:/)
    expect(css).toMatch(/:root\s*\{[^}]*--tnzi-ai-z-dialog:/)
    for (const selector of ['.t-dialog-backdrop', '.t-dialog__close', '.t-dialog__footer', '.t-dialog-host', '.t-confirm__message', '.t-popover-menu', '.t-popover', '.t-toast']) {
      expect(css, selector).toMatch(new RegExp(`\\${selector}(?![\\w-])`))
    }
  })

  // `TImageViewer` from `@tnzi/ui-ai/dialogs` on a page with this sheet alone: its layer, picture,
  // buttons and zoom bar (scoped), and every token they read, in both modes.
  it('holds the image viewer and the tokens it reads', () => {
    for (const selector of ['.t-image-viewer', '.t-image-viewer__image', '.t-image-viewer__btn', '.t-image-viewer__close', '.t-image-viewer__prev', '.t-image-viewer__bar', '.t-image-viewer__level', '.t-image-viewer__count', '.t-image-viewer--zoomed']) {
      expect(css, selector).toMatch(new RegExp(`\\${selector}(?![\\w-])[^{]*\\[data-v-[0-9a-f]{8}\\]`))
    }
    const viewer = stripComments(read('src/components/overlay/TImageViewer.vue').split('<style scoped>')[1]!)
    const root = [...css.matchAll(/:root\s*\{([^}]*)\}/g)].map((m) => m[1]).join(';')
    const declared = (block: string, name: string) => new RegExp(`${name}:\\s*([^;]*);`).exec(block)?.[1]
    // The tokens the viewer reads and those their values read in turn (the focus ring's accent).
    const tokens = new Set<string>()
    const queue = [...viewer.matchAll(/var\((--tnzi-ai-[a-z0-9-]+)/g)].map((m) => m[1]!)
    while (queue.length) {
      const token = queue.shift()!
      if (tokens.has(token)) continue
      tokens.add(token)
      const value = declared(root, token)
      expect(value, token).toBeTruthy()
      queue.push(...[...value!.matchAll(/var\((--tnzi-ai-[a-z0-9-]+)/g)].map((m) => m[1]!))
    }
    expect(tokens.size).toBeGreaterThan(5)
    // Dark mode reaches the viewer through the same tokens: each one the dark palette sets is in the sheet's dark block.
    const sourceDark = /\[data-theme='dark'\]\s*\{([^}]*)\}/.exec(stripComments(read('src/styles/tokens.css')))![1]!
    const builtDark = /\.dark,\s*\[data-theme=['"]?dark['"]?\]\s*\{([^}]*)\}/.exec(css)![1]!
    const darkened = [...tokens].filter((token) => declared(sourceDark, token) !== undefined)
    expect(darkened.length).toBeGreaterThan(0)
    const bare = (value: string | undefined) => value?.replace(/\s+/g, '')
    for (const token of darkened) expect(bare(declared(builtDark, token)), token).toBe(bare(declared(sourceDark, token)))
  })

  it('holds the scoped styles of the controls, under the attribute the main build renders', () => {
    const hashes = new Set([...css.matchAll(/\[data-v-([0-9a-f]{8})\]/g)].map((m) => m[1]))
    const scopedInGraph = [...graph].filter((f) => f.endsWith('.vue') && /<style[^>]*\bscoped\b/.test(readFileSync(f, 'utf8')))
    expect(hashes.size).toBe(scopedInGraph.length)
    // Each component's built module (`dist/<path>.vue.js`) carries its `__scopeId`.
    const js = scopedInGraph
      .map((f) => resolve(pkgRoot, 'dist', `${relative(resolve(pkgRoot, 'src'), f)}.js`))
      .filter(existsSync)
      .map((p) => readFileSync(p, 'utf8'))
      .join('\n')
    const missing = [...hashes].filter((h) => !js.includes(`data-v-${h}`))
    expect(missing).toEqual([])
  })

  it('holds no style of a component outside the controls entry', () => {
    // A component whose root is a native control styled by a global class
    // (`<input class="t-input">`), or a panel drawn from the shared dialog
    // chrome (`TDockedDialog`'s `.t-dialog`), shares that class with the controls.
    const globalClasses = new Set(
      ['src/components/controls/controls.css', 'src/components/overlay/dialog.css'].flatMap((sheet) =>
        [...read(sheet).matchAll(/\.(t-[\w-]+)/g)].map((m) => m[1]),
      ),
    )
    const outside = sfcFiles(resolve(pkgRoot, 'src')).filter((f) => !graph.has(f))
    expect(outside.length).toBeGreaterThan(50)
    const leaked = outside
      .map((f) => ({ file: relative(pkgRoot, f).replace(/\\/g, '/'), cls: rootClass(f) }))
      .filter(({ cls }) => cls && !(cls in ALLOWED_FOREIGN_CLASSES) && !globalClasses.has(cls))
      .filter(({ cls }) => new RegExp(`\\.${cls}(?![\\w-])`).test(css))
      .map(({ file, cls }) => `${file}: .${cls}`)
    expect(leaked).toEqual([])
  })

  it('styles every class the controls write in their templates', () => {
    // A class the package stylesheet styles through another layer (a utility
    // such as `sr-only`) is unstyled here: the badge's screen-reader sentence
    // once showed on a page built from the controls alone.
    const missing = [...graph]
      .filter((f) => f.endsWith('.vue'))
      .flatMap((f) => {
        const template = /<template>([\s\S]*)<\/template>/.exec(readFileSync(f, 'utf8'))?.[1] ?? ''
        return [...template.matchAll(/\sclass="([^"]+)"/g)]
          .flatMap((m) => m[1]!.split(/\s+/))
          .filter((cls) => cls && !(cls in HOOK_CLASSES) && !new RegExp(`\\.${cls}(?![\\w-])`).test(css))
          .map((cls) => `${relative(pkgRoot, f).replace(/\\/g, '/')}: .${cls}`)
      })
    expect(missing).toEqual([])
  })

  it('holds nothing of markdown, code highlighting, maths or the workflow canvas', () => {
    for (const marker of ['.shiki', '.katex', '.vue-flow', '.t-stream-markdown', '.t-sidebar-heading']) {
      expect(css, marker).not.toContain(marker)
    }
  })
})
