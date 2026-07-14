interface ExportFormatSelectorProps {
  value: 'mp4' | 'mov' | 'mkv';
  onChange: (v: 'mp4' | 'mov' | 'mkv') => void;
  disabled?: boolean;
}

const FORMATS: Array<'mp4' | 'mov' | 'mkv'> = ['mp4', 'mov', 'mkv'];

/**
 * 模块9：导出格式选择模块（ExportFormatSelectorModule）
 * MP4 / MOV / MKV 三选一排他按钮组
 */
export default function ExportFormatSelector({ value, onChange, disabled }: ExportFormatSelectorProps) {
  return (
    <div className="flex gap-2" role="radiogroup" aria-label="导出格式">
      {FORMATS.map((fmt) => {
        const selected = value === fmt;
        return (
          <button
            key={fmt}
            type="button"
            role="radio"
            aria-checked={selected}
            disabled={disabled}
            onClick={() => onChange(fmt)}
            className={
              selected
                ? 'flex-1 py-2.5 text-body-sm bg-primary text-on-primary rounded-[12px] font-bold transition-all'
                : 'flex-1 py-2.5 text-body-sm bg-surface-container-low hover:bg-surface-container-high rounded-[12px] border border-outline-variant/10 transition-all disabled:opacity-50 disabled:cursor-not-allowed'
            }
          >
            {fmt.toUpperCase()}
          </button>
        );
      })}
    </div>
  );
}
