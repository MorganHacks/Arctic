import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Undo03Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "./templates.module.css";

export function TemplateBackLink() {
  return (
    <Link href="/templates" className={`back ${styles.backLink}`} data-template-motion="back">
      <Icon icon={Undo03Icon} size={17} strokeWidth={2} />
      <span>Back</span>
    </Link>
  );
}
