/**
 * Global test setup.
 *
 * Icons. `TSvgIcon` renders `@iconify/vue`'s `Icon`, which asks the public
 * Iconify API for any icon that has not been bundled - through happy-dom's
 * `fetch`, from every test file that paints an icon. Those requests are still
 * in flight when vitest tears the environment down; happy-dom aborts them and
 * vitest reports the abort as an unhandled error (a red CI run on 2026-09-04
 * in the admin package with every test passing). Install an API module that
 * never issues a query: an unbundled icon renders as an empty span, which is
 * exactly what a consumer without network gets, and no test asserts on remote
 * icon data. Collections registered with `addCollection` still resolve locally.
 */
import { _api } from '@iconify/vue'

_api.setAPIModule('', {
  prepare: () => [],
  send: () => {},
})
