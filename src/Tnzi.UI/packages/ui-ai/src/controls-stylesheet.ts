/**
 * Build entry of the controls stylesheet (`vite.controls-css.config.ts`): the
 * controls and dialogs entries, for the scoped styles of every component they
 * reach, and the global rules they are used with. Only the CSS of this build
 * is kept.
 */
import './styles/controls-sheet.css';

export * from './controls';
export * from './dialogs';
