import React, { useEffect, useMemo, useState } from "react";
import clsx from "clsx";
import type { LibraryCategory, LibraryFileItem, LibraryIndex } from "@/lib/types";

type Props = {
  projectRoot: string;
  index: LibraryIndex;
  activePath: string | null;
  onOpenFile: (fullPath: string) => void;
  onRefresh: () => void;
};

type TabSpec = { id: LibraryCategory; label: string };

const TABS: TabSpec[] = [
  { id: "chapters", label: "正文" },
  { id: "objects", label: "设定" },
  { id: "roles", label: "人物小传" },
  { id: "rules", label: "规则" },
  { id: "other", label: "其他" },
];

function countLabel(items: LibraryFileItem[]): string {
  if (!items?.length) return "0";
  if (items.length > 999) return "999+";
  return String(items.length);
}

export default function LibraryPanel(props: Props) {
  const [tab, setTab] = useState<LibraryCategory>(() => {
    const v = String(localStorage.getItem("novel.libraryTab") ?? "");
    const allowed = new Set(TABS.map((t) => t.id));
    return allowed.has(v as any) ? (v as LibraryCategory) : "chapters";
  });
  const [query, setQuery] = useState("");

  useEffect(() => {
    localStorage.setItem("novel.libraryTab", tab);
  }, [tab]);

  const list = useMemo(() => {
    const items = props.index[tab] ?? [];
    const q = query.trim().toLowerCase();
    if (!q) return items;
    return items.filter((x) => {
      const hay = `${x.relPath}\n${x.name}`.toLowerCase();
      return hay.includes(q);
    });
  }, [props.index, query, tab]);

  return (
    <div className="Panel">
      <div className="PanelHeader">
        <div className="PanelHeaderTitle">资源</div>
        <button className="Btn" onClick={props.onRefresh} title="刷新文件索引">
          刷新
        </button>
      </div>

      <div className="LibraryBody">
        <div className="LibraryRoot" title={props.projectRoot || ""}>
          {props.projectRoot ? props.projectRoot : "未选择项目根目录"}
        </div>

        <div className="LibraryTabs" role="tablist" aria-label="Library tabs">
          {TABS.map((t) => (
            <button
              key={t.id}
              className={clsx("LibraryTab", tab === t.id && "LibraryTabActive")}
              onClick={() => setTab(t.id)}
              role="tab"
              aria-selected={tab === t.id}
              title={t.label}
            >
              <span className="LibraryTabLabel">{t.label}</span>
              <span className="LibraryTabCount">{countLabel(props.index[t.id] ?? [])}</span>
            </button>
          ))}
        </div>

        <div className="LibrarySearch">
          <input
            className="Input"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="搜索文件（按相对路径匹配）"
          />
        </div>

        <div className="LibraryList" role="list">
          {!props.projectRoot ? (
            <div className="EmptyHint">先点右上角“打开项目”。</div>
          ) : list.length === 0 ? (
            <div className="EmptyHint">没有找到文件（确认目录下有 .txt / .md）。</div>
          ) : (
            list.map((f) => {
              const active = props.activePath === f.path;
              const title = tab === "other" ? f.relPath : f.name;
              const sub = tab === "other" ? f.path : f.relPath;
              return (
                <div
                  key={f.path}
                  className={clsx("ListRow", active && "ListRowActive")}
                  role="listitem"
                  title={f.relPath}
                  onClick={() => props.onOpenFile(f.path)}
                >
                  <div className="ListRowTitle">{title}</div>
                  <div className="ListRowSub">{sub}</div>
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}


