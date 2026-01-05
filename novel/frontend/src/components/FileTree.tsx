import React, { useMemo, useState } from "react";
import clsx from "clsx";
import type { TreeNode } from "@/lib/types";

type Props = {
  root: TreeNode | null;
  activePath: string | null;
  onOpenFile: (fullPath: string) => void;
  onRefresh: () => void;
};

function iconFor(node: TreeNode, expanded: boolean): string {
  if (node.isDir) return expanded ? "▾" : "▸";
  const lower = node.name.toLowerCase();
  if (lower.endsWith(".md")) return "Ⓜ";
  if (lower.endsWith(".txt")) return "Ⓣ";
  return "•";
}

export default function FileTree(props: Props) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());

  const toggle = (path: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

  const rows = useMemo(() => {
    const out: Array<{ node: TreeNode; depth: number }> = [];

    const walk = (node: TreeNode, depth: number) => {
      out.push({ node, depth });
      if (!node.isDir) return;
      if (!expanded.has(node.path)) return;
      for (const ch of node.children ?? []) {
        walk(ch, depth + 1);
      }
    };

    if (props.root) {
      // Root is always expanded visually.
      for (const ch of props.root.children ?? []) {
        walk(ch, 0);
      }
    }
    return out;
  }, [expanded, props.root]);

  return (
    <div className="Panel">
      <div className="PanelHeader">
        <div className="PanelHeaderTitle">Explorer</div>
        <button className="Btn" onClick={props.onRefresh}>
          刷新
        </button>
      </div>
      <div className="Tree" role="tree">
        {!props.root ? (
          <div className="TreeRow">
            <span className="TreeName">未选择项目根目录</span>
          </div>
        ) : (
          <>
            <div className="TreeRow" style={{ opacity: 0.9 }}>
              <span className="TreeIcon">⌂</span>
              <span className="TreeName" title={props.root.path}>
                {props.root.path}
              </span>
            </div>
            {rows.map(({ node, depth }) => {
              const isExpanded = expanded.has(node.path);
              const isActive = props.activePath === node.path;
              return (
                <div
                  key={node.path}
                  className={clsx("TreeRow", isActive && "TreeRowActive")}
                  style={{ paddingLeft: 6 + depth * 14 }}
                  role="treeitem"
                  onClick={() => {
                    if (node.isDir) toggle(node.path);
                    else props.onOpenFile(node.path);
                  }}
                >
                  <span className="TreeIcon">{iconFor(node, isExpanded)}</span>
                  <span className="TreeName" title={node.path}>
                    {node.name}
                  </span>
                </div>
              );
            })}
          </>
        )}
      </div>
    </div>
  );
}


