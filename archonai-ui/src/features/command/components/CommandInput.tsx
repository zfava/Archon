import { useState, useRef, useEffect, type KeyboardEvent } from 'react';

interface Props {
  onSubmit: (value: string) => void;
  disabled: boolean;
  placeholder?: string;
}

export function CommandInput({ onSubmit, disabled, placeholder }: Props) {
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (!disabled) inputRef.current?.focus();
  }, [disabled]);

  function handleKey(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      if (value.trim() && !disabled) {
        onSubmit(value.trim());
        setValue('');
      }
    }
  }

  return (
    <div className="cmd-input-wrap">
      <div className="cmd-input-pill">
        <span className="cmd-input-prefix">
          <span aria-hidden="true">&#9889;</span> Command
          {!value && <span className="cmd-input-cursor" />}
        </span>
        <textarea
          ref={inputRef}
          className="cmd-input"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKey}
          placeholder={placeholder ?? 'Enter a command\u2026'}
          disabled={disabled}
          rows={1}
          autoFocus
        />
      </div>
      <button
        className="cmd-send"
        onClick={() => {
          if (value.trim() && !disabled) {
            onSubmit(value.trim());
            setValue('');
          }
        }}
        disabled={disabled || !value.trim()}
        aria-label="Send"
      >
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M22 2L11 13" />
          <path d="M22 2L15 22L11 13L2 9L22 2Z" />
        </svg>
      </button>
    </div>
  );
}
