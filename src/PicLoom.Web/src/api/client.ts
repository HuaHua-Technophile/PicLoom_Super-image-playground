import type {
  ProviderSettings,
  ProviderSettingsInput,
  ProviderTestResult,
  ReferenceImage,
  Resolution,
  ResolutionInput,
  SessionResponse,
  Task,
  TaskCreateInput,
  TaskGroup,
  TaskGroupInput,
  Template,
  TemplateInput,
  ApiErrorBody,
} from '../types';

export const CSRF_HEADER = 'X-PicLoom-CSRF';
const SESSION_PATH = '/api/session';
const BOOTSTRAP_PATH = '/api/session/bootstrap';
const PROVIDER_SETTINGS_PATH = '/api/settings/provider';
const PROVIDER_TEST_PATH = '/api/settings/provider/test';

export const KNOWN_ERROR_CODES = [
  'validation_failed',
  'not_found',
  'group_not_empty',
  'group_reserved',
  'unsupported_provider',
  'unsupported_model',
  'provider_not_configured',
  'prompt_required',
  'prompt_too_long',
  'prompt_source_invalid',
  'resolution_required',
  'resolution_source_invalid',
  'resolution_tier_unknown',
  'resolution_not_multiple_of_16',
  'resolution_edge_too_large',
  'resolution_aspect_too_extreme',
  'resolution_pixels_out_of_range',
  'resolution_invalid',
  'resolution_mode_invalid',
  'resolution_ratio_invalid',
  'options_invalid',
  'options_count_invalid',
  'reference_too_large',
  'reference_type_invalid',
  'reference_url_invalid',
  'reference_not_found',
  'reference_in_use',
  'reference_too_many',
  'reference_missing',
  'base_url_required',
  'invalid_base_url',
  'secret_required',
  'provider_key_required_for_new_origin',
  'provider_unauthorized',
  'provider_model_not_found',
  'provider_size_rejected',
  'provider_rate_limited',
  'provider_insufficient_quota',
  'provider_network',
  'provider_upstream_error',
  'provider_invalid_response',
  'unauthorized',
  'forbidden',
  'invalid_bootstrap',
  'conflict',
  'unknown_result',
  'internal_error',
  'network',
] as const;

export type KnownErrorCode = (typeof KNOWN_ERROR_CODES)[number];

const KNOWN_ERROR_CODE_SET: ReadonlySet<string> = new Set(KNOWN_ERROR_CODES);

export class ApiError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(code: string, status: number) {
    super(`API error ${code} (HTTP ${status})`);
    this.name = 'ApiError';
    this.code = code;
    this.status = status;
  }
}

export function errorMessageKey(code: string | null | undefined): string {
  return code && KNOWN_ERROR_CODE_SET.has(code) ? `errors.${code}` : 'errors.fallback';
}

export function toErrorCode(error: unknown): string {
  return error instanceof ApiError ? error.code : 'unknown_result';
}

// Authorization state must not survive in browser storage.
let csrfToken: string | null = null;
let sessionPromise: Promise<void> | null = null;

function readJsonSafely(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' ? (value as Record<string, unknown>) : null;
}

