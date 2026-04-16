/**
 * @tnzi/ui-ai
 *
 * AI components for chat, agent visualization, workflow builder,
 * and admin panels. Built on Vue 3 + UnoCSS (presetWind4 + presetTnzi).
 */

import 'virtual:uno.css';
import './styles/index.css';

// Theme
export * from './themes/index';

// Locale
export * from './locale/index';

// Utilities
export { formatCompactNumber } from './lib/utils';

// Components (Phase 2+)
export * from './components/index';

// Composables (Phase 2+)
export * from './composables/index';

// Chat (Phase 2+)
export * from './chat/index';

// Embed (Phase 3+)
export * from './embed/index';
