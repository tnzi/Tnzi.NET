/**
 * HTTP middleware helpers.
 */

import { useI18n } from '../adapters/i18n/runtime';
import { useMessage } from '../adapters/message';
import { createFailedApiResult, getApiResultErrorMessage, HttpError } from '../errors/api-error';
import type { ApiResult, HttpMethod } from '../types/api';
import { isSuccess } from './response';
import type { ZodTypeAny } from 'zod';

export interface HttpResponseContext {
  method: HttpMethod;
  url: string;
  fullUrl: string;
  status: number;
}

export type HttpResponseMiddleware = <T>(
  result: ApiResult<T>,
  context: HttpResponseContext
) => ApiResult<T> | Promise<ApiResult<T>>;

export interface HttpSchemaBinding {
  method?: HttpMethod;
  path: string | RegExp;
  schema: ZodTypeAny;
}

function normalizePath(value: string): string {
  return value.replace(/^https?:\/\/[^/]+/i, '').replace(/\/+$/, '');
}

function isPathMatch(pattern: string | RegExp, url: string): boolean {
  const normalizedUrl = normalizePath(url);
  if (typeof pattern === 'string') {
    return normalizePath(pattern) === normalizedUrl;
  }
  return pattern.test(normalizedUrl);
}

export function createSchemaResolver(bindings: HttpSchemaBinding[]) {
  return (context: HttpResponseContext): ZodTypeAny | undefined => {
    for (const binding of bindings) {
      if (binding.method && binding.method !== context.method) {
        continue;
      }
      if (isPathMatch(binding.path, context.url)) {
        return binding.schema;
      }
    }
    return undefined;
  };
}

export interface SchemaValidationMiddlewareOptions {
  resolveSchema: (context: HttpResponseContext) => ZodTypeAny | undefined;
  errorCode?: string;
  message?: string;
  coerceData?: boolean;
  onValidationError?: (error: HttpError, context: HttpResponseContext) => void;
}

export function createSchemaValidationMiddleware(
  options: SchemaValidationMiddlewareOptions
): HttpResponseMiddleware {
  const shouldCoerce = options.coerceData !== false;
  const errorCode = options.errorCode ?? 'ResponseValidationFailed';

  return <T>(result: ApiResult<T>, context: HttpResponseContext): ApiResult<T> => {
    if (!isSuccess(result)) {
      return result;
    }

    const schema = options.resolveSchema(context);
    if (!schema) {
      return result;
    }

    const parsed = schema.safeParse(result.data);
    if (parsed.success) {
      if (!shouldCoerce) {
        return result;
      }
      return {
        ...result,
        data: parsed.data as T,
      };
    }

    const message =
      options.message ?? `Response validation failed for ${context.method} ${context.url}`;

    const validationResult = createFailedApiResult<T>({
      code: 422,
      message,
      errorCode,
      details: {
        issues: parsed.error.issues,
        path: context.url,
        method: context.method,
      },
    });

    options.onValidationError?.(new HttpError(validationResult), context);
    return validationResult;
  };
}

export type ErrorMessageLevel = 'info' | 'success' | 'warning' | 'error';

export interface ErrorMessageMapping {
  message?: string;
  i18nKey?: string;
  level?: ErrorMessageLevel;
}

export interface ErrorMappingMiddlewareOptions {
  mappings: Record<string, ErrorMessageMapping>;
  fallbackI18nKey?: string;
  fallbackMessage?: string;
  defaultLevel?: ErrorMessageLevel;
  silentErrorCodes?: string[];
  shouldNotify?: <T>(result: ApiResult<T>, context: HttpResponseContext) => boolean;
  notifier?: <T>(
    message: string,
    level: ErrorMessageLevel,
    result: ApiResult<T>,
    context: HttpResponseContext
  ) => void;
}

function defaultNotifier<T>(
  message: string,
  level: ErrorMessageLevel,
  _result: ApiResult<T>,
  _context: HttpResponseContext
): void {
  const messageAdapter = useMessage();
  const notify = messageAdapter[level] ?? messageAdapter.error;
  notify.call(messageAdapter, message);
}

function resolveMappedMessage<T>(
  options: ErrorMappingMiddlewareOptions,
  result: ApiResult<T>
): { message: string; level: ErrorMessageLevel } {
  const mapping = result.errorCode ? options.mappings[result.errorCode] : undefined;
  const { t } = useI18n();
  const level = mapping?.level ?? options.defaultLevel ?? 'error';

  if (mapping?.message) {
    return { message: mapping.message, level };
  }
  if (mapping?.i18nKey) {
    return { message: t(mapping.i18nKey), level };
  }
  if (options.fallbackI18nKey) {
    return { message: t(options.fallbackI18nKey), level };
  }
  if (options.fallbackMessage) {
    return { message: options.fallbackMessage, level };
  }
  return { message: getApiResultErrorMessage(result), level };
}

export function createErrorMappingMiddleware(
  options: ErrorMappingMiddlewareOptions
): HttpResponseMiddleware {
  const silentCodes = new Set(options.silentErrorCodes ?? []);

  return <T>(result: ApiResult<T>, context: HttpResponseContext): ApiResult<T> => {
    if (isSuccess(result)) {
      return result;
    }

    if (result.errorCode && silentCodes.has(result.errorCode)) {
      return result;
    }

    if (options.shouldNotify && !options.shouldNotify(result, context)) {
      return result;
    }

    const resolved = resolveMappedMessage(options, result);
    (options.notifier ?? defaultNotifier)(
      resolved.message,
      resolved.level,
      result,
      context
    );
    return result;
  };
}
