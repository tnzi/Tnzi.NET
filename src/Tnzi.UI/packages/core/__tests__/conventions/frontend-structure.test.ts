// @vitest-environment node
/**
 * Convention gate: **file placement and file naming across all five packages**.
 *
 * ## Why this exists
 *
 * The five packages are edited by many hands, often one task at a time, and
 * every one of the rules below had already drifted by 2026-08-02 in a way that
 * nothing could notice:
 *
 *   - **Tests inside `src/`** - `core` and `mobile` kept them there while `ui`,
 *     `ui-ai` and `ui-admin` used the package root. Two of the strays were the
 *     only `.spec.ts` files in a repo of 377 test files, and because
 *     `ui-admin/tsconfig.build.json` was the one build config without an
 *     `exclude`, one of them shipped: `dist/components/pages/__tests__/
 *     TLoginPage.spec.d.ts` went out in the published package.
 *   - **Missing folder barrels** - `ui` and `ui-ai` had one per component
 *     folder; `ui-admin` was missing seven and `mobile` five, so whether you
 *     could import a folder depended on which folder you happened to pick.
 *   - **camelCase vs kebab-case** - both were in use inside single directories
 *     (`ui-admin/services/` held 28 `*-bridge.ts` next to `defineCrudBridge.ts`;
 *     `ui-ai/utils/` held `markdown-normalizers.ts` next to `fileIcon.ts`).
 *
 * None of it breaks a build or fails a behavioural test - the code works fine
 * either way - so a structural test is the only thing that can hold the line.
 *
 * ## The naming rule, and why it is this rule
 *
 * A module's filename is EITHER the name of its main value export (keeping that
 * symbol's casing) OR a kebab-case description of its topic.
 *
 *   useChat.ts          exports useChat          -> named after its export
 *   createAdminApp.ts   exports createAdminApp   -> named after its export
 *   vPermission.ts      exports vPermission      -> named after its export
 *   ai-bridge.ts        exports many functions   -> topic
 *   finance-format.ts   exports many helpers     -> topic
 *
 * This was picked by measuring candidates against the tree rather than by
 * preference. "One symbol only" scored 56% (it fails every `useX.ts` that also
 * exports its `UseXReturn` type); this one scored 85%, and every file in the
 * remaining 15% read as a genuine mistake on inspection.
 */
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, existsSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, join, relative } from 'node:path';

const packagesDir = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const PACKAGES = ['core', 'ui', 'ui-ai', 'ui-admin', 'mobile'];

