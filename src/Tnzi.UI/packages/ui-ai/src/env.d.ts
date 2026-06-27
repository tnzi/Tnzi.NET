/**
 * Ambient module shims for side-effect style imports.
 *
 * vue-tsc 3 (new Volar) and TypeScript 6's `bundler` resolution no longer
 * implicitly resolve `*.css` / virtual style entries without `vite/client`
 * in scope, so declare them here for the type checker (covers local styles,
 * `virtual:uno.css`, and third-party `@vue-flow/*/dist/*.css`).
 */
declare module '*.css';
declare module 'virtual:uno.css';