function fallbackCodeForStatus(status: number): string {
  switch (status) {
    case 400:
      return 'validation_failed';
    case 401:
      return 'unauthorized';
    case 403:
      return 'forbidden';
    case 404:
      return 'not_found';
    case 409:
      return 'conflict';
    default:
      return status >= 500 ? 'internal_error' : 'unknown_result';
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  let code: string | null = null;
  try {
    const parsed = readJsonSafely(await response.json());
    const body = parsed as ApiErrorBody | null;
    if (body && typeof body.code === 'string' && body.code.length > 0) {
      code = body.code;
    }
  } catch {
    // Non-JSON error responses fall back to the status code.
  }
  return new ApiError(code ?? fallbackCodeForStatus(response.status), response.status);
}

async function networkError(): Promise<ApiError> {
  return new ApiError('network', 0);
}

function readBootstrapToken(): string | null {
  const hash = window.location.hash;
  if (!hash || hash.length < 2) {
    return null;
  }
  const params = new URLSearchParams(hash.slice(1));
  return params.get('bootstrap');
}

/** Removes the launch token from the address bar before any network call. */
function clearFragment(): void {
  if (window.location.hash) {
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`);
  }
}

async function fetchCsrfToken(response: Response): Promise<string> {
  const parsed = readJsonSafely(await response.json());
  const session = parsed as SessionResponse | null;
  if (!session || typeof session.csrfToken !== 'string' || session.csrfToken.length === 0) {
    throw new ApiError('unknown_result', response.status);
  }
  return session.csrfToken;
}

async function startSession(): Promise<void> {
  const token = readBootstrapToken();
  clearFragment();

  if (token) {
    let response: Response;
    try {
      response = await fetch(BOOTSTRAP_PATH, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify({ token }),
      });
    } catch {
      throw await networkError();
    }
    if (!response.ok) {
      throw await toApiError(response);
    }
    csrfToken = await fetchCsrfToken(response);
    return;
  }

  let response: Response;
  try {
    response = await fetch(SESSION_PATH, {
      method: 'GET',
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
    });
  } catch {
    throw await networkError();
  }
  if (!response.ok) {
    throw await toApiError(response);
  }
  csrfToken = await fetchCsrfToken(response);
}

export function ensureSession(): Promise<void> {
  if (!sessionPromise) {
    sessionPromise = startSession().catch((error: unknown) => {
      sessionPromise = null;
      throw error;
    });
  }
  return sessionPromise;
}

export function resetSession(): void {
  csrfToken = null;
  sessionPromise = null;
}

export function isSessionReady(): boolean {
  return csrfToken !== null;
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? 'GET';
  const headers: Record<string, string> = { Accept: 'application/json' };

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  if (method !== 'GET') {
    if (!csrfToken) {
      throw new ApiError('unauthorized', 401);
    }
    headers[CSRF_HEADER] = csrfToken;
  }

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      credentials: 'same-origin',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw await networkError();
  }

  if (!response.ok) {
    throw await toApiError(response);
  }
  if (response.status === 204) {
    return undefined as T;
  }
  try {
    return (await response.json()) as T;
  } catch {
    throw new ApiError('unknown_result', response.status);
  }
}

function encodeId(id: string): string {
  return encodeURIComponent(id);
}

/**
 * Multipart request helper. Unlike {@link request} it never sets `Content-Type`: the browser must
 * add the multipart boundary itself.
 */
async function requestForm<T>(path: string, form: FormData): Promise<T> {
  if (!csrfToken) {
    throw new ApiError('unauthorized', 401);
  }

  let response: Response;
  try {
    response = await fetch(path, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { Accept: 'application/json', [CSRF_HEADER]: csrfToken },
      body: form,
    });
  } catch {
    throw await networkError();
  }

  if (!response.ok) {
    throw await toApiError(response);
  }
  try {
    return (await response.json()) as T;
  } catch {
    throw new ApiError('unknown_result', response.status);
  }
}

export function assetContentUrl(assetId: string): string {
  return `/api/assets/${encodeId(assetId)}/content`;
}

export function assetThumbnailUrl(assetId: string): string {
  return `/api/assets/${encodeId(assetId)}/thumbnail`;
}

export const api = {
  listTemplates: (signal?: AbortSignal) =>
    request<Template[]>('/api/templates', { signal }),

  createTemplate: (input: TemplateInput) =>
    request<Template>('/api/templates', { method: 'POST', body: input }),

  updateTemplate: (id: string, input: TemplateInput) =>
    request<Template>(`/api/templates/${encodeId(id)}`, { method: 'PUT', body: input }),

  deleteTemplate: (id: string) =>
    request<void>(`/api/templates/${encodeId(id)}`, { method: 'DELETE' }),

  listResolutions: (signal?: AbortSignal) =>
    request<Resolution[]>('/api/resolutions', { signal }),

  createResolution: (input: ResolutionInput) =>
    request<Resolution>('/api/resolutions', { method: 'POST', body: input }),

  updateResolution: (id: string, input: ResolutionInput) =>
    request<Resolution>(`/api/resolutions/${encodeId(id)}`, { method: 'PUT', body: input }),

  deleteResolution: (id: string) =>
    request<void>(`/api/resolutions/${encodeId(id)}`, { method: 'DELETE' }),

  listGroups: (signal?: AbortSignal) =>
    request<TaskGroup[]>('/api/groups', { signal }),

  createGroup: (input: TaskGroupInput) =>
    request<TaskGroup>('/api/groups', { method: 'POST', body: input }),

  updateGroup: (id: string, input: TaskGroupInput) =>
    request<TaskGroup>(`/api/groups/${encodeId(id)}`, { method: 'PUT', body: input }),

  deleteGroup: (id: string) =>
    request<void>(`/api/groups/${encodeId(id)}`, { method: 'DELETE' }),

  createTask: (input: TaskCreateInput) =>
    request<Task>('/api/tasks', { method: 'POST', body: input }),

  /** Without a `groupId` the API returns every task, including the "ungrouped" bucket. */
  listTasks: (groupId?: string, signal?: AbortSignal) =>
    request<Task[]>(
      groupId ? `/api/tasks?groupId=${encodeId(groupId)}` : '/api/tasks',
      { signal },
    ),

  getTask: (id: string, signal?: AbortSignal) =>
    request<Task>(`/api/tasks/${encodeId(id)}`, { signal }),

  getProviderSettings: (signal?: AbortSignal) =>
    request<ProviderSettings>(PROVIDER_SETTINGS_PATH, { signal }),

  saveProviderSettings: (input: ProviderSettingsInput) =>
    request<ProviderSettings>(PROVIDER_SETTINGS_PATH, { method: 'PUT', body: input }),

  clearProviderSettings: () =>
    request<void>(PROVIDER_SETTINGS_PATH, { method: 'DELETE' }),

  /** Probes `GET {base}/v1/models` only; never creates a task and never bills a generation. */
  testProviderSettings: (input?: ProviderSettingsInput) =>
    request<ProviderTestResult>(PROVIDER_TEST_PATH, { method: 'POST', body: input ?? {} }),

  /** Uploads one reference image as multipart form-data under the `file` field. */
  uploadReference: (file: File) => {
    const form = new FormData();
    form.append('file', file, file.name);
    return requestForm<ReferenceImage>('/api/references', form);
  },

  /** Adds a reference image by https URL; the server fetches and validates it. */
  addReferenceFromUrl: (url: string) =>
    request<ReferenceImage>('/api/references/from-url', { method: 'POST', body: { url } }),

  /** Deletes an unbound reference image. */
  deleteReference: (id: string) =>
    request<void>(`/api/references/${encodeId(id)}`, { method: 'DELETE' }),
};

export type PicLoomApi = typeof api;
