# Migration guide — `@tnzi/*` 0.1 → 0.2

This guide walks a consumer of `@tnzi/core`, `@tnzi/ui`, `@tnzi/ui-admin`, `@tnzi/ui-ai`, or `@tnzi/mobile` through the breaking changes introduced in the 0.2.0-preview.1 release. Read [CHANGELOG.md](./CHANGELOG.md) alongside this guide.

## Scope

0.2.0-preview.1 is the first major-version pre-release. The changes below are **breaking** — code that imports from 0.1.x will not work unchanged against 0.2.x. Patch-level changes (bug fixes, docs, tests) are listed in the CHANGELOG but don't require migration steps.

## Upgrade checklist

- [ ] Update `package.json` dependency ranges to `"@tnzi/core": "^0.2.0-preview.1"` (etc.)
- [ ] Run `pnpm install` (or the equivalent)
- [ ] Follow the sections below that apply to your consumer

---

## 1. `@tnzi/core` — service factories are now per-call

**Before (0.1):**

```ts
import { useProfileApi } from '@tnzi/core/services/identity'
const api = useProfileApi(client) // cached singleton
```

**After (0.2):** every call to a service factory returns a fresh API object. Bridges must call the factory inside each sub-contract method, not once at module scope.

```ts
// Bridge pattern
export function createIdentityBridge(client: HttpClient) {
  return {
    users: {
      async fetch(query: PagedQuery) {
        const api = useUsersApi(client)  // per-call
        return api.getPaged(query)
      },
      // ... other methods likewise
    },
  }
}
```

See `packages/ui-admin/src/services/bridges/identity-bridge.ts` for the canonical example.

---

## 2. `@tnzi/ui-admin` — new `createTnziUiAdmin` preset

**Before (0.1):** consumers manually wired routing, stores, plugins, and the admin shell.

**After (0.2):** use the preset factory.

```ts
// main.ts
import { createApp } from 'vue'
import { createTnziUiAdmin } from '@tnzi/ui-admin'
import { httpClient } from './api'
import App from './App.vue'

const app = createApp(App)
const admin = createTnziUiAdmin({
  client: httpClient,  // MUST inject — bridges need a client
  // optional: routes, plugins, locale, theme overrides
})
app.use(admin)
app.mount('#app')
```

This single call registers:

- `TAdminShell`, `TCrudPage`, `TFormModal`, `TCrudToolbar`, `TBatchActions`
- All 29 + 13 = 42 preset pages via `defaultAdminRoutes` (routes guarded by a `DefaultControllerEnabledMarker` equivalent on the frontend)
- Pinia stores (`useAdminAppStore`, `useAdminUserStore`, `useAdminPermissionStore`)
- i18n messages (en + zh-cn) under the `tnzi.admin.*` namespace
- Theme preset with `--tnzi-admin-*` CSS variables inheriting from `@tnzi/ui`'s palette

---

## 3. `@tnzi/ui-ai` — chat + workflow editor only

The `admin/` subdirectory has been removed. If you were importing `@tnzi/ui-ai/admin/{AgentManagement,SkillManagement,...}`, migrate to `@tnzi/ui-admin`:

```ts
// Before
import AgentManagement from '@tnzi/ui-ai/admin/AgentManagement.vue'

// After — use the preset routes or import directly
import { AgentList, AgentDetail } from '@tnzi/ui-admin/pages/ai/agents'
```

The `@tnzi/ui-ai` public surface now consists of:

- `chat/` — `TChat`, `TThreadList`, `TMessage*` (60+ components)
- `components/{agent,artifact,chat,context,knowledge,reasoning,skill,streaming,workflow}` — fine-grained building blocks
- `composables/` — `useChat`, `useAgentExecution`, `useAutoScroll`, `useEmbedMode`, `useLocalSearch`, `useMessageBranch`, `useMessageGroup`, `useRagChat`, `useSkillBrowser`, `useStreamMarkdown`, `useTokenCounter`, `useWorkflowVisualization`
- `embed/` — embed widget
- `lib/` — `formatCompactNumber`, `cn` (deprecated — use `:class` bindings)

---

## 4. Theme tokens — CSS variables instead of Tailwind `dark:`

`@tnzi/ui-ai` no longer uses Tailwind's `dark:` variant or the `cn()` utility. Token access is via CSS variables that inherit from `@tnzi/ui`:

**Before (0.1):**

