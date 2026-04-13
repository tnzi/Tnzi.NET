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
import type { ILoginFormProps, ILoginFormEmits } from "@tnzi/core/components";
```

### HTTP 客户端

```typescript
import {
  createHttpClient,
  createErrorMappingMiddleware,
  createSchemaResolver,
  createSchemaValidationMiddleware,
} from "@tnzi/core/http";
import { useUserApi } from "@tnzi/core/services/identity";
import { userDtoSchema } from "@tnzi/core/services/identity";
import { createPagedQuery } from "@tnzi/core/types";

const client = createHttpClient({
  baseUrl: "https://api.example.com",
  responseMiddlewares: [
    createSchemaValidationMiddleware({
      resolveSchema: createSchemaResolver([
        { method: "GET", path: "/identity/users/{id}", schema: userDtoSchema },
      ]),
    }),
    createErrorMappingMiddleware({
      mappings: {
        UserNotFound: { i18nKey: "identity.errors.userNotFound" },
      },
    }),
  ],
});
const userApi = useUserApi(client);
const users = await userApi.getList({ ...createPagedQuery(1, 10) });
```

### 第三方库适配器

```typescript
import { useMessage, useDialog } from "@tnzi/core/adapters";
```

### Schema 验证

```typescript
import { loginSchema } from "@tnzi/core/services/identity";
```

### 国际化

```typescript
import { provideI18n, useI18n } from "@tnzi/core/adapters/i18n";
```

### 无头控制器 & 工具

```typescript
import { PaginationController, SortController, FormController } from "@tnzi/core/headless";
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
