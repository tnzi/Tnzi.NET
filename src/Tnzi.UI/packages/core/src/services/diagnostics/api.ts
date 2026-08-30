/**
 * Diagnostics Module API - admin read access to runtime diagnostics.
 *
 * Mirrors `Tnzi.AspNetCore/Controllers/DefaultDiagnosticsAdminController` -
 * exception ring-buffer summary/recent (+ one DELETE to clear it), the
 * controller map, and the loaded module manifest list under
 * `/admin/diagnostics/*`.
 */

import type { HttpClient } from '../../http/http';
import type {
  ExceptionSummaryDto,
  ExceptionEntryDto,
  ControllerDiagnosticsResultDto,
  ModuleDiagnosticsDto,
  SensitiveEndpointReportDto,
} from './types';

const ADMIN_DIAGNOSTICS_BASE = '/admin/diagnostics';

/**
 * Admin Diagnostics API - runtime exception/controller/module introspection.
 *
 * Example:
 * ```ts
 * const api = useAdminDiagnosticsApi(client);
 * const summary = await api.getExceptionSummary(60);
 * const recent = await api.getRecentExceptions(50);
 * await api.clearExceptions();
 * ```
 */
export function useAdminDiagnosticsApi(client: HttpClient) {
  return {
    /** Aggregate exception counts over the last `minutes` window. */
    getExceptionSummary: (minutes = 60) =>
      client.get<ExceptionSummaryDto>(`${ADMIN_DIAGNOSTICS_BASE}/exceptions/summary?minutes=${minutes}`),

    /** Most recent `count` exception entries from the in-memory ring buffer. */
    getRecentExceptions: (count = 50) =>
      client.get<ExceptionEntryDto[]>(`${ADMIN_DIAGNOSTICS_BASE}/exceptions/recent?count=${count}`),

    /** Clear the in-memory exception ring buffer. */
    clearExceptions: () =>
      client.delete(`${ADMIN_DIAGNOSTICS_BASE}/exceptions`),

    /** List every registered controller with its route/module/default flag. */
    getControllers: () =>
      client.get<ControllerDiagnosticsResultDto>(`${ADMIN_DIAGNOSTICS_BASE}/controllers`),

    /** List the loaded module manifests. */
    getModules: () =>
      client.get<ModuleDiagnosticsDto[]>(`${ADMIN_DIAGNOSTICS_BASE}/modules`),

    /**
     * List every sensitive endpoint currently reachable in this deployment.
     *
     * Answers "what is this deployment actually exposing" - framework default
     * controllers activate on their own, so without this the only way to know was
     * to read each module's source. Suppressed endpoints are absent from the
     * result because it reads the live route table rather than scanning assemblies.
     *
     * Being absent from the list does NOT mean the capability is closed:
     * authorization lives in the service layer, this is visibility only.
     */
    getSensitiveEndpoints: () =>
      client.get<SensitiveEndpointReportDto>(`${ADMIN_DIAGNOSTICS_BASE}/sensitive-endpoints`),
  };
}
