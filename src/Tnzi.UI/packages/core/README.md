# @tnzi/core

> Framework-agnostic core utilities, types, and HTTP client for Tnzi.NET frontend applications.

## 安装

```bash
pnpm add @tnzi/core
```

### 类型定义

```typescript
import type { UserDto } from "@tnzi/core/services/identity";
import type { ApiResult, PagedList } from "@tnzi/core/types";
// UI 契约类型（ITableColumn / IMenuItem / IFormRule / IDynamicFormField …）
// 注意：core 没有 `components` 子路径，UI 契约类型一律从 types/shared-ui 取
import type { ITableColumn, IMenuItem } from "@tnzi/core/types/shared-ui";
```

### HTTP 客户端

```typescript
import {
  createHttpClient,
  createErrorMappingMiddleware,
  createSchemaResolver,
  createSchemaValidationMiddleware,
} from "@tnzi/core/http";
import { useAdminUserApi } from "@tnzi/core/services/identity";
import { createPagedQuery } from "@tnzi/core/types";
import { z } from "zod";

// Schema 由消费方自备（core 不内置 DTO schema），传给 resolver 做响应校验
const userDtoSchema = z.object({ id: z.string(), userName: z.string() });

const client = createHttpClient({
  baseUrl: "https://api.example.com",
  responseMiddlewares: [
    createSchemaValidationMiddleware({
      resolveSchema: createSchemaResolver([
        { method: "GET", path: "/admin/users/{id}", schema: userDtoSchema },
      ]),
    }),
    createErrorMappingMiddleware({
      mappings: {
        UserNotFound: { i18nKey: "identity.errors.userNotFound" },
      },
    }),
  ],
});
const userApi = useAdminUserApi(client);
const users = await userApi.getList({ ...createPagedQuery(1, 10) });
```

### 第三方库适配器

```typescript
import { useMessage, useDialog } from "@tnzi/core/adapters";
```

### Schema 验证

core 依赖 `zod` 但**不内置 DTO schema**，由消费方按需自备，再经
`createSchemaResolver` + `createSchemaValidationMiddleware` 挂到响应管线上（见上方 HTTP 客户端示例）。

### 国际化

```typescript
import { provideI18n, useI18n } from "@tnzi/core/adapters/i18n";
```

### 无头控制器 & 工具

```typescript
import { DataQueryController, PaginationController, SortController, SelectionController } from "@tnzi/core/headless";
import { calculateTotalPages, clampPageIndex, updatePageQuery } from "@tnzi/core/headless";
```

### 状态管理

```typescript
import type { AuthState, UserState, AppState } from "@tnzi/core/state";
import { AuthStateManager, UserStateManager, AppStateManager } from "@tnzi/core/state";
```

## 服务覆盖

| 服务         | 类型 | API | Schema |
| ------------ | ---- | --- | ------ |
| Identity     | ✅   | ✅  | ✅     |
| Storage      | ✅   | ✅  | ✅     |
| Notification | ✅   | ✅  | ✅     |
| Chat         | ✅   | ✅  | ✅     |
| Payment      | ✅   | ✅  | ✅     |
| Audit        | ✅   | ✅  | ✅     |
| Template     | ✅   | ✅  | ✅     |
| AI           | ✅   | ✅  | ⏳     |
| App          | ✅   | ✅  | ✅     |

## Service 层是手写的

`src/services` 下的类型与调用**不是生成的**，这是有意的选择而非欠账。

契约漂移由 `FrontendBackendContractTests` 检查：它反射后端每一个控制器，
与每份 `api.ts` 里的路径对账，漂移会让测试变红。

> 早前文档提到过一组 `pnpm codegen*` 命令。那三个脚本于 2026-08-15 删除 ——
> 它们需要两个从未在本仓存在过的文件，从来就跑不起来。

## License

MIT
