import React, { useEffect, useMemo, useRef, useState } from "react";
import type { CommandItem } from "@/lib/types";

type Props = {
  isOpen: boolean;
  items: CommandItem[];
  onClose: () => void;
};

export default function CommandPalette(props: Props) {
  const [q, setQ] = useState("");
  const inputRef = useRef<HTMLInputElement | null>(null);

  const filtered = useMemo(() => {
    const query = q.trim().toLowerCase();
    if (!query) return props.items;
    return props.items.filter((x) => {
      const hay = `${x.title} ${x.subtitle ?? ""}`.toLowerCase();
      return hay.includes(query);
    });
  }, [q, props.items]);

  useEffect(() => {
    if (!props.isOpen) return;
    setQ("");
    const t = window.setTimeout(() => inputRef.current?.focus(), 0);
    return () => window.clearTimeout(t);
  }, [props.isOpen]);

  useEffect(() => {
    if (!props.isOpen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        props.onClose();
      }
      if (e.key === "Enter") {
        const item = filtered[0];
        if (!item) return;
        e.preventDefault();
        props.onClose();
        item.run();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [props.isOpen, filtered, props]);

  if (!props.isOpen) return null;

  return (
    <div
      className="Overlay"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) props.onClose();
      }}
    >
      <div className="Palette" role="dialog" aria-modal="true">
        <input
          ref={inputRef}
          className="PaletteInput"
          placeholder="输入命令…（回车执行首条）"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        <div className="PaletteList">
          {filtered.length === 0 ? (
            <div className="PaletteItem">
              <div className="PaletteItemMain">
                <div className="PaletteItemTitle">无匹配命令</div>
                <div className="PaletteItemSubtitle">试试换个关键词</div>
              </div>
            </div>
          ) : (
            filtered.map((x) => (
              <div
                key={x.id}
                className="PaletteItem"
                onClick={() => {
                  props.onClose();
                  x.run();
                }}
              >
                <div className="PaletteItemMain">
                  <div className="PaletteItemTitle">{x.title}</div>
                  {x.subtitle ? <div className="PaletteItemSubtitle">{x.subtitle}</div> : null}
                </div>
                {x.shortcut ? <span className="Kbd">{x.shortcut}</span> : null}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}


