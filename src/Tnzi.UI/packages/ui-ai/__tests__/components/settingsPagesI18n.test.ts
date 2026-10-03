// @vitest-environment node
/**
 * Lock: the built-in settings pages (Account, Security, Personalization,
 * Usage) take every user-visible string from the package catalogue.
 *
 * They were written with English literals in the markup, so a Chinese UI that
 * switched its catalogue to `zhCn` still showed these four panes in English.
 * The catalogue's type already forces `zh-cn` to carry every key `en` has; what
 * it cannot see is a string that never became a key. That is what this checks,
 * on the source (SFCs have little mount coverage in this package):
 *
 *   - no text node in the template outside `{{ }}`;
 *   - no static `label` / `description` / `placeholder` / `title` attribute;
 *   - no prose-looking string literal (a space between words, or a capitalised
 *     word) anywhere in script or bound expressions.
 *
 * A second block checks the catalogue side: the four sections exist in both
 * languages and the Chinese entries are not English copied across.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { en } from '../../src/locales/en';
import { zhCn } from '../../src/locales/zh-cn';

const here = dirname(fileURLToPath(import.meta.url));

const PAGES = [
  'TAccountSettings.vue',
  'TSecuritySettings.vue',
  'TPersonalizationSettings.vue',
  'TUsageSettings.vue',
] as const;

function source(name: string): string {
  return readFileSync(resolve(here, '../../src/components/settings', name), 'utf8')
    .replace(/<style[\s\S]*?<\/style>/g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:'"`])\/\/[^\n]*/g, '$1');
}

function templateOf(text: string): string {
  return text.slice(text.indexOf('<template>'), text.lastIndexOf('</template>') + '</template>'.length);
}

// A start/end tag, allowing `>` inside quoted attribute values (`a > b`, `=>`).
const TAG = /<[^>"']*(?:"[^"]*"[^>"']*|'[^']*'[^>"']*)*>/g;

function textNodes(template: string): string[] {
  return template
    .replace(/\{\{[\s\S]*?\}\}/g, ' ')
    .replace(TAG, '\n')
    .split('\n')
    .map((s) => s.trim())
    .filter((s) => /[A-Za-z]/.test(s));
}

function staticCopyAttributes(template: string): string[] {
  return [...template.matchAll(/\s(label|description|placeholder|title|aria-label)="([^"]*)"/g)]
    .filter((m) => /[A-Za-z]/.test(m[2]))
    .map((m) => m[0].trim());
}

function proseLiterals(text: string): string[] {
  return [...text.matchAll(/'([^'\n]*)'|`([^`\n]*)`/g)]
    .map((m) => m[1] ?? m[2])
    .filter((s) => /[A-Za-z]+ +[A-Za-z]+/.test(s) || /\b[A-Z][a-z]+\b/.test(s))
    // Import specifiers are paths, not copy.
    .filter((s) => !/^\.{1,2}\/|^@?[\w-]+\//.test(s));
}

describe('settings pages: copy goes through the catalogue', () => {
  it.each(PAGES)('%s translates through useAiI18n', (name) => {
    expect(source(name)).toMatch(/const t = useAiI18n\(\)/);
  });

  it.each(PAGES)('%s has no literal text in its template', (name) => {
    expect(textNodes(templateOf(source(name)))).toEqual([]);
  });

  it.each(PAGES)('%s has no static copy attributes', (name) => {
    expect(staticCopyAttributes(templateOf(source(name)))).toEqual([]);
  });

  it.each(PAGES)('%s has no prose string literals', (name) => {
    expect(proseLiterals(source(name))).toEqual([]);
  });
});

describe('settings pages: catalogue', () => {
  const SECTIONS = ['accountSettings', 'securitySettings', 'personalizationSettings', 'usageSettings'] as const;
  // Values that are the same in every language on purpose.
  const SAME_IN_EVERY_LANGUAGE = new Set(['accountSettings.emailPlaceholder']);

  it.each(SECTIONS)('%s has the same keys in en and zh-cn', (section) => {
    expect(Object.keys(zhCn[section]).sort()).toEqual(Object.keys(en[section]).sort());
  });

  it.each(SECTIONS)('%s is translated in zh-cn, not copied from en', (section) => {
    const copied = Object.entries(zhCn[section])
      .filter(([key, value]) => !SAME_IN_EVERY_LANGUAGE.has(`${section}.${key}`) && value === (en[section] as Record<string, string>)[key])
      .map(([key]) => key);
    expect(copied).toEqual([]);
  });
});
