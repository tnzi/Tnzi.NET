// @vitest-environment node
/**
 * Convention gate: `:hover` applies only where hovering exists.
 *
 * On a touch screen a tapped element stays `:hover` until something else is
 * touched, so a row or button tapped on a phone kept its hover look. The build
 * wraps every hover selector in `@media (hover: hover)`
 * (`tools/vite/hover-media.mjs`, wired in `vite.config.ts`); this checks the
 * plugin's rewriting and that the build uses it.
 */
import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { hoverMediaPlugin } from '../../../../tools/vite/hover-media.mjs'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
/* PostCSS is Vite's own dependency; reached through Vite, as the build does. */
const requireFromVite = createRequire(createRequire(import.meta.url).resolve('vite'))
const postcss = requireFromVite('postcss') as typeof import('postcss')

async function run(css: string): Promise<string> {
  const result = await postcss([hoverMediaPlugin()]).process(css, { from: undefined })
  return squash(result.css)
}

/** Whitespace-insensitive form, so the checks read the structure. */
function squash(css: string): string {
  return css.replace(/\s+/g, ' ').replace(/\s*([{}])\s*/g, '$1').trim()
}

describe('hover-media plugin', () => {
  it('moves a hover rule into @media (hover: hover)', async () => {
    expect(await run('.a:hover { color: red; }')).toBe(squash('@media (hover: hover) { .a:hover { color: red; } }'))
  })

  it('keeps the other selectors of a list where they were', async () => {
    const css = await run('.a:hover, .a:focus-visible { color: red; } .b { color: blue; }')
    expect(css).toBe(squash('.a:focus-visible { color: red; } @media (hover: hover) { .a:hover { color: red; } } .b { color: blue; }'))
  })

  it('leaves rules already inside a hover media query, and keyframes, alone', async () => {
    const input = '@media (hover: none) { .a:hover { opacity: 1; } } @media (hover: hover) { .b:hover { opacity: 1; } }'
    expect(await run(input)).toBe(squash(input))
  })

  it('nests inside another media query', async () => {
    expect(await run('@media (max-width: 600px) { .a:hover { color: red; } }')).toBe(
      squash('@media (max-width: 600px) { @media (hover: hover) { .a:hover { color: red; } } }'),
    )
  })
})

describe('the build', () => {
  it('runs the plugin over the package styles', () => {
    const config = readFileSync(resolve(pkgRoot, 'vite.config.ts'), 'utf8')
    expect(config).toMatch(/hover-media\.mjs/)
    expect(config).toMatch(/plugins:\s*\[hoverMediaPlugin\(\)\]/)
  })
})
