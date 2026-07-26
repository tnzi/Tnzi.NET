/**
 * Ambient module shim for side-effect style imports.
 *
 * vue-tsc 3 (new Volar) and TypeScript 6's `bundler` resolution no longer
 * implicitly resolve `*.css` imports without `vite/client` in scope.
 */
declare module '*.css';

/** UnoCSS virtual entry; resolved by `unocss/vite` at build time. */
declare module 'virtual:uno.css';
