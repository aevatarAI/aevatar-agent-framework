import React from "react";
import type { CursorPos, OpenFile } from "@/lib/types";
import type { SidecarConnectionState } from "@/lib/sidecar";

type Props = {
  sidecarUrl: string;
  sidecarState: SidecarConnectionState;
  projectRoot: string;
  activeFile: OpenFile | null;
  cursor: CursorPos | null;
};

function stateDot(state: SidecarConnectionState): { cls: string; text: string } {
  switch (state) {
    case "connected":
      return { cls: "Dot DotOk", text: "Sidecar 已连接" };
    case "connecting":
      return { cls: "Dot DotWarn", text: "Sidecar 连接中…" };
    case "error":
      return { cls: "Dot DotBad", text: "Sidecar 连接失败" };
    default:
      return { cls: "Dot", text: "Sidecar 未连接" };
  }
}

export default function StatusBar(props: Props) {
  const dot = stateDot(props.sidecarState);
  const filePart = props.activeFile ? props.activeFile.path : "No file";
  const cursorPart = props.cursor ? `Ln ${props.cursor.line}, Col ${props.cursor.column}` : "";
  const dirtyPart = props.activeFile?.isDirty ? "Unsaved" : "";

  return (
    <div className="StatusBar">
      <div style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
        <span className="Tag" title={props.sidecarUrl}>
          <span className={dot.cls} />
          <span>{dot.text}</span>
        </span>
        <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {props.projectRoot ? `SSOT: ${props.projectRoot}` : "SSOT: 未设置"}
        </span>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        <span style={{ color: "var(--muted)" }}>{dirtyPart}</span>
        <span style={{ color: "var(--muted)" }}>{cursorPart}</span>
        <span style={{ maxWidth: 420, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {filePart}
        </span>
      </div>
    </div>
  );
}


