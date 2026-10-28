import { useRef, useState, type DragEvent } from 'react';
import { useIntl } from 'react-intl';
import { errorMessageKey } from '../api/client';
import { MAX_REFERENCE_IMAGES, type ReferenceImage } from '../types';
import { Notice } from './StateViews';

interface ReferencePickerProps {
  references: ReferenceImage[];
  uploading: boolean;
  /** Error code reported by the page (API or local validation); rendered with its own message. */
  errorCode: string | null;
  onAddFiles: (files: File[]) => void;
  onAddUrl: (url: string) => Promise<void>;
  onRemove: (id: string) => void;
}

/**
 * Reference image entry area: drag & drop anywhere on the block, click to pick files, or paste an
 * https URL. Thumbnails scroll horizontally so the page never grows wider than the viewport.
 */
export function ReferencePicker({
  references,
  uploading,
  errorCode,
  onAddFiles,
  onAddUrl,
  onRemove,
}: ReferencePickerProps) {
  const intl = useIntl();
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [dragging, setDragging] = useState(false);
  const [urlOpen, setUrlOpen] = useState(false);
  const [urlValue, setUrlValue] = useState('');
  const [urlBusy, setUrlBusy] = useState(false);
  const [urlErrorCode, setUrlErrorCode] = useState<string | null>(null);

  const openFileDialog = () => {
    fileInputRef.current?.click();
  };

  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragging(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (files.length > 0) {
      onAddFiles(files);
    }
  };

  const submitUrl = async () => {
    const value = urlValue.trim();
    if (value.length === 0) {
      return;
    }
    setUrlBusy(true);
    setUrlErrorCode(null);
    try {
      await onAddUrl(value);
      setUrlOpen(false);
      setUrlValue('');
    } catch (error) {
      const code = error instanceof Error && 'code' in error ? String(error.code) : 'reference_url_invalid';
      setUrlErrorCode(code);
    } finally {
      setUrlBusy(false);
    }
  };

  const dialogErrorCode = urlErrorCode ?? (urlOpen ? errorCode : null);

  return (
    <div
      className={dragging ? 'reference-picker is-dragging' : 'reference-picker'}
      data-testid="reference-dropzone"
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={handleDrop}
    >
      {references.length > 0 || uploading ? (
        <div className="reference-header">
          <span className="field-label">{intl.formatMessage({ id: 'tasks.references.label' })}</span>
          <span className="field-hint">{intl.formatMessage({ id: 'tasks.references.hint' })}</span>
        </div>
      ) : null}

      {references.length > 0 || uploading ? (
        <ul className="reference-strip" data-testid="reference-strip">
          {references.map((reference, index) => (
            <li className="reference-item" key={reference.id}>
              <img
                src={reference.thumbnailUrl}
                alt={intl.formatMessage({ id: 'tasks.references.alt' }, { index: index + 1 })}
                width={72}
                height={72}
                loading="lazy"
                decoding="async"
              />
              <button
                type="button"
                className="reference-remove"
                aria-label={intl.formatMessage({ id: 'tasks.references.remove' }, { index: index + 1 })}
                onClick={() => onRemove(reference.id)}
              >
                ×
              </button>
            </li>
          ))}
          {uploading ? (
            <li className="reference-item reference-uploading" aria-live="polite">
              <span className="spinner" aria-hidden="true" />
              <span className="reference-uploading-text">
                {intl.formatMessage({ id: 'tasks.references.uploading' })}
              </span>
            </li>
          ) : null}
        </ul>
      ) : null}

      <div className="reference-actions">
        <button
          type="button"
          className="btn btn-secondary btn-small"
          onClick={openFileDialog}
          disabled={uploading || references.length >= MAX_REFERENCE_IMAGES}
        >
          {references.length === 0
            ? intl.formatMessage({ id: 'tasks.references.add' })
            : intl.formatMessage({ id: 'tasks.references.addMore' })}
        </button>
        <button
          type="button"
          className="link-button"
          onClick={() => {
            setUrlErrorCode(null);
            setUrlOpen(true);
          }}
          disabled={uploading || references.length >= MAX_REFERENCE_IMAGES}
        >
          {intl.formatMessage({ id: 'tasks.references.fromUrl' })}
        </button>
        {references.length > 0 ? (
          <span className="field-hint">
            {intl.formatMessage({ id: 'tasks.references.count' }, { count: references.length })}
          </span>
        ) : null}
      </div>

      {errorCode && !urlOpen ? (
        <p className="field-error" role="alert">
          {intl.formatMessage({ id: errorMessageKey(errorCode) })}
        </p>
      ) : null}

      <input
        ref={fileInputRef}
        type="file"
        accept="image/png,image/jpeg,image/webp"
        multiple
        hidden
        data-testid="reference-file-input"
        onChange={(event) => {
          const files = Array.from(event.target.files ?? []);
          event.target.value = '';
          if (files.length > 0) {
            onAddFiles(files);
          }
        }}
      />

      {urlOpen ? (
        <div className="overlay" data-testid="reference-url-overlay" role="presentation">
          <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="reference-url-title">
            <h2 className="dialog-title" id="reference-url-title">
              {intl.formatMessage({ id: 'tasks.references.urlTitle' })}
            </h2>
            <p className="dialog-message">{intl.formatMessage({ id: 'tasks.references.urlHint' })}</p>
            <label className="field" htmlFor="reference-url">
              <span className="field-label">{intl.formatMessage({ id: 'tasks.references.urlLabel' })}</span>
              <input
                id="reference-url"
                className="input"
                type="url"
                value={urlValue}
                placeholder="https://…"
                autoComplete="off"
                onChange={(event) => setUrlValue(event.target.value)}
              />
            </label>
            {dialogErrorCode ? (
              <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(dialogErrorCode) })}</Notice>
            ) : null}
            <div className="dialog-actions">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => {
                  setUrlOpen(false);
                  setUrlErrorCode(null);
                }}
              >
                {intl.formatMessage({ id: 'common.cancel' })}
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => void submitUrl()}
                disabled={urlBusy || urlValue.trim().length === 0}
              >
                {urlBusy
                  ? intl.formatMessage({ id: 'tasks.references.adding' })
                  : intl.formatMessage({ id: 'tasks.references.confirm' })}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
