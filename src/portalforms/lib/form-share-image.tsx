import { ImageResponse } from "next/og";
import sharp from "sharp";
import type { PublicForm } from "./api";
import { formShareContent, shareText } from "./form-sharing";
import { formThemeStyle, resolveFormTheme } from "../../../libs/ui/form-theme";
import { readableTime } from "../../../libs/ui/zone";

async function headerPreview(image: string | null): Promise<string | null> {
  if (!image) return null;
  try {
    const png = await sharp(Buffer.from(image.split(",")[1], "base64"), { limitInputPixels: 16_000_000 })
      .resize({ width: 720, height: 240, fit: "inside", withoutEnlargement: true })
      .png().toBuffer();
    return `data:image/png;base64,${png.toString("base64")}`;
  } catch {
    return null;
  }
}

export async function formShareImage(form: PublicForm) {
  const theme = resolveFormTheme(form.theme);
  const colors = formThemeStyle({ ...theme, background: "neutral" });
  const content = formShareContent(form);
  const header = await headerPreview(theme.headerImage);
  const background = colors["--form-background"];
  const ink = colors["--form-ink"];
  const muted = colors["--form-muted"];
  const border = "rgba(32,36,43,0.12)";
  const deadline = readableTime(form.closesAt);

  return new ImageResponse(
    <div style={{ display: "flex", flexDirection: "column", justifyContent: "center", alignItems: "center", width: "100%", height: "100%", padding: "52px 90px", background, color: ink, fontFamily: "sans-serif", textAlign: "center" }}>
      {header ? <img src={header} width={420} height={140} alt="" style={{ objectFit: "contain", marginBottom: 30 }} /> : <div style={{ display: "flex", color: muted, fontSize: 25, marginBottom: 30 }}>MorganHacks</div>}
      <div style={{ display: "flex", justifyContent: "center", fontSize: content.title.length > 56 ? 46 : 64, lineHeight: 1.15, fontWeight: 700, letterSpacing: "-1.8px", overflow: "hidden", maxHeight: 160, wordBreak: "break-word" }}>{shareText(content.title, 100)}</div>
      {deadline ? <div style={{ display: "flex", flexDirection: "column", alignItems: "center", borderTop: `1px solid ${border}`, marginTop: 34, paddingTop: 26, gap: 12, minWidth: 480 }}>
        <div style={{ display: "flex", fontSize: 20, color: muted }}>{form.open ? "Submission deadline" : "Submissions closed"}</div>
        <div style={{ display: "flex", fontSize: 27, lineHeight: 1.4 }}>{deadline}</div>
      </div> : null}
    </div>,
    { width: 1200, height: 630, headers: { "Cache-Control": "public, max-age=0, s-maxage=300, must-revalidate" } },
  );
}
