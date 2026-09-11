/**
 * Extraction rules for the framework icon manifest.
 *
 * The manifest answers one question a downstream cannot answer for itself:
 * *which Iconify names does the framework render?* `TSvgIcon` takes a runtime
 * string (including strings that arrive from the backend), so no bundler-level
 * analyser - UnoCSS `presetIcons`, `unplugin-icons` - can see them. An
 * application that wants to run with no internet has to bundle those icons via
 * `addCollection`, and without a published list its only option is to
 * re-implement the two scans below against this repository.
 *
 * Both scans live here so the generator and `--check` share one definition; the
 * *independent* verification lives on the .NET side
 * (`tests/Tnzi.Architecture.Tests/IconManifestTests`), which reflects over the
 * real `[RuntimeSettingGroup]` attributes instead of reading them with a regex,
 * and re-scans the frontend with its own matcher. A gate that only agrees with
 * its own generator would prove nothing.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

/**
 * Iconify collections the framework is allowed to reference.
 *
 * An allowlist rather than "any `a:b` string" because the source is full of
 * lookalikes - `update:modelValue`, `module:key`, i18n keys, CSS shorthand.
 * The blind spot it creates (a *new* collection would be skipped silently) is
 * closed by `findUnknownPrefixCandidates`, which keys off icon-shaped call
 * sites instead of off the prefix.
 */
export const ICON_PREFIXES = Object.freeze([
  'line-md',
  'lucide',
  'material-symbols',
  'mdi',
  'nimbus',
  'simple-icons',
]);

/**
 * Packages that render icons, in dependency order.
 *
 * Each manifest is cumulative - the package's own names plus every name below
 * it - so a consumer imports exactly one manifest, the one matching the topmost
 * @tnzi package it depends on, and is done. `@tnzi/mobile` is absent on
 * purpose: it renders Vant icons and never touches Iconify.
 *
 * `bundler` says whether the package also ships {@link BUNDLER_SOURCE}. Every
 * package does except `@tnzi/core`: the helper calls `addCollection` from
 * `@iconify/vue`, and core is the headless package - it has no Vue-side
 * dependency and must not gain one for a list of six device glyphs. Core's
 * manifest stays pure data and its `@example` points at the layer above.
 */
export const ICON_PACKAGES = Object.freeze([
  { name: 'core', dependsOn: [], includeBackend: false, bundler: false },
  { name: 'ui', dependsOn: ['core'], includeBackend: false, bundler: true },
  { name: 'ui-ai', dependsOn: ['core', 'ui'], includeBackend: false, bundler: true },
  /**
   * The backend names land here: `[RuntimeSettingGroup(Icon = "mdi:...")]`
   * reaches the browser as `SettingsCenterGroupDto.icon` and is rendered by
   * ui-admin's settings centre. They appear in no frontend source at all, which
   * is the half of the problem a downstream is least likely to discover.
   */
  { name: 'ui-admin', dependsOn: ['core', 'ui', 'ui-ai'], includeBackend: true, bundler: true },
]);

/**
 * The runtime half of the manifest, embedded verbatim into every generated
 * manifest that sets `bundler`.
 *
 * ★ Why this is framework code and not three lines in the docs. The recipe it
 * replaces read `getIcons(collections[prefix], names)` / `addCollection(...)`
 * inline, and both steps are silent when wrong:
 *   - `getIcons` returns `IconifyJSON | null`, and `addCollection` accepts the
 *     `null` at runtime. Every icon in that collection then renders blank -
 *     no error, no failed request.
 *   - a collection the caller forgot to pass produced exactly the same silence.
 * Neither is a mistake a consumer can be asked to remember, so neither is left
 * at the copy-paste boundary. The helper returns what it could not do instead,
 * which is the whole point: this file exists because blank icons are invisible.
 *
 * ★ `[...names]` is load-bearing, not a style choice. The published type is
 * `readonly string[]` and `getIcons` takes `string[]`; passing it straight
 * through does not compile under `strict`, which is what the previous recipe
 * did (it was never type-checked - see `__tests__/icons/`).
 */
