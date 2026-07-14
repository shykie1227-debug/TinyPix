import type { ReactNode } from 'react';

interface ToolOptionCardProps {
  title?: string;
  subtitle?: string;
  children: ReactNode;
  className?: string;
}

export default function ToolOptionCard({
  title,
  subtitle,
  children,
  className = '',
}: ToolOptionCardProps) {
  return (
    <section
      className={`bg-surface-container-lowest rounded-[18px] p-6 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 ${className}`}
    >
      {(title || subtitle) && (
        <div className="mb-4">
          {title && (
            <h3
              className="text-on-surface text-lg leading-6 font-semibold"
            >
              {title}
            </h3>
          )}
          {subtitle && (
            <p className="text-on-surface-variant text-xs mt-1 leading-relaxed">
              {subtitle}
            </p>
          )}
        </div>
      )}
      {children}
    </section>
  );
}
