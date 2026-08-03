/**
 * `@tnzi/ui-ai/plugin` - application assembly.
 *
 * Requires `vue-router` (an optional peer of this package): only this entry
 * needs it, so a product that just embeds a component pays nothing.
 */
export { defineChatApp } from './defineChatApp';
export type {
  DefineChatAppOptions,
  DefineChatAppResult,
  ChatAppLoginConfig,
} from './defineChatApp';