export const BUNDLER_SOURCE = `/** What a bundling step could *not* do. Empty fields mean every icon is accounted for. */
export interface TnziIconBundleReport {
  /** Icons resolved out of the collections that were passed. */
  readonly loaded: number
  /** Collections this manifest needs that the caller did not pass. */
  readonly missingCollections: readonly string[]
  /** Qualified names that stayed unresolved, including every name from a missing collection. */
  readonly unresolved: readonly string[]
}

/** {@link selectTnziIcons}: the trimmed collections, plus what it could not find. */
export interface TnziIconSelection extends TnziIconBundleReport {
  /** One \`IconifyJSON\` per collection, carrying only this manifest's icons. */
  readonly subsets: readonly IconifyJSON[]
}

/**
 * Trims full Iconify collections down to just the icons this manifest names.
 *
 * ★ Use this at **build time** and write the result to a JSON file. Measured on
 * this manifest: 170 KB of glyphs versus 17 MB of collections, a 100x difference.
 * The reason is that \`getIcons\` runs at runtime, but a static \`import\` of
 * \`@iconify-json/material-symbols\` puts all 8 MB of it in the bundle first, and
 * no bundler can tree-shake *inside* a JSON object. {@link bundleTnziIcons}
 * pays that price for the convenience of a one-liner; this does not.
 *
 * Pure - it touches no icon storage, so it is safe to call from a Node script.
 */
export function selectTnziIcons(collections: readonly IconifyJSON[]): TnziIconSelection {
  const byPrefix = new Map(collections.map((collection) => [collection.prefix, collection]))
  const subsets: IconifyJSON[] = []
  const missingCollections: string[] = []
  const unresolved: string[] = []
  let loaded = 0

  for (const [prefix, names] of Object.entries(tnziIconsByPrefix)) {
    const qualify = (name: string) => \`\${prefix}:\${name}\`
    const collection = byPrefix.get(prefix)
    if (!collection) {
      missingCollections.push(prefix)
      unresolved.push(...names.map(qualify))
      continue
    }

    // \`[...names]\` because the published type is \`readonly string[]\` and
    // \`getIcons\` takes a mutable one. \`getIcons\` resolves alias chains, so the
    // subset can carry a name under either key; both count as resolved.
    const subset = getIcons(collection, [...names])
    if (!subset) {
      unresolved.push(...names.map(qualify))
      continue
    }

    subsets.push(subset)
    const resolved = new Set([
      ...Object.keys(subset.icons ?? {}),
      ...Object.keys(subset.aliases ?? {}),
    ])
    // Count the *requested* names that resolved, not the subset's entries:
    // resolving an alias pulls its parent in too, so the subset can hold more
    // icons than were asked for.
    loaded += names.filter((name) => resolved.has(name)).length
    unresolved.push(...names.filter((name) => !resolved.has(name)).map(qualify))
  }

  return { subsets, loaded, missingCollections, unresolved }
}

/**
 * Registers every icon in this manifest with \`@iconify/vue\`, so \`<Icon>\` stops
 * reaching for the public Iconify API at render time.
 *
 * Pass the collections as data - \`import { icons as mdi } from '@iconify-json/mdi'\`.
 * Each one carries its own \`prefix\`, so order and naming do not matter.
 *
 * ★ The imports must be **static**. A loop doing
 * \`import(\`@iconify-json/\${prefix}/icons.json\`)\` reads well and does not
 * survive bundling: the specifier is a bare package name built from a variable,
 * so Vite cannot analyse it, leaves it verbatim, and the browser then fails to
 * resolve it at runtime - with no build error and no warning.
 *
 * ★ This is the convenient path, not the small one. It puts the **whole** of
 * every collection in your bundle. If that matters, use {@link selectTnziIcons}
 * in a build step and \`addCollection\` the saved subsets instead.
 *
 * @returns what could not be loaded. Ignoring it is safe; reading it is how you
 * find out a collection is missing before your users see blank squares.
 */
export function bundleTnziIcons(collections: readonly IconifyJSON[]): TnziIconBundleReport {
  const { subsets, ...report } = selectTnziIcons(collections)
  for (const subset of subsets) addCollection(subset)
  return report
}`;

/** `line-md` -> `lineMd`, so a generated example can name its imports. */
export const camelize = (prefix) => prefix.replace(/-([a-z])/g, (_, c) => c.toUpperCase());

/**
 * The one copy of the icon-bundling recipe, rendered per package.
 *
 * Embedded into that package's manifest `@example` (and therefore into the
 * published `.d.ts`), quoted verbatim by `docs/frontend/*` for the aggregate
 * package, and **executed verbatim** by
 * `packages/ui-admin/__tests__/icons/bundle-recipe.ts` - which is the only
 * reason this is generated from one function rather than written out three
 * times.
 *
 * ★ Static imports, one per collection. See {@link BUNDLER_SOURCE} for what the
 * loop-with-a-dynamic-import version did instead: it built cleanly, shipped a
 * bare specifier the browser cannot resolve, and left the application in
 * exactly the state this manifest exists to prevent.
 */
