/**
 * The one-shot size choice made in `SizePickerModal` and sent with a task.
 *
 * A discriminated union rather than a bag of nullable fields: the modal, the read-only field on the
 * Tasks page and the wire payload all switch on exactly one `mode`, so an impossible combination
 * (a tier plus explicit pixels) cannot be constructed.
 */

import {
  calculateImageSize,
  type PixelSize,
  type TierAspectRatio,
  type TierImageSize,
} from './resolutionTiers';
import type { ResolutionTarget } from '../types';

export type SizeSelection =
  /** Ask the provider to pick; no `size` member is sent at all. */
  | { mode: 'auto' }
  /** One of the 24 fixed presets (8 ratios × 3 buckets). */
  | { mode: 'tier'; imageSize: TierImageSize; aspectRatio: TierAspectRatio }
  /** An arbitrary ratio; the pixels come from the shared grid search. */
  | { mode: 'ratio'; imageSize: TierImageSize; aspectRatio: string }
  /** Explicit pixel dimensions typed by the user. */
  | { mode: 'pixels'; width: number; height: number };

/** 1K 1:1 — the same default the task form used before the picker existed. */
export const DEFAULT_SIZE_SELECTION: SizeSelection = {
  mode: 'tier',
  imageSize: '1K',
  aspectRatio: '1:1',
};

/** The concrete pixel pair a selection resolves to, or null when it does not resolve. */
export function selectionPixels(selection: SizeSelection): PixelSize | null {
  switch (selection.mode) {
    case 'auto':
      return null;
    case 'tier':
    case 'ratio':
      return calculateImageSize(selection.imageSize, selection.aspectRatio);
    case 'pixels':
      return { width: selection.width, height: selection.height };
  }
}

/**
 * True when the selection can be submitted. `auto` always can; everything else needs a resolvable
 * pixel pair (which also implies the ratio was parseable and inside the budget).
 */
export function isSelectionResolved(selection: SizeSelection): boolean {
  return selection.mode === 'auto' || selectionPixels(selection) !== null;
}

/** Wire payload for `POST /api/tasks`. Unused members are explicit nulls, never omitted. */
export function toResolutionTarget(selection: SizeSelection): ResolutionTarget {
  switch (selection.mode) {
    case 'auto':
      return { mode: 'auto', imageSize: null, aspectRatio: null, width: null, height: null };
    case 'tier':
      return {
        mode: 'tier',
        imageSize: selection.imageSize,
        aspectRatio: selection.aspectRatio,
        width: null,
        height: null,
      };
    case 'ratio':
      return {
        mode: 'ratio',
        imageSize: selection.imageSize,
        aspectRatio: selection.aspectRatio,
        width: null,
        height: null,
      };
    case 'pixels':
      return {
        mode: 'pixels',
        imageSize: null,
        aspectRatio: null,
        width: selection.width,
        height: selection.height,
      };
  }
}
