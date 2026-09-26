import { UserIcon } from "@hugeicons/core-free-icons";
import { Avatar } from "@/components/ui/avatar";
import { Icon } from "@/components/ui/icon";
import type { FormField } from "@/lib/api";
import { respondentFor } from "./respondent";
import styles from "./responses.module.css";
import type { ResponseItem } from "./types";

export function ResponsePerson({ item, fields }: { item: ResponseItem; fields: FormField[] }) {
  const person = respondentFor(item, fields);

  return (
    <span className={styles.person}>
      {person.identified ? (
        <Avatar name={person.name} email={person.email} className={styles.avatar} appearance="soft" />
      ) : (
        <span className={`${styles.avatar} ${styles.anonymousAvatar}`}><Icon icon={UserIcon} size={17} /></span>
      )}
      <span className={styles.personText}>
        <span className={styles.personName} title={person.name}>{person.name}</span>
        <span className={styles.personSecondary} title={person.secondary}>{person.secondary}</span>
      </span>
    </span>
  );
}
