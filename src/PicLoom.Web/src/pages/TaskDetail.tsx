import { useEffect, useState } from 'react';
import { useIntl } from 'react-intl';
import { assetContentUrl, assetThumbnailUrl, errorMessageKey } from '../api/client';
import { StatusBadge } from '../components/StatusBadge';
import { EmptyState, Notice } from '../components/StateViews';
import {
  formatDateTime,
  formatModel,
  promptLabel,
  providerLabel,
  resolutionSummary,
  snapshotSize,
} from '../i18n/labels';
import { isTerminalTaskStatus, type Asset, type Task } from '../types';

interface TaskDetailProps {
  task: Task | null;
  pollErrorCode: string | null;
  refreshing: boolean;
  onRefresh: () => void;
}

export function TaskDetail({ task, pollErrorCode, refreshing, onRefresh }: TaskDetailProps) {
  const intl = useIntl();
  const [activeAssetId, setActiveAssetId] = useState<string | null>(null);

  // Keep the preview on the first image whenever a different task is selected.
  const taskId = task?.id ?? null;
  const firstAssetId = task?.assets[0]?.id ?? null;
  useEffect(() => {
    setActiveAssetId(firstAssetId);
  }, [taskId, firstAssetId]);

  const activeAsset =
    task?.assets.find((asset) => asset.id === activeAssetId) ?? task?.assets[0] ?? null;

  return (
    <section className="panel detail-panel" aria-labelledby="task-detail-heading">
      <div className="panel-header">
        <h2 id="task-detail-heading">{intl.formatMessage({ id: 'tasks.detailTitle' })}</h2>
        {task ? <StatusBadge status={task.status} /> : null}
      </div>

      {!task ? (
        <EmptyState title={intl.formatMessage({ id: 'tasks.detailEmpty' })} />
      ) : (
        <div className="panel-body detail-body">
          <dl className="detail-list">
            <div className="detail-row">
              <dt>{intl.formatMessage({ id: 'tasks.taskId' })}</dt>
              <dd className="mono">{task.id}</dd>
            </div>
            <div className="detail-row">
              <dt>{intl.formatMessage({ id: 'tasks.provider' })}</dt>
              <dd>{providerLabel(intl, task.provider)}</dd>
            </div>
            <div className="detail-row">
              <dt>{intl.formatMessage({ id: 'tasks.model' })}</dt>
              <dd>{formatModel(intl, task.model)}</dd>
            </div>
            <div className="detail-row">
              <dt>{intl.formatMessage({ id: 'tasks.size' })}</dt>
              <dd>{snapshotSize(intl, task.resolutionSnapshot)}</dd>
            </div>
            <div className="detail-row">
              <dt>{intl.formatMessage({ id: 'common.createdAt' })}</dt>
              <dd>{formatDateTime(intl, task.createdAt)}</dd>
            </div>
            <div className="detail-row">
              <dt>{intl.formatMessage({ id: 'common.updatedAt' })}</dt>
              <dd>{formatDateTime(intl, task.updatedAt)}</dd>
            </div>
            <div className="detail-row">
              <dt>{intl.formatMessage({ id: 'common.completedAt' })}</dt>
              <dd>{formatDateTime(intl, task.completedAt)}</dd>
            </div>
          </dl>

          {/*
            Per the error layering contract the badge only reads `status`; the reason line only reads
            `errorCode`. Unknown must never be rendered as a plain failure.
          */}
          {task.errorCode ? (
            <Notice tone={task.status === 'Unknown' ? 'warning' : 'error'}>
              {intl.formatMessage({ id: 'tasks.errorCode' }, { code: task.errorCode })}
              <br />
              {intl.formatMessage(
                { id: 'tasks.reason' },
                { message: intl.formatMessage({ id: errorMessageKey(task.errorCode) }) },
              )}
            </Notice>
          ) : null}

          {!isTerminalTaskStatus(task.status) && !pollErrorCode ? (
            <p className="polling" role="status" aria-live="polite">
              <span className="spinner spinner-small" aria-hidden="true" />
              {intl.formatMessage({ id: 'tasks.polling' })}
            </p>
          ) : null}

          {pollErrorCode ? (
            <Notice tone="error">
              {intl.formatMessage(
                { id: 'tasks.pollError' },
                { message: intl.formatMessage({ id: errorMessageKey(pollErrorCode) }) },
              )}
              <button
                type="button"
                className="btn btn-secondary btn-small notice-action"
                onClick={onRefresh}
                disabled={refreshing}
              >
                {intl.formatMessage({ id: 'tasks.pollRetry' })}
              </button>
            </Notice>
          ) : null}

          <div className="snapshot-grid">
            <div className="snapshot-block">
              <h3>{intl.formatMessage({ id: 'tasks.snapshot.prompt' })}</h3>
              <p className="snapshot-name">{promptLabel(intl, task.promptSnapshot)}</p>
              <p className="snapshot-meta">
                {task.promptSnapshot.source === 'inline'
                  ? intl.formatMessage({ id: 'tasks.snapshot.inlineHint' })
                  : intl.formatMessage(
                      { id: 'tasks.snapshot.revision' },
                      { revision: task.promptSnapshot.revision },
                    )}
              </p>
              <pre className="prompt-body">{task.promptSnapshot.body}</pre>
            </div>
            <div className="snapshot-block">
              <h3>{intl.formatMessage({ id: 'tasks.snapshot.resolution' })}</h3>
              <p className="snapshot-name">{snapshotSize(intl, task.resolutionSnapshot)}</p>
              <p className="snapshot-meta">
                {resolutionSummary(intl, task.resolutionSnapshot)} ·{' '}
                {intl.formatMessage({ id: `resolutions.mode.${task.resolutionSnapshot.mode}` })}
              </p>
            </div>
          </div>

          {task.options ? (
            <div className="param-panel">
              <h3>{intl.formatMessage({ id: 'tasks.detail.params' })}</h3>
              <dl className="param-list">
                <div className="param-item">
                  <dt>{intl.formatMessage({ id: 'tasks.params.quality' })}</dt>
                  <dd>
                    {task.options.quality
                      ? intl.formatMessage({ id: `tasks.params.quality.${task.options.quality}` })
                      : intl.formatMessage({ id: 'tasks.params.auto' })}
                  </dd>
                </div>
                <div className="param-item">
                  <dt>{intl.formatMessage({ id: 'tasks.params.count' })}</dt>
                  <dd>{task.options.count}</dd>
                </div>
                <div className="param-item">
                  <dt>{intl.formatMessage({ id: 'tasks.params.format' })}</dt>
                  <dd>
                    {task.options.format
                      ? intl.formatMessage({ id: `tasks.params.format.${task.options.format}` })
                      : intl.formatMessage({ id: 'tasks.params.default' })}
                  </dd>
                </div>
                <div className="param-item">
                  <dt>{intl.formatMessage({ id: 'tasks.params.transparent' })}</dt>
                  <dd>
                    {task.options.transparent
                      ? intl.formatMessage({ id: 'tasks.params.on' })
                      : intl.formatMessage({ id: 'tasks.params.off' })}
                  </dd>
                </div>
                <div className="param-item">
                  <dt>{intl.formatMessage({ id: 'tasks.params.moderation' })}</dt>
                  <dd>
                    {task.options.moderation
                      ? intl.formatMessage({ id: 'tasks.params.moderation.low' })
                      : intl.formatMessage({ id: 'tasks.params.auto' })}
                  </dd>
                </div>
              </dl>
              {task.actualCount !== null && task.actualCount !== task.options.count ? (
                <p className="field-hint">
                  {intl.formatMessage({ id: 'tasks.actualCount.requested' }, { count: task.options.count })}
                  {' · '}
                  {intl.formatMessage({ id: 'tasks.actualCount.actual' }, { count: task.actualCount })}
                </p>
              ) : null}
            </div>
          ) : null}

          {task.status === 'Unknown' ? (
            <Notice tone="warning">
              {intl.formatMessage({ id: 'tasks.unknownResult' })}{' '}
              {intl.formatMessage({ id: 'tasks.noAutoResend' })}
            </Notice>
          ) : null}

          <div className="assets-section">
            <h3>{intl.formatMessage({ id: 'tasks.assets' })}</h3>
            {task.assets.length === 0 ? (
              <p className="muted">{intl.formatMessage({ id: 'tasks.assets.empty' })}</p>
            ) : (
              <AssetViewer
                assets={task.assets}
                activeAsset={activeAsset}
                onSelect={(asset) => setActiveAssetId(asset.id)}
              />
            )}
          </div>

          <div className="references-section">
            <h3>{intl.formatMessage({ id: 'tasks.detail.references' })}</h3>
            {task.references.length === 0 ? (
              <p className="muted">{intl.formatMessage({ id: 'tasks.detail.noReferences' })}</p>
            ) : (
              <ul className="reference-strip readonly">
                {task.references.map((reference, index) => (
                  <li className="reference-item" key={reference.id}>
                    <img
                      src={reference.thumbnailUrl}
                      alt={intl.formatMessage({ id: 'tasks.referenceAlt' }, { index: index + 1 })}
                      width={72}
                      height={72}
                      loading="lazy"
                      decoding="async"
                    />
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

interface AssetViewerProps {
  assets: Asset[];
  activeAsset: Asset | null;
  onSelect: (asset: Asset) => void;
}

/** Renders a single asset list for one image, and a switcher for multi-image batches. */
function AssetViewer({ assets, activeAsset, onSelect }: AssetViewerProps) {
  const intl = useIntl();

  if (assets.length === 1) {
    const asset = assets[0];
    return (
      <ul className="asset-grid">
        <li className="asset-card">
          <a
            className="asset-thumb"
            href={assetContentUrl(asset.id)}
            target="_blank"
            rel="noreferrer noopener"
          >
            <img
              src={assetThumbnailUrl(asset.id)}
              alt={intl.formatMessage({ id: 'tasks.assetAlt' }, { index: 1 })}
              width={256}
              height={256}
              loading="lazy"
              decoding="async"
            />
          </a>
          <div className="asset-meta">
            <span className="cell-mono">
              {intl.formatMessage(
                { id: 'tasks.assetMeta' },
                { width: asset.width, height: asset.height, mimeType: asset.mimeType },
              )}
            </span>
            <a
              className="asset-link"
              href={assetContentUrl(asset.id)}
              target="_blank"
              rel="noreferrer noopener"
            >
              {intl.formatMessage({ id: 'tasks.openAsset' })}
            </a>
          </div>
        </li>
      </ul>
    );
  }

  if (!activeAsset) {
    return null;
  }

  return (
    <div className="asset-viewer">
      <a
        className="asset-thumb asset-main"
        href={assetContentUrl(activeAsset.id)}
        target="_blank"
        rel="noreferrer noopener"
      >
        <img
          src={assetThumbnailUrl(activeAsset.id)}
          alt={intl.formatMessage({ id: 'tasks.assetAlt' }, { index: 1 })}
          width={256}
          height={256}
          loading="lazy"
          decoding="async"
        />
      </a>
      <div className="asset-strip" role="group" aria-label={intl.formatMessage({ id: 'tasks.assetStrip' })}>
        {assets.map((asset, index) => (
          <button
            key={asset.id}
            type="button"
            className={asset.id === activeAsset.id ? 'asset-strip-item is-active' : 'asset-strip-item'}
            aria-pressed={asset.id === activeAsset.id}
            aria-label={intl.formatMessage({ id: 'tasks.assetSelect' }, { index: index + 1 })}
            data-testid="asset-item"
            onClick={() => onSelect(asset)}
          >
            <img
              src={assetThumbnailUrl(asset.id)}
              alt=""
              width={64}
              height={64}
              loading="lazy"
              decoding="async"
            />
          </button>
        ))}
      </div>
      <div className="asset-meta asset-meta-inline">
        <span className="cell-mono">
          {intl.formatMessage(
            { id: 'tasks.assetMeta' },
            { width: activeAsset.width, height: activeAsset.height, mimeType: activeAsset.mimeType },
          )}
        </span>
        <a
          className="asset-link"
          href={assetContentUrl(activeAsset.id)}
          target="_blank"
          rel="noreferrer noopener"
        >
          {intl.formatMessage({ id: 'tasks.openAsset' })}
        </a>
      </div>
    </div>
  );
}
