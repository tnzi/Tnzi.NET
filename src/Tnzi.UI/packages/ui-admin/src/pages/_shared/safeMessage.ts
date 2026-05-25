/**
 * `useSafeMessage` re-export. Sunk to `@tnzi/ui` in 0.2.x so site/chat/
 * mobile can reuse the same safety wrapper; admin call-sites keep their
 * existing import path through this barrel.
 */
export { useSafeMessage } from '@tnzi/ui'
