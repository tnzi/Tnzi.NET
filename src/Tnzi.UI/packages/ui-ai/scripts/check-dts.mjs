/**
 * Fails the build if any emitted `.d.ts` still names the `@/` path alias.
 *
 * `@/` is a Vite/vitest build-time concept. A *value* import through it is
 * fine - the bundler resolves it and the alias never reaches the output. A
 * *type* import is copied into the emitted declaration verbatim, where no
 * consumer can resolve it. TypeScript does not error on that: it silently
 * widens the type to `any`.
 *
 * That is how this package shipped `ChatMessage`, `ThreadItem`, `NavItem`,
 * `SidebarMode`, `CommandAction` and `SettingsSection` as `any` across 24
 * declaration files - through a green `typecheck`, a green `build` and a green
 * test run, because inside the package the alias resolves perfectly. It only
 * surfaced downstream, and only where a broken type happened to land on a
 * callback parameter under `noImplicitAny`.
 *
 * A unit test cannot catch this (it runs with the alias configured). Checking
 * the emitted artifact is the only place the truth shows up.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const packageRoot = fileURLToPath(new URL('..', import.meta.url));
const distRoot = join(packageRoot, 'dist');

/** Aliases declared in vite/vitest config that must never survive into d.ts. */
const FORBIDDEN = [/from ['"]@\/[^'"]+['"]/g];

function walk(dir) {
  const out = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) out.push(...walk(full));
    else if (entry.endsWith('.d.ts')) out.push(full);
  }
  return out;
}

let distFiles;
try {
  distFiles = walk(distRoot);
} catch {
  console.error('check-dts: dist/ not found - run the build first.');
  process.exit(1);
}

const offenders = [];
for (const file of distFiles) {
  const text = readFileSync(file, 'utf8');
  for (const pattern of FORBIDDEN) {
    for (const match of text.matchAll(pattern)) {
      offenders.push(`${relative(packageRoot, file)}: ${match[0]}`);
    }
  }
}

if (offenders.length > 0) {
  console.error(
    `\ncheck-dts: ${offenders.length} unresolved path alias(es) in emitted declarations.\n` +
      'Consumers cannot resolve these, so the imported types degrade to `any`.\n' +
      'Fix: use a relative path in the source import.\n',
  );
  for (const line of offenders) console.error('  ' + line);
  process.exit(1);
}

console.log(`check-dts: ${distFiles.length} declaration files, no unresolved aliases.`);
