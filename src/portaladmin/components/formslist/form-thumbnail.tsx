"use client";

import { useEffect, useRef, useState } from "react";
import { ArrowDown01Icon, Calendar03Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { FormRow } from "@/lib/api";
import { formThemeStyle, resolveFormTheme } from "../../../../libs/ui/form-theme";
import styles from "./forms-page.module.css";

type PreviewField = NonNullable<FormRow["preview"]>["fields"][number];

export function FormThumbnail({ form }: { form: FormRow }) {
  const canvas = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0.5);
  const theme = resolveFormTheme(form.preview?.theme);
  const fields = form.preview?.fields ?? [];
  const intro = fields[0]?.type === "section" ? fields[0] : null;
  const questions = intro ? fields.slice(1) : fields;

  useEffect(() => {
    if (!canvas.current) return;
    const observer = new ResizeObserver(([entry]) => setScale(entry.contentRect.width / 620));
    observer.observe(canvas.current);
    return () => observer.disconnect();
  }, []);

  return <div className={styles.previewCanvas} ref={canvas} aria-hidden="true">
    <div className={styles.previewPaper} style={{ ...formThemeStyle(theme), transform: `scale(${scale})` }}>
      {theme.headerImage ? <img className={styles.previewHeaderImage} src={theme.headerImage} alt="" /> : null}
      <div className={styles.previewHeader}>
        <p className={styles.previewTitle}>{form.name}</p>
        {intro ? <div className={styles.previewIntro}>
          {intro.label ? <p>{intro.label}</p> : null}
          {intro.help ? <span>{intro.help}</span> : null}
        </div> : null}
      </div>
      <div className={styles.previewFields}>
        {questions.length ? questions.map((field, index) => <PreviewQuestion key={index} field={field} />)
          : <div className={styles.previewEmpty}>{form.preview === undefined ? "Preview unavailable" : "No questions yet"}</div>}
      </div>
    </div>
  </div>;
}

function PreviewQuestion({ field }: { field: PreviewField }) {
  if (field.type === "section") return <div className={styles.previewSection}>
    <p>{field.label || "Untitled section"}</p>
    {field.help ? <span>{field.help}</span> : null}
  </div>;

  return <div className={styles.previewQuestion}>
    <p className={styles.previewLabel}>{field.label || "Untitled question"}{field.required ? <span> *</span> : null}</p>
    {field.help ? <p className={styles.previewHelp}>{field.help}</p> : null}
    {field.type === "radio" || field.type === "checkboxes" ? <div className={styles.previewChoices}>
      {field.options.map((option, index) => <div key={index}>
        <span className={styles.previewCheck} data-round={field.type === "radio"} />{option.label}
      </div>)}
    </div> : field.type === "consent" ? <span className={styles.previewCheck} />
      : <div className={styles.previewAnswer} data-type={field.type}>
        <span>{field.type === "select" ? "Choose an option" : field.type === "file" ? "Upload a file" : field.type === "date" ? "Select a date" : "Your answer"}</span>
        {field.type === "select" ? <Icon icon={ArrowDown01Icon} size={18} /> : field.type === "date" ? <Icon icon={Calendar03Icon} size={18} /> : null}
      </div>}
  </div>;
}
