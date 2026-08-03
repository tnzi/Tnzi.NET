// @vitest-environment node
//
// Node rather than the project-wide happy-dom: this file only compares text,
// and happy-dom leaves `import.meta.url` on a non-file scheme so path-based
// reads throw. Importing the stylesheet with `?raw` is not an alternative -
// vitest stubs CSS imports to an empty string by default, which would make
// every assertion below pass against nothing.
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { TOKEN_TO_VAR } from '../../src/theme/tokens';

const stylesheet = readFileSync(
  fileURLToPath(new URL('../../src/styles/index.css', import.meta.url)),
  'utf8',
);

/**
 * Guards the failure mode this package already shipped once: `TOKEN_TO_VAR`
 * mapped its keys to `--ai-*` names, nothing in the package reads those, and
 * so `applyAiTheme()` was a silent no-op for every single token. Nothing threw
 * and nothing looked wrong - the colours just never changed.
 *
 * Unit tests on `applyAiTheme` cannot catch it: they assert that the function
 * writes the variable the map names, which stays true when the map is wrong.
 * The only check that catches it compares the map against the stylesheet.
 */

/** Every `--tnzi-ai-*` custom property the stylesheet declares (light or dark). */
const declaredVars = new Set(
  Array.from(stylesheet.matchAll(/(--tnzi-ai-[a-z0-9-]+)\s*:/gi), (match) => match[1]),
);

describe('TOKEN_TO_VAR', () => {
  it('has entries to check', () => {
    // Either side reading as empty would make every other assertion here pass
    // vacuously - which is exactly how the first attempt at this test failed
    // silently against a stubbed stylesheet.
    expect(Object.keys(TOKEN_TO_VAR).length).toBeGreaterThan(20);
    expect(declaredVars.size).toBeGreaterThan(50);
  });

  it('maps every key to a variable the stylesheet declares', () => {
    const orphans = Object.entries(TOKEN_TO_VAR)
      .filter(([, cssVar]) => !declaredVars.has(cssVar))
      .map(([key, cssVar]) => `${key} -> ${cssVar}`);

    expect(orphans).toEqual([]);
  });

  it('names only --tnzi-ai-* variables', () => {
    const wrongPrefix = Object.entries(TOKEN_TO_VAR)
      .filter(([, cssVar]) => !cssVar.startsWith('--tnzi-ai-'))
      .map(([key, cssVar]) => `${key} -> ${cssVar}`);

    expect(wrongPrefix).toEqual([]);
  });

  it('does not point two keys at the same variable', () => {
    const seen = new Map<string, string>();
    const collisions: string[] = [];
    for (const [key, cssVar] of Object.entries(TOKEN_TO_VAR)) {
      const previous = seen.get(cssVar);
      if (previous) collisions.push(`${previous} and ${key} both -> ${cssVar}`);
      else seen.set(cssVar, key);
    }

    expect(collisions).toEqual([]);
  });
});
