/**
 * Wire contracts for the PicLoom local API.
 * All JSON payloads use camelCase. Field names here are the single source of truth
 * for request and response shapes used by the web client.
 */

export const ASPECT_RATIOS = ['1:1', '16:9', '9:16', '4:3', '3:4'] as const;
export type AspectRatio = (typeof ASPECT_RATIOS)[number];

export const IMAGE_SIZES = ['1K', '2K', '4K'] as const;
export type ImageSize = (typeof IMAGE_SIZES)[number];

export const RESOLUTION_MODES = ['exactPixels', 'aspectClass', 'auto'] as const;
export type ResolutionMode = (typeof RESOLUTION_MODES)[number];

export const MIN_EXACT_PIXELS = 1;
export const MAX_EXACT_PIXELS = 8192;

/** The only provider the app dispatches to: an OpenAI compatible relay configured with BYOK. */
export const OPENAI_PROVIDER = 'openai-images' as const;
export type Provider = typeof OPENAI_PROVIDER;

/** Built-in real model names; any other text is a relay alias. */
export const GPT_IMAGE_MODELS = ['gpt-image-1', 'gpt-image-2'] as const;

/** Sentinel option value that reveals the free-text model input. */
export const CUSTOM_MODEL_VALUE = 'custom' as const;

export const DEFAULT_REAL_MODEL: string = GPT_IMAGE_MODELS[0];

export const MODEL_MAX_LENGTH = 120;
export const PROMPT_MAX_LENGTH = 8000;

/** Built-in "ungrouped" bucket that every task falls back to. */
export const UNGROUPED_GROUP_ID = '00000000-0000-0000-0000-000000000001';
export const UNGROUPED_GROUP_NAME = '未分组';

export const TASK_STATUSES = [
  'Queued',
  'Dispatching',
  'Running',
  'Succeeded',
  'Failed',
  'Unknown',
  'Cancelled',
] as const;
export type TaskStatus = (typeof TASK_STATUSES)[number];

export const TERMINAL_TASK_STATUSES: readonly TaskStatus[] = [
  'Succeeded',
  'Failed',
  'Unknown',
  'Cancelled',
];

export function isTerminalTaskStatus(status: TaskStatus): boolean {
  return TERMINAL_TASK_STATUSES.includes(status);
}

export interface Template {
  id: string;
  name: string;
  body: string;
  tags: string[];
  revision: number;
  createdAt: string;
  updatedAt: string;
}

export interface TemplateInput {
  name: string;
  body: string;
  tags: string[];
}

export interface Resolution {
  id: string;
  name: string;
  mode: ResolutionMode;
  width: number | null;
  height: number | null;
  aspectRatio: AspectRatio | null;
  imageSize: ImageSize | null;
  createdAt: string;
  updatedAt: string;
}

export interface ResolutionInput {
  name: string;
  mode: ResolutionMode;
  width: number | null;
  height: number | null;
  aspectRatio: AspectRatio | null;
  imageSize: ImageSize | null;
}

