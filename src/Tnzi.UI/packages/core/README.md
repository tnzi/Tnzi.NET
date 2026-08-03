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

## OpenAPI Codegen（可选）

当前 service 层类型为手写维护。可通过 `Tnzi.Cli` 从 OpenAPI spec 自动生成到 `{module}/generated/` 目录：

```bash
# 前提: 安装 tnzi CLI + 项目根目录有 tnzi.json
pnpm -C src/Tnzi.UI codegen:url   # 从运行中的后端生成
pnpm -C src/Tnzi.UI codegen       # 从本地 openapi.json 生成
```

## License

MIT
