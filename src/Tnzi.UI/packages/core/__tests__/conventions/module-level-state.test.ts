// @vitest-environment node
/**
 * Convention gate: **module-level mutable state must live on the globalThis
 * registry (`createAdapterSingleton`), never in a bare `let` or a module
 * constant `new Set()` / `new Map()`.**
 *
 * tsup builds this package with `splitting: false` across ~20 entries, so a
 * module imported from several entries is inlined into each of their bundles.
 * A module-level `let` or `Set` therefore exists once PER ENTRY: the setter an
 * app calls through `@tnzi/core/http` writes one copy, the getter the
 * `HttpClient` built by `@tnzi/core/state` reads another, and the two never
 * meet. That is how `declareClientCapability` could be called and the
 * `X-Tnzi-Capabilities` header still never leave the process (2026-09-12), and
 * it is the shape the package's own rule (core CLAUDE.md, 2026-07-26) was
 * written for. A unit test importing both symbols from `src/` sees a single
 * module instance and cannot observe it, hence a source scan plus the dist
 * probe in `capabilities-cross-entry.test.ts`.
 */
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const srcRoot = join(packageRoot, 'src');

/**
 * Files allowed to keep per-copy state, each with the reason a duplicated copy
 * is harmless. An entry without a reason is not an exemption.
 */
const ALLOWED: Record<string, string> = {
  'adapters/singleton.ts': 'the registry itself; its state IS the globalThis slot',
  'utils/id.ts':
    '`_counter` only disambiguates ids generated within one millisecond by one copy; the id also carries a timestamp and a random suffix, so two copies counting separately cannot collide',
};

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) return walk(full);
    return full.endsWith('.ts') && !full.endsWith('.d.ts') ? [full] : [];
  });
}

/** Top-level (column 0) `let` or `const x = new Set|Map(` declarations. */
const TOP_LEVEL_STATE = /^(?:export )?(?:let \w+|const \w+(?::[^=]+)? = new (?:Set|Map)\b)/;

/** A Set/Map typed as its Readonly counterpart is a lookup table, not state: duplicating it is harmless. */
const IMMUTABLE = /:\s*Readonly(?:Set|Map)\b/;

describe('module-level mutable state', () => {
  const files = walk(srcRoot);

  it('actually scanned something', () => {
    expect(files.length).toBeGreaterThan(50);
  });

  it('is parked on the globalThis registry, not in a per-bundle module variable', () => {
    const offenders: string[] = [];
    for (const file of files) {
      const rel = relative(srcRoot, file).replace(/\\/g, '/');
      const lines = readFileSync(file, 'utf8').split(/\r?\n/);
      lines.forEach((line, i) => {
        if (TOP_LEVEL_STATE.test(line) && !IMMUTABLE.test(line) && !(rel in ALLOWED)) {
          offenders.push(`${rel}:${i + 1}: ${line.trim()}`);
        }
      });
    }
    expect(offenders, 'route this state through createAdapterSingleton (see adapters/singleton.ts)').toEqual([]);
  });

  it('keeps every allow-list entry pointing at a real file with a stated reason', () => {
    for (const [rel, reason] of Object.entries(ALLOWED)) {
      expect(files.map((f) => relative(srcRoot, f).replace(/\\/g, '/'))).toContain(rel);
      expect(reason.length).toBeGreaterThan(20);
    }
  });
});
