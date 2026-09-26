import { isFormHeaderImage } from "../../../../../libs/ui/form-theme";

export async function prepareHeaderImage(file: File): Promise<string> {
  if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) {
    throw new Error("Choose a JPG, PNG or WebP image.");
  }
  if (file.size > 10 * 1024 * 1024) {
    throw new Error("Choose an image smaller than 10 MB.");
  }

  let image: ImageBitmap;
  try {
    image = await createImageBitmap(file);
  } catch {
    throw new Error("This image could not be opened. Try another image.");
  }

  try {
    const canvas = document.createElement("canvas");
    canvas.width = 1600;
    canvas.height = 400;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("This image could not be prepared. Try again.");
    const scale = Math.max(canvas.width / image.width, canvas.height / image.height);
    const width = image.width * scale;
    const height = image.height * scale;
    context.drawImage(image, (canvas.width - width) / 2, (canvas.height - height) / 2, width, height);
    for (const quality of [0.85, 0.7, 0.5]) {
      const result = canvas.toDataURL("image/webp", quality);
      if (isFormHeaderImage(result)) return result;
    }
    throw new Error("This image is too detailed. Try a smaller or simpler image.");
  } finally {
    image.close();
  }
}
