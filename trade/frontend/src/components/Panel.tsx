import React from "react";

export function Panel(props: { title: string; subtitle?: string; right?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div className="panel">
      <div className="panelHeader">
        <div className="panelTitle">
          <h2>{props.title}</h2>
          {props.subtitle ? <p>{props.subtitle}</p> : null}
        </div>
        {props.right}
      </div>
      <div className="panelBody">{props.children}</div>
    </div>
  );
}


