/**
 * AI Components
 *
 * Two kinds of subdirectory live here, and the distinction is deliberate:
 *
 *   - **Structural domains** (`layout/`, `overlay/`) hold components that
 *     carry no AI semantics and only divide or float space. They are the
 *     building blocks a consumer reaches for when assembling a custom shell.
 *   - **Business domains** (`chat/`, `agent/`, `reasoning/`, `artifact/`,
 *     `skill/`, `knowledge/`, `context/`, `streaming/`, `workflow/`, `cli/`)
 *     hold components tied to one AI capability.
 *
 * Before the structural domains existed, `chat/` was the de-facto bucket for
 * anything that fit nowhere else, which is how a generic popover container and
 * a topbar frame ended up filed under "chat".
 */

// -- Structural domains ----------------------------------------------------

// Layout chrome (topbar, setting rows/groups)
export * from './layout/index';

// Floating surfaces (popover menu, user menu)
export * from './overlay/index';

// -- Business domains ------------------------------------------------------

// Streaming primitives
export * from './streaming/index';

// Reasoning components
export * from './reasoning/index';

// Chat components
export * from './chat/index';

// Context components
export * from './context/index';

// Agent components
export * from './agent/index';

// Workflow components are deliberately NOT re-exported here. This barrel is
// reachable from the root barrel, and every `TWorkflow*` SFC imports
// `@vue-flow/core` (plus background/minimap and their stylesheets) at module
// scope - so naming them here drags the package's heaviest dependency into the
// module graph of anything that touches `@tnzi/ui-ai` or
// `@tnzi/ui-ai/components`. They live behind `@tnzi/ui-ai/workflow` instead:
//
//   import { TWorkflowCanvas, Handle, Position } from '@tnzi/ui-ai/workflow'

// Artifact components
export * from './artifact/index';

// Skill components
export * from './skill/index';

// Knowledge components
export * from './knowledge/index';

// External CLI agent run components
export * from './cli/index';