export function renderBundlingRecipe(packageName, prefixes) {
  const sorted = [...prefixes].sort();
  // The package root, not `/icons.json`: the root is the entry point that ships
  // a `.d.ts` typing it as `IconifyJSON`, whereas the raw `.json` has no `types`
  // in its `exports` and leaves TypeScript to infer a 3 MB object literal.
  // Tree-shaking drops the `info`/`metadata`/`chars` the root also exports.
  const imports = sorted
    .map((prefix) => `import { icons as ${camelize(prefix)} } from '@iconify-json/${prefix}'`)
    .join('\n');
  return `import { bundleTnziIcons } from '@tnzi/${packageName}/icons'
${imports}

bundleTnziIcons([${sorted.map(camelize).join(', ')}])`;
}

const SOURCE_EXTENSIONS = ['.ts', '.tsx', '.vue', '.mts', '.js', '.mjs'];
const SKIPPED_DIRECTORIES = new Set(['node_modules', 'dist', 'coverage', 'bin', 'obj']);

/**
 * Where each package's generated manifest lives, relative to its `src/`.
 *
 * It must be excluded from the scan, and not as tidiness: the manifest is a
 * file full of quoted icon names sitting inside the tree being scanned, so
 * including it would make every package's "own" set absorb its dependencies',
 * and would make a *removed* icon survive forever - the file would keep
 * re-asserting the name that justifies its own contents, and the drift gate
 * would never go red for a deletion.
 */
export const MANIFEST_RELATIVE_PATH = join('icons', 'index.ts');

const prefixAlternation = ICON_PREFIXES.join('|');

/**
 * A quoted `prefix:name` literal.
 *
 * Single and double quotes only - every backtick occurrence in the tree is
 * prose inside a doc comment (`` `mdi:home` ``) and no icon is built from a
 * template literal. Quoted names *inside* comments are kept: there are a
 * handful, they are all real icons, and over-inclusion costs a downstream a few
 * unused glyphs while under-inclusion is the blank square this manifest exists
 * to prevent.
 */
const ICON_LITERAL = new RegExp(`(['"])((?:${prefixAlternation}):[a-z0-9][a-z0-9-]*)\\1`, 'g');

/** Any quoted `prefix:name`, whatever the collection. Used only inside icon-shaped regions. */
const ANY_ICON_LITERAL = /(['"])([a-z][a-z0-9-]*:[a-z0-9][a-z0-9-]*)\1/g;

/**
 * `prefix:name` in a position that is unmistakably an icon, whatever the prefix.
 *
 * Only used to detect a collection nobody added to {@link ICON_PREFIXES};
 * matching on context rather than on the prefix is the entire point.
 *
 * The value must be a *closed* quoted literal. Without that, the selector
 * `.t-button-icon:active:not(:disabled)` reads as an icon named `active:not`.
 */
const ICON_CONTEXT = /\bicon[A-Za-z]*\s*[:=]\s*(['"])([a-z][a-z0-9-]*:[a-z0-9][a-z0-9-]*)\1/g;

/**
 * The head of an array literal assigned to an icon-ish name.
 *
 * ★ Added after a mutation test showed the gap: dropping `'tabler:brand-github'`
 * into `TIconPicker`'s `DEFAULT_ICONS` left **both** gates green.
 * {@link ICON_CONTEXT} only recognises `icon…: 'a:b'`, and an array element is
 * not that shape - while `DEFAULT_ICONS` is precisely the list an administrator
 * picks from, whose choice is stored and later rendered. The most important
 * list in the tree sat in the blind spot.
 */
const ICON_ARRAY_HEAD = /\b([A-Za-z_$][\w$]*)\s*(?::[^=;\n]*)?=\s*\[/g;

/**
 * The regions of `text` that are array literals bound to an icon-ish name.
 *
 * Bracket-counting rather than a regex: an icon array spans many lines and
 * contains `[` inside neither strings nor comments in practice, but quoted
 * brackets are skipped anyway so a stray `']'` in a comment cannot truncate
 * the region and silently hide the rest of the list.
 */
function iconArrayRegions(text) {
  const regions = [];
  for (const match of text.matchAll(ICON_ARRAY_HEAD)) {
    if (!/icons?/i.test(match[1])) continue;
    const open = match.index + match[0].length - 1;
    let depth = 0;
    let quote = null;
    for (let i = open; i < text.length; i++) {
      const ch = text[i];
      if (quote) {
        if (ch === '\\') i++;
        else if (ch === quote) quote = null;
        continue;
      }
      if (ch === "'" || ch === '"' || ch === '`') quote = ch;
      else if (ch === '[') depth++;
      else if (ch === ']' && --depth === 0) {
        regions.push(text.slice(open, i + 1));
        break;
      }
    }
  }
  return regions;
}

/** `[RuntimeSettingGroup(...)]` attribute applications, and their `Icon =` argument. */
const RUNTIME_SETTING_GROUP_OPEN = '[RuntimeSettingGroup(';
const CSHARP_ICON_ARGUMENT = /\bIcon\s*=\s*"([^"]+)"/;

function walk(dir, accept, out = []) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return out;
  }
  for (const entry of entries) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (SKIPPED_DIRECTORIES.has(entry.name)) continue;
      walk(full, accept, out);
    } else if (entry.isFile() && accept(entry.name)) {
      out.push(full);
    }
  }
  return out;
}

