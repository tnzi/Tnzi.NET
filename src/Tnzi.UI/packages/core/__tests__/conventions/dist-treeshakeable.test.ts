// @vitest-environment node
/**
 * Convention gate: **importing one functional area must not drag in the other
 * eighteen**, and the sibling packages must reach core by area rather than
 * through the root barrel.
 *
 * ## What is actually going on
 *
 * `@tnzi/core` carries the contracts for all 19 backend modules, and two thirds
 * of its source is that service layer. That is the right home for it - the
 * contracts are UI-framework-agnostic, and moving them up into `@tnzi/ui-admin`
 * would force anyone wanting to call an admin endpoint without the Naive-UI
 * admin shell to depend on the whole UI package. The cost of keeping them here
 * is that a consumer who wants *one* helper must not pay for *all* of them.
 *
 * Two separate import paths, with very different behaviour:
 *
 *   - **By area** (`@tnzi/core/utils`, `@tnzi/core/services/identity`, …) shakes
 *     cleanly. tsup emits one self-contained bundle per entry, and rollup takes
 *     it apart per symbol: `services/identity` down to `useAuthApi` measures
 *     3,568 B with zero admin endpoint literals.
 *   - **Root barrel** (`import { X } from '@tnzi/core'`) does not shake, because
 *     `dist/index.js` is one big module rather than a module graph. It used to
 *     also re-export all 19 service namespaces, which put two symbols at
 *     336,714 B and all 107 admin endpoint literals; dropping that re-export
 *     (zero consumers ecosystem-wide) took the same call to 21,154 B and 0
 *     literals. Services are reached by area only.
 *
 * So the rule is: **consume core by area**. On 2026-08-15 `@tnzi/ui-ai` was not,
 * and a pure chat application shipped the finance, audit and data-destruction
 * endpoint tables to anonymous visitors for the sake of `formatFileSize` and
 * `createTnziAuthGuard`. Pointing those three imports at `@tnzi/core/utils` and
 * `@tnzi/core/guards` took chat from 108 admin endpoint literals to 3.
 *
 * ## Why not just make the root barrel shakeable
 *
 * Tried it. `splitting: true` + `"sideEffects": false` does fix the root barrel
 * (336,714 B -> 1,895 B), but esbuild then slices chunks by "shared between
 * entries", so one chunk mixes several services and every by-area consumer has
 * to swallow the whole chunk. Measured on the same commit: chat 3 -> 41 literals,
 * a site that only ever imports subpaths 1 -> 37. Rescuing the root barrel by
 * making every disciplined consumer pay for it is a bad trade, so the build
 * stays unsplit and this gate holds the by-area contract instead.
 *
 * ## Why this bundles for real instead of asserting the config
 *
 * Asserting `splitting === false` pins the one condition we know about today.
 * It also has to be *this* toolchain: `rollup entry.js --file out.js` on the
 * root-barrel import reported 165 B and 0 literals where vite reported 336 kB
 * and 107 - the naked rollup CLI skips the plugin chain consumers actually run
 * through, so it passes things that are broken.
 */
import { describe, it, expect, beforeAll } from 'vitest';
import { build } from 'vite';
import { mkdtempSync, rmSync, writeFileSync, readFileSync, existsSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join, relative } from 'node:path';
import { tmpdir } from 'node:os';

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const packagesDir = resolve(packageRoot, '..');

/** Endpoint literals under the admin surface, e.g. `"/admin/finance/accounts"`. */
const ADMIN_LITERAL = /"\/admin\/[a-z0-9/-]+"/g;

