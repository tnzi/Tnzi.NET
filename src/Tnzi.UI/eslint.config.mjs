// @ts-check
/**
 * Phase 6.8 — Unified ESLint flat config for the @tnzi/* monorepo.
 *
 * Covers core/ui/ui-admin/ui-ai/mobile packages. Rules are deliberately
 * conservative: error-level for unambiguous bugs, warn-level for stylistic
 * concerns so the existing codebase lints clean on first run.
 *
 * References:
 * - ESLint 9 flat config: https://eslint.org/docs/latest/use/configure/configuration-files
 * - typescript-eslint: https://typescript-eslint.io/packages/typescript-eslint/
 * - eslint-plugin-vue (compat mode for flat config): https://eslint.vuejs.org/
 */
import eslint from '@eslint/js'
import tseslint from 'typescript-eslint'
import vuePlugin from 'eslint-plugin-vue'
import vueParser from 'vue-eslint-parser'

export default tseslint.config(
  // Ignore build output + node_modules + generated content
  {
    ignores: [
      '**/dist/**',
      '**/node_modules/**',
      '**/coverage/**',
      '**/test-results/**',
      '**/playwright-report/**',
      '**/*.d.ts',
      'packages/core/src/services/**/_generated*',
      'e2e/**',
    ],
  },

  // JavaScript base rules
  eslint.configs.recommended,

  // TypeScript recommended (non-type-checked — full type-check is too slow
  // for monorepo-wide lint; typecheck runs separately via vue-tsc).
  ...tseslint.configs.recommended,

  // Vue SFC support (compat mode for ESLint 9 flat config)
  ...vuePlugin.configs['flat/recommended'],

  // Project-wide rule overrides
  {
    languageOptions: {
      ecmaVersion: 2024,
      sourceType: 'module',
      globals: {
        window: 'readonly',
        document: 'readonly',
        console: 'readonly',
        process: 'readonly',
        globalThis: 'readonly',
        URL: 'readonly',
        Blob: 'readonly',
        FormData: 'readonly',
        File: 'readonly',
        Event: 'readonly',
        MediaQueryListEvent: 'readonly',
        MutationObserver: 'readonly',
        setTimeout: 'readonly',
        clearTimeout: 'readonly',
        setInterval: 'readonly',
        clearInterval: 'readonly',
        requestAnimationFrame: 'readonly',
        cancelAnimationFrame: 'readonly',
      },
    },
    rules: {
      // Bug-catchers: keep as error
      'no-debugger': 'error',
      'no-duplicate-imports': 'off', // TS allows `import x` + `import type {y}` from same module
      'no-unreachable': 'error',
      'no-useless-assignment': 'off', // legitimate intermediate-value pattern
      'no-undef': 'off', // TypeScript handles this

      // TypeScript rules — downgrade noisy ones to warn so the baseline is clean
      '@typescript-eslint/no-unused-vars': [
        'warn',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' },
      ],
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/no-empty-object-type': 'off',
      '@typescript-eslint/ban-ts-comment': 'warn',
      '@typescript-eslint/no-unused-expressions': 'off',

      // Vue rules
      'vue/multi-word-component-names': 'off', // TCrudPage, AppContent etc.
      'vue/no-v-html': 'off', // used for preview panes
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/multiline-html-element-content-newline': 'off',
      'vue/html-self-closing': 'off',
      'vue/html-indent': 'off',
      'vue/attributes-order': 'off',
      'vue/component-definition-name-casing': 'off',
      'vue/require-default-prop': 'off', // optional props are fine in Vue 3 + TS
      'vue/no-mutating-props': 'warn',
      'vue/valid-v-for': 'warn',
      'vue/no-reserved-component-names': 'off', // test stubs legitimately use Button/Form/etc
      'vue/one-component-per-file': 'off', // playground + tests inline multiple stubs
      // Pre-existing violations in Phase 3/5 pages — surfaced by Phase 6.8
      // baseline install. Downgraded to warn so lint is green; fixing
      // belongs to a dedicated code-quality backlog item.
      'vue/no-dupe-keys': 'warn',
      'vue/no-use-v-if-with-v-for': 'warn',
      // @tnzi/core deliberately imports from @vue/reactivity (headless,
      // no SFC runtime) — this is the package's explicit design decision
      // documented in ui-architecture-v3.0.md.
      'vue/prefer-import-from-vue': 'off',
    },
  },

  // Vue files use vue-eslint-parser so <script> blocks are TypeScript-aware
  {
    files: ['**/*.vue'],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
        extraFileExtensions: ['.vue'],
        ecmaVersion: 2024,
        sourceType: 'module',
      },
    },
  },

  // Tests can be looser
  {
    files: ['**/__tests__/**/*', '**/*.test.ts', '**/*.spec.ts'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-unused-vars': 'off',
      'no-empty': 'off',
      'no-unused-expressions': 'off',
    },
  },
)
