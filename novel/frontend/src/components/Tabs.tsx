import React from "react";
import clsx from "clsx";
import type { OpenFile } from "@/lib/types";

type Props = {
  files: OpenFile[];
  activePath: string | null;
  onActivate: (path: string) => void;
  onClose: (path: string) => void;
};

export default function Tabs(props: Props) {
  if (props.files.length === 0) return <div className="Tabs" />;

  return (
    <div className="Tabs" role="tablist">
      {props.files.map((f) => (
        <div
          key={f.path}
          className={clsx("Tab", props.activePath === f.path && "TabActive")}
          role="tab"
          onClick={() => props.onActivate(f.path)}
          title={f.path}
        >
          {f.isDirty ? <span className="TabDirty" title="未保存" /> : null}
          {f.isLoading ? <span className="TabLoading" title="加载中" /> : null}
          <span>{f.name}</span>
          <button
            className="TabClose"
            onClick={(e) => {
              e.stopPropagation();
              props.onClose(f.path);
            }}
            aria-label="Close"
            title="关闭"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}


