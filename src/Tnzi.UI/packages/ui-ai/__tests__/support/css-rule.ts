import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const src = resolve(dirname(fileURLToPath(import.meta.url)), '../../src')

/**
 * The bodies of every rule written exactly as `selector {` at the start of a
 * line in a file under `src/` (an SFC's styles or a stylesheet), joined. Styles
 * are pinned on the source because happy-dom computes no layout.
 */
export function cssRule(file: string, selector: string): string {
  const source = readFileSync(resolve(src, file), 'utf8').replace(/\r\n/g, '\n')
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return [...source.matchAll(new RegExp(`\\n${escaped} \\{([^}]*)\\}`, 'g'))].map((match) => match[1]).join('')
}
