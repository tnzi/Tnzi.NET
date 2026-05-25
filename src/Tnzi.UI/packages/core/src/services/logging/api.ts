/**
 * Logging Module API - admin read access to on-disk Serilog file outputs.
 *
 * Mirrors `Tnzi.AspNetCore/Controllers/DefaultLogFileAdminController` —
 * every endpoint under `/admin/logs/*` is read-only because the log
 * files are runtime artefacts owned by Serilog (retention configured
 * server-side via `LoggingOptions.FileOutput.*.RetainedFileCountLimit`).
 */

import type { HttpClient } from '../../http/http';
import type {
  LogLevelInfoDto,
  LogFileInfoDto,
  LogTailResultDto,
  LogSearchResultDto,
  LogTailQueryDto,
  LogSearchQueryDto,
} from './types';

const ADMIN_LOGS_BASE = '/admin/logs';

/**
 * Admin Log File API — read-only access to per-level rolling-day log files.
 *
 * Example:
 * ```ts
 * const api = useAdminLogFileApi(client);
 * const levels = await api.getLevels();  // sidebar tree data
 * const files = await api.getFiles('Error');
 * const tail = await api.tail({ level: 'Error', file: 'log-20260518.txt', lines: 500 });
 * ```
 */
export function useAdminLogFileApi(client: HttpClient) {
  return {
    /** List every configured log level + per-level summary. */
    getLevels: () =>
      client.get<LogLevelInfoDto[]>(`${ADMIN_LOGS_BASE}/levels`),

    /** List log files under a single level, sorted newest first. */
    getFiles: (level: string) =>
      client.get<LogFileInfoDto[]>(`${ADMIN_LOGS_BASE}/files`, {
        params: { level },
      }),

    /** Read the last N lines of a single log file. */
    tail: (query: LogTailQueryDto) =>
      client.get<LogTailResultDto>(`${ADMIN_LOGS_BASE}/tail`, {
        params: query as unknown as Record<string, unknown>,
      }),

    /** Case-insensitive keyword search across log files. */
    search: (query: LogSearchQueryDto) =>
      client.get<LogSearchResultDto>(`${ADMIN_LOGS_BASE}/search`, {
        params: query as unknown as Record<string, unknown>,
      }),
  };
}