const isSourceFile = (name) => SOURCE_EXTENSIONS.some((ext) => name.endsWith(ext));

/** A package's `src/` files, minus its own generated manifest. */
function packageSources(uiRoot, packageName) {
  const root = join(uiRoot, 'packages', packageName, 'src');
  const manifest = join(root, MANIFEST_RELATIVE_PATH);
  return walk(root, isSourceFile).filter((file) => file !== manifest);
}

/** Iconify names referenced by one package's own `src/` (not its dependencies'). */
export function scanPackage(uiRoot, packageName) {
  const names = new Set();
  for (const file of packageSources(uiRoot, packageName)) {
    for (const match of readFileSync(file, 'utf8').matchAll(ICON_LITERAL)) {
      names.add(match[2]);
    }
  }
  return names;
}

/**
 * Iconify names the backend hands to the browser.
 *
 * Anchored on the attribute rather than on `"mdi:..."` anywhere in a .cs file:
 * `SettingDefinitionGroup.Icon`'s own XML doc spells an example as `"mdi:web"`,
 * and a scan that swallowed doc comments would publish example names.
 */
export function scanBackend(repoRoot) {
  const root = join(repoRoot, 'src');
  const uiPath = join(root, 'Tnzi.UI') + sep;
  const names = new Set();
  for (const file of walk(root, (name) => name.endsWith('.cs'))) {
    if (file.startsWith(uiPath)) continue;
    const text = readFileSync(file, 'utf8');
    let cursor = text.indexOf(RUNTIME_SETTING_GROUP_OPEN);
    while (cursor !== -1) {
      const end = text.indexOf(')]', cursor);
      const icon = CSHARP_ICON_ARGUMENT.exec(text.slice(cursor, end === -1 ? text.length : end));
      if (icon) names.add(icon[1]);
      cursor = text.indexOf(RUNTIME_SETTING_GROUP_OPEN, cursor + RUNTIME_SETTING_GROUP_OPEN.length);
    }
  }
  return names;
}

/**
 * Icon-shaped references whose collection is not in {@link ICON_PREFIXES}.
 *
 * Non-empty means the allowlist has fallen behind the source, and every name in
 * that collection is missing from the manifest - the exact silent shortfall
 * this mechanism exists to prevent, reproduced one level up.
 *
 * Two matchers, because one shape is not enough: a call site
 * ({@link ICON_CONTEXT}) and a curated list ({@link ICON_ARRAY_HEAD}).
 */
export function findUnknownPrefixCandidates(uiRoot, repoRoot) {
  const known = new Set(ICON_PREFIXES);
  const findings = [];
  const record = (name, file) => {
    if (known.has(name.slice(0, name.indexOf(':')))) return;
    findings.push({ name, file: relative(repoRoot, file).split(sep).join('/') });
  };

  for (const pkg of ICON_PACKAGES) {
    for (const file of packageSources(uiRoot, pkg.name)) {
      const text = readFileSync(file, 'utf8');
      for (const match of text.matchAll(ICON_CONTEXT)) record(match[2], file);
      for (const region of iconArrayRegions(text)) {
        for (const match of region.matchAll(ANY_ICON_LITERAL)) record(match[2], file);
      }
    }
  }
  return findings;
}

/** Per-package cumulative name lists, sorted, keyed by package name. */
export function buildManifest(uiRoot, repoRoot) {
  const own = new Map(ICON_PACKAGES.map((pkg) => [pkg.name, scanPackage(uiRoot, pkg.name)]));
  const backend = scanBackend(repoRoot);
  const manifest = new Map();
  for (const pkg of ICON_PACKAGES) {
    const names = new Set(own.get(pkg.name));
    for (const dep of pkg.dependsOn) {
      for (const name of own.get(dep)) names.add(name);
    }
    if (pkg.includeBackend) {
      for (const name of backend) names.add(name);
    }
    manifest.set(pkg.name, [...names].sort());
  }
  return { manifest, own, backend };
}

export function groupByPrefix(names) {
  const grouped = {};
  for (const name of names) {
    (grouped[name.slice(0, name.indexOf(':'))] ??= []).push(name);
  }
  return grouped;
}
