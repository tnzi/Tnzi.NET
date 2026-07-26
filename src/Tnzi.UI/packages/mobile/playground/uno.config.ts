import { defineConfig } from 'unocss';
import base from '../uno.config';

/**
 * Playground UnoCSS config.
 *
 * Reuses the package presets, but widens the scan to `../src` as well: the
 * playground renders the components straight from source, so the utilities they
 * use have to be generated here too.
 */
export default defineConfig({
  ...base,
  content: {
    filesystem: ['./src/**/*.{vue,ts,tsx}', '../src/**/*.{vue,ts,tsx}'],
  },
});
