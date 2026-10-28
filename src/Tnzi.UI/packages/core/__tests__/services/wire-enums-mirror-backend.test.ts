// @vitest-environment node
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * The string enums under `src/services` mirror backend enums of the same name,
 * which the server serializes by member name. Drift is silent in both
 * directions:
 *
 * - a member the TS side lacks cannot be chosen in the UI. The account
 *   editor's system-role select had no `WagesPayable` / `NonRecoverableTaxExpense`,
 *   the roles pay-run posting and non-recoverable purchase tax resolve, so a
 *   hand-built chart of accounts could never carry them;
 * - a member the backend lacks is rejected by model binding the moment a
 *   caller sends it.
 *
 * Reads the C# sources (the same way `ui-admin`'s route-permission test reads
 * controllers) and compares member NAMES of every TS string enum whose name
 * matches exactly one backend enum. Names declared by more than one backend
 * enum are ambiguous and skipped.
 */
const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const repoRoot = resolve(packageRoot, '../../../..');

function walk(dir: string, extension: string, skip: readonly string[], out: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    if (skip.includes(entry)) continue;
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, extension, skip, out);
    else if (entry.endsWith(extension)) out.push(full);
  }
  return out;
}

/** Comments go first: a `}` inside a doc comment would otherwise end the enum body early. */
function stripComments(text: string): string {
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');
}

function backendEnums(): Map<string, string[] | null> {
  const enums = new Map<string, string[] | null>();
  const files = walk(join(repoRoot, 'src'), '.cs', ['bin', 'obj', 'node_modules', 'Tnzi.UI']);
  for (const file of files) {
    const text = stripComments(readFileSync(file, 'utf8'));
    for (const match of text.matchAll(/\benum\s+(\w+)\s*(?::\s*\w+\s*)?\{([^}]*)\}/g)) {
      const members = match[2]
        .replace(/\[[^\]]*\]/g, '')
        .split(',')
        .map((part) => part.trim().split(/[\s=]/)[0])
        .filter((name) => name.length > 0);
      // A second declaration of the same name makes it ambiguous: null.
      enums.set(match[1], enums.has(match[1]) ? null : members);
    }
  }
  return enums;
}

interface FrontendEnum {
  name: string;
  file: string;
  values: string[];
}

function frontendStringEnums(): FrontendEnum[] {
  const result: FrontendEnum[] = [];
  for (const file of walk(join(packageRoot, 'src', 'services'), '.ts', [])) {
    const text = stripComments(readFileSync(file, 'utf8'));
    for (const match of text.matchAll(/export\s+enum\s+(\w+)\s*\{([^}]*)\}/g)) {
      const values = [...match[2].matchAll(/=\s*'([^']*)'/g)].map((m) => m[1]);
      if (values.length > 0) result.push({ name: match[1], file: relative(packageRoot, file), values });
    }
  }
  return result;
}

describe('service wire enums mirror the backend', () => {
  const backend = backendEnums();
  const checked = frontendStringEnums().filter((e) => Array.isArray(backend.get(e.name)));

  it('found enums on both sides (guards against a broken source path)', () => {
    expect(backend.get('AccountSystemRole')).toBeTruthy();
    expect(checked.length).toBeGreaterThan(30);
  });

  for (const fe of checked) {
    it(`${fe.name} (${fe.file}) has exactly the backend members`, () => {
      expect([...fe.values].sort()).toEqual([...backend.get(fe.name)!].sort());
    });
  }
});

/**
 * Mirrors that stay numeric on purpose, each with the reason the member name
 * never reaches a comparison against them.
 */
const NUMERIC_BY_DESIGN: Readonly<Record<string, string>> = {
  TrendGranularity: 'sent as a query parameter only; the model binder accepts the number',
  PendingUserActions: '[Flags]: read through hasFlag / parseFlags, which expand member names',
};

/**
 * Numeric mirrors (`export enum X { A = 1 }` or `export const X = { A: 1 } as const`)
 * whose name matches a backend enum.
 */
function frontendNumericMirrors(backend: Map<string, string[] | null>): Array<{ name: string; file: string }> {
  const result: Array<{ name: string; file: string }> = [];
  for (const file of walk(join(packageRoot, 'src', 'services'), '.ts', [])) {
    const text = stripComments(readFileSync(file, 'utf8'));
    const bodies = [
      ...text.matchAll(/export\s+enum\s+(\w+)\s*\{([^}]*)\}/g),
      ...text.matchAll(/export\s+const\s+(\w+)\s*=\s*\{([^}]*)\}\s*as\s+const/g),
    ];
    for (const match of bodies) {
      if (!/[=:]\s*\(?\d/.test(match[2])) continue;
      if (backend.has(match[1])) result.push({ name: match[1], file: relative(packageRoot, file) });
    }
  }
  return result;
}

/**
 * The opposite drift: the TS side mirrors a backend enum with NUMBERS. The
 * server writes the member name, so `dto.platform === DevicePlatform.Ios`
 * compares `"Ios"` with `2` and is false for every row, and a label map keyed
 * by number never matches. The push-devices page showed raw member names
 * instead of its platform badges this way. Requests are unaffected (the
 * converter accepts integers), which is why it goes unnoticed.
 */
describe('service enums that mirror a backend enum are not numeric', () => {
  const backend = backendEnums();
  const numeric = frontendNumericMirrors(backend);

  it('every exemption still names a numeric mirror (no stale entries)', () => {
    expect(Object.keys(NUMERIC_BY_DESIGN).filter((name) => !numeric.some((fe) => fe.name === name))).toEqual([]);
  });

  for (const fe of numeric) {
    it(`${fe.name} (${fe.file}) carries member names`, () => {
      expect(NUMERIC_BY_DESIGN[fe.name], `${fe.name} is numeric but the backend writes member names`).toBeTruthy();
    });
  }
});
