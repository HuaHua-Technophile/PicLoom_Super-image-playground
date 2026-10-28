import { useIntl } from 'react-intl';
import { statusMessageKey } from '../i18n/labels';
import type { TaskStatus } from '../types';

export function StatusBadge({ status }: { status: TaskStatus }) {
  const intl = useIntl();
  const tone = status === 'Succeeded' ? 'ok' : status === 'Failed' ? 'danger' : status === 'Unknown' ? 'warn' : 'neutral';
  return <span className={`status status-${tone}`}>{intl.formatMessage({ id: statusMessageKey(status) })}</span>;
}
