import { useEffect, useState, type FormEvent } from 'react';
import { useIntl } from 'react-intl';
import { api, errorMessageKey, toErrorCode } from '../api/client';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { PageHeader } from '../components/Shell';
import { Notice } from '../components/StateViews';
import { SUPPORTED_LOCALES, useLocale, type Locale } from '../i18n/LocaleProvider';
import type { ProviderSettings, ProviderTestOutcome, ProviderTestResult } from '../types';

interface SettingsPageProps {
  sessionReady: boolean;
}

function localeLabelId(locale: Locale): string {
  return locale === 'zh-CN' ? 'settings.language.zh' : 'settings.language.en';
}

function outcomeMessageId(outcome: ProviderTestOutcome): string {
  return `settings.provider.result.${outcome}`;
}

export function SettingsPage({ sessionReady }: SettingsPageProps) {
  const intl = useIntl();
  const { locale, setLocale } = useLocale();

  const [settings, setSettings] = useState<ProviderSettings | null>(null);
  const [loadErrorCode, setLoadErrorCode] = useState<string | null>(null);
  const [baseUrl, setBaseUrl] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [model, setModel] = useState('');
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionErrorCode, setActionErrorCode] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<ProviderTestResult | null>(null);

  useEffect(() => {
    let active = true;
    api
      .getProviderSettings()
      .then((value) => {
        if (!active) {
          return;
        }
        setSettings(value);
        setBaseUrl(value.baseUrl ?? '');
        setModel(value.model ?? '');
        setLoadErrorCode(null);
      })
      .catch((error: unknown) => {
        if (active) {
          setLoadErrorCode(toErrorCode(error));
        }
      });
    return () => {
      active = false;
    };
  }, []);

  const applyView = (value: ProviderSettings) => {
    setSettings(value);
    setBaseUrl(value.baseUrl ?? '');
    setModel(value.model ?? '');
    setApiKey('');
  };

  const handleSave = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setNotice(null);
    setActionErrorCode(null);
    setTestResult(null);
    setSaving(true);
    try {
      const saved = await api.saveProviderSettings({
        baseUrl: baseUrl.trim(),
        apiKey: apiKey.trim().length > 0 ? apiKey.trim() : null,
        model: model.trim(),
        enabled: true,
      });
      applyView(saved);
      setNotice(intl.formatMessage({ id: 'settings.provider.saved' }));
    } catch (error) {
      setActionErrorCode(toErrorCode(error));
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    setNotice(null);
    setActionErrorCode(null);
    setTestResult(null);
    setTesting(true);
    try {
      // Unsaved input wins; an empty body would fall back to the stored values.
      const result = await api.testProviderSettings({
        baseUrl: baseUrl.trim().length > 0 ? baseUrl.trim() : null,
        apiKey: apiKey.trim().length > 0 ? apiKey.trim() : null,
        model: model.trim().length > 0 ? model.trim() : null,
      });
      setTestResult(result);
    } catch (error) {
      setActionErrorCode(toErrorCode(error));
    } finally {
      setTesting(false);
    }
  };

  const handleClear = async () => {
    setClearing(true);
    setNotice(null);
    setActionErrorCode(null);
    setTestResult(null);
    try {
      await api.clearProviderSettings();
      const refreshed = await api.getProviderSettings();
      applyView(refreshed);
      setConfirmClear(false);
      setNotice(intl.formatMessage({ id: 'settings.provider.cleared' }));
    } catch (error) {
      setConfirmClear(false);
      setActionErrorCode(toErrorCode(error));
    } finally {
      setClearing(false);
    }
  };

  const configured = settings?.configured === true;
  const busy = saving || testing || clearing;

  return (
    <div className="page">
      <PageHeader
        title={intl.formatMessage({ id: 'settings.title' })}
        subtitle={intl.formatMessage({ id: 'settings.subtitle' })}
      />

      {notice ? <Notice tone="success">{notice}</Notice> : null}
      {actionErrorCode ? (
        <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(actionErrorCode) })}</Notice>
      ) : null}

      <section className="panel" aria-labelledby="settings-provider-heading">
        <div className="panel-header">
          <h2 id="settings-provider-heading">
            {intl.formatMessage({ id: 'settings.provider.title' })}
          </h2>
          <span className={`status ${configured ? 'status-ok' : 'status-neutral'}`} id="byok-status">
            {intl.formatMessage({
              id: configured ? 'settings.provider.configured' : 'settings.provider.notConfigured',
            })}
          </span>
        </div>
        <div className="panel-body">
          <p className="panel-hint">
            {intl.formatMessage({ id: 'settings.provider.description' })}
          </p>

          {loadErrorCode ? (
            <Notice tone="error">
              {intl.formatMessage({ id: 'settings.provider.loadFailed' })} —{' '}
              {intl.formatMessage({ id: errorMessageKey(loadErrorCode) })}
            </Notice>
          ) : null}

          {configured && settings?.maskedKey ? (
            <p className="field-hint" id="byok-masked-key">
              {settings.maskedKey}
            </p>
          ) : null}

          <form className="form" onSubmit={(event) => void handleSave(event)} noValidate>
            <div className="field-grid">
              <label className="field field-full" htmlFor="byok-base-url">
                <span className="field-label">
                  {intl.formatMessage({ id: 'settings.provider.baseUrl' })}
                </span>
                <input
                  id="byok-base-url"
                  name="baseUrl"
                  className="input"
                  type="url"
                  value={baseUrl}
                  placeholder={intl.formatMessage({ id: 'settings.provider.baseUrlPlaceholder' })}
                  autoComplete="off"
                  onChange={(event) => setBaseUrl(event.target.value)}
                />
              </label>

              <label className="field" htmlFor="byok-api-key">
                <span className="field-label">
                  {intl.formatMessage({ id: 'settings.provider.apiKey' })}
                </span>
                <input
                  id="byok-api-key"
                  name="apiKey"
                  className="input"
                  type="password"
                  value={apiKey}
                  placeholder={intl.formatMessage({ id: 'settings.provider.apiKeyNewPlaceholder' })}
                  autoComplete="new-password"
                  onChange={(event) => setApiKey(event.target.value)}
                />
              </label>

              <label className="field" htmlFor="byok-model">
                <span className="field-label">
                  {intl.formatMessage({ id: 'settings.provider.model' })}
                </span>
                <input
                  id="byok-model"
                  name="model"
                  className="input"
                  type="text"
                  value={model}
                  placeholder={intl.formatMessage({ id: 'settings.provider.modelPlaceholder' })}
                  autoComplete="off"
                  onChange={(event) => setModel(event.target.value)}
                />
              </label>
            </div>

            {configured ? (
              <p className="field-hint">
                {intl.formatMessage({ id: 'settings.provider.apiKeySavedHint' })}
              </p>
            ) : null}
            <p className="field-hint">{intl.formatMessage({ id: 'settings.provider.hint' })}</p>

            <div className="form-actions">
              <button type="submit" className="btn btn-primary" disabled={busy}>
                {saving
                  ? intl.formatMessage({ id: 'common.saving' })
                  : intl.formatMessage({ id: 'common.save' })}
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => void handleTest()}
                disabled={busy}
              >
                {testing
                  ? intl.formatMessage({ id: 'settings.provider.testing' })
                  : intl.formatMessage({ id: 'settings.provider.test' })}
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-ghost-danger"
                onClick={() => setConfirmClear(true)}
                disabled={busy || !configured}
              >
                {intl.formatMessage({ id: 'settings.provider.clear' })}
              </button>
            </div>
          </form>

          {testResult ? (
            <Notice tone={testResult.result === 'ok' ? 'success' : 'warning'}>
              {intl.formatMessage(
                { id: 'settings.provider.testResult' },
                { message: intl.formatMessage({ id: outcomeMessageId(testResult.result) }) },
              )}
            </Notice>
          ) : null}
        </div>
      </section>

      <section className="panel" aria-labelledby="settings-language-heading">
        <div className="panel-header">
          <h2 id="settings-language-heading">
            {intl.formatMessage({ id: 'settings.language.title' })}
          </h2>
        </div>
        <div className="panel-body">
          <p className="panel-hint">{intl.formatMessage({ id: 'settings.language.description' })}</p>
          <fieldset className="fieldset">
            <legend className="field-label">
              {intl.formatMessage({ id: 'settings.language.title' })}
            </legend>
            <div className="radio-group">
              {SUPPORTED_LOCALES.map((option) => (
                <label key={option} className="radio-row" htmlFor={`locale-${option}`}>
                  <input
                    id={`locale-${option}`}
                    type="radio"
                    name="locale"
                    value={option}
                    checked={locale === option}
                    onChange={() => setLocale(option)}
                  />
                  <span>{intl.formatMessage({ id: localeLabelId(option) })}</span>
                </label>
              ))}
            </div>
          </fieldset>
        </div>
      </section>

      <section className="panel" aria-labelledby="settings-session-heading">
        <div className="panel-header">
          <h2 id="settings-session-heading">
            {intl.formatMessage({ id: 'settings.session.title' })}
          </h2>
          <span className={`status ${sessionReady ? 'status-ok' : 'status-warn'}`}>
            {intl.formatMessage({
              id: sessionReady ? 'settings.session.ready' : 'settings.session.pending',
            })}
          </span>
        </div>
        <div className="panel-body">
          <p className="panel-hint">{intl.formatMessage({ id: 'settings.session.description' })}</p>
        </div>
      </section>

      {confirmClear ? (
        <ConfirmDialog
          title={intl.formatMessage({ id: 'confirm.title' })}
          message={intl.formatMessage({ id: 'settings.provider.clearConfirm' })}
          confirmLabel={intl.formatMessage({ id: 'settings.provider.clear' })}
          busy={clearing}
          onCancel={() => setConfirmClear(false)}
          onConfirm={() => {
            void handleClear();
          }}
        />
      ) : null}
    </div>
  );
}
