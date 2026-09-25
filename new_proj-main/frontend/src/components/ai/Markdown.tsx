"use client";

import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { cn } from "@/lib/cn";

/**
 * Model answers arrive as markdown — bold, lists, headings, occasionally a table. Rendered with react-markdown, which
 * builds React elements from the text and never injects raw HTML, so a model (or a poisoned document quoted by it)
 * cannot smuggle markup into the page. Only the elements mapped below can appear.
 */
export function Markdown({ content, className }: { content: string; className?: string }) {
  return (
    <div className={cn("text-[13.5px] leading-relaxed", className)}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          p: (props) => <p className="mb-2 last:mb-0" {...props} />,
          strong: (props) => <strong className="font-semibold text-ink-900" {...props} />,
          em: (props) => <em className="italic" {...props} />,
          ul: (props) => <ul className="mb-2 list-disc space-y-1 pl-5 last:mb-0" {...props} />,
          ol: (props) => <ol className="mb-2 list-decimal space-y-1 pl-5 last:mb-0" {...props} />,
          li: (props) => <li className="marker:text-ink-400" {...props} />,
          h1: (props) => <h3 className="mb-1.5 mt-3 text-[14.5px] font-semibold text-ink-900 first:mt-0" {...props} />,
          h2: (props) => <h3 className="mb-1.5 mt-3 text-[14px] font-semibold text-ink-900 first:mt-0" {...props} />,
          h3: (props) => <h4 className="mb-1.5 mt-3 text-[13.5px] font-semibold text-ink-900 first:mt-0" {...props} />,
          blockquote: (props) => <blockquote className="mb-2 border-l-2 border-border pl-3 text-ink-600 last:mb-0" {...props} />,
          code: (props) => <code className="rounded bg-ink-100 px-1 py-0.5 font-mono text-[12.5px] text-ink-800" {...props} />,
          pre: (props) => <pre className="mb-2 overflow-x-auto rounded-lg bg-ink-900 p-3 font-mono text-[12.5px] text-ink-50 last:mb-0" {...props} />,
          hr: () => <hr className="my-3 border-border" />,
          a: (props) => <a className="text-brand-600 underline underline-offset-2" target="_blank" rel="noreferrer" {...props} />,
          table: (props) => (
            <div className="mb-2 overflow-x-auto last:mb-0">
              <table className="w-full border-collapse text-[13px]" {...props} />
            </div>
          ),
          th: (props) => <th className="border border-border bg-ink-50 px-2 py-1 text-left font-medium" {...props} />,
          td: (props) => <td className="border border-border px-2 py-1 align-top" {...props} />,
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  );
}
