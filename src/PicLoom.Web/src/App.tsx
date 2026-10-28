import { useCallback, useEffect, useState } from 'react';
import { useIntl } from 'react-intl';
import { ensureSession, errorMessageKey, toErrorCode } from './api/client';
import { Shell, type PageKey } from './components/Shell';
import { ErrorState, LoadingState } from './components/StateViews';
import { LocaleProvider } from './i18n/LocaleProvider';
import { GroupsPage } from './pages/GroupsPage';
import { SettingsPage } from './pages/SettingsPage';
import { TasksPage } from './pages/TasksPage';
import { TemplatesPage } from './pages/TemplatesPage';

type SessionPhase = 'connecting' | 'ready' | 'unauthorized' | 'error';

export default function App() {
  return (
    <LocaleProvider>
      <SessionGate />
    </LocaleProvider>
  );
}

function SessionGate() {
  const intl = useIntl();
  const [phase, setPhase] = useState<SessionPhase>('connecting');
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const connect = useCallback(() => {
    setPhase('connecting');
    setErrorCode(null);
    ensureSession()
      .then(() => {
        setPhase('ready');
      })
      .catch((error: unknown) => {
        const code = toErrorCode(error);
        setErrorCode(code);
        if (code === 'unauthorized' || code === 'invalid_bootstrap') {
          setPhase('unauthorized');
        } else {
          setPhase('error');
        }
      });
  }, []);

  useEffect(() => {
    connect();
  }, [connect]);

  if (phase === 'connecting') {
    return (
      <div className="fullscreen-state">
        <div className="auth-card">
          <BrandLockup />
          <LoadingState label={intl.formatMessage({ id: 'session.checking' })} />
        </div>
      </div>
    );
  }

  if (phase === 'unauthorized') {
    return (
      <div className="fullscreen-state">
        <div className="auth-card">
          <BrandLockup />
          <h1 className="auth-title">{intl.formatMessage({ id: 'session.unauthorized.title' })}</h1>
          <p className="launch-instruction" lang="zh-CN">
            {intl.formatMessage({ id: 'session.unauthorized.launchZh' })}
          </p>
          <p className="launch-instruction" lang="en">
            {intl.formatMessage({ id: 'session.unauthorized.launchEn' })}
          </p>
          <p className="auth-hint">{intl.formatMessage({ id: 'session.unauthorized.hint' })}</p>
          {errorCode ? (
            <p className="auth-code">{intl.formatMessage({ id: errorMessageKey(errorCode) })}</p>
          ) : null}
          <button type="button" className="btn btn-primary" onClick={connect}>
            {intl.formatMessage({ id: 'session.retry' })}
          </button>
        </div>
      </div>
    );
  }

  if (phase === 'error') {
    return (
      <div className="fullscreen-state">
        <div className="auth-card auth-card-wide">
          <BrandLockup />
          <ErrorState
            title={intl.formatMessage({ id: 'session.error.title' })}
            message={intl.formatMessage({ id: errorMessageKey(errorCode) })}
            onRetry={connect}
          />
        </div>
      </div>
    );
  }

  return <Workbench />;
}

function Workbench() {
  const [page, setPage] = useState<PageKey>('tasks');

  return (
    <Shell active={page} onNavigate={setPage}>
      {page === 'tasks' ? <TasksPage onNavigate={setPage} /> : null}
      {page === 'templates' ? <TemplatesPage /> : null}
      {page === 'groups' ? <GroupsPage /> : null}
      {page === 'settings' ? <SettingsPage sessionReady /> : null}
    </Shell>
  );
}

function BrandLockup() {
  const intl = useIntl();
  return (
    <div className="brand-lockup">
      <span className="brand-mark brand-mark-large" aria-hidden="true">
        P
      </span>
      <div className="brand-text">
        <span className="brand-name">{intl.formatMessage({ id: 'app.name' })}</span>
        <span className="brand-tagline">{intl.formatMessage({ id: 'app.tagline' })}</span>
      </div>
    </div>
  );
}
