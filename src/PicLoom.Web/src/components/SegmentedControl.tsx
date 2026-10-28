import type { CSSProperties } from 'react';

export interface SegmentedOption {
  value: string;
  label: string;
  disabled?: boolean;
  title?: string;
}

interface SegmentedControlProps {
  /** Stable id prefix; each option gets `${id}-${value}` so tests can target a single pill. */
  id: string;
  name: string;
  /** Accessible group label, also rendered visually by the caller. */
  label: string;
  value: string;
  options: SegmentedOption[];
  disabled?: boolean;
  onChange: (value: string) => void;
  style?: CSSProperties;
}

/**
 * Radio-group based segmented control.
 *
 * Deliberately built on native radios rather than buttons: the browser then provides
 * roving focus, arrow-key navigation within the group and correct `checked` semantics
 * for screen readers, with no custom key handling to get wrong.
 */
export function SegmentedControl({
  id,
  name,
  label,
  value,
  options,
  disabled = false,
  onChange,
  style,
}: SegmentedControlProps) {
  return (
    <div className="segmented" id={id} role="radiogroup" aria-label={label} style={style}>
      {options.map((option) => {
        const optionId = `${id}-${option.value}`;
        const isDisabled = disabled || option.disabled === true;
        return (
          <label
            key={option.value}
            className={`segmented-option${value === option.value ? ' is-selected' : ''}${
              isDisabled ? ' is-disabled' : ''
            }`}
            htmlFor={optionId}
            title={option.title}
          >
            <input
              id={optionId}
              type="radio"
              name={name}
              value={option.value}
              checked={value === option.value}
              disabled={isDisabled}
              onChange={() => onChange(option.value)}
            />
            <span>{option.label}</span>
          </label>
        );
      })}
    </div>
  );
}
