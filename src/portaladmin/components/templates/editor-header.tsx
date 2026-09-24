import { ArrowRight01Icon, CircleIcon, Edit03Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { ReactNode } from "react";
import { TemplateBackLink } from "./back-link";
import { kindLabel, type EditorStep, type Template } from "./types";
import styles from "./templates.module.css";

const steps = ["Settings", "Design", "API"];

export function EditorHeader({
  template,
  name,
  completedSteps,
  step: activeStep,
  onStep,
  onPrepareDesign,
  canManage,
  saving,
  confirming,
  onSave,
  designTools,
}: {
  template: Template | null;
  name: string;
  completedSteps: readonly number[];
  step: EditorStep;
  onStep: (step: EditorStep) => void;
  onPrepareDesign: () => void;
  canManage: boolean;
  saving: boolean;
  confirming: boolean;
  onSave: () => void;
  designTools?: ReactNode;
}) {
  const designing = activeStep === "design";
  const workflowHeader = designing || activeStep === "api";
  const identity = (
    <div className={`${styles.headerIdentity} ${designing ? styles.designIdentity : ""}`} data-template-motion="identity">
      <h1 className={styles.headerTitle}>{name.trim() || template?.name || template?.key || "Unsaved template"}</h1>
      {template ? <p className={styles.headerMeta}>
        {kindLabel(template.kind)} · {template.hasDraft ? "Draft" : `Version ${template.version}`}
      </p> : null}
    </div>
  );

  return (
    <>
      {!workflowHeader ? <TemplateBackLink /> : null}
      <section className={`${styles.editorHeader} ${workflowHeader ? styles.designHeader : ""}`} aria-label="Template header">
        {workflowHeader ? <TemplateBackLink /> : identity}

        <ol className={styles.headerSteps} aria-label="Template steps" data-template-motion="steps">
          {steps.map((step, index) => (
            <li key={step} className={styles.headerStep} aria-label={completedSteps.includes(index) ? `${step} completed` : undefined}>
              <button type="button" className={styles.stepLabel}
                aria-current={step.toLowerCase() === activeStep ? "step" : undefined}
                disabled={saving || (index > 0 && !completedSteps.includes(index - 1))}
                onMouseEnter={index === 1 ? onPrepareDesign : undefined}
                onFocus={index === 1 ? onPrepareDesign : undefined}
                onClick={() => onStep(step.toLowerCase() as EditorStep)}>
                <span className={`${styles.stepIcon} ${completedSteps.includes(index) ? styles.stepComplete : ""}`}>
                  {completedSteps.includes(index) ? <Icon icon={Tick02Icon} size={18} strokeWidth={2.2} /> : (
                    <>
                      <Icon icon={CircleIcon} size={24} strokeWidth={1.5} />
                      {step.toLowerCase() === activeStep ? <Icon icon={Edit03Icon} size={13} className={styles.stepPencil} /> : null}
                    </>
                  )}
                </span>
                {step}
              </button>
              {index < steps.length - 1 ? <Icon icon={ArrowRight01Icon} size={16} /> : null}
            </li>
          ))}
        </ol>

        {canManage ? (
          <div className={styles.headerAction} data-template-motion="action">
            {designing ? designTools : null}
            <button
              type="button"
              className={`button primary ${styles.headerSave}`}
              onClick={onSave}
              disabled={saving || confirming}
            >
              {activeStep === "api" ? "Done" : saving ? "Saving…" : "Save and continue"}
            </button>
          </div>
        ) : null}
      </section>
    </>
  );
}
