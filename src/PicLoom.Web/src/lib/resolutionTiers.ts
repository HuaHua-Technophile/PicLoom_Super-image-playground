/**
 * Frontend mirror of `PicLoom.Core/ResolutionTiers.cs`.
 *
 * The preset table is a single literal array: values must match the backend byte for byte, so a
 * product change is a one-line edit here plus the same edit in the C# static initializer.
 * Two backend tests parse this file directly
 * (`ResolutionBoundaryTests.The_frontend_mirror_is_identical_to_the_backend_table`), so the literal
 * must keep the `{ imageSize: '…', aspectRatio: '…', width: N, height: N }` shape.
 *
 * `calculateImageSize` is a byte-for-byte port of `ResolutionSizing.TryCalculateRatioSize`; both must
 * produce the same pair for the same input or the preview shown on the Tasks page would lie about
 * what the backend is going to dispatch.
 */

export const TIER_IMAGE_SIZES = ['1K', '2K', '4K'] as const;
export type TierImageSize = (typeof TIER_IMAGE_SIZES)[number];

export const TIER_ASPECT_RATIOS = ['1:1', '3:2', '2:3', '16:9', '9:16', '4:3', '3:4', '21:9'] as const;
export type TierAspectRatio = (typeof TIER_ASPECT_RATIOS)[number];

export interface ResolutionTier {
  imageSize: TierImageSize;
  aspectRatio: TierAspectRatio;
  width: number;
  height: number;
}

/** The 24 presets: 8 ratios × 3 sizes, ratio-major order. */
export const RESOLUTION_TIERS: readonly ResolutionTier[] = [
  { imageSize: '1K', aspectRatio: '1:1', width: 1024, height: 1024 },
  { imageSize: '2K', aspectRatio: '1:1', width: 2048, height: 2048 },
  { imageSize: '4K', aspectRatio: '1:1', width: 2880, height: 2880 },
  { imageSize: '1K', aspectRatio: '3:2', width: 1536, height: 1024 },
  { imageSize: '2K', aspectRatio: '3:2', width: 2160, height: 1440 },
  { imageSize: '4K', aspectRatio: '3:2', width: 3456, height: 2304 },
  { imageSize: '1K', aspectRatio: '2:3', width: 1024, height: 1536 },
  { imageSize: '2K', aspectRatio: '2:3', width: 1440, height: 2160 },
  { imageSize: '4K', aspectRatio: '2:3', width: 2304, height: 3456 },
  { imageSize: '1K', aspectRatio: '16:9', width: 1280, height: 720 },
  { imageSize: '2K', aspectRatio: '16:9', width: 2560, height: 1440 },
  { imageSize: '4K', aspectRatio: '16:9', width: 3840, height: 2160 },
  { imageSize: '1K', aspectRatio: '9:16', width: 720, height: 1280 },
  { imageSize: '2K', aspectRatio: '9:16', width: 1440, height: 2560 },
  { imageSize: '4K', aspectRatio: '9:16', width: 2160, height: 3840 },
  { imageSize: '1K', aspectRatio: '4:3', width: 1024, height: 768 },
  { imageSize: '2K', aspectRatio: '4:3', width: 2048, height: 1536 },
  { imageSize: '4K', aspectRatio: '4:3', width: 3200, height: 2400 },
  { imageSize: '1K', aspectRatio: '3:4', width: 768, height: 1024 },
  { imageSize: '2K', aspectRatio: '3:4', width: 1536, height: 2048 },
  { imageSize: '4K', aspectRatio: '3:4', width: 2400, height: 3200 },
  { imageSize: '1K', aspectRatio: '21:9', width: 1280, height: 544 },
  { imageSize: '2K', aspectRatio: '21:9', width: 2560, height: 1088 },
  { imageSize: '4K', aspectRatio: '21:9', width: 3840, height: 1600 },
];

export const RESOLUTION_LIMITS = {
  multiple: 16,
  maxEdge: 3840,
  maxRatio: 3,
  minPixels: 655_360,
  maxPixels: 8_294_400,
} as const;

/** Pixel budget per image size bucket, mirroring the CookSleep reference table. */
export const TIER_PIXEL_BUDGETS: Readonly<Record<TierImageSize, number>> = {
  '1K': 1_572_864,
  '2K': 4_194_304,
  '4K': 8_294_400,
};

/**
 * Most decimal places accepted when scaling one part of a ratio. Six places cover every spelling the
 * UI suggests (`2.39:1`) and keep the cross multiplication exact in double precision.
 */
