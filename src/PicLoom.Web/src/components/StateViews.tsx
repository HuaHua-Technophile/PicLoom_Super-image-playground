import type { ReactNode } from 'react';
import { useIntl } from 'react-intl';

interface LoadingStateProps {
  label?: string;
}

export function LoadingState({ label }: LoadingStateProps) {
  const intl = useIntl();
  return (
    <div className="state state-loading" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <span>{label ?? intl.formatMessage({ id: 'common.loading' })}</span>
    </div>
  );
}

interface EmptyStateProps {
  title: string;
  hint?: string;
  action?: ReactNode;
}

export function EmptyState({ title, hint, action }: EmptyStateProps) {
  return (
    <div className="state state-empty">
      <p className="state-title">{title}</p>
      {hint ? <p className="state-hint">{hint}</p> : null}
      {action ? <div className="state-action">{action}</div> : null}
    </div>
  );
}

interface ErrorStateProps {
  message: string;
  onRetry?: () => void;
  retryLabel?: string;
  title?: string;
}

export function ErrorState({ message, onRetry, retryLabel, title }: ErrorStateProps) {
  const intl = useIntl();
  return (
    <div className="state state-error" role="alert">
      <p className="state-title">{title ?? intl.formatMessage({ id: 'common.errorTitle' })}</p>
      <p className="state-hint">{message}</p>
      {onRetry ? (
        <div className="state-action">
          <button type="button" className="btn btn-secondary" onClick={onRetry}>
            {retryLabel ?? intl.formatMessage({ id: 'common.retry' })}
          </button>
        </div>
      ) : null}
    </div>
  );
}

type NoticeTone = 'info' | 'success' | 'warning' | 'error';

interface NoticeProps {
  tone?: NoticeTone;
  children: ReactNode;
}

export function Notice({ tone = 'info', children }: NoticeProps) {
  return (
    <div className={`notice notice-${tone}`} role={tone === 'error' ? 'alert' : 'status'}>
      {children}
    </div>
  );
}
