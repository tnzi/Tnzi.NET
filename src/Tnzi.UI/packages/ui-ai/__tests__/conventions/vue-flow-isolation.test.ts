// @vitest-environment node
/**
 * Convention gate: `@vue-flow/*` is reachable from exactly one build entry.
 *
 * ## Why this replaces the old check
 *
 * `CLAUDE.md` used to prescribe:
 *
 *   for f in index components chat embed shell utils; do grep -c vue-flow dist/$f.js; done   # all 0
 *
 * Every one of those reads 0 - and the invariant was still broken for months.
 * `grep` on a single built file reports **direct** references only. The leak
 * travelled one hop further out: `src/components/index.ts` re-exported
 * `./workflow/index`, whose `TWorkflow*` SFCs each `import '@vue-flow/core'`
 * themselves. A barrel never has to *name* the dependency to drag it in, so the
 * grep stayed clean while `dist/index.js` imported
 * `./components/workflow/TWorkflowCanvas.vue.js` on line 24.
 *
 * So this walks the import graph instead of grepping a file, and it walks
 * `src/` rather than `dist/` so it fails before a build rather than after one.
 *
 * ## Why the invariant is worth a gate
 *
 * `@vue-flow/core` + background + minimap (and their stylesheets) are the
 * heaviest thing this package depends on, and only the workflow editor needs
 * them. Any consumer building a pure chat product should never pay for it.
 * Nothing about a violation is observable at runtime - the app works fine, it
 * is just bigger - which is exactly the kind of regression a behavioural test
 * cannot notice.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, relative } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

/** The dependency prefixes that must stay behind the `workflow` entry. */
const ISOLATED = ['@vue-flow/'];

/** The single entry allowed to reach them, relative to the package root. */
const ALLOWED_ENTRY = 'src/workflow/index.ts';

/**
 * Build entries come from `vite.config.ts` rather than a list maintained here:
 * a new entry added there must be covered automatically, otherwise this gate
 * silently stops watching the very thing that was just added.
 */
function buildEntries(): string[] {
  const config = readFileSync(resolve(pkgRoot, 'vite.config.ts'), 'utf8');
  const entryBlock = config.slice(config.indexOf('entry: {'), config.indexOf('name: '));
  return [...entryBlock.matchAll(/resolve\(__dirname,\s*'([^']+)'\)/g)].map((m) => m[1]);
}

/** Resolve a relative specifier the way the bundler would. */
function resolveSpecifier(fromFile: string, specifier: string): string | null {
  const base = resolve(dirname(fromFile), specifier);
  const candidates = [
    base,
    `${base}.ts`,
    `${base}.vue`,
    resolve(base, 'index.ts'),
    resolve(base, 'index.vue'),
  ];
  for (const candidate of candidates) {
    if (existsSync(candidate) && statSync(candidate).isFile()) return candidate;
  }
  return null;
}

/**
 * Every module specifier a file imports or re-exports at **runtime**, `.vue`
 * included.
 *
 * `import type` / `export type` are skipped on purpose: they are erased at
 * compile time and never reach a bundle. `headless/useWorkflowVisualization.ts`
 * is exactly this case - it takes `Node` / `Edge` as types to describe its
 * return shape, and `dist/headless/useWorkflowVisualization.js` contains no
 * reference to @vue-flow at all. Counting those would make the gate demand a
 * split that buys nothing.
 */
function specifiersOf(file: string): string[] {
  const source = readFileSync(file, 'utf8');
  const withFrom = [...source.matchAll(/(?:^|\n)(\s*(?:import|export)([^'"\n]*))from\s*['"]([^'"]+)['"]/g)]
    .filter(([, , clause]) => !/^\s+type\s/.test(clause))
    .map((m) => m[3]);
  const bareImports = [...source.matchAll(/(?:^|\n)\s*import\s*['"]([^'"]+)['"]/g)].map((m) => m[1]);
  return [...withFrom, ...bareImports];
}

/**
 * Walk the graph from one entry and report which isolated packages it reaches,
 * along with the path that got there - a bare "it leaks" is not actionable when
 * the hop is three barrels deep.
 */
function isolatedReachableFrom(entry: string): string[] {
  const start = resolve(pkgRoot, entry);
  const seen = new Set<string>();
  const findings: string[] = [];
  const queue: Array<{ file: string; trail: string[] }> = [{ file: start, trail: [entry] }];

  while (queue.length) {
    const { file, trail } = queue.shift()!;
    if (seen.has(file)) continue;
    seen.add(file);

    for (const specifier of specifiersOf(file)) {
      const isolated = ISOLATED.find((prefix) => specifier.startsWith(prefix));
      if (isolated) {
        findings.push(`${trail.join(' -> ')} -> ${specifier}`);
        continue;
      }
      if (!specifier.startsWith('.')) continue;
      const next = resolveSpecifier(file, specifier);
      if (next) {
        queue.push({ file: next, trail: [...trail, relative(pkgRoot, next).replace(/\\/g, '/')] });
      }
    }
  }
  return findings;
}

describe('@vue-flow isolation', () => {
  const entries = buildEntries();

  // Guards the guard, twice over. Without the first, an entry-parsing change
  // would make every assertion below pass over an empty list. Without the
  // second, a broken graph walker would report "nothing leaks" everywhere -
  // including from the one file that certainly does.
  it('can see the build entries', () => {
    expect(entries.length).toBeGreaterThan(5);
    expect(entries).toContain(ALLOWED_ENTRY);
  });

  it('detects the dependency where it is supposed to be', () => {
    expect(isolatedReachableFrom(ALLOWED_ENTRY).length).toBeGreaterThan(0);
  });

  it.each(buildEntries().filter((e) => e !== ALLOWED_ENTRY))(
    '%s does not reach @vue-flow',
    (entry) => {
      const leaks = isolatedReachableFrom(entry);
      expect(
        leaks,
        leaks.length
          ? `\`${entry}\` reaches an isolated dependency:\n  ${leaks.join('\n  ')}\n\n` +
              'Re-exporting a `TWorkflow*` component is enough to cause this - the SFC ' +
              'imports @vue-flow/core itself, so the barrel never mentions it. Keep them ' +
              `behind \`${ALLOWED_ENTRY}\` (the @tnzi/ui-ai/workflow subpath).`
          : '',
      ).toEqual([]);
    },
  );
});
