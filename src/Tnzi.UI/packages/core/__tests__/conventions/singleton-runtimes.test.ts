// @vitest-environment node
/**
 * Convention gate: **single-instance runtimes must be owned by the host app**.
 *
 * ## Why this exists
 *
 * On 2026-08-07 every list page in a consuming application stopped rendering.
 * The API returned 200 with the right rows; the page sat on its skeleton
 * forever and the pager read "Total 0". No console error, no warning, no
 * rejected promise.
 *
 * The cause was two physical copies of Vue's reactivity runtime. `@tnzi/core`
 * declared `@vue/reactivity` as a regular `dependency` and imported `reactive`
 * from it, while `@tnzi/ui-admin` and the app imported `computed`/`watch` from
 * `vue`. Under a `link:` install those resolve to different files - the linked
 * `dist/` resolves bare specifiers upward into the *framework's* own
 * `node_modules`, not the app's. Dependency tracking in Vue is module-level
 * state, so a `computed` created by instance B never subscribes to a `reactive`
 * proxy created by instance A. The controller kept updating; the view was
 * structurally incapable of hearing about it.
 *
 * Version numbers were identical (3.5.38 on both sides). **Two distinct module
 * instances are enough** - matching versions do not help, and neither does a
 * type error, a test failure or a runtime warning. Nothing about this class of
 * defect is observable from inside a single package's test suite, because
 * within one pnpm workspace everything dedupes to one copy in the store and it
 * all works. It only breaks at the consumer boundary. That is precisely why it
 * needs a structural gate rather than a behavioural test.
 *
 * ## The two rules
 *
 * **R1 - never a `dependency`.** A single-instance runtime may appear in
 * `peerDependencies` (the host owns it) and in `devDependencies` (we need it to
 * build and test), but never in `dependencies`. A `dependency` is a promise to
 * ship our own copy, which is the one thing that must not happen.
 *
 * **R2 - the dedupe list may only name runtimes the host is guaranteed to
 * own.** `resolve.dedupe` re-resolves a specifier from the app's own root; when
 * the app has no copy there, Vite's `tryNodeResolve` returns nothing rather
 * than falling back to the importer. So a dedupe entry for something the app
 * does not directly depend on cannot fire - which is exactly what happened to
 * the `@vue/reactivity` entry that was supposed to prevent this bug. R2 pins
 * `TNZI_SINGLETON_DEPS` to the runtimes below that are demanded as required
 * peers, so the list cannot silently grow an entry that can never fire, nor
 * lose one that must.
 *
 * Plus a source-level rule: nothing may import `@vue/reactivity` directly.
 * `vue` re-exports the same bindings and is the copy every consumer already
 * pins, so importing from `vue` makes the second instance impossible instead of
 * merely unlikely.
 */
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, resolve, join } from 'node:path';

const packagesDir = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const PACKAGES = ['core', 'ui', 'ui-ai', 'ui-admin', 'mobile'];

/**
 * Runtimes that must be host-owned: a second copy silently breaks the
 * framework, AND a consuming app inevitably has its own copy.
 *
 * Both halves matter. The first is why they may never be a `dependency`; the
 * second is what makes a dedupe entry able to fire at all.
 *
 * `echarts` and `@iconify/vue` satisfy the first half only, so they are
 * deliberately absent. Both hold a genuine module-level registry, but
 * `@tnzi/ui-admin` ships them as regular dependencies and an app can consume
 * every chart and icon the framework renders without importing either itself -
 * Acme's own admin app does exactly that and has no copy of `@iconify/vue`
 * at its root. Promoting them to peers would invent an install requirement for
 * apps that never touch them, and listing them for dedupe would repeat the
 * `@vue/reactivity` mistake. They stay dependencies; apps that DO use them
 * directly pass them via `tnziSingletons({ extra })`.
 */
const SINGLETON_RUNTIMES = [
  'vue',
  '@vue/reactivity',
  '@vue/runtime-core',
  '@vue/runtime-dom',
  '@vue/shared',
  'vue-router',
  'pinia',
  'naive-ui',
];

function readManifest(pkg: string): {
  dependencies?: Record<string, string>;
  peerDependencies?: Record<string, string>;
  peerDependenciesMeta?: Record<string, { optional?: boolean }>;
} {
  return JSON.parse(readFileSync(join(packagesDir, pkg, 'package.json'), 'utf8'));
}

function walk(dir: string, out: string[] = []): string[] {
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else if (/\.(ts|tsx|vue|mts)$/.test(entry.name)) out.push(full);
  }
  return out;
}

describe('single-instance runtimes', () => {
  it('R1: no package declares one as a regular dependency', () => {
    const violations: string[] = [];

    for (const pkg of PACKAGES) {
      const deps = Object.keys(readManifest(pkg).dependencies ?? {});
      for (const dep of deps) {
        if (SINGLETON_RUNTIMES.includes(dep)) {
          violations.push(`@tnzi/${pkg} declares "${dep}" in dependencies (must be a peerDependency)`);
        }
      }
    }

    expect(violations).toEqual([]);
  });

  it('R2: the dedupe list matches the set of required peers', async () => {
    const { TNZI_SINGLETON_DEPS } = await import(
      pathToFileURL(join(packagesDir, 'ui', 'vite.mjs')).href
    );

    // A runtime is "host-owned" once some package demands it as a NON-optional
    // peer - that is what guarantees the app has its own copy to dedupe to.
    const requiredPeers = new Set<string>();
    for (const pkg of PACKAGES) {
      const manifest = readManifest(pkg);
      for (const [name, _range] of Object.entries(manifest.peerDependencies ?? {})) {
        if (!SINGLETON_RUNTIMES.includes(name)) continue;
        if (manifest.peerDependenciesMeta?.[name]?.optional) continue;
        requiredPeers.add(name);
      }
    }

    expect([...TNZI_SINGLETON_DEPS].sort()).toEqual([...requiredPeers].sort());
  });

  it('nothing imports @vue/reactivity directly - use "vue"', () => {
    const offenders: string[] = [];

    for (const pkg of PACKAGES) {
      for (const file of walk(join(packagesDir, pkg, 'src'))) {
        const source = readFileSync(file, 'utf8');
        // Import/re-export/dynamic-import forms; plain prose in comments is fine.
        if (/(?:from|import\s*\()\s*['"]@vue\/reactivity['"]/.test(source)) {
          offenders.push(file.slice(packagesDir.length + 1).replace(/\\/g, '/'));
        }
      }
    }

    expect(offenders).toEqual([]);
  });
});
