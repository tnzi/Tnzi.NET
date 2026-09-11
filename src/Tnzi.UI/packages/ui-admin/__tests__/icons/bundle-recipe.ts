/**
 * The Iconify collections the documented recipe imports, as data.
 *
 * ★ Why the package root (`@iconify-json/mdi`) and not `/icons.json`: the root
 * is the entry that ships a `.d.ts` typing it as `IconifyJSON`. The raw
 * `.json` subpath has no `types` in its `exports`, so TypeScript falls back to
 * inferring the literal type of a 3 MB object. Both compile; the root is the
 * one that compiles *fast* and gives the real type, which is why the recipe
 * uses it and why this fixture mirrors it exactly.
 *
 * ★ This file no longer executes the recipe. It used to, and that was the
 * weakest part of the gate: the recipe's target is a **browser bundle**, and
 * running it under vitest exercises Node's resolver instead. The version that
 * shipped looped over a dynamic `import()` of a bare specifier built from a
 * variable - fine in Node, silently left verbatim by Vite, unresolvable in a
 * browser. `icon-manifest.test.ts` now runs a real Vite build for that half;
 * this file supplies the collections for the resolution half.
 */
import { icons as lineMd } from '@iconify-json/line-md'
import { icons as lucide } from '@iconify-json/lucide'
import { icons as materialSymbols } from '@iconify-json/material-symbols'
import { icons as mdi } from '@iconify-json/mdi'
import { icons as nimbus } from '@iconify-json/nimbus'
import { icons as simpleIcons } from '@iconify-json/simple-icons'

/**
 * Every collection any manifest can need.
 *
 * Passing all of them to a package that needs two is deliberate and matches
 * what `bundleTnziIcons` promises: collections carry their own `prefix`, so
 * extras are ignored rather than mis-assigned.
 */
export const ALL_COLLECTIONS = [lineMd, lucide, materialSymbols, mdi, nimbus, simpleIcons]
