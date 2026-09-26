import {
  Add01Icon,
  AlertCircleIcon,
  ArrowDown02Icon,
  ArrowUp02Icon,
  Calendar03Icon,
  Cancel01Icon,
  ChartColumnIcon,
  CheckListIcon,
  CheckmarkSquare01Icon,
  Copy01Icon,
  Delete02Icon,
  FileCheckIcon,
  FileUploadIcon,
  FloppyDiskIcon,
  InputLongTextIcon,
  InputShortTextIcon,
  Layout2RowIcon,
  Mail01Icon,
  RadioButtonIcon,
  Select01Icon,
  SmartPhone01Icon,
  TextNumberSignIcon,
  Rocket01Icon,
} from "@hugeicons/core-free-icons";
import { Icon, type IconSvgElement } from "@/components/ui/icon";
import type { FieldType } from "@/lib/api";

// Keep the builder's control API while sharing the console's Hugeicons style.
export function ArrowUp() {
  return <Icon icon={ArrowUp02Icon} size={16} />;
}
export function ArrowDown() {
  return <Icon icon={ArrowDown02Icon} size={16} />;
}
export function Duplicate() {
  return <Icon icon={Copy01Icon} size={16} />;
}
export function Trash() {
  return <Icon icon={Delete02Icon} size={16} />;
}
export function Cross() {
  return <Icon icon={Cancel01Icon} size={16} />;
}
export function Plus() {
  return <Icon icon={Add01Icon} size={16} />;
}
export function Warning() {
  return <Icon icon={AlertCircleIcon} size={13} />;
}
export function Copy() {
  return <Icon icon={Copy01Icon} size={14} />;
}
export function Save() {
  return <Icon icon={FloppyDiskIcon} size={16} />;
}
export function Publish() {
  return <Icon icon={Rocket01Icon} size={16} />;
}
export function PageBreakIcon({ size = 18 }: { size?: number }) {
  return <Icon icon={Layout2RowIcon} size={size} />;
}
export function Questions() {
  return <Icon icon={CheckListIcon} size={15} />;
}
export function Chart() {
  return <Icon icon={ChartColumnIcon} size={15} />;
}

const fieldIcons = {
  shortText: InputShortTextIcon,
  paragraph: InputLongTextIcon,
  email: Mail01Icon,
  phone: SmartPhone01Icon,
  number: TextNumberSignIcon,
  date: Calendar03Icon,
  select: Select01Icon,
  radio: RadioButtonIcon,
  checkboxes: CheckmarkSquare01Icon,
  consent: FileCheckIcon,
  file: FileUploadIcon,
  section: Layout2RowIcon,
} satisfies Record<FieldType, IconSvgElement>;

export function TypeIcon({ type }: { type: FieldType }) {
  return <Icon icon={fieldIcons[type] ?? InputShortTextIcon} size={16} />;
}
