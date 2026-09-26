import { loadForm } from "@/lib/api";
import { formShareImage } from "@/lib/form-share-image";

export const runtime = "nodejs";

export async function GET(_request: Request, { params }: { params: Promise<{ code: string }> }) {
  const { code } = await params;
  const form = await loadForm(code, { anonymous: true });
  if (!form) return new Response("Form not found", { status: 404, headers: { "Cache-Control": "no-store" } });
  return formShareImage(form);
}
