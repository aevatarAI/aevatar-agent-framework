import React from "react";

type Props = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "default" | "primary" | "danger";
};

export function Button({ variant = "default", className, ...rest }: Props) {
  const cls = ["btn", variant !== "default" ? variant : "", className ?? ""]
    .filter(Boolean)
    .join(" ");
  return <button {...rest} className={cls} />;
}