const MAX_RATIO_SCALE = 6;

/** Largest digit value accepted for one part of a ratio, for the same exactness reason. */
const MAX_RATIO_PART = 1_000_000;

const RATIO_PATTERN = /^\s*(\d+(?:\.\d+)?)\s*[:xX×]\s*(\d+(?:\.\d+)?)\s*$/;

export interface PixelSize {
  width: number;
  height: number;
}

export interface CustomSizeError {
  code: string;
  /** Suggested nearest multiples, only meaningful for the 16-multiple failure. */
  suggestion: PixelSize | null;
}

export function resolveTier(
  imageSize: string | null | undefined,
  aspectRatio: string | null | undefined,
): ResolutionTier | null {
  if (!imageSize || !aspectRatio) {
    return null;
  }
  return (
    RESOLUTION_TIERS.find(
      (tier) => tier.imageSize === imageSize && tier.aspectRatio === aspectRatio,
    ) ?? null
  );
}

export function isTierImageSize(value: string | null | undefined): value is TierImageSize {
  return typeof value === 'string' && (TIER_IMAGE_SIZES as readonly string[]).includes(value);
}

export function isTierAspectRatio(value: string | null | undefined): value is TierAspectRatio {
  return typeof value === 'string' && (TIER_ASPECT_RATIOS as readonly string[]).includes(value);
}

/**
 * Parses `"16:9"`, `"5:4"`, `"2.39:1"`, `"16x9"` or `"16×9"` into its two positive decimal parts.
 * Returns `null` for anything else, including empty input.
 */
export function parseRatio(text: string | null | undefined): PixelSize | null {
  if (typeof text !== 'string') {
    return null;
  }
  const match = RATIO_PATTERN.exec(text);
  if (!match) {
    return null;
  }
  const width = Number.parseFloat(match[1]);
  const height = Number.parseFloat(match[2]);
  if (!Number.isFinite(width) || !Number.isFinite(height)) {
    return null;
  }
  return { width, height };
}

/**
 * True when `text` denotes the same value as one of the eight preset ratios. The comparison is
 * mathematical (cross multiplication on decimal-scaled integers), never textual, so `21:9`,
 * `42:18` and `7:3` all match the 21:9 preset while `5:4` and `2.39:1` match none of them. This is
 * what PRD §3.1 rule 7 requires: "a ratio that hits one of the eight table ratios uses the table
 * value".
 */
export function matchPresetRatio(text: string | null | undefined): string | null {
  if (typeof text !== 'string') {
    return null;
  }
  const match = RATIO_PATTERN.exec(text);
  if (!match) {
    return null;
  }

  const widthPart = parseDecimalPart(match[1]);
  const heightPart = parseDecimalPart(match[2]);
  if (!widthPart || !heightPart) {
    return null;
  }

  // Put both parts on the same power of ten: "2.39:1" -> 239:100, "1.5:1" -> 15:10.
  const width = widthPart.value * 10 ** heightPart.scale;
  const height = heightPart.value * 10 ** widthPart.scale;
  if (height === 0) {
    return null;
  }

  for (const key of TIER_ASPECT_RATIOS) {
    const preset = parseRatio(key);
    if (!preset) {
      continue;
    }
    // Presets are whole numbers <= 21, so every product here stays an exact double.
    if (width * preset.height === preset.width * height) {
      return key;
    }
  }

  return null;
}

/**
 * Splits one decimal part of a ratio into its digits and its scale: `"2.39"` becomes `(239, 2)` and
 * `"16"` becomes `(16, 0)`. The documented bounds keep the cross multiplication above exact.
 */
function parseDecimalPart(text: string): { value: number; scale: number } | null {
  const dot = text.indexOf('.');
  const digits = dot < 0 ? text : `${text.slice(0, dot)}${text.slice(dot + 1)}`;
  if (digits.length === 0 || !/^\d+$/.test(digits)) {
    return null;
  }

  const scale = dot < 0 ? 0 : text.length - dot - 1;
  if (scale > MAX_RATIO_SCALE) {
    return null;
  }

  const value = Number.parseInt(digits, 10);
  if (!Number.isFinite(value) || value <= 0 || value > MAX_RATIO_PART) {
    return null;
  }

  return { value, scale };
}