export interface TaskGroup {
  id: string;
  name: string;
  /** True for the built-in group; it cannot be renamed or deleted. */
  isSystem: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface TaskGroupInput {
  name: string;
}

export const PROMPT_SOURCES = ['template', 'inline'] as const;
export type PromptSource = (typeof PROMPT_SOURCES)[number];

export interface PromptSnapshot {
  templateId: string;
  revision: number;
  name: string;
  body: string;
  source: PromptSource;
}

export interface ResolutionSnapshot {
  profileId: string;
  name: string;
  mode: ResolutionMode;
  width: number | null;
  height: number | null;
  aspectRatio: string | null;
  imageSize: string | null;
}

/** How a size was requested. Mirrors `ResolutionSizing.Mode*` on the backend. */
export const SIZE_TARGET_MODES = ['auto', 'tier', 'ratio', 'pixels'] as const;
export type SizeTargetMode = (typeof SIZE_TARGET_MODES)[number];

/**
 * One-shot size request. `mode` is the explicit discriminator; when it is omitted the backend infers
 * it from the present fields so request bodies written by older pages keep working.
 */
export interface ResolutionTarget {
  mode?: SizeTargetMode | null;
  imageSize?: string | null;
  aspectRatio?: string | null;
  width?: number | null;
  height?: number | null;
}

/** Quality values accepted by the generation API; `null` means "let the model decide". */
export const GENERATION_QUALITIES = ['low', 'medium', 'high'] as const;
export type GenerationQuality = (typeof GENERATION_QUALITIES)[number];

/** Output format values accepted by the generation API; `null` means the upstream default. */
export const GENERATION_FORMATS = ['png', 'jpeg', 'webp'] as const;
export type GenerationFormat = (typeof GENERATION_FORMATS)[number];

/** Moderation values accepted by the generation API; `null` means the upstream default. */
export const GENERATION_MODERATIONS = ['low'] as const;
export type GenerationModeration = (typeof GENERATION_MODERATIONS)[number];

export const GENERATION_COUNTS = [1, 2, 3, 4] as const;
export const MAX_GENERATION_COUNT = 4;
export const MIN_GENERATION_COUNT = 1;

/** Reference image limits, mirrored by `ReferenceRules` on the backend. */
export const MAX_REFERENCE_IMAGES = 4;
export const MAX_REFERENCE_BYTES = 10 * 1024 * 1024;
export const REFERENCE_MIME_TYPES = ['image/png', 'image/jpeg', 'image/webp'] as const;

export interface GenerationOptions {
  quality: GenerationQuality | null;
  count: number;
  format: GenerationFormat | null;
  transparent: boolean;
  moderation: GenerationModeration | null;
}

/** Only the fields the user changed are sent; omitted fields keep their documented defaults. */
export type GenerationOptionsInput = Partial<GenerationOptions>;

/** A stored reference image. URLs point at the authenticated local endpoints. */
export interface ReferenceImage {
  id: string;
  width: number;
  height: number;
  mimeType: string;
  contentUrl: string;
  thumbnailUrl: string;
}

export interface Asset {
  id: string;
  mimeType: string;
  width: number;
  height: number;
  hash: string;
  relativePath: string;
  thumbnailPath: string;
  taskId: string;
  attemptId: string;
  /** Position inside the response batch, starting at zero. */
  ordinal?: number;
}

export interface Task {
  id: string;
  groupId: string;
  promptSnapshot: PromptSnapshot;
  resolutionSnapshot: ResolutionSnapshot;
  /**
   * Persisted provider id. Rows written by older releases may carry an id this build no longer
   * registers, so the value is kept as plain text and rendered verbatim instead of failing.
   */
  provider: string;
  model: string;
  status: TaskStatus;
  errorCode: string | null;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  assets: Asset[];
  /** Generation parameters captured at submission time; null on tasks created before they existed. */
  options: GenerationOptions | null;
  /** Number of images actually returned upstream (may be lower than `options.count`). */
  actualCount: number | null;
  /** Reference images bound to this task, in submission order. */
  references: ReferenceImage[];
}

/** Both the inline path (prompt + resolution) and the catalog path (templateId + resolutionId). */
export interface TaskCreateInput {
  prompt?: string;
  templateId?: string | null;
  resolution?: ResolutionTarget | null;
  resolutionId?: string | null;
  groupId?: string | null;
  provider?: string;
  model?: string;
  /** 0..4 reference image ids produced by `POST /api/references`. */
  referenceIds?: string[];
  options?: GenerationOptionsInput;
}

export interface ProviderSettings {
  protocol: 'openaiImages';
  baseUrl: string | null;
  model: string | null;
  configured: boolean;
  enabled: boolean;
  maskedKey: string | null;
  updatedAt: string | null;
}

export interface ProviderSettingsInput {
  baseUrl?: string | null;
  apiKey?: string | null;
  model?: string | null;
  enabled?: boolean | null;
}

export const PROVIDER_TEST_OUTCOMES = ['ok', 'unauthorized', 'unknown'] as const;
export type ProviderTestOutcome = (typeof PROVIDER_TEST_OUTCOMES)[number];

export interface ProviderTestResult {
  result: ProviderTestOutcome;
  status: number | null;
}

export interface SessionResponse {
  csrfToken: string;
}

export interface ApiErrorBody {
  code: string;
}

export const TEMPLATE_TAGS_LIMIT = 12;
export const TEMPLATE_NAME_MAX_LENGTH = 120;
export const TEMPLATE_BODY_MAX_LENGTH = 8000;

export function resolutionSnapshotFrom(resolution: Resolution): ResolutionSnapshot {
  return {
    profileId: resolution.id,
    name: resolution.name,
    mode: resolution.mode,
    width: resolution.width,
    height: resolution.height,
    aspectRatio: resolution.aspectRatio,
    imageSize: resolution.imageSize,
  };
}
