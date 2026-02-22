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
import type { ILoginFormProps, IDataTableProps } from "@tnzi/core/components";
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

### 无头数据状态工具

```typescript
import { calculateTotalPages, clampPageIndex, toggleSort } from "@tnzi/core/utils";
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

## OpenAPI 生成 API（可选）

每个服务模块均包含 `generated/` 入口（例如 `@tnzi/core/services/identity` 下的 `generated/api.generated.ts`），统一通过 `Tnzi.Cli` 生成并更新：

```bash
pnpm -C src/Tnzi.UI contracts:sync
```

## License

MIT
