import { isFormHeaderImage, MAX_LINK_CARD_IMAGE_LENGTH, MAX_HEADER_IMAGE_LENGTH } from "../../../../../libs/ui/form-theme";

export async function prepareHeaderImage(file: File): Promise<string> {
  return prepareImage(file, 1600, 400, MAX_HEADER_IMAGE_LENGTH);
}

export async function prepareLinkCardImage(file: File): Promise<string> {
  return prepareImage(file, 480, 240, MAX_LINK_CARD_IMAGE_LENGTH);
}

async function prepareImage(file: File, width: number, height: number, maxLength: number): Promise<string> {
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
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("This image could not be prepared. Try again.");
    const scale = Math.max(canvas.width / image.width, canvas.height / image.height);
    const drawnWidth = image.width * scale;
    const drawnHeight = image.height * scale;
    context.drawImage(image, (canvas.width - drawnWidth) / 2, (canvas.height - drawnHeight) / 2, drawnWidth, drawnHeight);
    for (const quality of [0.85, 0.7, 0.5]) {
      const result = canvas.toDataURL("image/webp", quality);
      if (isFormHeaderImage(result) && result.length <= maxLength) return result;
    }
    throw new Error("This image is too detailed. Try a smaller or simpler image.");
  } finally {
    image.close();
  }
}