function walk(dir: string, out: string[] = []): string[] {
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

const srcFiles = (pkg: string): string[] => walk(resolve(packagesDir, pkg, 'src'));
const rel = (pkg: string, file: string): string =>
  `${pkg}/${relative(resolve(packagesDir, pkg), file).split(/[\\/]/).join('/')}`;

describe('frontend package structure', () => {
  // Guards the guard: a bad root would make every assertion below pass over an
  // empty list, which is the exact silent-green failure these tests exist for.
  it('can see all five packages', () => {
    expect(PACKAGES.filter((p) => srcFiles(p).length > 0)).toEqual(PACKAGES);
  });

  describe('tests live at the package root, never inside src/', () => {
    it.each(PACKAGES)('%s', (pkg) => {
      const strays = srcFiles(pkg)
        .filter((f) => /\.(test|spec)\.[tj]sx?$/.test(f) || /[\\/]__tests__[\\/]/.test(f))
        .map((f) => rel(pkg, f));

      expect(
        strays,
        strays.length
          ? `Tests found under src/:\n  ${strays.join('\n  ')}\n` +
              `Move them to packages/${pkg}/__tests__/. A test under src/ is reachable ` +
              `by the declaration build, and the one config that forgot an \`exclude\` ` +
              `published a .spec.d.ts to npm.`
          : '',
      ).toEqual([]);
    });

    it('a test file is named after the module it covers', () => {
      // Added after the 2026-08-02 rename pass renamed 32 modules and left 15
      // test files carrying the old name (`headless/chatSounds.test.ts` next to
      // `headless/chat-sounds.ts`). Nothing broke - vitest finds tests by glob,
      // not by name - so only a structural check can see it.
      const orphans = PACKAGES.flatMap((pkg) => {
        const sources = new Set(
          srcFiles(pkg)
            .filter((f) => /\.(ts|vue)$/.test(f) && !f.endsWith('.d.ts'))
            .map((f) => f.split(/[\\/]/).pop()!.replace(/\.(ts|vue)$/, '')),
        );
        return walk(resolve(packagesDir, pkg, '__tests__'))
          .filter((f) => f.endsWith('.test.ts'))
          .map((f) => ({ file: f, base: f.split(/[\\/]/).pop()!.slice(0, -8) }))
          // Only flag a test whose name looks like a module name that no longer
          // exists but whose kebab-case form does - i.e. it was left behind by a
          // rename. Suite-style names (`builtin-pages`, `crud-handlers-coverage`)
          // legitimately match no single module.
          .filter(({ base }) => {
            if (sources.has(base)) return false;
            const kebab = base.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
            return kebab !== base && sources.has(kebab);
          })
          .map(({ file, base }) => {
            const kebab = base.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
            return `${rel(pkg, file)} -> rename to ${kebab}.test.ts`;
          });
      });

      expect(
        orphans,
        orphans.length
          ? `Test files left behind by a module rename:\n  ${orphans.join('\n  ')}`
          : '',
      ).toEqual([]);
    });

    it('every package names its test files .test.ts, not .spec.ts', () => {
      // 377 test files, and the only two `.spec.ts` were also the only two
      // living under src/ - the same batch of misplaced work, twice over.
      const specs = PACKAGES.flatMap((pkg) =>
        walk(resolve(packagesDir, pkg, '__tests__'))
          .filter((f) => /\.spec\.[tj]sx?$/.test(f))
          .map((f) => rel(pkg, f)),
      );
      expect(specs).toEqual([]);
    });
  });

  /*
   * NOT CHECKED: "every component folder owns an index.ts".
   *
   * It looks like drift - `ui` and `ui-ai` have one per folder, `ui-admin` has
   * 8 of 15, `mobile` none - and it was very nearly gated here. Reading the
   * barrels says otherwise: a folder gets an index.ts when ALL of it is public,
   * and folders whose contents are mostly internal are hand-picked into
   * `components/index.ts` instead. `ui-admin/components/chat/` exports exactly
   * one of its 25 files ("the richer chat widgets stay internal to the shell"),
   * `settings/` holds back `TSettingsField` ("the panel's internal field
   * renderer and stays private"). Forcing a barrel onto those folders would
   * publish every internal component of the admin shell.
   *
   * So the rule is "a barrel means the whole folder is public", and the thing
   * worth gating - that a folder which HAS a barrel is re-exported wholesale
   * rather than cherry-picked - is already covered by
   * `ui-admin/__tests__/publicApi.test.ts`.
   */

  describe('module filenames', () => {
    /**
     * Value exports only. `useChat.ts` exports `useChat` plus `UseChatReturn`
     * and `UseChatOptions`; counting types would make almost every composable
     * look like a multi-export "topic" module.
     */
    function valueExports(source: string): Set<string> {
      const names = new Set<string>();
      for (const m of source.matchAll(
        /^export\s+(?:async\s+)?(?:function|const|class|let|enum)\s+([A-Za-z_$][\w$]*)/gm,
      )) {
        names.add(m[1]);
      }
      for (const block of source.matchAll(/^export\s*\{([^}]*)\}/gm)) {
        for (const part of block[1].split(',')) {
          const trimmed = part.trim();
          if (!trimmed || trimmed.startsWith('type ')) continue;
          names.add(trimmed.split(/\s+as\s+/).pop()!.trim());
        }
      }
      return names;
    }

    const camelize = (s: string): string => s.replace(/-(\w)/g, (_, c: string) => c.toUpperCase());

    /**
     * `locales/` filenames are BCP 47 tags (`zh-cn.ts` exporting `zhCn`). The
     * hyphen is part of the standard, not a naming choice - renaming it to
     * `zhCn.ts` would make the folder stop reading as a set of language tags.
     */
    const isLocaleTag = (file: string): boolean => /[\\/]locales[\\/]/.test(file);

    it.each(PACKAGES)('%s', (pkg) => {
      const offenders: string[] = [];

      for (const file of srcFiles(pkg)) {
        if (!file.endsWith('.ts') || file.endsWith('.d.ts')) continue;
        const base = file.split(/[\\/]/).pop()!.slice(0, -3);
        if (base === 'index') continue;
        // Single lowercase words (`utils.ts`, `theme.ts`) satisfy both forms.
        if (!base.includes('-') && !/[a-z][A-Z]/.test(base)) continue;
        if (isLocaleTag(file)) continue;
        // `TSchemaForm.ts` / `TAdminWindowHandles.ts` declare components with a
        // render function instead of a template. They are components, and this
        // repo names components `T` + PascalCase whether the file ends in .vue
        // or .ts - so they follow the .vue rule below, not this one.
        if (/^T[A-Z]/.test(base)) continue;

        const exports = valueExports(readFileSync(file, 'utf8'));
        const namedAfterExport = exports.has(camelize(base)) || exports.has(base);
        const isKebab = base.includes('-');

        // Only one direction is a defect. A camelCase filename *promises* a
        // symbol of that name; when there is none, the name is simply wrong -
        // `plugin/chatConfig.ts` exports `mapChatConfig` and `ChatConfig`, so
        // "chatConfig" names nothing at all.
        //
        // The mirror case is left alone on purpose: `utils/deep-equal.ts`
        // exports exactly `deepEqual`, and both readings are honest - it is
        // that export, and it is also a kebab-case topic. Forcing it either way
        // would be churn dressed up as consistency.
        if (!namedAfterExport && !isKebab) {
          const kebab = base.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
          offenders.push(`${rel(pkg, file)} -> rename to ${kebab}.ts (no export of that name)`);
        }
      }

      expect(
        offenders,
        offenders.length
          ? `Filenames that are neither their main export nor a kebab-case topic:\n  ${offenders.join('\n  ')}\n` +
              'A module filename is EITHER the name of its main value export (keep the ' +
              "symbol's casing) OR a kebab-case description of its topic."
          : '',
      ).toEqual([]);
    });

    it('every .vue file is PascalCase', () => {
      const offenders = PACKAGES.flatMap((pkg) =>
        srcFiles(pkg)
          .filter((f) => f.endsWith('.vue'))
          .filter((f) => !/^[A-Z]/.test(f.split(/[\\/]/).pop()!))
          .map((f) => rel(pkg, f)),
      );
      expect(
        offenders,
        offenders.length
          ? `Non-PascalCase single-file components:\n  ${offenders.join('\n  ')}\n` +
              'A component file is named after the component it declares.'
          : '',
      ).toEqual([]);
    });
  });
});
