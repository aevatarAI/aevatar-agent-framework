import React from "react";

function classify(s: string): "ok" | "warn" | "bad" {
  const x = s.toLowerCase();
  if (x.includes("error") || x.includes("failed") || x.includes("exception")) return "bad";
  if (x.includes("stopped") || x.includes("disabled") || x.includes("not")) return "warn";
  return "ok";
}

export function StatusPill(props: { label: string; value: string }) {
  const level = classify(props.value);
  return (
    <div className="pill" title={props.value}>
      <span className={`dot ${level}`} />
      <span style={{ color: "var(--muted2)" }}>{props.label}:</span>
      <span>{props.value}</span>
    </div>
  );
}


