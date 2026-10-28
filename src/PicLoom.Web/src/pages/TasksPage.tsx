import { useEffect, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useIntl } from 'react-intl';
import { api, errorMessageKey, toErrorCode } from '../api/client';
import { PageHeader, type PageKey } from '../components/Shell';
import { ReferencePicker } from '../components/ReferencePicker';
import { SegmentedControl } from '../components/SegmentedControl';
import { SizePickerModal } from '../components/SizePickerModal';
import { EmptyState, ErrorState, LoadingState, Notice } from '../components/StateViews';
import { StatusBadge } from '../components/StatusBadge';
import { useApiList } from '../hooks/useApiList';
import {
  formatDateTime,
  groupLabel,
  promptPreview,
  providerLabel,
  snapshotSize,
} from '../i18n/labels';
import {
  DEFAULT_SIZE_SELECTION,
  isSelectionResolved,
  selectionPixels,
  toResolutionTarget,
  type SizeSelection,
} from '../lib/sizeSelection';
import {
  CUSTOM_MODEL_VALUE,
  DEFAULT_REAL_MODEL,
  GENERATION_COUNTS,
  GENERATION_FORMATS,
  GENERATION_QUALITIES,
  GPT_IMAGE_MODELS,
  MAX_REFERENCE_BYTES,
  MAX_REFERENCE_IMAGES,
  MODEL_MAX_LENGTH,
  OPENAI_PROVIDER,
  PROMPT_MAX_LENGTH,
  REFERENCE_MIME_TYPES,
  isTerminalTaskStatus,
  type GenerationFormat,
  type GenerationModeration,
  type GenerationOptionsInput,
  type GenerationQuality,
  type ProviderSettings,
  type ReferenceImage,
  type Task,
  type TaskCreateInput,
} from '../types';
import { TaskDetail } from './TaskDetail';

const POLL_INTERVAL_MS = 1500;
const FILTER_ALL = '__all__';

const fetchGroups = (signal: AbortSignal) => api.listGroups(signal);

interface TasksPageProps {
  onNavigate?: (page: PageKey) => void;
}

