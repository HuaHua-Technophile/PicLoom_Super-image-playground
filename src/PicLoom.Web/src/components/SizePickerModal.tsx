import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useIntl } from 'react-intl';
import {
  TIER_ASPECT_RATIOS,
  TIER_IMAGE_SIZES,
  calculateImageSize,
  parseRatio,
  parseSizeInput,
  resolveTier,
  validateCustomSize,
  type TierAspectRatio,
  type TierImageSize,
} from '../lib/resolutionTiers';
import { isSelectionResolved, selectionPixels, type SizeSelection } from '../lib/sizeSelection';

/** Sentinel card value that reveals the free-text ratio input. */
const CUSTOM_RATIO_VALUE = '__custom_ratio__';

type PickerTab = 'auto' | 'ratio' | 'pixels';

const TAB_ORDER: readonly PickerTab[] = ['auto', 'ratio', 'pixels'];

interface SizePickerModalProps {
  /** Selection the page currently holds; the dialog opens on the matching tab. */
  value: SizeSelection;
  onCancel: () => void;
  onConfirm: (selection: SizeSelection) => void;
}

interface PickerState {
  tab: PickerTab;
  imageSize: TierImageSize;
  /** One of the eight preset ratios, or {@link CUSTOM_RATIO_VALUE}. */
  ratioChoice: TierAspectRatio | typeof CUSTOM_RATIO_VALUE;
  customRatio: string;
  widthInput: string;
  heightInput: string;
  /**
   * True once the user has typed into either pixel field. Guards the prefill so switching tabs back
   * and forth never overwrites a hand-edited size.
   */
  pixelsDirty: boolean;
}

/** Fallback bucket used when the current size carries no dimensions at all (`auto`). */
const PIXEL_PREFILL_FALLBACK = 1024;

/**
 * The numbers the pixel tab starts from: the size that is currently in effect, so the user edits
 * "2880 × 2880" instead of retyping two values from scratch. `auto` has no dimensions by design, so
 * it falls back to a 1:1 square.
 */
function prefillPixels(value: SizeSelection): { widthInput: string; heightInput: string } {
  const pixels = selectionPixels(value);
  return pixels
    ? { widthInput: String(pixels.width), heightInput: String(pixels.height) }
    : {
        widthInput: String(PIXEL_PREFILL_FALLBACK),
        heightInput: String(PIXEL_PREFILL_FALLBACK),
      };
}

/** Projects the page's selection onto the dialog's local, editable state. */
function toPickerState(value: SizeSelection): PickerState {
  switch (value.mode) {
    case 'auto':
      return {
        tab: 'auto',
        imageSize: '1K',
        ratioChoice: '1:1',
        customRatio: '',
        widthInput: '',
        heightInput: '',
        pixelsDirty: false,
      };
    case 'tier':
      return {
        tab: 'ratio',
        imageSize: value.imageSize,
        ratioChoice: value.aspectRatio,
        customRatio: '',
        widthInput: '',
        heightInput: '',
        pixelsDirty: false,
      };
    case 'ratio':
      return {
        tab: 'ratio',
        imageSize: value.imageSize,
        ratioChoice: CUSTOM_RATIO_VALUE,
        customRatio: value.aspectRatio,
        widthInput: '',
        heightInput: '',
        pixelsDirty: false,
      };
    case 'pixels':
      return {
        tab: 'pixels',
        imageSize: '1K',
        ratioChoice: '1:1',
        customRatio: '',
        widthInput: String(value.width),
        heightInput: String(value.height),
        pixelsDirty: false,
      };
  }
}

/** Draws a small proportional glyph for one aspect ratio. Sizing lives in the stylesheet. */
function RatioGlyph({ ratio }: { ratio: string }) {
  return (
    <span className="ratio-glyph" data-ratio={ratio} aria-hidden="true">
      <span className="ratio-glyph-inner" />
    </span>
  );
}

/**
 * Size chooser for one task. Three tabs map onto three payload shapes: `auto` (no `size` upstream),
 * a preset ratio (fixed table) and explicit pixels. Typing a custom ratio stays in the ratio tab but
 * switches the payload to the computed `ratio` mode.
 */
