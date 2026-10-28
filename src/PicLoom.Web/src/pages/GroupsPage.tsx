import { useState, type FormEvent } from 'react';
import { useIntl } from 'react-intl';
import { api, errorMessageKey, toErrorCode } from '../api/client';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { PageHeader } from '../components/Shell';
import { EmptyState, ErrorState, LoadingState, Notice } from '../components/StateViews';
import { useApiList } from '../hooks/useApiList';
import { formatDateTime, groupLabel } from '../i18n/labels';
import type { TaskGroup } from '../types';

const fetchGroups = (signal: AbortSignal) => api.listGroups(signal);

type FormState = { kind: 'create' } | { kind: 'edit'; group: TaskGroup };

export function GroupsPage() {
  const intl = useIntl();
  const { items, loading, errorCode, reload, setItems } = useApiList(fetchGroups);
  const [form, setForm] = useState<FormState | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionErrorCode, setActionErrorCode] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<TaskGroup | null>(null);
  const [deleting, setDeleting] = useState(false);

  const openCreate = () => {
    setNotice(null);
    setActionErrorCode(null);
    setForm({ kind: 'create' });
  };

  const openEdit = (group: TaskGroup) => {
    setNotice(null);
    setActionErrorCode(null);
    setForm({ kind: 'edit', group });
  };

  const handleSaved = (saved: TaskGroup, mode: 'create' | 'edit') => {
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
    setNotice(intl.formatMessage({ id: mode === 'create' ? 'groups.created' : 'groups.updated' }));
  };

  const confirmDelete = async () => {
    const target = pendingDelete;
    if (!target) {
      return;
    }
    setDeleting(true);
    try {
      await api.deleteGroup(target.id);
      setItems((previous) => (previous ? previous.filter((item) => item.id !== target.id) : previous));
      setPendingDelete(null);
      setActionErrorCode(null);
      setNotice(intl.formatMessage({ id: 'groups.deleted' }));
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
        title={intl.formatMessage({ id: 'groups.title' })}
        subtitle={intl.formatMessage({ id: 'groups.subtitle' })}
        actions={
          <button
            type="button"
            className="btn btn-primary"
            onClick={openCreate}
            disabled={form !== null}
          >
            {intl.formatMessage({ id: 'groups.new' })}
          </button>
        }
      />

      {notice ? <Notice tone="success">{notice}</Notice> : null}
      {actionErrorCode ? (
        <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(actionErrorCode) })}</Notice>
      ) : null}

      {form ? (
        <GroupForm
          key={form.kind === 'edit' ? form.group.id : 'create'}
          initial={form.kind === 'edit' ? form.group : undefined}
          onCancel={() => setForm(null)}
          onSaved={handleSaved}
        />
      ) : null}

      <section className="panel" aria-labelledby="groups-list-heading">
        <div className="panel-header">
          <h2 id="groups-list-heading">{intl.formatMessage({ id: 'groups.title' })}</h2>
          {items ? (
            <span className="list-count">
              {intl.formatMessage({ id: 'groups.listSummary' }, { count: items.length })}
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
            title={intl.formatMessage({ id: 'groups.empty' })}
            hint={intl.formatMessage({ id: 'groups.emptyHint' })}
          />
        ) : null}

        {items !== null && items.length > 0 ? (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">{intl.formatMessage({ id: 'common.name' })}</th>
                  <th scope="col">{intl.formatMessage({ id: 'common.createdAt' })}</th>
                  <th scope="col">{intl.formatMessage({ id: 'common.updatedAt' })}</th>
                  <th scope="col" className="col-actions">
                    {intl.formatMessage({ id: 'common.actions' })}
                  </th>
                </tr>
              </thead>
              <tbody>
                {items.map((group) => (
                  <tr key={group.id}>
                    <td className="cell-strong">
                      <span className="cell-strong">{groupLabel(intl, group)}</span>
                      {group.isSystem ? (
                        <span className="chip" title={intl.formatMessage({ id: 'groups.reservedHint' })}>
                          {intl.formatMessage({ id: 'groups.systemBadge' })}
                        </span>
                      ) : null}
                    </td>
                    <td className="cell-muted">{formatDateTime(intl, group.createdAt)}</td>
                    <td className="cell-muted">{formatDateTime(intl, group.updatedAt)}</td>
                    <td className="col-actions">
                      {group.isSystem ? (
                        <span className="field-hint">{intl.formatMessage({ id: 'groups.reservedHint' })}</span>
                      ) : (
                        <div className="row-actions">
                          <button
                            type="button"
                            className="btn btn-ghost btn-small"
                            aria-label={`${intl.formatMessage({ id: 'common.edit' })} ${group.name}`}
                            onClick={() => openEdit(group)}
                          >
                            {intl.formatMessage({ id: 'common.edit' })}
                          </button>
                          <button
                            type="button"
                            className="btn btn-ghost btn-small btn-ghost-danger"
                            aria-label={`${intl.formatMessage({ id: 'common.delete' })} ${group.name}`}
                            onClick={() => setPendingDelete(group)}
                          >
                            {intl.formatMessage({ id: 'common.delete' })}
                          </button>
                        </div>
                      )}
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
          message={intl.formatMessage({ id: 'groups.deleteConfirm' }, { name: pendingDelete.name })}
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

interface GroupFormProps {
  initial?: TaskGroup;
  onCancel: () => void;
  onSaved: (group: TaskGroup, mode: 'create' | 'edit') => void;
}

function GroupForm({ initial, onCancel, onSaved }: GroupFormProps) {
  const intl = useIntl();
  const [name, setName] = useState(initial?.name ?? '');
  const [fieldErrorId, setFieldErrorId] = useState<string | null>(null);
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmedName = name.trim();
    if (!trimmedName) {
      setFieldErrorId('groups.nameRequired');
      return;
    }

    setFieldErrorId(null);
    setErrorCode(null);
    setSaving(true);
    try {
      const saved = initial
        ? await api.updateGroup(initial.id, { name: trimmedName })
        : await api.createGroup({ name: trimmedName });
      onSaved(saved, initial ? 'edit' : 'create');
    } catch (error) {
      setErrorCode(toErrorCode(error));
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="panel form-panel" aria-labelledby="group-form-heading">
      <div className="panel-header">
        <h2 id="group-form-heading">
          {intl.formatMessage({ id: initial ? 'groups.editTitle' : 'groups.createTitle' })}
        </h2>
      </div>
      <form className="form" onSubmit={(event) => void handleSubmit(event)} noValidate>
        {errorCode ? (
          <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(errorCode) })}</Notice>
        ) : null}

        <div className="field-grid">
          <label className="field" htmlFor="group-name">
            <span className="field-label">{intl.formatMessage({ id: 'groups.name' })}</span>
            <input
              id="group-name"
              name="name"
              className="input"
              type="text"
              value={name}
              placeholder={intl.formatMessage({ id: 'groups.namePlaceholder' })}
              autoComplete="off"
              onChange={(event) => setName(event.target.value)}
            />
          </label>
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