/**
 * Computes concrete pixel dimensions for an arbitrary ratio inside the pixel budget of `imageSize`.
 * A ratio that equals one of the eight presets resolves to the fixed table; every other ratio is
 * searched on the 16px grid, keeping the largest result whose divergence from the requested ratio is
 * at most 1%.
 */
export function calculateImageSize(
  imageSize: string | null | undefined,
  aspectRatio: string | null | undefined,
): PixelSize | null {
  const parsed = parseRatio(aspectRatio);
  if (!parsed || !isTierImageSize(imageSize)) {
    return null;
  }

  const presetKey = matchPresetRatio(aspectRatio);
  if (presetKey) {
    const preset = resolveTier(imageSize, presetKey);
    if (preset) {
      return { width: preset.width, height: preset.height };
    }
  }

  const targetRatio = parsed.width / parsed.height;
  if (!Number.isFinite(targetRatio) || targetRatio <= 0) {
    return null;
  }

  const budget = TIER_PIXEL_BUDGETS[imageSize];
  const multiple = RESOLUTION_LIMITS.multiple;
  const maxEdge = RESOLUTION_LIMITS.maxEdge;
  let best: { width: number; height: number; pixels: number } | null = null;

  for (let width = multiple; width <= maxEdge; width += multiple) {
    const idealHeight = width / targetRatio;
    for (let step = 0; step < 2; step += 1) {
      const candidate =
        step === 0 ? Math.floor(idealHeight / multiple) * multiple : Math.ceil(idealHeight / multiple) * multiple;

      // Written as negated comparisons on purpose: NaN and infinity fall through to the skip branch
      // exactly like they do in the C# port.
      if (!(candidate >= multiple) || !(candidate <= maxEdge)) {
        continue;
      }

      const pixels = width * candidate;
      if (pixels > budget || pixels < RESOLUTION_LIMITS.minPixels) {
        continue;
      }
      const actualRatio = width / candidate;
      if (Math.max(actualRatio, candidate / width) > RESOLUTION_LIMITS.maxRatio) {
        continue;
      }
      if (Math.abs((actualRatio - targetRatio) / targetRatio) > 0.01) {
        continue;
      }
      if (best && pixels <= best.pixels) {
        continue;
      }
      best = { width, height: candidate, pixels };
    }
  }

  return best ? { width: best.width, height: best.height } : null;
}

/**
 * Validates a custom size in exactly the same order as the backend so only the closest error is
 * reported: positive integers → multiple of 16 → single edge ≤ 3840 → ratio ≤ 3:1 → pixel range.
 */
export function validateCustomSize(
  width: number | null | undefined,
  height: number | null | undefined,
): CustomSizeError | null {
  if (!isPositiveInteger(width) || !isPositiveInteger(height)) {
    return { code: 'resolution_invalid', suggestion: null };
  }

  const w = width as number;
  const h = height as number;
  if (w % RESOLUTION_LIMITS.multiple !== 0 || h % RESOLUTION_LIMITS.multiple !== 0) {
    return {
      code: 'resolution_not_multiple_of_16',
      suggestion: { width: closestMultiple(w), height: closestMultiple(h) },
    };
  }
  if (w > RESOLUTION_LIMITS.maxEdge || h > RESOLUTION_LIMITS.maxEdge) {
    return { code: 'resolution_edge_too_large', suggestion: null };
  }

  const longEdge = Math.max(w, h);
  const shortEdge = Math.min(w, h);
  if (longEdge > shortEdge * RESOLUTION_LIMITS.maxRatio) {
    return { code: 'resolution_aspect_too_extreme', suggestion: null };
  }

  const pixels = w * h;
  if (pixels < RESOLUTION_LIMITS.minPixels || pixels > RESOLUTION_LIMITS.maxPixels) {
    return { code: 'resolution_pixels_out_of_range', suggestion: null };
  }

  return null;
}

/** Rounds to the nearest multiple of 16 (ties round down). */
export function closestMultiple(value: number): number {
  const multiple = RESOLUTION_LIMITS.multiple;
  return Math.max(multiple, Math.round(value / multiple) * multiple);
}

/** Parses a size input; blank or non-numeric text becomes null. */
export function parseSizeInput(value: string): number | null {
  const trimmed = value.trim();
  if (!/^\d+$/.test(trimmed)) {
    return null;
  }
  const parsed = Number.parseInt(trimmed, 10);
  return Number.isFinite(parsed) ? parsed : null;
}

function isPositiveInteger(value: number | null | undefined): boolean {
  return typeof value === 'number' && Number.isInteger(value) && value > 0;
}