export function SizePickerModal({ value, onCancel, onConfirm }: SizePickerModalProps) {
  const intl = useIntl();
  const dialogRef = useRef<HTMLDivElement>(null);
  const tabRefs = useRef<Record<PickerTab, HTMLButtonElement | null>>({
    auto: null,
    ratio: null,
    pixels: null,
  });
  const [state, setState] = useState<PickerState>(() => toPickerState(value));

  const message = useCallback(
    (id: string, values?: Record<string, string | number>) => intl.formatMessage({ id }, values),
    [intl],
  );

  // Focus moves into the dialog on open and returns to the opener on close.
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialogRef.current?.focus();
    return () => {
      previous?.focus?.();
    };
  }, []);

  useEffect(() => {
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        onCancel();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onCancel]);

  const customRatioParsed = state.customRatio.trim() === '' ? null : parseRatio(state.customRatio);

  /**
   * Resolved pixel pair for the ratio tab: a preset card reads the fixed table, a typed ratio goes
   * through the same grid search the backend uses.
   */
  const ratioPixels = useMemo(() => {
    if (state.ratioChoice === CUSTOM_RATIO_VALUE) {
      return customRatioParsed === null
        ? null
        : calculateImageSize(state.imageSize, state.customRatio);
    }
    return resolveTier(state.imageSize, state.ratioChoice);
  }, [customRatioParsed, state.customRatio, state.imageSize, state.ratioChoice]);

  const widthPx = parseSizeInput(state.widthInput);
  const heightPx = parseSizeInput(state.heightInput);
  const pixelTouched = state.widthInput.trim() !== '' || state.heightInput.trim() !== '';
  const pixelError =
    widthPx === null || heightPx === null ? null : validateCustomSize(widthPx, heightPx);

  const customRatioMissing = state.ratioChoice === CUSTOM_RATIO_VALUE && customRatioParsed === null;
  const customRatioNotComputable =
    state.ratioChoice === CUSTOM_RATIO_VALUE && customRatioParsed !== null && ratioPixels === null;

  /** The selection the confirm button would commit, or null when the current tab is unusable. */
  const pending = useMemo<SizeSelection | null>(() => {
    switch (state.tab) {
      case 'auto':
        return { mode: 'auto' };
      case 'ratio':
        if (ratioPixels === null) {
          return null;
        }
        return state.ratioChoice === CUSTOM_RATIO_VALUE
          ? { mode: 'ratio', imageSize: state.imageSize, aspectRatio: state.customRatio.trim() }
          : { mode: 'tier', imageSize: state.imageSize, aspectRatio: state.ratioChoice };
      case 'pixels':
        return widthPx !== null && heightPx !== null && validateCustomSize(widthPx, heightPx) === null
          ? { mode: 'pixels', width: widthPx, height: heightPx }
          : null;
    }
  }, [heightPx, ratioPixels, state, widthPx]);

  const preview = useMemo(() => {
    if (!pending) {
      return null;
    }
    const pixels = selectionPixels(pending);
    return pixels
      ? message('tasks.sizeLabel', { width: pixels.width, height: pixels.height })
      : message('size.autoValue');
  }, [message, pending]);

  const currentLabel = useMemo(() => {
    const pixels = selectionPixels(value);
    return pixels
      ? message('tasks.sizeLabel', { width: pixels.width, height: pixels.height })
      : message('size.autoValue');
  }, [message, value]);

  const canConfirm = pending !== null && isSelectionResolved(pending);

  /**
   * Roving tabindex: only the selected tab is in the tab order, and every move (click, arrow keys,
   * Home/End) also moves DOM focus. Without the focus move the dialog would stay on the old button
   * and repeated arrow presses would keep recomputing from the same index.
   *
   * Entering the pixel tab also pre-fills the two inputs from the size currently in effect, but only
   * while the user has not typed anything yet — otherwise a hand-edited size would be clobbered.
   */
  const selectTab = (tab: PickerTab, moveFocus = false) => {
    setState((previous) => {
      const next: PickerState = { ...previous, tab };
      if (tab === 'pixels' && !previous.pixelsDirty) {
        return { ...next, ...prefillPixels(value) };
      }
      return next;
    });
    if (moveFocus) {
      tabRefs.current[tab]?.focus();
    }
  };

  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, tab: PickerTab) => {
    const index = TAB_ORDER.indexOf(tab);
    let nextIndex: number | null = null;

    if (event.key === 'ArrowRight') nextIndex = (index + 1) % TAB_ORDER.length;
    else if (event.key === 'ArrowLeft') nextIndex = (index - 1 + TAB_ORDER.length) % TAB_ORDER.length;
    else if (event.key === 'Home') nextIndex = 0;
    else if (event.key === 'End') nextIndex = TAB_ORDER.length - 1;

    if (nextIndex === null) {
      return;
    }
    event.preventDefault();
    selectTab(TAB_ORDER[nextIndex], true);
  };

  return (
    <div
      className="overlay size-overlay"
      data-testid="size-overlay"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onCancel();
        }
      }}
    >
      <div
        ref={dialogRef}
        className="dialog size-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="size-picker-title"
        tabIndex={-1}
      >
        <div className="size-dialog-header">
          <div>
            <h2 id="size-picker-title" className="dialog-title">
              {message('size.title')}
            </h2>
            <p className="size-dialog-current">
              {message('size.subtitle', { current: currentLabel })}
            </p>
          </div>
          <button
            type="button"
            className="icon-button"
            aria-label={message('common.close')}
            onClick={onCancel}
          >
            <span aria-hidden="true">×</span>
          </button>
        </div>

        <div className="size-tabs" role="tablist" aria-label={message('size.title')}>
          {TAB_ORDER.map((tab) => (
            <button
              key={tab}
              ref={(element) => {
                tabRefs.current[tab] = element;
              }}
              type="button"
              role="tab"
              id={`size-tab-${tab}`}
              className={`size-tab${state.tab === tab ? ' is-active' : ''}`}
              aria-selected={state.tab === tab}
              aria-controls={`size-panel-${tab}`}
              tabIndex={state.tab === tab ? 0 : -1}
              onClick={() => selectTab(tab)}
              onKeyDown={(event) => handleTabKeyDown(event, tab)}
            >
              {message(`size.tab.${tab}`)}
            </button>
          ))}
        </div>

        <div className="size-dialog-body">
          {state.tab === 'auto' ? (
            <div
              className="size-panel"
              id="size-panel-auto"
              role="tabpanel"
              aria-labelledby="size-tab-auto"
            >
              <div className="size-auto">
                <span className="size-auto-icon" aria-hidden="true">
                  <svg viewBox="0 0 48 48" width="48" height="48" focusable="false">
                    <rect
                      x="6"
                      y="12"
                      width="36"
                      height="24"
                      rx="4"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                    />
                    <path
                      d="M14 30l6-7 5 5 4-4 5 6"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinejoin="round"
                    />
                    <circle cx="17" cy="19" r="2.5" fill="currentColor" />
                  </svg>
                </span>
                <p className="size-auto-title">{message('size.auto.title')}</p>
                <p className="size-auto-hint">{message('size.auto.hint1')}</p>
                <p className="size-auto-hint">{message('size.auto.hint2')}</p>
              </div>
            </div>
          ) : null}

          {state.tab === 'ratio' ? (
            <div
              className="size-panel"
              id="size-panel-ratio"
              role="tabpanel"
              aria-labelledby="size-tab-ratio"
            >
              <p className="size-group-label">{message('size.tier')}</p>
              <div className="size-tier-group" role="group" aria-label={message('size.tier')}>
                {TIER_IMAGE_SIZES.map((size) => (
                  <button
                    key={size}
                    type="button"
                    className={`size-tier${state.imageSize === size ? ' is-active' : ''}`}
                    aria-pressed={state.imageSize === size}
                    onClick={() => setState((previous) => ({ ...previous, imageSize: size }))}
                  >
                    {size}
                  </button>
                ))}
              </div>

              <p className="size-group-label">{message('size.ratio')}</p>
              <div className="size-ratio-grid" role="group" aria-label={message('size.ratio')}>
                {TIER_ASPECT_RATIOS.map((ratio) => {
                  const active = state.ratioChoice === ratio;
                  return (
                    <button
                      key={ratio}
                      type="button"
                      className={`size-ratio-card${active ? ' is-active' : ''}`}
                      aria-pressed={active}
                      onClick={() => setState((previous) => ({ ...previous, ratioChoice: ratio }))}
                    >
                      <RatioGlyph ratio={ratio} />
                      <span className="size-ratio-label">{ratio}</span>
                    </button>
                  );
                })}
                <button
                  type="button"
                  className={`size-ratio-card size-ratio-custom${
                    state.ratioChoice === CUSTOM_RATIO_VALUE ? ' is-active' : ''
                  }`}
                  aria-pressed={state.ratioChoice === CUSTOM_RATIO_VALUE}
                  onClick={() =>
                    setState((previous) => ({ ...previous, ratioChoice: CUSTOM_RATIO_VALUE }))
                  }
                >
                  <span className="size-ratio-label">{message('size.ratio.custom')}</span>
                </button>
              </div>

              {state.ratioChoice === CUSTOM_RATIO_VALUE ? (
                <div className="size-custom-ratio">
                  <label className="field-label" htmlFor="size-custom-ratio">
                    {message('size.ratio.custom')}
                  </label>
                  <input
                    id="size-custom-ratio"
                    className={`input${
                      customRatioMissing || customRatioNotComputable ? ' input-invalid' : ''
                    }`}
                    type="text"
                    autoComplete="off"
                    placeholder={message('size.ratio.placeholder')}
                    aria-invalid={customRatioMissing || customRatioNotComputable}
                    value={state.customRatio}
                    onChange={(event) =>
                      setState((previous) => ({ ...previous, customRatio: event.target.value }))
                    }
                  />
                  {customRatioMissing || customRatioNotComputable ? (
                    <p className="field-error" role="alert">
                      {message('size.ratio.invalid')}
                    </p>
                  ) : null}
                </div>
              ) : null}
            </div>
          ) : null}

          {state.tab === 'pixels' ? (
            <div
              className="size-panel"
              id="size-panel-pixels"
              role="tabpanel"
              aria-labelledby="size-tab-pixels"
            >
              <div className="size-pixel-row">
                <label className="field" htmlFor="size-width">
                  <span className="field-label">{message('size.width')}</span>
                  <input
                    id="size-width"
                    className={`input${pixelError ? ' input-invalid' : ''}`}
                    type="text"
                    inputMode="numeric"
                    autoComplete="off"
                    value={state.widthInput}
                    aria-invalid={pixelError !== null}
                    onChange={(event) =>
                      setState((previous) => ({
                        ...previous,
                        widthInput: event.target.value,
                        pixelsDirty: true,
                      }))
                    }
                  />
                </label>
                <span className="size-pixel-times" aria-hidden="true">
                  ×
                </span>
                <label className="field" htmlFor="size-height">
                  <span className="field-label">{message('size.height')}</span>
                  <input
                    id="size-height"
                    className={`input${pixelError ? ' input-invalid' : ''}`}
                    type="text"
                    inputMode="numeric"
                    autoComplete="off"
                    value={state.heightInput}
                    aria-invalid={pixelError !== null}
                    onChange={(event) =>
                      setState((previous) => ({
                        ...previous,
                        heightInput: event.target.value,
                        pixelsDirty: true,
                      }))
                    }
                  />
                </label>
              </div>

              {pixelTouched && pixelError ? (
                <p className="field-error" role="alert">
                  {message(`errors.${pixelError.code}`)}
                  {pixelError.suggestion
                    ? ` ${message('tasks.customSizeSuggestion', {
                        width: pixelError.suggestion.width,
                        height: pixelError.suggestion.height,
                      })}`
                    : ''}
                </p>
              ) : null}

              <p className="size-hint">{message('size.pixelsHint')}</p>
            </div>
          ) : null}
        </div>

        <div className="size-dialog-footer">
          <p className="size-preview">
            {message('size.preview')}{' '}
            <span className="size-preview-value">{preview ?? message('common.notSet')}</span>
          </p>
          <div className="size-dialog-actions">
            <button type="button" className="btn btn-secondary" onClick={onCancel}>
              {message('common.cancel')}
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={!canConfirm}
              onClick={() => {
                if (pending) {
                  onConfirm(pending);
                }
              }}
            >
              {message('size.confirm')}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