```vue
<div class="bg-white dark:bg-gray-900 text-black dark:text-white">...</div>
```

**After (0.2):**

```vue
<div class="bg-[--tnzi-ai-surface] text-[--tnzi-ai-text]">...</div>
```

Or consume via Tailwind's theme extension if you've configured it:

```js
// tailwind.config.js (consumer)
module.exports = {
  theme: {
    extend: {
      colors: {
        'tnzi-ai': {
          surface: 'var(--tnzi-ai-surface)',
          text: 'var(--tnzi-ai-text)',
          // ...
        },
      },
    },
  },
}
```

---

## 5. Stores — factory pattern + reset helpers

**Before (0.1):**

```ts
import { useAuthStore } from '@tnzi/ui/stores/auth'
// Direct usage — singleton auth-state manager hidden inside
const auth = useAuthStore()
```

**After (0.2):** the store pattern is unchanged for consumers, but the internal manager is now a lazy singleton that can be reset for SSR / test isolation.

```ts
import { useAuthStore, resetAuthRuntime } from '@tnzi/ui/stores/auth'

// In tests:
beforeEach(() => {
  resetAuthRuntime()      // fresh manager per test
  setActivePinia(createPinia())
})
```

Similarly `resetStoreRuntime()` from `@tnzi/ui/stores` resets the shared HTTP client + storage adapter injected by `initStoreRuntime()`.

---

## 6. HTTP client — 401 auto-refresh, GET deduplication

The `HttpClient` in `@tnzi/core` gained two opt-in behaviors:

- **`refreshTokenFn`** — when set, 401 responses trigger a single refresh attempt (mutex-guarded) and the original request retries once. Configure it alongside `notifyUnauthorized` in your client construction.
- **`deduplicateGets: true`** (default) — concurrent GET requests to the same URL share a single in-flight promise (with shallow copy on return to prevent mutation). Disable via `createHttpClient({ deduplicateGets: false })` if your app depends on per-call fresh fetches.

No action required unless you were relying on duplicate GETs or stock 401 behavior.

---

## 7. DTO shapes — canonical-compact rule

Phase 5 applied the "canonical compact" rule: page configs follow real backend DTO shapes, not plan sketches. If your code consumed DTO fields that only existed in the plan (not the backend), TypeScript will now fail at build time. Check `packages/core/src/services/*/dtos.ts` for the canonical shape.

Common renames:

| Old (plan-only)      | New (backend) |
|----------------------|---------------|
| `AgentPersonaDto.avatarUrl`    | *(removed — not in DTO)* |
| `AgentPersonaDto.isEnabled`    | *(removed)* |
| `AgentPersonaDto.content`      | system-prompt body (unchanged) |

---

## 8. Test helpers — `resetAuthRuntime` / `resetStoreRuntime`

If you had tests mocking `@tnzi/core/state.AuthStateManager`, make sure each test resets the runtime:

```ts
import { resetAuthRuntime } from '@tnzi/ui/stores/auth'

beforeEach(() => {
  resetAuthRuntime()  // wipes module-level singleton
  setActivePinia(createPinia())
})
```

---

## 9. Coverage thresholds (if your CI mirrors ours)

Phase 6 coverage thresholds (per package `vitest.config.ts`):

| Package          | Lines | Statements | Branches | Functions |
|------------------|-------|------------|----------|-----------|
| `@tnzi/ui`       | 80%   | 80%        | 70%      | 80%       |
| `@tnzi/ui-admin` | 80%   | 80%        | 70%      | 60%       |
| `@tnzi/ui-ai`    | 80%   | 80%        | 70%      | 60%       |

The lowered ui-admin / ui-ai function thresholds reflect the fact that mount-based integration tests under happy-dom can't reach 80% without full user-flow simulation — covered instead by Playwright E2E in `e2e/`. Lines/statements/branches meet 80/70 honestly.

---

## 10. Rolling back

If 0.2.0-preview.1 blocks your release and you need to roll back:

```bash
pnpm add @tnzi/core@^0.1.2 @tnzi/ui@^0.1.1 @tnzi/ui-admin@^0.1.0 @tnzi/ui-ai@^0.1.0 @tnzi/mobile@^0.1.0
```

Then revert any code changes from sections 1–8 above.

---

## Questions

File an issue at `https://github.com/tnzi/tnzi.net/issues` or consult the design spec at `docs/superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md` for architectural context.