/** Bundle `source` through vite the way a consumer app does. */
async function bundleAgainstDist(
  source: string,
  alias: { find: RegExp; replacement: string }[],
): Promise<string> {
  const dir = mkdtempSync(join(tmpdir(), 'tnzi-core-shake-'));
  try {
    writeFileSync(join(dir, 'entry.ts'), source, 'utf8');
    await build({
      root: dir,
      logLevel: 'silent',
      resolve: { alias },
      build: {
        lib: { entry: join(dir, 'entry.ts'), formats: ['es'], fileName: () => 'out.js' },
        rollupOptions: { external: ['vue'] },
        outDir: join(dir, 'out'),
        minify: false,
        write: true,
      },
    });
    return readFileSync(join(dir, 'out/out.js'), 'utf8');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

describe('importing core by area stays scoped to that area', () => {
  beforeAll(() => {
    // Never skip when `dist` is absent. A gate that silently opts out when its
    // input is missing is indistinguishable from a passing one - that is how
    // `codegen:check` stayed green for months without checking anything.
    if (!existsSync(join(packageRoot, 'dist/index.js'))) {
      throw new Error(
        'dist is missing. This gate bundles the built output, so run ' +
          '`pnpm --filter @tnzi/core build` before `test`.',
      );
    }
  });

  it('gives a chat-shaped consumer only the AI surface', async () => {
    // The exact shape `@tnzi/ui-ai` consumes: the AI service by subpath, plus
    // the two loose helpers that used to come from the root barrel.
    const out = await bundleAgainstDist(
      `import { useChatApi, useThreadApi, streamChat } from '@tnzi/core/services/ai'\n` +
        `import { formatFileSize } from '@tnzi/core/utils'\n` +
        `import { createTnziAuthGuard } from '@tnzi/core/guards'\n` +
        `export { useChatApi, useThreadApi, streamChat, formatFileSize, createTnziAuthGuard }\n`,
      [
        { find: /^@tnzi\/core\/services\/ai$/, replacement: join(packageRoot, 'dist/services/ai/index.js') },
        { find: /^@tnzi\/core\/utils$/, replacement: join(packageRoot, 'dist/utils/index.js') },
        { find: /^@tnzi\/core\/guards$/, replacement: join(packageRoot, 'dist/guards/index.js') },
      ],
    );

    const literals = [...new Set(out.match(ADMIN_LITERAL) ?? [])];
    const foreign = literals.filter((l) =>
      /finance|audit|payroll|template|logging|signing|payment|storage|notification/.test(l),
    );
    expect(foreign, 'modules outside the AI surface leaked into a chat-shaped bundle').toEqual([]);
  }, 60_000);

  it('gives an auth-only consumer no admin surface at all', async () => {
    const out = await bundleAgainstDist(
      `import { useAuthApi } from '@tnzi/core/services/identity'\nexport { useAuthApi }\n`,
      [
        {
          find: /^@tnzi\/core\/services\/identity$/,
          replacement: join(packageRoot, 'dist/services/identity/index.js'),
        },
      ],
    );

    const literals = [...new Set(out.match(ADMIN_LITERAL) ?? [])].sort();
    expect(literals, 'an auth-only consumer pulled in admin endpoints').toEqual([]);
    expect(out.length).toBeLessThan(20_000);
  }, 60_000);
});

/**
 * Nothing in this repository may reach for the core root barrel at runtime -
 * not the sibling packages, not the documentation examples, not the scaffolding
 * templates that generate new projects.
 *
 * Scope notes, both of which are load-bearing:
 *
 *   - **`@tnzi/core` only.** The other four packages build with vite's
 *     `preserveModules: true`, so their `dist` *is* a module graph and a root
 *     barrel import shakes fine (`@tnzi/mobile` root barrel down to one
 *     component measures 10,882 B). `import { createAdminApp } from
 *     '@tnzi/ui-admin'` is the documented, correct way to use that package.
 *     Only core ships one big module per entry, so only core has this problem.
 *   - **`import type` is fine.** It is erased at compile time, which is why
 *     `@tnzi/ui` stayed clean through five root-barrel imports. A *value*
 *     import is what costs the 128 kB barrel.
 *
 * `ui-admin` is exempt as a consumer: it is the admin shell, it legitimately
 * consumes most of the admin surface, and it has ~80 such imports (all of them
 * utils helpers and base types, measured). Its *documentation* is not exempt.
 *
 * ## Why docs and tools are in scope
 *
 * The two problems this gate was extended to catch on 2026-08-16 both lived
 * outside compiled code, so nothing could notice them: `capabilities.md` taught
 * `import { declareClientCapability } from '@tnzi/core'`, and the CLI's frontend
 * scaffolder emitted `import { createHttpClient } from '@tnzi/core'` into every
 * generated project. Neither breaks a build - the symbols really are on the
 * root barrel - they just hand each reader and each new project the full bill.
 */
describe('nothing reaches core through the root barrel', () => {
  const repoRoot = resolve(packageRoot, '../../../..');

  /**
   * Scan roots, each with the extensions worth reading there.
   *
   * `docs/` and `tools/` are absent from the public mirror, which projects only
   * `src/` and `tests/`. A missing root is therefore legitimate rather than a
   * failure - but "all roots missing" must not read as a pass, so the surface
   * itself is asserted below.
   */
  const ROOTS = [
    { name: 'packages/ui/src', dir: join(packagesDir, 'ui', 'src'), ext: /\.(ts|vue)$/ },
    { name: 'packages/ui-ai/src', dir: join(packagesDir, 'ui-ai', 'src'), ext: /\.(ts|vue)$/ },
    { name: 'packages/mobile/src', dir: join(packagesDir, 'mobile', 'src'), ext: /\.(ts|vue)$/ },
    { name: 'docs', dir: join(repoRoot, 'docs'), ext: /\.md$/ },
    { name: 'tools', dir: join(repoRoot, 'tools'), ext: /\.(cs|ts|vue)$/ },
  ];

  const SKIP_DIR = /(^|[\\/])(node_modules|dist|obj|bin|\.git)([\\/]|$)/;

  function filesUnder(dir: string, ext: RegExp, out: string[] = []): string[] {
    if (!existsSync(dir) || SKIP_DIR.test(dir)) return out;
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = join(dir, entry.name);
      if (entry.isDirectory()) filesUnder(full, ext, out);
      else if (ext.test(entry.name)) out.push(full);
    }
    return out;
  }

  /**
   * Markdown mentions the bad form on purpose when explaining why it is bad, so
   * only fenced code blocks count there. Prose and inline code are prose.
   */
  function offendingLines(file: string): string[] {
    const isMarkdown = file.endsWith('.md');
    const found: string[] = [];
    let insideFence = false;

    for (const [index, line] of readFileSync(file, 'utf8').split('\n').entries()) {
      if (isMarkdown) {
        if (line.trimStart().startsWith('```')) {
          insideFence = !insideFence;
          continue;
        }
        if (!insideFence) continue;
      }
      if (!/from\s+['"]@tnzi\/core['"]/.test(line)) continue;
      if (/^\s*import\s+type\s/.test(line)) continue; // erased at compile time
      found.push(`${relative(repoRoot, file).replace(/\\/g, '/')}:${index + 1}: ${line.trim()}`);
    }
    return found;
  }

  it.each(ROOTS)('$name has no value import from the bare @tnzi/core specifier', ({ dir, ext }) => {
    const offenders = filesUnder(dir, ext).flatMap(offendingLines);

    expect(
      offenders,
      'reach into the matching subpath instead (@tnzi/core/utils, /http, /adapters, /guards, ' +
        '/services/*) - a value import from the bare specifier drags in the whole barrel',
    ).toEqual([]);
  });

  it('actually scanned something', () => {
    // Guards the scan surface itself. Every root being absent - a moved
    // directory, a partial checkout - would otherwise make this whole block
    // vacuously green, which is the failure mode every gate here is written
    // against. `packages/*/src` always exists wherever this test can run.
    const scanned = ROOTS.filter((r) => existsSync(r.dir));
    expect(scanned.map((r) => r.name)).toContain('packages/ui/src');
    expect(filesUnder(scanned[0].dir, scanned[0].ext).length).toBeGreaterThan(0);
  });
});

/**
 * Every declared subpath must resolve on disk.
 *
 * The first version of this block asserted against the *text* of
 * `tsup.config.ts`. Commenting an entry out left the string in place, so the
 * gate stayed green while the build had stopped producing the file - source
 * text cannot tell code from a comment. Assert the artifacts instead.
 */
describe('every declared subpath resolves on disk', () => {
  const pkg = JSON.parse(readFileSync(join(packageRoot, 'package.json'), 'utf8')) as {
    exports: Record<string, Record<string, string>>;
  };

  const conditionTargets = Object.entries(pkg.exports)
    .filter(([subpath]) => !subpath.includes('*'))
    .flatMap(([subpath, conditions]) =>
      Object.entries(conditions).map(([condition, target]) => ({ subpath, condition, target })),
    );

  it.each(conditionTargets)('$subpath ($condition) -> $target', ({ target }) => {
    expect(existsSync(join(packageRoot, target))).toBe(true);
  });

  it('builds every services/* directory the wildcard export promises', () => {
    // `"./services/*"` is a pattern export, so the loop above cannot check it -
    // and a pattern resolving to nothing fails at the consumer's import, not
    // here. Walk the source directories: each is a subpath a consumer can
    // legitimately reach for, and each needs its own tsup entry.
    const modules = readdirSync(join(packageRoot, 'src/services'), { withFileTypes: true })
      .filter((e) => e.isDirectory())
      .map((e) => e.name);

    expect(modules.length).toBeGreaterThan(0);

    const missing = modules.filter(
      (m) => !existsSync(join(packageRoot, `dist/services/${m}/index.js`)),
    );
    expect(missing, 'services dirs with no built subpath (missing tsup entry?)').toEqual([]);
  });

  it('reaches guards without going through the root barrel', () => {
    // 191 lines, no runtime dependencies, and the one thing a lightweight chat
    // app needs from core. Root-barrel-only until 2026-08-15, which is how a
    // chat bundle ended up carrying the finance endpoint table.
    expect(pkg.exports['./guards']).toBeDefined();
    expect(existsSync(join(packageRoot, 'dist/guards/index.js'))).toBe(true);
  });
});
