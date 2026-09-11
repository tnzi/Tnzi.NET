/**
 * The documented icon-bundling recipe, compiled the way a consumer compiles it.
 *
 * ★ This file exists because the previous recipe **did not compile**, and
 * nothing noticed. It handed `getIcons`'s `IconifyJSON | null` straight to
 * `addCollection` (which takes a non-null `IconifyJSON`) and passed a
 * `readonly string[]` where a mutable one was required - two `TS2345`s for
 * anyone who copied it into a `strict` project. The fixture that "executed the
 * recipe" silently avoided both: it cast the argument, and it lived in
 * `__tests__/icons/`, which `tsconfig.json`'s `include` never covered. Vitest
 * transpiles without type-checking, so the whole gate ran blind to types.
 *
 * The null case is the sharpest part: TypeScript was *able* to catch the exact
 * silent failure ("getIcons returned null, every icon renders blank") that the
 * manifest exists to prevent, and the recipe was written in the one shape that
 * routes around it.
 *
 * Named `*.ts`, not `*.test.ts`, on purpose - see the note in `tsconfig.json`.
 * `tsconfig.build.json` drops the `__tests__` tree, so it never reaches `dist`.
 */
import { bundleTnziIcons, selectTnziIcons } from '../../src/icons'
import { icons as lineMd } from '@iconify-json/line-md'
import { icons as lucide } from '@iconify-json/lucide'
import { icons as materialSymbols } from '@iconify-json/material-symbols'
import { icons as mdi } from '@iconify-json/mdi'
import { icons as nimbus } from '@iconify-json/nimbus'
import { icons as simpleIcons } from '@iconify-json/simple-icons'

const collections = [lineMd, lucide, materialSymbols, mdi, nimbus, simpleIcons]

/** The runtime path, exactly as `docs/frontend/getting-started.md` prints it. */
export function runtimeRecipe(): void {
  bundleTnziIcons(collections)
}

/** The build-time path, and the report shape a consumer is told to check. */
export function buildTimeRecipe(): string {
  const { subsets, missingCollections, unresolved } = selectTnziIcons(collections)
  if (missingCollections.length > 0 || unresolved.length > 0) {
    throw new Error(`icons missing: ${[...missingCollections, ...unresolved].join(', ')}`)
  }
  return JSON.stringify(subsets)
}
