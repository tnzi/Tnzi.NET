// @vitest-environment node
/**
 * Convention gate: no `<iframe>` in any `@tnzi/*` package pairs
 * `allow-scripts` with `allow-same-origin` by default.
 *
 * ## The bug this locks out
 *
 * `allow-scripts` + `allow-same-origin` together void the sandbox: the framed
 * document runs on the host's origin, so it can read `parent.localStorage`
 * (where the bearer token lives by default), call `/api/*` as the user, and
 * even strip its own `sandbox` attribute and reload unsandboxed. A panel that
 * exists to preview model-generated HTML must never ship that pairing.
 *
 * The 2026-07-26 P0 fix (0f90dc25) removed it from `TArtifactPanel` and wrote
 * the hazard into that component's prop doc. Its sibling `TArtifactPreview`
 * carried the identical static attribute on both of its iframes and was not
 * touched - the fix note described the class of bug, but nothing scanned for
 * a second instance. This test is that scan, and it deliberately walks EVERY
 * package's `src/` rather than only this one: the third copy could appear in
 * ui-admin or ui just as easily.
 *
 * ## What counts
 *
 * Three places a default can hide, all checked after comments are stripped
 * (prop docs legitimately quote the forbidden pairing as the thing to avoid):
 *
 *   1. a static `sandbox="…"` attribute on an `<iframe>`;
 *   2. a bound `:sandbox="'…'"` whose expression is a string literal;
 *   3. a `withDefaults` entry for a prop whose name ends in `sandbox`
 *      (`iframeSandbox: '…'`, `sandbox: '…'`).
 *
 * A consumer opting back in for first-party content (`:iframe-sandbox="'allow-scripts
 * allow-same-origin'"` in an app) is outside this package tree and outside
 * this gate on purpose - the rule is about what the framework ships as the
 * default, not what an application decides for content it wrote itself.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join, relative } from 'node:path';

const packagesRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');

function vueFiles(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) vueFiles(full, out);
    else if (entry.name.endsWith('.vue')) out.push(full);
  }
  return out;
}

const files = readdirSync(packagesRoot, { withFileTypes: true })
  .filter((entry) => entry.isDirectory() && existsSync(join(packagesRoot, entry.name, 'src')))
  .flatMap((entry) => vueFiles(join(packagesRoot, entry.name, 'src')))
  .map((path) => ({
    path: relative(packagesRoot, path).replace(/\\/g, '/'),
    text: stripComments(readFileSync(path, 'utf8')),
  }));

/** Drop HTML, block and line comments so quoted examples in docs do not trip the scan. */
function stripComments(source: string): string {
  return source
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:'"`])\/\/[^\n]*/g, '$1');
}

interface SandboxDefault {
  file: string;
  source: 'static attribute' | 'bound literal' | 'prop default';
  value: string;
}

function isVoided(value: string): boolean {
  const tokens = value.trim().split(/\s+/);
  return tokens.includes('allow-scripts') && tokens.includes('allow-same-origin');
}

/** Every iframe tag across the workspace, and every sandbox default it could pick up. */
function collect() {
  const iframes: string[] = [];
  const defaults: SandboxDefault[] = [];

  for (const { path, text } of files) {
    for (const [tag] of text.matchAll(/<iframe\b[^>]*>/g)) {
      iframes.push(path);
      const staticAttr = tag.match(/\ssandbox\s*=\s*"([^"]*)"/);
      if (staticAttr) defaults.push({ file: path, source: 'static attribute', value: staticAttr[1]! });
      const bound = tag.match(/\s(?::|v-bind:)sandbox\s*=\s*"([^"]*)"/);
      const literal = bound?.[1]?.match(/^\s*'([^']*)'\s*$/);
      if (literal) defaults.push({ file: path, source: 'bound literal', value: literal[1]! });
    }
    for (const [, , , value] of text.matchAll(/\b(\w*[sS]andbox)\s*:\s*(['"`])([^'"`]*)\2/g)) {
      defaults.push({ file: path, source: 'prop default', value: value! });
    }
  }
  return { iframes, defaults };
}

describe('iframe sandbox', () => {
  const { iframes, defaults } = collect();

  /** A silent empty scan would make the gate below pass for the wrong reason. */
  it('has entries to check', () => {
    expect(files.length).toBeGreaterThan(50);
    // TArtifactPreview (2), TArtifactPanel, ui-admin THtmlPreview at minimum.
    expect(iframes.length).toBeGreaterThanOrEqual(4);
    expect(new Set(iframes.map((p) => p.split('/')[0])).size).toBeGreaterThanOrEqual(2);
    expect(defaults.length).toBeGreaterThanOrEqual(3);
    // Each extracted value must be a real token list (or empty), never a stray
    // quote character - a destructuring slip in the prop-default branch would
    // otherwise let that branch pass on anything.
    for (const d of defaults) expect(d.value).toMatch(/^(\s*[a-z-]+\s*)*$/);
    expect(defaults.some((d) => d.source === 'prop default' && d.value.includes('allow-scripts'))).toBe(true);
  });

  it('never pairs allow-scripts with allow-same-origin by default', () => {
    const voided = defaults.filter((d) => isVoided(d.value));
    expect(
      voided.map((d) => `${d.file} (${d.source}): "${d.value}"`),
      'allow-scripts + allow-same-origin voids the sandbox; drop allow-same-origin and let first-party callers opt in via the prop',
    ).toEqual([]);
  });

  /**
   * A static attribute cannot be overridden by a consumer, so an iframe that
   * needs scripts must expose the value as a prop (the `iframeSandbox` shape
   * `TArtifactPanel` established) rather than hardcoding it.
   */
  it('exposes any script-enabled sandbox as a prop rather than a static attribute', () => {
    const hardcoded = defaults.filter(
      (d) => d.source === 'static attribute' && d.value.split(/\s+/).includes('allow-scripts'),
    );
    expect(hardcoded.map((d) => `${d.file}: "${d.value}"`)).toEqual([]);
  });
});
