/**
 * Phase 6.7 — commitlint config for the @tnzi/* monorepo.
 *
 * Enforces conventional commits (type: description), matching the
 * existing commit style in the history.
 */
module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    // Allow longer subject lines for scoped phase commits
    'header-max-length': [2, 'always', 120],
    'body-max-line-length': [0],
    'footer-max-line-length': [0],
    // Permit emoji-free but informative scopes
    'scope-case': [0],
    'subject-case': [0],
    'type-enum': [
      2,
      'always',
      [
        'feat',
        'fix',
        'refactor',
        'docs',
        'test',
        'chore',
        'perf',
        'ci',
        'build',
        'style',
        'revert',
      ],
    ],
  },
}
