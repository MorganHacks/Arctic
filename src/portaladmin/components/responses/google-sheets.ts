export const sheetsScope = "https://www.googleapis.com/auth/drive.file";

export type GoogleToken = { access_token?: string; scope?: string; error?: string };
export type GoogleOAuth = {
  initTokenClient: (options: {
    client_id: string;
    scope: string;
    include_granted_scopes: boolean;
    callback: (token: GoogleToken) => void;
    error_callback: (error: { type: string }) => void;
  }) => { requestAccessToken: (options: { prompt: string }) => void };
  hasGrantedAllScopes: (token: GoogleToken, scope: string) => boolean;
};

type GoogleWindow = Window & { google?: { accounts?: { oauth2?: GoogleOAuth } } };
let sdk: Promise<GoogleOAuth> | null = null;

export function loadGoogleOAuth(): Promise<GoogleOAuth> {
  const oauth = (window as GoogleWindow).google?.accounts?.oauth2;
  if (oauth) return Promise.resolve(oauth);
  if (sdk) return sdk;

  sdk = new Promise<GoogleOAuth>((resolve, reject) => {
    const script = document.createElement("script");
    const timer = window.setTimeout(() => fail(), 15000);
    function fail() {
      window.clearTimeout(timer);
      script.remove();
      sdk = null;
      reject(new Error("Google could not be loaded. Check your connection and try again."));
    }
    script.src = "https://accounts.google.com/gsi/client";
    script.async = true;
    script.onload = () => {
      window.clearTimeout(timer);
      const loaded = (window as GoogleWindow).google?.accounts?.oauth2;
      if (loaded) resolve(loaded); else fail();
    };
    script.onerror = fail;
    document.head.appendChild(script);
  });
  return sdk;
}

export async function createResponseSheet({ formId, name, accessToken, signal }: {
  formId: string;
  name: string;
  accessToken: string;
  signal?: AbortSignal;
}): Promise<string> {
  const csv = await fetch(`/api/admin/forms/${encodeURIComponent(formId)}/responses.csv`, {
    credentials: "same-origin", cache: "no-store", signal,
  });
  if (!csv.ok) {
    throw new Error(csv.status === 401 || csv.status === 403
      ? "You no longer have permission to download these responses."
      : "The responses could not be downloaded. Please try again.");
  }
  if (!csv.headers.get("content-type")?.includes("text/csv")) {
    throw new Error("The responses could not be downloaded. Please refresh and try again.");
  }
  const data = await csv.blob();
  const started = await fetch("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${accessToken}`,
      "Content-Type": "application/json; charset=UTF-8",
      "X-Upload-Content-Type": "text/csv",
      "X-Upload-Content-Length": String(data.size),
    },
    body: JSON.stringify({ name: `${name} — Responses`, mimeType: "application/vnd.google-apps.spreadsheet" }),
    signal,
  });
  if (!started.ok) throw new Error(googleError(started.status));

  const location = started.headers.get("location");
  if (!location || new URL(location).origin !== "https://www.googleapis.com") {
    throw new Error("Google could not start the spreadsheet. Please try again.");
  }
  const uploaded = await fetch(location, {
    method: "PUT",
    headers: { "Content-Type": "text/csv" },
    body: data,
    signal,
  });
  if (!uploaded.ok) throw new Error(googleError(uploaded.status));

  const result: { id?: unknown } = await uploaded.json();
  if (typeof result.id !== "string" || !/^[a-zA-Z0-9_-]+$/.test(result.id)) {
    throw new Error("Google did not return a spreadsheet link. Check your Google Drive before trying again.");
  }
  return `https://docs.google.com/spreadsheets/d/${result.id}/edit`;
}

function googleError(status: number): string {
  if (status === 401) return "Your Google connection expired. Choose your account again.";
  if (status === 403) return "Google could not create the spreadsheet. Check that access was granted and Google Drive is enabled for this app.";
  return "The spreadsheet could not be created. Please try again or download the CSV.";
}
