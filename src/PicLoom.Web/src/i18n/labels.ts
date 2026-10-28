import type { IntlShape } from 'react-intl';
import { resolveTier } from '../lib/resolutionTiers';
import { OPENAI_PROVIDER, type PromptSnapshot, type TaskGroup, type TaskStatus } from '../types';

export function statusMessageKey(status: TaskStatus): string {
  return `tasks.status.${status}`;
}

/** The system group is rendered through i18n instead of its persisted name. */
export function groupLabel(intl: IntlShape, group: Pick<TaskGroup, 'name' | 'isSystem'>): string {
  return group.isSystem ? intl.formatMessage({ id: 'groups.ungrouped' }) : group.name;
}

export function ungroupedLabel(intl: IntlShape): string {
  return intl.formatMessage({ id: 'groups.ungrouped' });
}

/**
 * Provider label. The registered provider is localized; anything else is a value persisted by an
 * older release (a provider this build no longer ships) and is rendered verbatim so old tasks never
 * break the list or the detail panel.
 */
export function providerLabel(intl: IntlShape, provider: string): string {
  return provider === OPENAI_PROVIDER
    ? intl.formatMessage({ id: 'tasks.providerOpenAi' })
    : provider;
}

export function resolutionSummary(
  intl: IntlShape,
  resolution: {
    mode: string;
    width: number | null;
    height: number | null;
    aspectRatio: string | null;
    imageSize: string | null;
  },
): string {
  if (resolution.mode === 'auto') {
    return intl.formatMessage({ id: 'resolutions.mode.auto' });
  }
  if (resolution.mode === 'aspectClass' && resolution.aspectRatio && resolution.imageSize) {
    return intl.formatMessage(
      { id: 'resolutions.aspectLabel' },
      { aspectRatio: resolution.aspectRatio, imageSize: resolution.imageSize },
    );
  }
  if (resolution.width !== null && resolution.height !== null) {
    return intl.formatMessage(
      { id: 'resolutions.exactLabel' },
      { width: resolution.width, height: resolution.height },
    );
  }
  return intl.formatMessage({ id: 'common.notSet' });
}

/**
 * Snapshot-first size label. Preset snapshots carry concrete pixels; rows written by older
 * releases may still hold only an aspect class, so the tier table is used as a fallback.
 * An `auto` snapshot has no dimensions at all by design, so it is labelled instead of "not set".
 */
export function snapshotSize(
  intl: IntlShape,
  snapshot: {
    mode?: string;
    width: number | null;
    height: number | null;
    aspectRatio: string | null;
    imageSize: string | null;
  },
): string {
  if (snapshot.width !== null && snapshot.height !== null) {
    return intl.formatMessage(
      { id: 'tasks.sizeLabel' },
      { width: snapshot.width, height: snapshot.height },
    );
  }
  if (snapshot.mode === 'auto') {
    return intl.formatMessage({ id: 'size.autoValue' });
  }
  const tier = resolveTier(snapshot.imageSize, snapshot.aspectRatio);
  if (tier) {
    return intl.formatMessage({ id: 'tasks.sizeLabel' }, { width: tier.width, height: tier.height });
  }
  return intl.formatMessage({ id: 'common.notSet' });
}

export function promptLabel(intl: IntlShape, snapshot: PromptSnapshot): string {
  if (snapshot.source === 'inline') {
    return intl.formatMessage({ id: 'tasks.snapshot.inline' });
  }
  return snapshot.name || intl.formatMessage({ id: 'common.notSet' });
}

/** Short one-line preview used by the task list. */
export function promptPreview(snapshot: PromptSnapshot, maxLength = 48): string {
  const text = snapshot.body.trim().replace(/\s+/g, ' ');
  if (text.length <= maxLength) {
    return text;
  }
  return `${text.slice(0, maxLength)}…`;
}

export function formatDateTime(intl: IntlShape, value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return intl.formatDate(date, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Model names are free text and are always shown verbatim. */
export function formatModel(_intl: IntlShape, model: string): string {
  return model;
}
