/**
 * Ambient module shims for side-effect style imports.
 *
 * vue-tsc 3 (new Volar) and TypeScript 6's `bundler` resolution no longer
 * implicitly resolve `*.css` / virtual style entries without `vite/client`
 * in scope. These ambient declarations are global, so they also cover the
 * `@tnzi/ui` sources pulled in via tsconfig `paths`.
 */
declare module '*.css';
declare module 'virtual:uno.css';
