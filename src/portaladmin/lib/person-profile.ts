export function displayName(fullName?: string | null, email?: string | null) {
  return fullName?.trim() || email?.split("@")[0]?.trim() || "Account";
}

export function profileInitials(name: string) {
  const words = name.trim().split(/[\s._-]+/).filter(Boolean);
  return (words.length > 1
    ? words[0][0] + words[words.length - 1][0]
    : name.slice(0, 2)
  ).toUpperCase();
}

export function profileColor(identity: string) {
  let hash = 0;
  for (const character of identity.toLowerCase()) {
    hash = (hash * 31 + character.charCodeAt(0)) | 0;
  }
  return `hsl(${Math.abs(hash) % 360} 65% 42%)`;
}