export function TasksPage({ onNavigate }: TasksPageProps) {
  const intl = useIntl();
  const groups = useApiList(fetchGroups);

  const [prompt, setPrompt] = useState('');
  const [sizeSelection, setSizeSelection] = useState<SizeSelection>(DEFAULT_SIZE_SELECTION);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [modelChoice, setModelChoice] = useState<string | null>(null);
  const [customModel, setCustomModel] = useState('');
  const [groupId, setGroupId] = useState<string | null>(null);
  const [filterGroupId, setFilterGroupId] = useState<string>(FILTER_ALL);
  const [settings, setSettings] = useState<ProviderSettings | null>(null);

  // Image-to-image inputs and the five generation parameters.
  const [references, setReferences] = useState<ReferenceImage[]>([]);
  const [referenceUploading, setReferenceUploading] = useState(false);
  const [referenceErrorCode, setReferenceErrorCode] = useState<string | null>(null);
  const [quality, setQuality] = useState<GenerationQuality | null>(null);
  const [imageCount, setImageCount] = useState<number>(1);
  const [outputFormat, setOutputFormat] = useState<GenerationFormat | null>(null);
  const [transparent, setTransparent] = useState(false);
  const [moderation, setModeration] = useState<GenerationModeration | null>(null);

  const [submitting, setSubmitting] = useState(false);
  const [fieldErrorId, setFieldErrorId] = useState<string | null>(null);
  const [formErrorCode, setFormErrorCode] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [tasks, setTasks] = useState<Task[] | null>(null);
  const [tasksLoading, setTasksLoading] = useState(false);
  const [tasksErrorCode, setTasksErrorCode] = useState<string | null>(null);
  const [listVersion, setListVersion] = useState(0);

  const [selected, setSelected] = useState<Task | null>(null);
  const [pollErrorCode, setPollErrorCode] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const groupList = groups.items;
  const keyConfigured = settings?.configured === true && settings.enabled === true;

  // There is exactly one provider; the model name is free text taken from the relay docs.
  const provider = OPENAI_PROVIDER;
  const effectiveModelChoice = modelChoice ?? DEFAULT_REAL_MODEL;
  const model = effectiveModelChoice === CUSTOM_MODEL_VALUE ? customModel.trim() : effectiveModelChoice;
  const notConfigured = !keyConfigured;

  const effectiveGroupId = groupId ?? groupList?.[0]?.id ?? '';
  const activeFilterGroup =
    filterGroupId === FILTER_ALL ? null : (groupList ?? []).find((item) => item.id === filterGroupId) ?? null;

  // The read-only field only ever shows what the next task would dispatch.
  const sizePixels = selectionPixels(sizeSelection);
  const sizeLabel = sizePixels
    ? intl.formatMessage({ id: 'tasks.sizeLabel' }, { width: sizePixels.width, height: sizePixels.height })
    : intl.formatMessage({ id: 'size.autoValue' });

  const promptTooLong = prompt.length > PROMPT_MAX_LENGTH;
  const modelMissing = model.length === 0 || model.length > MODEL_MAX_LENGTH;
  const sizeProblem = !isSelectionResolved(sizeSelection);
  const submitDisabled = submitting || promptTooLong || modelMissing || notConfigured || sizeProblem;

  useEffect(() => {
    let active = true;
    api
      .getProviderSettings()
      .then((value) => {
        if (active) {
          setSettings(value);
        }
      })
      .catch(() => {
        if (active) {
          setSettings(null);
        }
      });
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setTasksLoading(true);
    setTasksErrorCode(null);
    setTasks(null);
    setSelected(null);
    setPollErrorCode(null);

    api
      .listTasks(filterGroupId === FILTER_ALL ? undefined : filterGroupId, controller.signal)
      .then((list) => {
        if (active) {
          setTasks(Array.isArray(list) ? list : []);
        }
      })
      .catch((error: unknown) => {
        if (active && !controller.signal.aborted) {
          setTasksErrorCode(toErrorCode(error));
        }
      })
      .finally(() => {
        if (active) {
          setTasksLoading(false);
        }
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [filterGroupId, listVersion]);

  useEffect(() => {
    if (!selected || isTerminalTaskStatus(selected.status) || pollErrorCode) {
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(() => {
      api
        .getTask(selected.id)
        .then((task) => {
          if (cancelled) {
            return;
          }
          setSelected(task);
          setTasks((previous) =>
            previous ? previous.map((item) => (item.id === task.id ? task : item)) : previous,
          );
        })
        .catch((error: unknown) => {
          if (!cancelled) {
            setPollErrorCode(toErrorCode(error));
          }
        });
    }, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [selected, pollErrorCode]);

  const refreshSelected = async () => {
    if (!selected) {
      return;
    }
    setRefreshing(true);
    setPollErrorCode(null);
    try {
      const task = await api.getTask(selected.id);
      setSelected(task);
      setTasks((previous) =>
        previous ? previous.map((item) => (item.id === task.id ? task : item)) : previous,
      );
    } catch (error) {
      setPollErrorCode(toErrorCode(error));
    } finally {
      setRefreshing(false);
    }
  };

  const addReferenceFiles = async (files: File[]) => {
    setReferenceErrorCode(null);
    const remaining = MAX_REFERENCE_IMAGES - references.length;
    if (remaining <= 0) {
      setReferenceErrorCode('reference_too_many');
      return;
    }

    const accepted = files.slice(0, remaining);
    if (files.length > remaining) {
      setReferenceErrorCode('reference_too_many');
    }

    for (const file of accepted) {
      if (!REFERENCE_MIME_TYPES.includes(file.type as (typeof REFERENCE_MIME_TYPES)[number])) {
        setReferenceErrorCode('reference_type_invalid');
        continue;
      }
      if (file.size > MAX_REFERENCE_BYTES) {
        setReferenceErrorCode('reference_too_large');
        continue;
      }

      setReferenceUploading(true);
      try {
        const uploaded = await api.uploadReference(file);
        setReferences((previous) => [...previous, uploaded]);
      } catch (error) {
        setReferenceErrorCode(toErrorCode(error));
      } finally {
        setReferenceUploading(false);
      }
    }
  };

  const addReferenceUrl = async (url: string) => {
    if (references.length >= MAX_REFERENCE_IMAGES) {
      setReferenceErrorCode('reference_too_many');
      throw new Error('reference_too_many');
    }
    setReferenceErrorCode(null);
    setReferenceUploading(true);
    try {
      const uploaded = await api.addReferenceFromUrl(url);
      setReferences((previous) => [...previous, uploaded]);
    } finally {
      setReferenceUploading(false);
    }
  };

  const removeReference = async (id: string) => {
    setReferenceErrorCode(null);
    try {
      await api.deleteReference(id);
      setReferences((previous) => previous.filter((reference) => reference.id !== id));
    } catch (error) {
      setReferenceErrorCode(toErrorCode(error));
    }
  };

  const changeFormat = (value: GenerationFormat | null) => {
    setOutputFormat(value);
    // JPEG has no alpha channel upstream, so transparency is refused locally as well.
    if (value === 'jpeg') {
      setTransparent(false);
    }
  };

  const buildOptions = (): GenerationOptionsInput | undefined => {
    const options: GenerationOptionsInput = {};
    if (quality !== null) options.quality = quality;
    if (imageCount !== 1) options.count = imageCount;
    if (outputFormat !== null) options.format = outputFormat;
    if (transparent) options.transparent = true;
    if (moderation !== null) options.moderation = moderation;
    return Object.keys(options).length === 0 ? undefined : options;
  };

  const submit = async () => {
    if (submitDisabled) {
      return;
    }
    setFormErrorCode(null);

    if (prompt.trim().length === 0) {
      setFieldErrorId('tasks.promptRequired');
      return;
    }
    if (!isSelectionResolved(sizeSelection)) {
      setFieldErrorId('errors.resolution_invalid');
      return;
    }

    const input: TaskCreateInput = {
      prompt: prompt.trim(),
      resolution: toResolutionTarget(sizeSelection),
      provider,
      model,
    };
    if (effectiveGroupId) {
      input.groupId = effectiveGroupId;
    }
    if (references.length > 0) {
      input.referenceIds = references.map((reference) => reference.id);
    }
    const options = buildOptions();
    if (options) {
      input.options = options;
    }

    setFieldErrorId(null);
    setSubmitting(true);
    try {
      const task = await api.createTask(input);
      const visible = filterGroupId === FILTER_ALL || filterGroupId === task.groupId;
      if (visible) {
        setTasks((previous) => [task, ...(previous ?? []).filter((item) => item.id !== task.id)]);
      } else {
        setListVersion((version) => version + 1);
      }
      setSelected(task);
      setPollErrorCode(null);
      setNotice(intl.formatMessage({ id: 'tasks.created' }));
      // Bound reference images cannot be reused; clear the picker for the next task.
      setReferences([]);
      setReferenceErrorCode(null);
    } catch (error) {
      setFormErrorCode(toErrorCode(error));
    } finally {
      setSubmitting(false);
    }
  };

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    void submit();
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLFormElement>) => {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      void submit();
    }
  };

  const goTo = (page: PageKey) => {
    if (onNavigate) {
      onNavigate(page);
    }
  };

  return (
    <div className="page">
      <PageHeader
        title={intl.formatMessage({ id: 'tasks.title' })}
        subtitle={intl.formatMessage({ id: 'tasks.subtitle' })}
      />

      {notice ? <Notice tone="success">{notice}</Notice> : null}

      {settings !== null && !keyConfigured ? (
        <Notice tone="warning">
          {intl.formatMessage({ id: 'tasks.keyMissing' })}{' '}
          <button type="button" className="link-button" onClick={() => goTo('settings')}>
            {intl.formatMessage({ id: 'tasks.keyMissingGoSettings' })}
          </button>
        </Notice>
      ) : null}

      <section className="panel form-panel" aria-labelledby="task-form-heading">
        <div className="panel-header">
          <h2 id="task-form-heading">{intl.formatMessage({ id: 'tasks.formTitle' })}</h2>
        </div>

        {groups.errorCode ? (
          <ErrorState
            message={intl.formatMessage({ id: errorMessageKey(groups.errorCode) })}
            onRetry={groups.reload}
          />
        ) : null}

        {groups.items === null && groups.loading ? <LoadingState /> : null}

        <div className="panel-body">
          <p className="panel-hint">{intl.formatMessage({ id: 'tasks.formHint' })}</p>

          {formErrorCode ? (
            <Notice tone="error">{intl.formatMessage({ id: errorMessageKey(formErrorCode) })}</Notice>
          ) : null}

          <form
            className="form"
            onSubmit={handleSubmit}
            onKeyDown={handleKeyDown}
            noValidate
          >
            <div className="field field-full">
              <label className="field-label" htmlFor="task-prompt">
                {intl.formatMessage({ id: 'tasks.prompt' })}
              </label>
              <textarea
                id="task-prompt"
                name="prompt"
                className="input textarea"
                rows={4}
                value={prompt}
                placeholder={intl.formatMessage({ id: 'tasks.promptPlaceholder' })}
                onChange={(event) => setPrompt(event.target.value)}
              />
              <p className={promptTooLong ? 'field-hint field-error' : 'field-hint'}>
                {intl.formatMessage({ id: 'tasks.promptCount' }, { count: prompt.length })}
              </p>
              {promptTooLong ? (
                <p className="field-error" role="alert">
                  {intl.formatMessage({ id: 'tasks.promptTooLong' })}
                </p>
              ) : null}
            </div>

            <div className="field field-full">
              <ReferencePicker
                references={references}
                uploading={referenceUploading}
                errorCode={referenceErrorCode}
                onAddFiles={(files) => {
                  void addReferenceFiles(files);
                }}
                onAddUrl={addReferenceUrl}
                onRemove={(id) => {
                  void removeReference(id);
                }}
              />
            </div>

            <div className="field">
              <span className="field-label" id="task-size-label">
                {intl.formatMessage({ id: 'size.fieldLabel' })}
              </span>
              <div className="size-readout" role="group" aria-labelledby="task-size-label">
                <output className="size-readout-value" id="task-size-value">
                  {sizeLabel}
                </output>
                <button
                  type="button"
                  className="btn btn-secondary btn-small"
                  onClick={() => {
                    setFieldErrorId(null);
                    setPickerOpen(true);
                  }}
                >
                  {intl.formatMessage({ id: 'size.change' })}
                </button>
              </div>
              <p className="field-hint" id="task-actual-size">
                {sizePixels
                  ? intl.formatMessage(
                      { id: 'tasks.actualSize' },
                      { width: sizePixels.width, height: sizePixels.height },
                    )
                  : intl.formatMessage({ id: 'tasks.actualSizeAuto' })}
              </p>
            </div>

            <div className="param-row">
              <div className="field">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.params.quality' })}</span>
                <SegmentedControl
                  id="task-quality"
                  name="quality"
                  label={intl.formatMessage({ id: 'tasks.params.quality' })}
                  value={quality ?? 'auto'}
                  options={[
                    { value: 'auto', label: intl.formatMessage({ id: 'tasks.params.auto' }) },
                    ...GENERATION_QUALITIES.map((value) => ({
                      value,
                      label: intl.formatMessage({ id: `tasks.params.quality.${value}` }),
                    })),
                  ]}
                  onChange={(value) => setQuality(value === 'auto' ? null : (value as GenerationQuality))}
                />
              </div>

              <div className="field">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.params.count' })}</span>
                <SegmentedControl
                  id="task-count"
                  name="count"
                  label={intl.formatMessage({ id: 'tasks.params.count' })}
                  value={String(imageCount)}
                  options={GENERATION_COUNTS.map((value) => ({ value: String(value), label: String(value) }))}
                  onChange={(value) => setImageCount(Number(value))}
                />
              </div>

              <div className="field">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.params.format' })}</span>
                <SegmentedControl
                  id="task-format"
                  name="format"
                  label={intl.formatMessage({ id: 'tasks.params.format' })}
                  value={outputFormat ?? 'default'}
                  options={[
                    { value: 'default', label: intl.formatMessage({ id: 'tasks.params.default' }) },
                    ...GENERATION_FORMATS.map((value) => ({
                      value,
                      label: intl.formatMessage({ id: `tasks.params.format.${value}` }),
                    })),
                  ]}
                  onChange={(value) => changeFormat(value === 'default' ? null : (value as GenerationFormat))}
                />
              </div>

              <div className="field">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.params.transparent' })}</span>
                <label className="switch-row" htmlFor="task-transparent">
                  <input
                    id="task-transparent"
                    name="transparent"
                    type="checkbox"
                    checked={transparent}
                    disabled={outputFormat === 'jpeg'}
                    onChange={(event) => setTransparent(event.target.checked)}
                  />
                  <span>
                    {transparent
                      ? intl.formatMessage({ id: 'tasks.params.on' })
                      : intl.formatMessage({ id: 'tasks.params.off' })}
                  </span>
                </label>
                {outputFormat === 'jpeg' ? (
                  <p className="field-hint">{intl.formatMessage({ id: 'tasks.params.jpegNoAlpha' })}</p>
                ) : null}
              </div>

              <div className="field">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.params.moderation' })}</span>
                <SegmentedControl
                  id="task-moderation"
                  name="moderation"
                  label={intl.formatMessage({ id: 'tasks.params.moderation' })}
                  value={moderation ?? 'auto'}
                  options={[
                    { value: 'auto', label: intl.formatMessage({ id: 'tasks.params.auto' }) },
                    { value: 'low', label: intl.formatMessage({ id: 'tasks.params.moderation.low' }) },
                  ]}
                  onChange={(value) => setModeration(value === 'auto' ? null : (value as GenerationModeration))}
                />
              </div>
            </div>

            <div className="field-grid">
              <label className="field" htmlFor="task-model">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.model' })}</span>
                <select
                  id="task-model"
                  name="model"
                  className="input select"
                  value={effectiveModelChoice}
                  onChange={(event) => {
                    setModelChoice(event.target.value);
                    setFieldErrorId(null);
                  }}
                >
                  {GPT_IMAGE_MODELS.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                  <option value={CUSTOM_MODEL_VALUE}>
                    {intl.formatMessage({ id: 'tasks.model.customOption' })}
                  </option>
                </select>
              </label>
              <p className="field-hint">{providerLabel(intl, provider)}</p>

              {effectiveModelChoice === CUSTOM_MODEL_VALUE ? (
                <label className="field" htmlFor="task-custom-model">
                  <span className="field-label">{intl.formatMessage({ id: 'tasks.model.customLabel' })}</span>
                  <input
                    id="task-custom-model"
                    name="customModel"
                    className="input"
                    type="text"
                    value={customModel}
                    maxLength={MODEL_MAX_LENGTH}
                    autoComplete="off"
                    onChange={(event) => setCustomModel(event.target.value)}
                  />
                </label>
              ) : null}

              <label className="field" htmlFor="task-group">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.group' })}</span>
                <select
                  id="task-group"
                  name="groupId"
                  className="input select"
                  value={effectiveGroupId}
                  onChange={(event) => setGroupId(event.target.value)}
                >
                  {(groupList ?? []).map((group) => (
                    <option key={group.id} value={group.id}>
                      {groupLabel(intl, group)}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            <p className="field-hint">{intl.formatMessage({ id: 'tasks.groupHint' })}</p>
            <p className="field-hint">{intl.formatMessage({ id: 'tasks.modelHint' })}</p>

            {notConfigured ? (
              <Notice tone="warning">{intl.formatMessage({ id: 'tasks.realModelNeedsKey' })}</Notice>
            ) : null}

            {fieldErrorId ? (
              <p className="field-error" role="alert">
                {intl.formatMessage({ id: fieldErrorId })}
              </p>
            ) : null}

            <div className="form-actions">
              <button type="submit" className="btn btn-primary" disabled={submitDisabled}>
                {submitting
                  ? intl.formatMessage({ id: 'tasks.submitting' })
                  : intl.formatMessage({ id: 'tasks.submit' })}
              </button>
              <span className="field-hint">{intl.formatMessage({ id: 'tasks.submitHint' })}</span>
            </div>
          </form>
        </div>
      </section>

      <div className="task-workbench">
        <section className="panel" aria-labelledby="task-list-heading">
          <div className="panel-header">
            <h2 id="task-list-heading">
              {activeFilterGroup
                ? intl.formatMessage({ id: 'tasks.listTitle' }, { group: groupLabel(intl, activeFilterGroup) })
                : intl.formatMessage({ id: 'tasks.listTitleAll' })}
            </h2>
            <div className="panel-tools">
              {tasks ? (
                <span className="list-count">
                  {intl.formatMessage({ id: 'tasks.listSummary' }, { count: tasks.length })}
                </span>
              ) : null}
              <label className="field-inline" htmlFor="task-filter">
                <span className="field-label">{intl.formatMessage({ id: 'tasks.filter' })}</span>
                <select
                  id="task-filter"
                  name="filterGroupId"
                  className="input select select-inline"
                  value={filterGroupId}
                  onChange={(event) => setFilterGroupId(event.target.value)}
                >
                  <option value={FILTER_ALL}>{intl.formatMessage({ id: 'tasks.filter.all' })}</option>
                  {(groupList ?? []).map((group) => (
                    <option key={group.id} value={group.id}>
                      {groupLabel(intl, group)}
                    </option>
                  ))}
                </select>
              </label>
              <button
                type="button"
                className="btn btn-ghost btn-small"
                onClick={() => setListVersion((version) => version + 1)}
                disabled={tasksLoading}
              >
                {intl.formatMessage({ id: 'common.refresh' })}
              </button>
            </div>
          </div>

          {tasksErrorCode ? (
            <ErrorState
              message={intl.formatMessage({ id: errorMessageKey(tasksErrorCode) })}
              onRetry={() => setListVersion((version) => version + 1)}
            />
          ) : null}

          {tasksErrorCode === null && tasks === null && tasksLoading ? <LoadingState /> : null}

          {tasks !== null && tasks.length === 0 && !tasksLoading ? (
            <EmptyState
              title={intl.formatMessage({ id: 'tasks.empty' })}
              hint={intl.formatMessage({ id: 'tasks.emptyHint' })}
            />
          ) : null}

          {tasks !== null && tasks.length > 0 ? (
            <div className="table-wrap">
              <table className="data-table dense">
                <thead>
                  <tr>
                    <th scope="col">{intl.formatMessage({ id: 'tasks.status' })}</th>
                    <th scope="col">{intl.formatMessage({ id: 'tasks.prompt' })}</th>
                    <th scope="col">{intl.formatMessage({ id: 'tasks.size' })}</th>
                    <th scope="col">{intl.formatMessage({ id: 'common.createdAt' })}</th>
                    <th scope="col" className="col-actions">
                      {intl.formatMessage({ id: 'common.actions' })}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {tasks.map((task) => (
                    <tr key={task.id} className={selected?.id === task.id ? 'is-selected' : undefined}>
                      <td>
                        <StatusBadge status={task.status} />
                      </td>
                      <td className="cell-prompt" title={task.promptSnapshot.body}>
                        <span className="cell-prompt-text">{promptPreview(task.promptSnapshot)}</span>
                        {task.assets.length > 1 ? (
                          <span className="multi-badge">
                            {intl.formatMessage({ id: 'tasks.multiBadge' }, { count: task.assets.length })}
                          </span>
                        ) : null}
                      </td>
                      <td className="cell-size">{snapshotSize(intl, task.resolutionSnapshot)}</td>
                      <td className="cell-muted cell-date">{formatDateTime(intl, task.createdAt)}</td>
                      <td className="col-actions">
                        <button
                          type="button"
                          className="btn btn-ghost btn-small"
                          aria-label={`${intl.formatMessage({ id: 'tasks.view' })} ${task.id}`}
                          onClick={() => {
                            setSelected(task);
                            setPollErrorCode(null);
                          }}
                        >
                          {intl.formatMessage({ id: 'tasks.view' })}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </section>

        <TaskDetail
          task={selected}
          pollErrorCode={pollErrorCode}
          refreshing={refreshing}
          onRefresh={() => {
            void refreshSelected();
          }}
        />
      </div>

      {pickerOpen ? (
        <SizePickerModal
          value={sizeSelection}
          onCancel={() => setPickerOpen(false)}
          onConfirm={(selection) => {
            setSizeSelection(selection);
            setPickerOpen(false);
            setFieldErrorId(null);
          }}
        />
      ) : null}
    </div>
  );
}
