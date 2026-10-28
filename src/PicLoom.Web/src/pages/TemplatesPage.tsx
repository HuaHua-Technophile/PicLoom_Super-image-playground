import { useState, type FormEvent } from 'react';
import { useIntl } from 'react-intl';
import { api, errorMessageKey, toErrorCode } from '../api/client';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { PageHeader } from '../components/Shell';
import { EmptyState, ErrorState, LoadingState, Notice } from '../components/StateViews';
import { useApiList } from '../hooks/useApiList';
import { formatDateTime } from '../i18n/labels';
import { parseTags } from '../lib/templates';
import {
  TEMPLATE_BODY_MAX_LENGTH,
  TEMPLATE_NAME_MAX_LENGTH,
  type Template,
  type TemplateInput,
} from '../types';

const fetchTemplates = (signal: AbortSignal) => api.listTemplates(signal);

type FormState = { kind: 'create' } | { kind: 'edit'; template: Template };

export function TemplatesPage() {
  const intl = useIntl();
  const { items, loading, errorCode, reload, setItems } = useApiList(fetchTemplates);
  const [form, setForm] = useState<FormState | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionErrorCode, setActionErrorCode] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<Template | null>(null);
  const [deleting, setDeleting] = useState(false);

  const openCreate = () => {
    setNotice(null);
    setActionErrorCode(null);
    setForm({ kind: 'create' });
  };

  const openEdit = (template: Template) => {
    setNotice(null);
    setActionErrorCode(null);
    setForm({ kind: 'edit', template });
  };

  const handleSaved = (saved: Template, mode: 'create' | 'edit') => {
    setItems((previous) => {
      if (!previous) {
        return [saved];
      }
      return mode === 'create'
        ? [saved, ...previous.filter((item) => item.id !== saved.id)]
        : previous.map((item) => (item.id === saved.id ? saved : item));
    });
    setForm(null);
    setActionErrorCode(null);
    setNotice(
      intl.formatMessage({ id: mode === 'create' ? 'templates.created' : 'templates.updated' }),
    );
  };

  const confirmDelete = async () => {
    const target = pendingDelete;
    if (!target) {
      return;
    }
    setDeleting(true);
    try {
      await api.deleteTemplate(target.id);
      setItems((previous) => (previous ? previous.filter((item) => item.id !== target.id) : previous));
      setPendingDelete(null);
      setActionErrorCode(null);
      setNotice(intl.formatMessage({ id: 'templates.deleted' }));
    } catch (error) {
      setPendingDelete(null);
      setActionErrorCode(toErrorCode(error));
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="page">
      <PageHeader
        title={intl.formatMessage({ id: 'templates.title' })}
        subtitle={intl.formatMessage({ id: 'templates.subtitle' })}
        actions={
          <button
            type="button"
            className="btn btn-primary"
            onClick={openCreate}
            disabled={form !== null}
          >
            {intl.formatMessage({ id: 'templates.new' })}
          </button>
        }
      />

      {notice ? <Notice tone="success">{notice}</Notice> : null}
      {actionErrorCode ? (
        <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(actionErrorCode) })}</Notice>
      ) : null}

      {form ? (
        <TemplateForm
          key={form.kind === 'edit' ? form.template.id : 'create'}
          initial={form.kind === 'edit' ? form.template : undefined}
          onCancel={() => setForm(null)}
          onSaved={handleSaved}
        />
      ) : null}

      <section className="panel" aria-labelledby="templates-list-heading">
        <div className="panel-header">
          <h2 id="templates-list-heading">{intl.formatMessage({ id: 'templates.title' })}</h2>
          {items ? (
            <span className="list-count">
              {intl.formatMessage({ id: 'templates.listSummary' }, { count: items.length })}
            </span>
          ) : null}
        </div>

        {errorCode && items === null ? (
          <ErrorState
            message={intl.formatMessage({ id: errorMessageKey(errorCode) })}
            onRetry={reload}
          />
        ) : null}

        {items === null && loading ? <LoadingState /> : null}

        {errorCode && items !== null ? (
          <div className="panel-body">
            <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(errorCode) })}</Notice>
          </div>
        ) : null}

        {items !== null && items.length === 0 && !loading ? (
          <EmptyState
            title={intl.formatMessage({ id: 'templates.empty' })}
            hint={intl.formatMessage({ id: 'templates.emptyHint' })}
          />
        ) : null}

        {items !== null && items.length > 0 ? (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">{intl.formatMessage({ id: 'common.name' })}</th>
                  <th scope="col">{intl.formatMessage({ id: 'templates.revision' })}</th>
                  <th scope="col">{intl.formatMessage({ id: 'templates.tags' })}</th>
                  <th scope="col">{intl.formatMessage({ id: 'common.updatedAt' })}</th>
                  <th scope="col" className="col-actions">
                    {intl.formatMessage({ id: 'common.actions' })}
                  </th>
                </tr>
              </thead>
              <tbody>
                {items.map((template) => (
                  <tr key={template.id}>
                    <td className="cell-strong">{template.name}</td>
                    <td>
                      <span className="chip">v{template.revision}</span>
                    </td>
                    <td className="cell-tags">
                      {template.tags.length > 0 ? template.tags.join(' · ') : '—'}
                    </td>
                    <td className="cell-muted">{formatDateTime(intl, template.updatedAt)}</td>
                    <td className="col-actions">
                      <div className="row-actions">
                        <button
                          type="button"
                          className="btn btn-ghost btn-small"
                          aria-label={`${intl.formatMessage({ id: 'common.edit' })} ${template.name}`}
                          onClick={() => openEdit(template)}
                        >
                          {intl.formatMessage({ id: 'common.edit' })}
                        </button>
                        <button
                          type="button"
                          className="btn btn-ghost btn-small btn-ghost-danger"
                          aria-label={`${intl.formatMessage({ id: 'common.delete' })} ${template.name}`}
                          onClick={() => setPendingDelete(template)}
                        >
                          {intl.formatMessage({ id: 'common.delete' })}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </section>

      {pendingDelete ? (
        <ConfirmDialog
          title={intl.formatMessage({ id: 'confirm.title' })}
          message={intl.formatMessage(
            { id: 'templates.deleteConfirm' },
            { name: pendingDelete.name },
          )}
          confirmLabel={intl.formatMessage({ id: 'common.delete' })}
          busy={deleting}
          onCancel={() => setPendingDelete(null)}
          onConfirm={() => {
            void confirmDelete();
          }}
        />
      ) : null}
    </div>
  );
}

interface TemplateFormProps {
  initial?: Template;
  onCancel: () => void;
  onSaved: (template: Template, mode: 'create' | 'edit') => void;
}

function TemplateForm({ initial, onCancel, onSaved }: TemplateFormProps) {
  const intl = useIntl();
  const [name, setName] = useState(initial?.name ?? '');
  const [body, setBody] = useState(initial?.body ?? '');
  const [tags, setTags] = useState(initial?.tags.join(', ') ?? '');
  const [fieldErrorId, setFieldErrorId] = useState<string | null>(null);
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmedName = name.trim();
    const trimmedBody = body.trim();
    if (!trimmedName) {
      setFieldErrorId('templates.nameRequired');
      return;
    }
    if (!trimmedBody) {
      setFieldErrorId('templates.bodyRequired');
      return;
    }

    const payload: TemplateInput = {
      name: trimmedName,
      body: trimmedBody,
      tags: parseTags(tags),
    };

    setFieldErrorId(null);
    setErrorCode(null);
    setSaving(true);
    try {
      const saved = initial
        ? await api.updateTemplate(initial.id, payload)
        : await api.createTemplate(payload);
      onSaved(saved, initial ? 'edit' : 'create');
    } catch (error) {
      setErrorCode(toErrorCode(error));
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="panel form-panel" aria-labelledby="template-form-heading">
      <div className="panel-header">
        <h2 id="template-form-heading">
          {intl.formatMessage({ id: initial ? 'templates.editTitle' : 'templates.createTitle' })}
        </h2>
      </div>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        {errorCode ? (
          <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(errorCode) })}</Notice>
        ) : null}

        <div className="field-grid">
          <label className="field field-full" htmlFor="template-name">
            <span className="field-label">{intl.formatMessage({ id: 'templates.name' })}</span>
            <input
              id="template-name"
              name="name"
              className="input"
              type="text"
              value={name}
              maxLength={TEMPLATE_NAME_MAX_LENGTH}
              placeholder={intl.formatMessage({ id: 'templates.namePlaceholder' })}
              autoComplete="off"
              onChange={(event) => setName(event.target.value)}
            />
          </label>

          <label className="field field-full" htmlFor="template-body">
            <span className="field-label">{intl.formatMessage({ id: 'templates.body' })}</span>
            <textarea
              id="template-body"
              name="body"
              className="input textarea"
              rows={6}
              value={body}
              maxLength={TEMPLATE_BODY_MAX_LENGTH}
              placeholder={intl.formatMessage({ id: 'templates.bodyPlaceholder' })}
              onChange={(event) => setBody(event.target.value)}
            />
          </label>

          <label className="field field-full" htmlFor="template-tags">
            <span className="field-label">{intl.formatMessage({ id: 'templates.tags' })}</span>
            <input
              id="template-tags"
              name="tags"
              className="input"
              type="text"
              value={tags}
              placeholder={intl.formatMessage({ id: 'templates.tagsPlaceholder' })}
              autoComplete="off"
              onChange={(event) => setTags(event.target.value)}
            />
          </label>
          <p className="field-hint field-full">{intl.formatMessage({ id: 'templates.tagsHint' })}</p>
        </div>

        {fieldErrorId ? (
          <p className="field-error" role="alert">
            {intl.formatMessage({ id: fieldErrorId })}
          </p>
        ) : null}

        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={onCancel} disabled={saving}>
            {intl.formatMessage({ id: 'common.cancel' })}
          </button>
          <button type="submit" className="btn btn-primary" disabled={saving}>
            {saving
              ? intl.formatMessage({ id: 'common.saving' })
              : intl.formatMessage({ id: 'common.save' })}
          </button>
        </div>
      </form>
    </section>
  );
}
